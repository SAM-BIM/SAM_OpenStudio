// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Imports an OSM or OSW file into a SAM <see cref="AnalyticalModel"/>.
        /// <para>
        /// The loaded OpenStudio model is disposed before this method returns: the result carries
        /// SAM objects and diagnostics only, never a live native model.
        /// </para>
        /// <para>
        /// For an <b>OSM</b> the file is loaded through the existing VersionTranslator path and
        /// converted directly — the OpenStudio CLI is never invoked, and no EPW is needed to
        /// convert geometry and analytical data.
        /// </para>
        /// <para>
        /// For an <b>OSW</b> the behaviour depends on
        /// <see cref="Core.OpenStudio.OpenStudioImportOptions.ExecuteWorkflow"/>; see
        /// <see cref="ToSAM_Osw"/>. An OSW is a workflow description, so parsing it is never
        /// treated as equivalent to importing the model it would produce.
        /// </para>
        /// </summary>
        /// <param name="path">OSM or OSW file path.</param>
        /// <param name="openStudioImportOptions">Import options; defaults when null.</param>
        /// <param name="openStudioRunOptions">Run options for OSW execution (CLI path, timeout); defaults when null.</param>
        /// <param name="progress">Optional stage progress sink, used during workflow execution.</param>
        /// <param name="cancellationToken">Cancellation; terminates the CLI/EnergyPlus process tree.</param>
        /// <returns>The import result; never null.</returns>
        public static OpenStudioImportResult ToSAM(string path, Core.OpenStudio.OpenStudioImportOptions openStudioImportOptions = null, Core.OpenStudio.OpenStudioRunOptions openStudioRunOptions = null, IProgress<Core.OpenStudio.OpenStudioSimulationProgress> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            Core.OpenStudio.OpenStudioImportOptions options = openStudioImportOptions ?? new Core.OpenStudio.OpenStudioImportOptions();
            List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = new List<Core.OpenStudio.OpenStudioDiagnostic>();

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                diagnostics.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.InputPathInvalid, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("The input path is missing or does not exist: {0}", path)));
                return new OpenStudioImportResult(diagnostics, path);
            }

            string extension = Path.GetExtension(path);

            if (string.Equals(extension, ".osm", StringComparison.OrdinalIgnoreCase))
            {
                return ToSAM_Osm(path, path, options, diagnostics);
            }

            if (string.Equals(extension, ".osw", StringComparison.OrdinalIgnoreCase))
            {
                return ToSAM_Osw(path, options, openStudioRunOptions, diagnostics, progress, cancellationToken);
            }

            diagnostics.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.InputExtensionUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Unsupported file extension '{0}'; expected .osm or .osw", extension)));
            return new OpenStudioImportResult(diagnostics, path);
        }

        /// <summary>
        /// Asynchronous wrapper over
        /// <see cref="ToSAM(string, Core.OpenStudio.OpenStudioImportOptions, Core.OpenStudio.OpenStudioRunOptions, IProgress{Core.OpenStudio.OpenStudioSimulationProgress}, CancellationToken)"/>:
        /// the whole import runs on a worker thread. Worth having even for a plain OSM — a large
        /// model takes seconds to convert — and essential for an executed workflow, which takes
        /// minutes.
        /// </summary>
        public static Task<OpenStudioImportResult> ToSAMAsync(string path, Core.OpenStudio.OpenStudioImportOptions openStudioImportOptions = null, Core.OpenStudio.OpenStudioRunOptions openStudioRunOptions = null, IProgress<Core.OpenStudio.OpenStudioSimulationProgress> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.Run(() => ToSAM(path, openStudioImportOptions, openStudioRunOptions, progress, cancellationToken), cancellationToken);
        }

        /// <summary>
        /// Loads an OSM and converts it, disposing the native model deterministically afterwards.
        /// An unloadable OSM produces a blocking SAM-OSI-OSM-001 — never an empty
        /// <see cref="AnalyticalModel"/> that would look like a building with nothing in it.
        /// </summary>
        /// <param name="osmPath">OSM to load.</param>
        /// <param name="sourcePath">Path the caller supplied (the OSW for a workflow import).</param>
        /// <param name="options">Import options.</param>
        /// <param name="diagnostics">Diagnostics accumulated before the model was loaded.</param>
        private static OpenStudioImportResult ToSAM_Osm(string osmPath, string sourcePath, Core.OpenStudio.OpenStudioImportOptions options, List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics)
        {
            string originalVersion;
            List<string> translationMessages;
            string failureReason;

            global::OpenStudio.Model model = Core.OpenStudio.Create.Model(osmPath, out originalVersion, out translationMessages, out failureReason);

            if (model == null)
            {
                diagnostics.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.OsmLoadFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("The OSM could not be loaded: {0} ({1})", failureReason, osmPath)));
                return new OpenStudioImportResult(diagnostics, sourcePath, osmPath);
            }

            try
            {
                OpenStudioImportContext context = new OpenStudioImportContext(model, options);
                context.SourceDirectory = Path.GetDirectoryName(Path.GetFullPath(osmPath));

                foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in diagnostics)
                {
                    context.Diagnostics.Add(diagnostic);
                }

                if (!string.IsNullOrWhiteSpace(originalVersion))
                {
                    context.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.OsmVersionTranslationFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format("The OSM was written by OpenStudio {0} and translated on load", originalVersion), (string)null);
                }

                // Translator warnings are surfaced even on a successful load: a model that needed
                // repair to open is a model whose imported content deserves a second look.
                foreach (string translationMessage in translationMessages)
                {
                    context.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.OsmVersionTranslationFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("The version translator reported: {0}", translationMessage), (string)null);
                }

                AnalyticalModel analyticalModel = ToSAM_AnalyticalModel(context);

                if (analyticalModel != null)
                {
                    analyticalModel.SetValue(OpenStudioSourceParameter.SourcePath, osmPath);
                    if (!string.IsNullOrWhiteSpace(originalVersion))
                    {
                        analyticalModel.SetValue(OpenStudioSourceParameter.OpenStudioVersion, originalVersion);
                    }
                }

                return new OpenStudioImportResult(context, analyticalModel, sourcePath, osmPath, originalVersion);
            }
            finally
            {
                // Deterministic disposal: the result never exposes a live native model, so this
                // is the last point at which the SWIG wrapper can be released.
                model.Dispose();
            }
        }

        /// <summary>
        /// Imports an OSW.
        /// <para>
        /// <b>Execution disabled</b> (the default): the seed model is resolved and imported, with
        /// SAM-OSI-OSW-004 stating that the workflow's measures were NOT applied — the imported
        /// model is the seed, not the workflow's output. With no seed, SAM-OSI-OSW-002 blocks:
        /// there is nothing to import.
        /// </para>
        /// <para>
        /// <b>Execution enabled</b>: the workflow runs verbatim through the existing CLI runner
        /// (its steps and arguments untouched) in an isolated run directory, and the final
        /// post-model-measure OSM is located deterministically and converted. This also covers a
        /// seedless workflow whose model-creation measures build the model.
        /// </para>
        /// </summary>
        private static OpenStudioImportResult ToSAM_Osw(string oswPath, Core.OpenStudio.OpenStudioImportOptions options, Core.OpenStudio.OpenStudioRunOptions openStudioRunOptions, List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics, IProgress<Core.OpenStudio.OpenStudioSimulationProgress> progress, CancellationToken cancellationToken)
        {
            string failureReason;
            OpenStudioWorkflow workflow = OpenStudioWorkflow.Parse(oswPath, out failureReason);
            if (workflow == null)
            {
                diagnostics.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.OswParseFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("The OSW could not be parsed: {0} ({1})", failureReason, oswPath)));
                return new OpenStudioImportResult(diagnostics, oswPath);
            }

            List<string> searchedPaths;
            string seedPath = workflow.ResolveSeedPath(out searchedPaths);

            if (string.IsNullOrWhiteSpace(workflow.SeedFile))
            {
                if (!options.ExecuteWorkflow)
                {
                    diagnostics.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.OswSeedMissing, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "The OSW declares no seed_file and workflow execution is disabled, so no OpenStudio model exists to import. Enable workflow execution to let the workflow's model-creation measures build one."));
                    return new OpenStudioImportResult(diagnostics, oswPath);
                }
            }
            else if (seedPath == null)
            {
                string message = string.Format("The OSW seed_file '{0}' could not be found. Searched: {1}", workflow.SeedFile, string.Join("; ", searchedPaths));

                if (!options.ExecuteWorkflow)
                {
                    diagnostics.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.OswSeedNotFound, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, message));
                    return new OpenStudioImportResult(diagnostics, oswPath);
                }

                diagnostics.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.OswSeedNotFound, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, message + " The workflow will still be executed; it must create the model itself."));
            }

            if (!options.ExecuteWorkflow)
            {
                diagnostics.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.OswWorkflowNotExecuted, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("The workflow was NOT executed: the imported model is the seed OSM, without the {0} workflow step(s) applied. Enable workflow execution to import the model the workflow actually produces.", workflow.StepMeasureNames.Count)));
                return ToSAM_Osm(seedPath, oswPath, options, diagnostics);
            }

            return ToSAM_ExecutedOsw(workflow, seedPath, options, openStudioRunOptions, diagnostics, progress, cancellationToken);
        }

        /// <summary>
        /// Runs the workflow and imports the model it produced. Reuses the existing CLI
        /// discovery, asynchronous execution, timeout, cancellation and process-tree termination
        /// in <see cref="OpenStudioSimulationRunner"/> — nothing about running OpenStudio is
        /// reimplemented here.
        /// </summary>
        private static OpenStudioImportResult ToSAM_ExecutedOsw(OpenStudioWorkflow workflow, string seedPath, Core.OpenStudio.OpenStudioImportOptions options, Core.OpenStudio.OpenStudioRunOptions openStudioRunOptions, List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics, IProgress<Core.OpenStudio.OpenStudioSimulationProgress> progress, CancellationToken cancellationToken)
        {
            string outputDirectory = options.OutputDirectory;
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                outputDirectory = Path.GetDirectoryName(workflow.Path);
            }

            // A per-import directory, always: the CLI writes its run folder beside the workflow
            // file, so two parallel imports of one OSW would otherwise share a run directory and
            // overwrite each other's output.
            string runDirectory = Path.Combine(outputDirectory, "SAM_OpenStudio_Import_" + Guid.NewGuid().ToString("N").Substring(0, 8));

            string isolatedOswPath;
            try
            {
                isolatedOswPath = workflow.WriteIsolatedCopy(runDirectory, seedPath);
            }
            catch (Exception exception)
            {
                diagnostics.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.OswExecutionFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("The workflow could not be staged into an isolated run directory ({0}: {1})", exception.GetType().Name, exception.Message)));
                return new OpenStudioImportResult(diagnostics, workflow.Path, seedPath);
            }

            if (workflow.MayContainEnergyPlusMeasures)
            {
                diagnostics.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.OswEnergyPlusMeasureNotRepresentable, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format("The workflow has {0} step(s). Any EnergyPlus measure among them modifies only the generated IDF, not the OpenStudio model, so its changes cannot appear in the imported SAM model: what is imported is the final OpenStudio model after the model measures.", workflow.StepMeasureNames.Count)));
            }

            DateTime runStartedUtc = DateTime.UtcNow;

            Core.OpenStudio.OpenStudioRunOptions runOptions = openStudioRunOptions ?? new Core.OpenStudio.OpenStudioRunOptions();

            progress?.Report(new Core.OpenStudio.OpenStudioSimulationProgress(Core.OpenStudio.OpenStudioSimulationStage.RunningCli, isolatedOswPath));

            // The shared runner's diagnostics are collected separately, not straight into the
            // import's list. It judges a run by whether EnergyPlus produced results, which is the
            // right test for a simulation and the wrong one for an import: an import needs the
            // final OpenStudio model and nothing else. Its verdict is re-badged below once we
            // know whether a model was actually produced.
            List<Core.OpenStudio.OpenStudioDiagnostic> runDiagnostics = new List<Core.OpenStudio.OpenStudioDiagnostic>();

            OpenStudioLoadSummary loadSummary;
            Core.OpenStudio.OpenStudioRunResult runResult = OpenStudioSimulationRunner.Run(isolatedOswPath, null, runDirectory, runOptions, out loadSummary, runDiagnostics, cancellationToken);

            string how;
            string finalOsmPath = OpenStudioWorkflow.FindFinalOsm(runDirectory, runStartedUtc, out how);

            AppendRunDiagnostics(diagnostics, runDiagnostics, runResult, finalOsmPath != null);

            if (finalOsmPath == null)
            {
                // Distinguish "the run failed" from "the run succeeded but produced no model":
                // they need different fixes, and collapsing them wastes the reader's time.
                diagnostics.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.OswFinalOsmNotFound, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("No final OpenStudio model was produced by the workflow (run directory: {0}). {1}", runDirectory, runResult != null && runResult.Success ? "The run reported success, so the workflow itself creates no OSM." : "The run did not complete successfully - see the run diagnostics above.")));

                if (runResult != null && !runResult.Success)
                {
                    diagnostics.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.OswExecutionFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Workflow execution failed (exit code {0}); no model could be imported", runResult.ExitCode)));
                }

                return new OpenStudioImportResult(diagnostics, workflow.Path, null, runResult);
            }

            diagnostics.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.OswWorkflowExecuted, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format("The workflow was executed; the imported model is the final post-model-measure OSM, located by {0}", how)));

            OpenStudioImportResult result = ToSAM_Osm(finalOsmPath, workflow.Path, options, diagnostics);


            // Re-wrap so the workflow's run outcome travels with the imported model; the OSM
            // import already carries every diagnostic, including the run diagnostics passed into
            // it.
            return new OpenStudioImportResult(result.Diagnostics, result.AnalyticalModel, workflow.Path, finalOsmPath, result.OpenStudioVersion, runResult);
        }

        /// <summary>
        /// Folds the shared simulation runner's diagnostics into the import's list, re-badging the
        /// one verdict that does not transfer between the two jobs.
        /// <para>
        /// <see cref="OpenStudioSimulationRunner"/> calls a run failed when EnergyPlus produced no
        /// SQLite results. For a simulation that is exactly right; for an import it is not a
        /// failure at all — a workflow of model measures with no simulation step legitimately
        /// produces a model and no results. Left as an Error it would make
        /// <see cref="OpenStudioImportResult.IsValid"/> false and report a perfectly good import
        /// as failed.
        /// </para>
        /// <para>
        /// So when the workflow did produce a final model, the CLI exited cleanly and nothing was
        /// fatal, that specific verdict is downgraded to an informational
        /// <c>SAM-OSI-OSW-009</c> explaining why results are irrelevant here. Every other run
        /// diagnostic — EnergyPlus severe errors, real CLI failures, cancellations — passes
        /// through untouched at its original severity.
        /// </para>
        /// </summary>
        /// <param name="diagnostics">The import's diagnostic list.</param>
        /// <param name="runDiagnostics">Diagnostics produced by the simulation runner.</param>
        /// <param name="runResult">Run outcome, used to tell a clean run from a failed one.</param>
        /// <param name="modelProduced">True when the workflow's final OSM was located.</param>
        private static void AppendRunDiagnostics(List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics, List<Core.OpenStudio.OpenStudioDiagnostic> runDiagnostics, Core.OpenStudio.OpenStudioRunResult runResult, bool modelProduced)
        {
            // A clean exit with no fatal errors: the CLI did its job, whatever it was asked to do.
            bool cleanRun = runResult != null
                && runResult.ExitCode == 0
                && (runResult.FatalErrors == null || runResult.FatalErrors.Count == 0);

            foreach (Core.OpenStudio.OpenStudioDiagnostic runDiagnostic in runDiagnostics)
            {
                if (runDiagnostic == null)
                {
                    continue;
                }

                bool resultsOnlyFailure = modelProduced
                    && cleanRun
                    && runDiagnostic.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error
                    && runDiagnostic.Code == Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed;

                if (resultsOnlyFailure)
                {
                    diagnostics.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.OswNoSimulationResults, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format("The workflow completed (exit code 0, no fatal errors) and produced the model that was imported, but no EnergyPlus simulation results - an import needs the OpenStudio model, not the results, so this does not affect the imported model. The simulation runner reported: {0}", runDiagnostic.Message)));
                    continue;
                }

                diagnostics.Add(runDiagnostic);
            }
        }
    }
}
