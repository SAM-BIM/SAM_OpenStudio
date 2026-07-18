// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace SAM.Analytical.OpenStudio
{
    /// <summary>
    /// Saves the converted model (OSM), generates a minimal OSW workflow, executes the OpenStudio
    /// CLI (discovered per plan §11.2), parses eplusout.err, discovers the SQLite results file and
    /// extracts annual Ideal Loads energy through the SQL layer. Requires no Rhino/Grasshopper.
    /// </summary>
    public static class OpenStudioSimulationRunner
    {
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
        /// <returns>Result snapshot with paths, run outcome and loads.</returns>
        public static OpenStudioConversionResult Run(OpenStudioConversionContext openStudioConversionContext, string epwPath, string outputDirectory, Core.OpenStudio.OpenStudioRunOptions openStudioRunOptions = null, bool execute = true)
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

            Directory.CreateDirectory(directory);

            string modelName = Core.OpenStudio.Query.SanitizeName(openStudioConversionContext.Source?.Name);
            if (string.IsNullOrWhiteSpace(modelName))
            {
                modelName = "SAM_Model";
            }

            string osmPath = Path.Combine(directory, modelName + ".osm");
            string oswPath = Path.Combine(directory, modelName + ".osw");

            if (!openStudioConversionContext.Target.save(global::OpenStudio.OpenStudioUtilitiesCore.toPath(osmPath), true))
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("The OSM could not be saved to {0}", osmPath));
                return new OpenStudioConversionResult(openStudioConversionContext);
            }

            File.WriteAllText(oswPath, string.Format("{{\n  \"seed_file\": \"{0}\",\n  \"weather_file\": \"{1}\",\n  \"steps\": []\n}}\n", Path.GetFileName(osmPath), (epwPath ?? string.Empty).Replace('\\', '/')));

            OpenStudioConversionResult result;

            if (!execute)
            {
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

            int exitCode;
            string output = ExecuteCli(cliPath, oswPath, directory, runOptions.TimeoutSeconds, out exitCode);

            string runDirectory = Path.Combine(directory, "run");
            string errorFilePath = Path.Combine(runDirectory, "eplusout.err");
            string sqlPath = Path.Combine(runDirectory, "eplusout.sql");

            List<string> severeErrors = new List<string>();
            List<string> fatalErrors = new List<string>();
            if (File.Exists(errorFilePath))
            {
                foreach (string line in File.ReadAllLines(errorFilePath))
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

            bool sqlExists = File.Exists(sqlPath);
            bool success = exitCode == 0 && fatalErrors.Count == 0 && sqlExists;

            if (!success)
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
            if (success)
            {
                loadSummary = ExtractLoads(sqlPath);
                if (loadSummary == null)
                {
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "Ideal Loads results could not be extracted from the SQLite output");
                    success = false;
                }
            }

            result = new OpenStudioConversionResult(openStudioConversionContext);
            result.OsmPath = osmPath;
            result.OswPath = oswPath;
            result.RunResult = new Core.OpenStudio.OpenStudioRunResult(success, exitCode, osmPath, oswPath, sqlExists ? sqlPath : null, File.Exists(errorFilePath) ? errorFilePath : null, severeErrors, fatalErrors);
            result.Loads = loadSummary;
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
        /// <returns>The run result (never null).</returns>
        public static Core.OpenStudio.OpenStudioRunResult Run(string path, string epwPath, string outputDirectory, Core.OpenStudio.OpenStudioRunOptions openStudioRunOptions, out OpenStudioLoadSummary loadSummary, IList<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = null)
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
            int exitCode;
            string output = ExecuteCli(cliPath, oswPath, workingDirectory, runOptions.TimeoutSeconds, out exitCode);

            string runDirectory = Path.Combine(workingDirectory, "run");
            string errorFilePath = Path.Combine(runDirectory, "eplusout.err");
            string sqlPath = Path.Combine(runDirectory, "eplusout.sql");

            List<string> severeErrors = new List<string>();
            List<string> fatalErrors = new List<string>();
            if (File.Exists(errorFilePath))
            {
                foreach (string line in File.ReadAllLines(errorFilePath))
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

            bool sqlExists = File.Exists(sqlPath);
            bool success = exitCode == 0 && fatalErrors.Count == 0 && sqlExists;

            if (!success)
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

        private static string ExecuteCli(string cliPath, string oswPath, string workingDirectory, int timeoutSeconds, out int exitCode)
        {
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

            using (Process process = Process.Start(processStartInfo))
            {
                string standardOutput = process.StandardOutput.ReadToEnd();
                string standardError = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(timeoutSeconds <= 0 ? 3600000 : timeoutSeconds * 1000))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (System.Exception)
                    {
                        // the process may have exited between the timeout and the kill
                    }

                    exitCode = -1;
                    return "TIMEOUT after " + timeoutSeconds + " s\n" + standardOutput + standardError;
                }

                exitCode = process.ExitCode;
                return standardOutput + standardError;
            }
        }

        private static OpenStudioLoadSummary ExtractLoads(string sqlPath)
        {
            global::OpenStudio.SqlFile sqlFile = null;
            try
            {
                sqlFile = new global::OpenStudio.SqlFile(global::OpenStudio.OpenStudioUtilitiesCore.toPath(sqlPath));

                Dictionary<string, double> zoneHeating = ReadAnnualEnergy(sqlFile, "Zone Ideal Loads Supply Air Total Heating Energy");
                Dictionary<string, double> zoneCooling = ReadAnnualEnergy(sqlFile, "Zone Ideal Loads Supply Air Total Cooling Energy");
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
            finally
            {
                sqlFile?.Dispose();
            }
        }

        private static Dictionary<string, double> ReadAnnualEnergy(global::OpenStudio.SqlFile sqlFile, string variableName)
        {
            global::OpenStudio.OptionalStringVector optionalKeys = sqlFile.execAndReturnVectorOfString(string.Format("SELECT DISTINCT rdd.KeyValue FROM ReportDataDictionary rdd WHERE rdd.Name = '{0}'", variableName));
            if (optionalKeys == null || optionalKeys.isNull())
            {
                return null;
            }

            Dictionary<string, double> result = new Dictionary<string, double>();
            foreach (string key in optionalKeys.get())
            {
                global::OpenStudio.OptionalDouble joules = sqlFile.execAndReturnFirstDouble(string.Format("SELECT SUM(rd.Value) FROM ReportData rd JOIN ReportDataDictionary rdd ON rd.ReportDataDictionaryIndex = rdd.ReportDataDictionaryIndex WHERE rdd.Name = '{0}' AND rdd.KeyValue = '{1}'", variableName, key));
                if (joules == null || joules.isNull())
                {
                    continue;
                }

                result[key] = joules.get() / 3600000.0;
            }

            return result;
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
