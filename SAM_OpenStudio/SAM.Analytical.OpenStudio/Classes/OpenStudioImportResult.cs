// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;

namespace SAM.Analytical.OpenStudio
{
    /// <summary>
    /// Immutable snapshot of a completed OpenStudio → SAM import: the produced
    /// <see cref="AnalyticalModel"/>, the source and resolved paths, diagnostics and statistics.
    /// <para>
    /// Unlike <see cref="OpenStudioConversionResult"/>, this type owns nothing native and is not
    /// disposable: any OpenStudio model loaded from disk is disposed inside the import, before
    /// the result is handed back. That is deliberate — a public result holding a live SWIG model
    /// would tie the caller's lifetime to native memory for no benefit, and a Grasshopper
    /// document would leak one model per solution.
    /// </para>
    /// </summary>
    public sealed class OpenStudioImportResult
    {
        /// <summary>The imported SAM analytical model; null when the import could not produce one.</summary>
        public AnalyticalModel AnalyticalModel { get; }

        /// <summary>The path the caller supplied (OSM or OSW); null for an in-memory model.</summary>
        public string SourcePath { get; }

        /// <summary>
        /// The OSM actually converted: the input itself for an OSM, the resolved seed for a
        /// non-executed OSW, or the final post-model-measure OSM for an executed workflow.
        /// </summary>
        public string ResolvedOsmPath { get; }

        /// <summary>OSM version recorded before version translation, when the translator reported it.</summary>
        public string OpenStudioVersion { get; }

        /// <summary>Outcome of the OSW workflow run, when one was executed.</summary>
        public Core.OpenStudio.OpenStudioRunResult RunResult { get; }

        /// <summary>Diagnostics recorded up to the moment this result was created.</summary>
        public IReadOnlyList<Core.OpenStudio.OpenStudioDiagnostic> Diagnostics { get; }

        /// <summary>Import statistics snapshot.</summary>
        public Core.OpenStudio.OpenStudioConversionStatistics Statistics { get; }

        /// <summary>Creates a result snapshot from an import context.</summary>
        /// <param name="openStudioImportContext">Context to snapshot; may be null when the import failed before a model was loaded.</param>
        /// <param name="analyticalModel">The imported SAM model, when one was produced.</param>
        /// <param name="sourcePath">Caller-supplied path.</param>
        /// <param name="resolvedOsmPath">The OSM actually converted.</param>
        /// <param name="openStudioVersion">Pre-translation OSM version, when known.</param>
        /// <param name="runResult">Workflow run outcome, when one was executed.</param>
        public OpenStudioImportResult(OpenStudioImportContext openStudioImportContext, AnalyticalModel analyticalModel, string sourcePath = null, string resolvedOsmPath = null, string openStudioVersion = null, Core.OpenStudio.OpenStudioRunResult runResult = null)
        {
            AnalyticalModel = analyticalModel;
            SourcePath = sourcePath;
            ResolvedOsmPath = resolvedOsmPath;
            OpenStudioVersion = openStudioVersion;
            RunResult = runResult;
            Diagnostics = openStudioImportContext == null
                ? new List<Core.OpenStudio.OpenStudioDiagnostic>()
                : new List<Core.OpenStudio.OpenStudioDiagnostic>(openStudioImportContext.Diagnostics);
            Statistics = openStudioImportContext == null
                ? new Core.OpenStudio.OpenStudioConversionStatistics()
                : openStudioImportContext.Statistics.Snapshot();
        }

        /// <summary>
        /// Creates a failure snapshot carrying diagnostics only — used when the import stops
        /// before an OpenStudio model exists (bad path, unsupported extension, OSM load failure,
        /// OSW without a seed).
        /// </summary>
        /// <param name="diagnostics">Diagnostics collected so far; may be null.</param>
        /// <param name="sourcePath">Caller-supplied path.</param>
        /// <param name="resolvedOsmPath">The OSM that was attempted, when one was identified.</param>
        /// <param name="runResult">Workflow run outcome, when one was executed.</param>
        public OpenStudioImportResult(IEnumerable<Core.OpenStudio.OpenStudioDiagnostic> diagnostics, string sourcePath = null, string resolvedOsmPath = null, Core.OpenStudio.OpenStudioRunResult runResult = null)
        {
            AnalyticalModel = null;
            SourcePath = sourcePath;
            ResolvedOsmPath = resolvedOsmPath;
            RunResult = runResult;

            List<Core.OpenStudio.OpenStudioDiagnostic> list = new List<Core.OpenStudio.OpenStudioDiagnostic>();
            if (diagnostics != null)
            {
                list.AddRange(diagnostics);
            }

            Diagnostics = list;

            Core.OpenStudio.OpenStudioConversionStatistics statistics = new Core.OpenStudio.OpenStudioConversionStatistics();
            foreach (Core.OpenStudio.OpenStudioDiagnostic openStudioDiagnostic in list)
            {
                switch (openStudioDiagnostic?.Severity)
                {
                    case Core.OpenStudio.OpenStudioDiagnosticSeverity.Information:
                        statistics.InformationCount++;
                        break;

                    case Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning:
                        statistics.WarningCount++;
                        break;

                    case Core.OpenStudio.OpenStudioDiagnosticSeverity.Error:
                        statistics.ErrorCount++;
                        break;
                }
            }

            Statistics = statistics;
        }

        /// <summary>
        /// True when a model was produced and no diagnostic has Error severity. Warnings — an
        /// unpaired surface, an approximated material, a workflow that was not executed — do not
        /// invalidate a result.
        /// </summary>
        public bool IsValid
        {
            get
            {
                if (AnalyticalModel == null)
                {
                    return false;
                }

                foreach (Core.OpenStudio.OpenStudioDiagnostic openStudioDiagnostic in Diagnostics)
                {
                    if (openStudioDiagnostic != null && openStudioDiagnostic.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// True when a model was produced at all, regardless of severity. An import that
        /// recovered from errors still returns a usable — if incomplete — model, and the caller
        /// must be able to tell that apart from having nothing.
        /// </summary>
        public bool Successful
        {
            get { return AnalyticalModel != null; }
        }
    }
}
