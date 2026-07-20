// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SAM.Analytical.OpenStudio
{
    /// <summary>
    /// Saves the converted model (OSM), generates a minimal OSW workflow, executes the OpenStudio
    /// CLI (discovered per plan §11.2), parses eplusout.err, discovers the SQLite results file and
    /// extracts annual Ideal Loads energy through the SQL layer. Requires no Rhino/Grasshopper.
    /// C6: cancellation tokens share the timeout's Job-Object tree-kill path, RunAsync executes
    /// off the caller thread, runs execute in unique GUID subdirectories by default (an
    /// in-progress lock file guards non-unique directories), and progress stages are reported
    /// through IProgress.
    /// </summary>
    public static class OpenStudioSimulationRunner
    {
        /// <summary>
        /// Asynchronous wrapper over <see cref="Run(OpenStudioConversionContext, string, string, Core.OpenStudio.OpenStudioRunOptions, bool, System.IProgress{Core.OpenStudio.OpenStudioSimulationProgress}, CancellationToken)"/>:
        /// the full save → OSW → CLI → parse pipeline executes on a worker thread; cancellation
        /// terminates the whole CLI/EnergyPlus process tree.
        /// </summary>
        public static Task<OpenStudioConversionResult> RunAsync(OpenStudioConversionContext openStudioConversionContext, string epwPath, string outputDirectory, Core.OpenStudio.OpenStudioRunOptions openStudioRunOptions = null, bool execute = true, System.IProgress<Core.OpenStudio.OpenStudioSimulationProgress> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run(() => Run(openStudioConversionContext, epwPath, outputDirectory, openStudioRunOptions, execute, progress, cancellationToken), cancellationToken);
        }

        /// <summary>
        /// Runs the full save → OSW → CLI → parse pipeline for a converted context and returns
        /// the result snapshot including <see cref="Core.OpenStudio.OpenStudioRunResult"/> and
        /// <see cref="OpenStudioLoadSummary"/>. All failures are surfaced as SAM-OS-RUN-001 /
        /// SAM-OS-EPLUS-001 diagnostics — never silently.
        /// </summary>
        /// <param name="openStudioConversionContext">Converted context (weather and settings already applied).</param>
        /// <param name="epwPath">EPW weather file path (referenced by the OSW).</param>
        /// <param name="outputDirectory">Directory for OSM/OSW and the run folder.</param>
        /// <param name="openStudioRunOptions">Run options; defaults when null.</param>
        /// <param name="execute">False saves the OSM/OSW without launching the CLI (RunResult stays null).</param>
        /// <param name="progress">Optional stage progress sink.</param>
        /// <param name="cancellationToken">Cancellation; kills the CLI/EnergyPlus process tree when triggered.</param>
        /// <returns>Result snapshot with paths, run outcome and loads.</returns>
        public static OpenStudioConversionResult Run(OpenStudioConversionContext openStudioConversionContext, string epwPath, string outputDirectory, Core.OpenStudio.OpenStudioRunOptions openStudioRunOptions = null, bool execute = true, System.IProgress<Core.OpenStudio.OpenStudioSimulationProgress> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (openStudioConversionContext == null)
            {
                return null;
            }

            Core.OpenStudio.OpenStudioRunOptions runOptions = openStudioRunOptions ?? new Core.OpenStudio.OpenStudioRunOptions();
            string directory = runOptions.RunDirectory ?? outputDirectory;

            if (string.IsNullOrWhiteSpace(directory))
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "No output directory was provided for the simulation run");
                return new OpenStudioConversionResult(openStudioConversionContext);
            }

            if (runOptions.UseUniqueRunDirectory)
            {
                directory = Path.Combine(directory, "SAM_OpenStudio_" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
            }

            Directory.CreateDirectory(directory);

            // Non-unique directories: in-progress guard (a stale lock from a crashed run is
            // honoured — never overwritten) plus deterministic cleanup of the old run folder.
            string lockPath = null;
            FileStream lockStream = null;
            if (!runOptions.UseUniqueRunDirectory)
            {
                lockPath = Path.Combine(directory, "sam_openstudio_run.lock");
                try
                {
                    lockStream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                }
                catch (IOException)
                {
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Another run is in progress in {0} (lock file present); use unique run directories for concurrent runs", directory));
                    return new OpenStudioConversionResult(openStudioConversionContext);
                }
            }

            try
            {
                return RunInDirectory(openStudioConversionContext, epwPath, directory, runOptions, execute, progress, cancellationToken);
            }
            finally
            {
                if (lockStream != null)
                {
                    lockStream.Dispose();
                    try
                    {
                        File.Delete(lockPath);
                    }
                    catch (System.Exception)
                    {
                        // lock cleanup is best effort
                    }
                }
            }
        }

        private static OpenStudioConversionResult RunInDirectory(OpenStudioConversionContext openStudioConversionContext, string epwPath, string directory, Core.OpenStudio.OpenStudioRunOptions runOptions, bool execute, System.IProgress<Core.OpenStudio.OpenStudioSimulationProgress> progress, CancellationToken cancellationToken)
        {
            string oldRunDirectory = Path.Combine(directory, "run");
            if (Directory.Exists(oldRunDirectory))
            {
                try
                {
                    Directory.Delete(oldRunDirectory, true);
                }
                catch (System.Exception)
                {
                    // a leftover run folder must not fail the new run
                }
            }

            string modelName = Core.OpenStudio.Query.SanitizeName(openStudioConversionContext.Source?.Name);
            if (string.IsNullOrWhiteSpace(modelName))
            {
                modelName = "SAM_Model";
            }

            string osmPath = Path.Combine(directory, modelName + ".osm");
            string oswPath = Path.Combine(directory, modelName + ".osw");

            progress?.Report(new Core.OpenStudio.OpenStudioSimulationProgress(Core.OpenStudio.OpenStudioSimulationStage.SavingOsm, osmPath));
            if (!openStudioConversionContext.Target.save(global::OpenStudio.OpenStudioUtilitiesCore.toPath(osmPath), true))
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("The OSM could not be saved to {0}", osmPath));
                return new OpenStudioConversionResult(openStudioConversionContext);
            }

            progress?.Report(new Core.OpenStudio.OpenStudioSimulationProgress(Core.OpenStudio.OpenStudioSimulationStage.WritingOsw, oswPath));
            File.WriteAllText(oswPath, string.Format("{{\n  \"seed_file\": \"{0}\",\n  \"weather_file\": \"{1}\",\n  \"steps\": []\n}}\n", Path.GetFileName(osmPath), (epwPath ?? string.Empty).Replace('\\', '/')));

            OpenStudioConversionResult result;

            if (!execute)
            {
                progress?.Report(new Core.OpenStudio.OpenStudioSimulationProgress(Core.OpenStudio.OpenStudioSimulationStage.Complete, "Saved without execution"));
                result = new OpenStudioConversionResult(openStudioConversionContext);
                result.OsmPath = osmPath;
                result.OswPath = oswPath;
                return result;
            }

            if (openStudioConversionContext.HasErrors)
            {
                // Never simulate a model whose conversion already failed — the CLI would burn
                // minutes on a model known to be invalid (review P3-08). The OSM/OSW stay on
                // disk for inspection.
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "Simulation not executed: the conversion reported errors; the OSM/OSW were saved for inspection");
                result = new OpenStudioConversionResult(openStudioConversionContext);
                result.OsmPath = osmPath;
                result.OswPath = oswPath;
                return result;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "Simulation cancelled before the CLI was started");
                result = new OpenStudioConversionResult(openStudioConversionContext);
                result.OsmPath = osmPath;
                result.OswPath = oswPath;
                return result;
            }

            string cliPath = Core.OpenStudio.Query.OpenStudioCliPath(runOptions.CliPath);
            if (cliPath == null)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "openstudio.exe was not found (searched explicit path, PATH, direct installations and the ladybug_tools bundle)");
                result = new OpenStudioConversionResult(openStudioConversionContext);
                result.OsmPath = osmPath;
                result.OswPath = oswPath;
                return result;
            }

            progress?.Report(new Core.OpenStudio.OpenStudioSimulationProgress(Core.OpenStudio.OpenStudioSimulationStage.RunningCli, cliPath));

            int exitCode;
            bool cancelled;
            Stopwatch stopwatch = Stopwatch.StartNew();
            string output = ExecuteCli(cliPath, oswPath, directory, runOptions.TimeoutSeconds, cancellationToken, out exitCode, out cancelled);
            stopwatch.Stop();
            double runtimeSeconds = stopwatch.Elapsed.TotalSeconds;

            if (cancelled)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "Simulation cancelled; the CLI/EnergyPlus process tree was terminated");
            }

            string runDirectory = Path.Combine(directory, "run");
            string errorFilePath = Path.Combine(runDirectory, "eplusout.err");
            string sqlPath = Path.Combine(runDirectory, "eplusout.sql");

            List<string> severeErrors = new List<string>();
            List<string> fatalErrors = new List<string>();
            int warningCount = 0;
            if (File.Exists(errorFilePath))
            {
                string[] errorLines;
                try
                {
                    // After a timeout kill the err file can still be locked by the dying
                    // process or be partially written — parsing is best effort.
                    errorLines = File.ReadAllLines(errorFilePath);
                }
                catch (System.Exception)
                {
                    errorLines = null;
                }

                if (errorLines != null)
                {
                    foreach (string line in errorLines)
                    {
                        if (line.Contains("** Severe"))
                        {
                            severeErrors.Add(line.Trim());
                        }
                        else if (line.Contains("**  Fatal") || line.Contains("** Fatal"))
                        {
                            fatalErrors.Add(line.Trim());
                        }
                        else if (line.Contains("** Warning"))
                        {
                            warningCount++;
                        }
                    }
                }
            }

            bool sqlExists = File.Exists(sqlPath);
            bool success = !cancelled && exitCode == 0 && fatalErrors.Count == 0 && sqlExists;

            if (!success && !cancelled)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("OpenStudio CLI run failed (exit code {0}, SQL {1}, {2} fatal error(s)). Output tail: {3}", exitCode, sqlExists ? "present" : "missing", fatalErrors.Count, Tail(output, 500)));
            }

            int severeCount = 0;
            foreach (string severeError in severeErrors)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.EnergyPlusSevereError, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, severeError);
                severeCount++;
                if (severeCount >= 10)
                {
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.EnergyPlusSevereError, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("... {0} further severe errors omitted", severeErrors.Count - severeCount));
                    break;
                }
            }

            OpenStudioLoadSummary loadSummary = null;
            OpenStudioSimulationResultSet resultSet = null;
            if (success)
            {
                progress?.Report(new Core.OpenStudio.OpenStudioSimulationProgress(Core.OpenStudio.OpenStudioSimulationStage.ReadingResults, sqlPath));
                loadSummary = ExtractLoads(sqlPath);
                if (loadSummary == null)
                {
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "Ideal Loads results could not be extracted from the SQLite output");
                    success = false;
                }
                else
                {
                    resultSet = ExtractResultSet(sqlPath, loadSummary, runOptions.ExtractTimeSeries, runtimeSeconds, warningCount, severeErrors.Count, fatalErrors.Count);
                }
            }

            progress?.Report(new Core.OpenStudio.OpenStudioSimulationProgress(Core.OpenStudio.OpenStudioSimulationStage.Complete, cancelled ? "Cancelled" : success ? "Success" : "Failed"));

            result = new OpenStudioConversionResult(openStudioConversionContext);
            result.OsmPath = osmPath;
            result.OswPath = oswPath;
            result.RunResult = new Core.OpenStudio.OpenStudioRunResult(success, exitCode, osmPath, oswPath, sqlExists ? sqlPath : null, File.Exists(errorFilePath) ? errorFilePath : null, severeErrors, fatalErrors);
            result.Loads = loadSummary;
            result.Results = resultSet;
            return result;
        }

        /// <summary>
        /// Runs an existing OSM or OSW file through the OpenStudio CLI without a conversion
        /// context — for an OSM an OSW is generated first (EPW required). Failures and
        /// EnergyPlus severe errors are appended to <paramref name="diagnostics"/> when provided.
        /// </summary>
        /// <param name="path">Existing .osm or .osw file.</param>
        /// <param name="epwPath">EPW weather file (required for .osm input; ignored for .osw).</param>
        /// <param name="outputDirectory">Directory for the generated OSW and run folder; defaults next to the input.</param>
        /// <param name="openStudioRunOptions">Run options; defaults when null.</param>
        /// <param name="loadSummary">Annual Ideal Loads extracted on success; null otherwise.</param>
        /// <param name="diagnostics">Optional diagnostics sink.</param>
        /// <param name="cancellationToken">Cancellation; kills the CLI/EnergyPlus process tree when triggered.</param>
        /// <returns>The run result (never null).</returns>
        public static Core.OpenStudio.OpenStudioRunResult Run(string path, string epwPath, string outputDirectory, Core.OpenStudio.OpenStudioRunOptions openStudioRunOptions, out OpenStudioLoadSummary loadSummary, IList<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            loadSummary = null;
            Core.OpenStudio.OpenStudioRunOptions runOptions = openStudioRunOptions ?? new Core.OpenStudio.OpenStudioRunOptions();

            void AddDiagnostic(string code, Core.OpenStudio.OpenStudioDiagnosticSeverity severity, string message)
            {
                diagnostics?.Add(new Core.OpenStudio.OpenStudioDiagnostic(code, severity, message));
            }

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Input file not found: {0}", path));
                return new Core.OpenStudio.OpenStudioRunResult(false, -1, null, null, null, null, null, null);
            }

            string directory = runOptions.RunDirectory ?? outputDirectory ?? Path.GetDirectoryName(path);
            if (runOptions.UseUniqueRunDirectory)
            {
                directory = Path.Combine(directory, "SAM_OpenStudio_" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
            }

            Directory.CreateDirectory(directory);

            string osmPath = null;
            string oswPath;
            string extension = Path.GetExtension(path);
            if (string.Equals(extension, ".osw", System.StringComparison.OrdinalIgnoreCase))
            {
                oswPath = path;
            }
            else
            {
                osmPath = path;
                if (string.IsNullOrWhiteSpace(epwPath) || !File.Exists(epwPath))
                {
                    AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("EPW weather file not found: {0}", epwPath));
                    return new Core.OpenStudio.OpenStudioRunResult(false, -1, osmPath, null, null, null, null, null);
                }

                oswPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(path) + ".osw");
                File.WriteAllText(oswPath, string.Format("{{\n  \"seed_file\": \"{0}\",\n  \"weather_file\": \"{1}\",\n  \"steps\": []\n}}\n", Path.GetFullPath(path).Replace('\\', '/'), Path.GetFullPath(epwPath).Replace('\\', '/')));
            }

            string cliPath = Core.OpenStudio.Query.OpenStudioCliPath(runOptions.CliPath);
            if (cliPath == null)
            {
                AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "openstudio.exe was not found (searched explicit path, PATH, direct installations and the ladybug_tools bundle)");
                return new Core.OpenStudio.OpenStudioRunResult(false, -1, osmPath, oswPath, null, null, null, null);
            }

            string workingDirectory = Path.GetDirectoryName(oswPath);

            string oldRunDirectory = Path.Combine(workingDirectory, "run");
            if (Directory.Exists(oldRunDirectory))
            {
                try
                {
                    Directory.Delete(oldRunDirectory, true);
                }
                catch (System.Exception)
                {
                    // a leftover run folder must not fail the new run
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "Simulation cancelled before the CLI was started");
                return new Core.OpenStudio.OpenStudioRunResult(false, -1, osmPath, oswPath, null, null, null, null);
            }

            int exitCode;
            bool cancelled;
            string output = ExecuteCli(cliPath, oswPath, workingDirectory, runOptions.TimeoutSeconds, cancellationToken, out exitCode, out cancelled);

            if (cancelled)
            {
                AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "Simulation cancelled; the CLI/EnergyPlus process tree was terminated");
            }

            string runDirectory = Path.Combine(workingDirectory, "run");
            string errorFilePath = Path.Combine(runDirectory, "eplusout.err");
            string sqlPath = Path.Combine(runDirectory, "eplusout.sql");

            List<string> severeErrors = new List<string>();
            List<string> fatalErrors = new List<string>();
            if (File.Exists(errorFilePath))
            {
                string[] errorLines;
                try
                {
                    errorLines = File.ReadAllLines(errorFilePath);
                }
                catch (System.Exception)
                {
                    errorLines = null;
                }

                if (errorLines != null)
                {
                    foreach (string line in errorLines)
                    {
                        if (line.Contains("** Severe"))
                        {
                            severeErrors.Add(line.Trim());
                        }
                        else if (line.Contains("**  Fatal") || line.Contains("** Fatal"))
                        {
                            fatalErrors.Add(line.Trim());
                        }
                    }
                }
            }

            bool sqlExists = File.Exists(sqlPath);
            bool success = !cancelled && exitCode == 0 && fatalErrors.Count == 0 && sqlExists;

            if (!success && !cancelled)
            {
                AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("OpenStudio CLI run failed (exit code {0}, SQL {1}, {2} fatal error(s)). Output tail: {3}", exitCode, sqlExists ? "present" : "missing", fatalErrors.Count, Tail(output, 500)));
            }

            foreach (string severeError in severeErrors)
            {
                AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.EnergyPlusSevereError, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, severeError);
            }

            if (success)
            {
                loadSummary = ExtractLoads(sqlPath);
            }

            return new Core.OpenStudio.OpenStudioRunResult(success, exitCode, osmPath, oswPath, sqlExists ? sqlPath : null, File.Exists(errorFilePath) ? errorFilePath : null, severeErrors, fatalErrors);
        }

        private static string ExecuteCli(string cliPath, string oswPath, string workingDirectory, int timeoutSeconds, CancellationToken cancellationToken, out int exitCode, out bool cancelled)
        {
            cancelled = false;
            bool cancellationRequested = false;

            ProcessStartInfo processStartInfo = new ProcessStartInfo
            {
                FileName = cliPath,
                Arguments = string.Format("run -w \"{0}\"", oswPath),
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            System.Text.StringBuilder standardOutput = new System.Text.StringBuilder();
            System.Text.StringBuilder standardError = new System.Text.StringBuilder();
            object sync = new object();

            using (Process process = Process.Start(processStartInfo))
            {
                if (process == null)
                {
                    exitCode = -1;
                    return "The CLI process could not be started";
                }

                // Job object first (best effort): on timeout OR cancellation the whole tree —
                // the CLI and any EnergyPlus child it spawned — is terminated when the job
                // handle closes.
                System.IntPtr jobHandle = ProcessJobObject.CreateKillOnCloseJob();
                ProcessJobObject.TryAssign(jobHandle, process);

                try
                {
                    // Asynchronous reads: synchronous ReadToEnd on both pipes can deadlock
                    // (child blocks writing to a full stderr pipe while the parent blocks on
                    // stdout) and blocks the timeout from ever being evaluated.
                    process.OutputDataReceived += (sender, e) => { if (e.Data != null) { lock (sync) { standardOutput.AppendLine(e.Data); } } };
                    process.ErrorDataReceived += (sender, e) => { if (e.Data != null) { lock (sync) { standardError.AppendLine(e.Data); } } };
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    using (cancellationToken.Register(() =>
                    {
                        cancellationRequested = true;
                        ProcessJobObject.Terminate(jobHandle);
                        try
                        {
                            process.Kill();
                        }
                        catch (System.Exception)
                        {
                            // the process may have exited between cancellation and the kill
                        }
                    }))
                    {
                        int timeoutMilliseconds = timeoutSeconds <= 0 ? 3600000 : timeoutSeconds * 1000;
                        if (!process.WaitForExit(timeoutMilliseconds))
                        {
                            // Terminate the tree first (children keep spawning while the root
                            // dies), then kill the root as the fallback for a failed job
                            // assignment.
                            ProcessJobObject.Terminate(jobHandle);

                            try
                            {
                                process.Kill();
                            }
                            catch (System.Exception)
                            {
                                // the process may have exited between the timeout and the kill
                            }

                            try
                            {
                                process.WaitForExit(5000);
                            }
                            catch (System.Exception)
                            {
                                // best effort
                            }

                            exitCode = -1;
                            cancelled = cancellationRequested;
                            lock (sync)
                            {
                                return cancelled ? "CANCELLED\n" + standardOutput + standardError.ToString() : "TIMEOUT after " + timeoutSeconds + " s\n" + standardOutput + standardError.ToString();
                            }
                        }

                        process.WaitForExit(); // let the asynchronous output handlers flush
                        cancelled = cancellationRequested;
                        exitCode = cancelled ? -1 : process.ExitCode;
                        lock (sync)
                        {
                            return cancelled ? "CANCELLED\n" + standardOutput + standardError.ToString() : standardOutput.ToString() + standardError;
                        }
                    }
                }
                finally
                {
                    ProcessJobObject.Close(jobHandle);
                }
            }
        }

        private static OpenStudioLoadSummary ExtractLoads(string sqlPath)
        {
            try
            {
                Dictionary<string, double> zoneHeating = ReadAnnualEnergy(sqlPath, "Zone Ideal Loads Supply Air Total Heating Energy");
                Dictionary<string, double> zoneCooling = ReadAnnualEnergy(sqlPath, "Zone Ideal Loads Supply Air Total Cooling Energy");
                if (zoneHeating == null || zoneCooling == null)
                {
                    return null;
                }

                return new OpenStudioLoadSummary(zoneHeating, zoneCooling);
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Builds the engine-neutral result set (C5): per-zone peaks with hour-of-year,
        /// building-level coincident peaks, unmet hours, annual gains breakdown and —
        /// when requested — the hourly temperature/operative/humidity series. All SQL is
        /// parameterised and restricted to the weather-run environment.
        /// </summary>
        private static OpenStudioSimulationResultSet ExtractResultSet(string sqlPath, OpenStudioLoadSummary loadSummary, bool extractTimeSeries, double runtimeSeconds, int warningCount, int severeCount, int fatalCount)
        {
            try
            {
                Dictionary<string, List<KeyValuePair<int, double>>> heatingSeries = ReadHourlyEnergySeries(sqlPath, "Zone Ideal Loads Supply Air Total Heating Energy");
                Dictionary<string, List<KeyValuePair<int, double>>> coolingSeries = ReadHourlyEnergySeries(sqlPath, "Zone Ideal Loads Supply Air Total Cooling Energy");

                Dictionary<string, double> peakHeatingLoad = new Dictionary<string, double>();
                Dictionary<string, double> peakCoolingLoad = new Dictionary<string, double>();
                Dictionary<string, int> peakHeatingHour = new Dictionary<string, int>();
                Dictionary<string, int> peakCoolingHour = new Dictionary<string, int>();

                FillPeaks(heatingSeries, peakHeatingLoad, peakHeatingHour);
                FillPeaks(coolingSeries, peakCoolingLoad, peakCoolingHour);

                double peakHeatingLoadTotal;
                int peakHeatingHourTotal;
                CoincidentPeak(heatingSeries, out peakHeatingLoadTotal, out peakHeatingHourTotal);

                double peakCoolingLoadTotal;
                int peakCoolingHourTotal;
                CoincidentPeak(coolingSeries, out peakCoolingLoadTotal, out peakCoolingHourTotal);

                Dictionary<string, double> unmetHeatingHours = ReadAnnualSum(sqlPath, "Zone Heating Setpoint Not Met Time", 1.0);
                Dictionary<string, double> unmetCoolingHours = ReadAnnualSum(sqlPath, "Zone Cooling Setpoint Not Met Time", 1.0);

                Dictionary<string, double> infiltrationGains = ReadAnnualEnergy(sqlPath, "Zone Infiltration Sensible Heat Gain Energy");
                Dictionary<string, double> infiltrationLosses = ReadAnnualEnergy(sqlPath, "Zone Infiltration Sensible Heat Loss Energy");
                if (infiltrationGains != null && infiltrationLosses != null)
                {
                    foreach (KeyValuePair<string, double> keyValuePair in infiltrationLosses)
                    {
                        infiltrationGains[keyValuePair.Key] = (infiltrationGains.TryGetValue(keyValuePair.Key, out double gain) ? gain : 0) - keyValuePair.Value;
                    }
                }

                Dictionary<string, double[]> temperatureSeries = null;
                Dictionary<string, double[]> operativeTemperatureSeries = null;
                Dictionary<string, double[]> relativeHumiditySeries = null;
                if (extractTimeSeries)
                {
                    temperatureSeries = ToSeries(ReadHourlyValueSeries(sqlPath, "Zone Mean Air Temperature"));
                    operativeTemperatureSeries = ToSeries(ReadHourlyValueSeries(sqlPath, "Zone Operative Temperature"));
                    relativeHumiditySeries = ToSeries(ReadHourlyValueSeries(sqlPath, "Zone Air Relative Humidity"));
                }

                // Zone identity normalisation: Ideal Loads variables key on the system name,
                // zone-level variables on the ThermalZone name, enclosure variables on the
                // Space name — all end with the same deterministic SAM Guid suffix. Every
                // per-zone dictionary is remapped onto the energy key so the result set has
                // ONE zone identity.
                List<string> zoneKeys = new List<string>(loadSummary.ZoneHeating.Keys);

                return new OpenStudioSimulationResultSet(
                    loadSummary.ZoneHeating,
                    loadSummary.ZoneCooling,
                    peakHeatingLoad,
                    peakCoolingLoad,
                    peakHeatingHour,
                    peakCoolingHour,
                    peakHeatingLoadTotal,
                    peakHeatingHourTotal,
                    peakCoolingLoadTotal,
                    peakCoolingHourTotal,
                    NormalizeKeys(unmetHeatingHours, zoneKeys),
                    NormalizeKeys(unmetCoolingHours, zoneKeys),
                    NormalizeKeys(ReadAnnualEnergy(sqlPath, "Zone People Total Heating Energy"), zoneKeys),
                    NormalizeKeys(ReadAnnualEnergy(sqlPath, "Zone Lights Total Heating Energy"), zoneKeys),
                    NormalizeKeys(ReadAnnualEnergy(sqlPath, "Zone Electric Equipment Total Heating Energy"), zoneKeys),
                    // Zone-window-solar variables are Enclosure-scoped in current EnergyPlus
                    // ("Zone Windows ..." was renamed).
                    NormalizeKeys(ReadAnnualEnergy(sqlPath, "Enclosure Windows Total Transmitted Solar Radiation Energy"), zoneKeys),
                    NormalizeKeys(infiltrationGains, zoneKeys),
                    ReadAnnualEnergy(sqlPath, "Zone Ideal Loads Outdoor Air Sensible Heating Energy"),
                    ReadAnnualEnergy(sqlPath, "Zone Ideal Loads Outdoor Air Sensible Cooling Energy"),
                    NormalizeKeys(temperatureSeries, zoneKeys),
                    NormalizeKeys(operativeTemperatureSeries, zoneKeys),
                    NormalizeKeys(relativeHumiditySeries, zoneKeys),
                    runtimeSeconds,
                    warningCount,
                    severeCount,
                    fatalCount);
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Remaps report keys onto the energy-dictionary zone keys by their shared deterministic
        /// SAM Guid suffix (last 8 characters, case-insensitive): Ideal Loads variables key on
        /// the system name, zone-level variables on the ThermalZone name, enclosure variables on
        /// the Space name — the result set exposes ONE zone identity. Unmatched keys pass
        /// through unchanged.
        /// </summary>
        private static Dictionary<string, double> NormalizeKeys(Dictionary<string, double> source, List<string> zoneKeys)
        {
            if (source == null)
            {
                return null;
            }

            Dictionary<string, double> result = new Dictionary<string, double>();
            foreach (KeyValuePair<string, double> keyValuePair in source)
            {
                result[NormalizeKey(keyValuePair.Key, zoneKeys)] = keyValuePair.Value;
            }

            return result;
        }

        private static Dictionary<string, double[]> NormalizeKeys(Dictionary<string, double[]> source, List<string> zoneKeys)
        {
            if (source == null)
            {
                return null;
            }

            Dictionary<string, double[]> result = new Dictionary<string, double[]>();
            foreach (KeyValuePair<string, double[]> keyValuePair in source)
            {
                result[NormalizeKey(keyValuePair.Key, zoneKeys)] = keyValuePair.Value;
            }

            return result;
        }

        private static string NormalizeKey(string key, List<string> zoneKeys)
        {
            if (key == null || zoneKeys == null || zoneKeys.Count == 0 || key.Length < 8)
            {
                return key;
            }

            string suffix = key.Substring(key.Length - 8);
            foreach (string zoneKey in zoneKeys)
            {
                if (zoneKey != null && zoneKey.Length >= 8 && string.Equals(zoneKey.Substring(zoneKey.Length - 8), suffix, System.StringComparison.OrdinalIgnoreCase))
                {
                    return zoneKey;
                }
            }

            return key;
        }

        private static void FillPeaks(Dictionary<string, List<KeyValuePair<int, double>>> series, Dictionary<string, double> peakLoad, Dictionary<string, int> peakHour)        {
            if (series == null)
            {
                return;
            }

            foreach (KeyValuePair<string, List<KeyValuePair<int, double>>> keyValuePair in series)
            {
                double peakJoules = double.MinValue;
                int peakHourOfYear = -1;
                foreach (KeyValuePair<int, double> point in keyValuePair.Value)
                {
                    if (point.Value > peakJoules)
                    {
                        peakJoules = point.Value;
                        peakHourOfYear = point.Key;
                    }
                }

                if (peakHourOfYear >= 0)
                {
                    peakLoad[keyValuePair.Key] = Core.OpenStudio.Query.JoulesPerIntervalToWatts(peakJoules) / 1000.0;
                    peakHour[keyValuePair.Key] = peakHourOfYear;
                }
            }
        }

        private static void CoincidentPeak(Dictionary<string, List<KeyValuePair<int, double>>> series, out double peakLoad, out int peakHour)
        {
            peakLoad = 0;
            peakHour = -1;
            if (series == null)
            {
                return;
            }

            SortedDictionary<int, double> totals = new SortedDictionary<int, double>();
            foreach (KeyValuePair<string, List<KeyValuePair<int, double>>> keyValuePair in series)
            {
                foreach (KeyValuePair<int, double> point in keyValuePair.Value)
                {
                    totals.TryGetValue(point.Key, out double current);
                    totals[point.Key] = current + point.Value;
                }
            }

            double peakJoules = double.MinValue;
            foreach (KeyValuePair<int, double> keyValuePair in totals)
            {
                if (keyValuePair.Value > peakJoules)
                {
                    peakJoules = keyValuePair.Value;
                    peakHour = keyValuePair.Key;
                }
            }

            if (peakHour >= 0)
            {
                peakLoad = Core.OpenStudio.Query.JoulesPerIntervalToWatts(peakJoules) / 1000.0;
            }
        }

        private static Dictionary<string, double[]> ToSeries(Dictionary<string, List<KeyValuePair<int, double>>> hourlyValues)
        {
            if (hourlyValues == null)
            {
                return null;
            }

            Dictionary<string, double[]> result = new Dictionary<string, double[]>();
            foreach (KeyValuePair<string, List<KeyValuePair<int, double>>> keyValuePair in hourlyValues)
            {
                List<KeyValuePair<int, double>> points = keyValuePair.Value;
                if (points.Count == 0)
                {
                    continue;
                }

                double[] values = new double[points.Count];
                for (int i = 0; i < points.Count; i++)
                {
                    values[i] = points[i].Value;
                }

                result[keyValuePair.Key] = values;
            }

            return result;
        }

        /// <summary>Sums any report variable per key over the weather-run environment with an explicit scale factor (no unit conversion).</summary>
        private static Dictionary<string, double> ReadAnnualSum(string sqlPath, string variableName, double factor)
        {
            if (string.IsNullOrWhiteSpace(sqlPath) || !File.Exists(sqlPath))
            {
                return null;
            }

            try
            {
                using (System.Data.SQLite.SQLiteConnection connection = new System.Data.SQLite.SQLiteConnection(new System.Data.SQLite.SQLiteConnectionStringBuilder { DataSource = sqlPath, ReadOnly = true, FailIfMissing = true }.ConnectionString))
                {
                    connection.Open();
                    bool filterEnvironment = HasWeatherRunEnvironment(connection);

                    List<string> keys = ReadKeys(connection, variableName);
                    Dictionary<string, double> result = new Dictionary<string, double>();
                    foreach (string key in keys)
                    {
                        using (System.Data.SQLite.SQLiteCommand command = connection.CreateCommand())
                        {
                            command.CommandText = filterEnvironment
                                ? "SELECT SUM(rd.Value) FROM ReportData rd JOIN ReportDataDictionary rdd ON rd.ReportDataDictionaryIndex = rdd.ReportDataDictionaryIndex JOIN Time t ON rd.TimeIndex = t.TimeIndex WHERE rdd.Name = @name AND rdd.KeyValue = @key AND t.EnvironmentPeriodIndex IN (SELECT EnvironmentPeriodIndex FROM EnvironmentPeriods WHERE EnvironmentType = 3)"
                                : "SELECT SUM(rd.Value) FROM ReportData rd JOIN ReportDataDictionary rdd ON rd.ReportDataDictionaryIndex = rdd.ReportDataDictionaryIndex WHERE rdd.Name = @name AND rdd.KeyValue = @key";
                            command.Parameters.AddWithValue("@name", variableName);
                            command.Parameters.AddWithValue("@key", key);

                            object value = command.ExecuteScalar();
                            if (value != null && value != System.DBNull.Value)
                            {
                                result[key] = System.Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture) * factor;
                            }
                        }
                    }

                    return result;
                }
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        /// <summary>Hourly energy [J] series per key over the weather-run environment: zone key → (hour-of-year, value) pairs in time order.</summary>
        private static Dictionary<string, List<KeyValuePair<int, double>>> ReadHourlyEnergySeries(string sqlPath, string variableName)
        {
            return ReadHourlyValueSeries(sqlPath, variableName);
        }

        /// <summary>Hourly series per key over the weather-run environment (raw values, no conversion): zone key → (hour-of-year, value) pairs in time order.</summary>
        private static Dictionary<string, List<KeyValuePair<int, double>>> ReadHourlyValueSeries(string sqlPath, string variableName)
        {
            if (string.IsNullOrWhiteSpace(sqlPath) || !File.Exists(sqlPath))
            {
                return null;
            }

            try
            {
                using (System.Data.SQLite.SQLiteConnection connection = new System.Data.SQLite.SQLiteConnection(new System.Data.SQLite.SQLiteConnectionStringBuilder { DataSource = sqlPath, ReadOnly = true, FailIfMissing = true }.ConnectionString))
                {
                    connection.Open();
                    bool filterEnvironment = HasWeatherRunEnvironment(connection);

                    List<string> keys = ReadKeys(connection, variableName);
                    Dictionary<string, List<KeyValuePair<int, double>>> result = new Dictionary<string, List<KeyValuePair<int, double>>>();
                    foreach (string key in keys)
                    {
                        List<KeyValuePair<int, double>> points = new List<KeyValuePair<int, double>>();
                        using (System.Data.SQLite.SQLiteCommand command = connection.CreateCommand())
                        {
                            command.CommandText = "SELECT t.Month, t.Day, t.Hour, rd.Value FROM ReportData rd JOIN ReportDataDictionary rdd ON rd.ReportDataDictionaryIndex = rdd.ReportDataDictionaryIndex JOIN Time t ON rd.TimeIndex = t.TimeIndex WHERE rdd.Name = @name AND rdd.KeyValue = @key"
                                + (filterEnvironment ? " AND t.EnvironmentPeriodIndex IN (SELECT EnvironmentPeriodIndex FROM EnvironmentPeriods WHERE EnvironmentType = 3)" : string.Empty)
                                + " ORDER BY t.TimeIndex";
                            command.Parameters.AddWithValue("@name", variableName);
                            command.Parameters.AddWithValue("@key", key);

                            using (System.Data.SQLite.SQLiteDataReader reader = command.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    int month = reader.GetInt32(0);
                                    int day = reader.GetInt32(1);
                                    int hour = reader.GetInt32(2);
                                    double value = reader.GetDouble(3);

                                    int monthClamped = System.Math.Max(1, System.Math.Min(12, month));
                                    int dayClamped = System.Math.Max(1, System.Math.Min(System.DateTime.DaysInMonth(2023, monthClamped), day));
                                    int dayOfYear = new System.DateTime(2023, monthClamped, dayClamped).DayOfYear;
                                    points.Add(new KeyValuePair<int, double>((dayOfYear - 1) * 24 + (hour - 1), value));
                                }
                            }
                        }

                        if (points.Count > 0)
                        {
                            result[key] = points;
                        }
                    }

                    return result;
                }
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private static bool HasWeatherRunEnvironment(System.Data.SQLite.SQLiteConnection connection)
        {
            using (System.Data.SQLite.SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'EnvironmentPeriods'";
                if ((long)command.ExecuteScalar() == 0)
                {
                    return false;
                }

                command.CommandText = "SELECT COUNT(*) FROM EnvironmentPeriods WHERE EnvironmentType = 3";
                return (long)command.ExecuteScalar() > 0;
            }
        }

        private static List<string> ReadKeys(System.Data.SQLite.SQLiteConnection connection, string variableName)
        {
            List<string> result = new List<string>();
            using (System.Data.SQLite.SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT DISTINCT rdd.KeyValue FROM ReportDataDictionary rdd WHERE rdd.Name = @name";
                command.Parameters.AddWithValue("@name", variableName);
                using (System.Data.SQLite.SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (!reader.IsDBNull(0))
                        {
                            result.Add(reader.GetString(0));
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Reads the annual (summed) energy per key for one report variable from the EnergyPlus
        /// SQLite output, converted J → kWh, restricted to the weather-file RunPeriod
        /// environment (EnvironmentType 3) so imported design days never double-count into the
        /// annual totals. All queries are parameterised (System.Data.SQLite) — zone/key names
        /// are never string-concatenated into SQL, so names containing quotes or SQL syntax
        /// cannot break or inject a query.
        /// </summary>
        /// <param name="sqlPath">Path to eplusout.sql.</param>
        /// <param name="variableName">ReportDataDictionary variable name.</param>
        /// <returns>KeyValue → annual energy [kWh]; null when the file cannot be read.</returns>
        internal static Dictionary<string, double> ReadAnnualEnergy(string sqlPath, string variableName)
        {
            if (string.IsNullOrWhiteSpace(sqlPath) || !File.Exists(sqlPath) || string.IsNullOrWhiteSpace(variableName))
            {
                return null;
            }

            System.Data.SQLite.SQLiteConnectionStringBuilder connectionStringBuilder = new System.Data.SQLite.SQLiteConnectionStringBuilder
            {
                DataSource = sqlPath,
                ReadOnly = true,
                FailIfMissing = true,
            };

            using (System.Data.SQLite.SQLiteConnection connection = new System.Data.SQLite.SQLiteConnection(connectionStringBuilder.ConnectionString))
            {
                connection.Open();

                // Environment filter: sum weather-run rows only. Falls back to unfiltered when
                // no weather-run environment row exists (defensive — E+ always writes one).
                bool filterEnvironment = false;
                using (System.Data.SQLite.SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'EnvironmentPeriods'";
                    if ((long)command.ExecuteScalar() > 0)
                    {
                        command.CommandText = "SELECT COUNT(*) FROM EnvironmentPeriods WHERE EnvironmentType = 3";
                        filterEnvironment = (long)command.ExecuteScalar() > 0;
                    }
                }

                List<string> keys = new List<string>();
                using (System.Data.SQLite.SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT DISTINCT rdd.KeyValue FROM ReportDataDictionary rdd WHERE rdd.Name = @name";
                    command.Parameters.AddWithValue("@name", variableName);
                    using (System.Data.SQLite.SQLiteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            keys.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
                        }
                    }
                }

                Dictionary<string, double> result = new Dictionary<string, double>();
                foreach (string key in keys)
                {
                    if (key == null)
                    {
                        continue;
                    }

                    using (System.Data.SQLite.SQLiteCommand command = connection.CreateCommand())
                    {
                        command.CommandText = filterEnvironment
                            ? "SELECT SUM(rd.Value) FROM ReportData rd JOIN ReportDataDictionary rdd ON rd.ReportDataDictionaryIndex = rdd.ReportDataDictionaryIndex JOIN Time t ON rd.TimeIndex = t.TimeIndex WHERE rdd.Name = @name AND rdd.KeyValue = @key AND t.EnvironmentPeriodIndex IN (SELECT EnvironmentPeriodIndex FROM EnvironmentPeriods WHERE EnvironmentType = 3)"
                            : "SELECT SUM(rd.Value) FROM ReportData rd JOIN ReportDataDictionary rdd ON rd.ReportDataDictionaryIndex = rdd.ReportDataDictionaryIndex WHERE rdd.Name = @name AND rdd.KeyValue = @key";
                        command.Parameters.AddWithValue("@name", variableName);
                        command.Parameters.AddWithValue("@key", key);

                        object value = command.ExecuteScalar();
                        if (value == null || value == System.DBNull.Value)
                        {
                            continue;
                        }

                        result[key] = Core.OpenStudio.Query.JoulesToKilowattHours(System.Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture));
                    }
                }

                return result;
            }
        }

        private static string Tail(string value, int length)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            value = value.Replace("\r", string.Empty).Replace('\n', ' ');
            return value.Length <= length ? value : value.Substring(value.Length - length);
        }
    }
}
