// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;

namespace SAM.Analytical.OpenStudio
{
    /// <summary>
    /// Immutable snapshot of a completed SAM → OpenStudio conversion: the produced model, file
    /// paths, run outcome, diagnostics, translation statistics and the SAM Guid → OpenStudio
    /// name map. Snapshotting means later mutation of the conversion context does not affect an
    /// already created result. The result OWNS the OpenStudio model: dispose the result when the
    /// model is no longer needed (native SWIG resources are released); do not use the model
    /// afterwards.
    /// </summary>
    public sealed class OpenStudioConversionResult : IDisposable
    {
        /// <summary>The produced OpenStudio model (null when conversion could not start). Owned by this result — disposed with it.</summary>
        public global::OpenStudio.Model Model { get; }

        /// <summary>Path of the saved OSM file, when saved.</summary>
        public string OsmPath { get; internal set; }

        /// <summary>Path of the generated OSW workflow file, when generated.</summary>
        public string OswPath { get; internal set; }

        /// <summary>Outcome of the simulation run, when one was executed.</summary>
        public Core.OpenStudio.OpenStudioRunResult RunResult { get; internal set; }

        /// <summary>Annual Ideal Loads energy extracted from the run, when available.</summary>
        public OpenStudioLoadSummary Loads { get; internal set; }

        /// <summary>Engine-neutral simulation result set (annual energies, peaks, unmet hours, gains, optional series), when extracted.</summary>
        public OpenStudioSimulationResultSet Results { get; internal set; }

        /// <summary>Diagnostics recorded up to the moment this result was created.</summary>
        public IReadOnlyList<Core.OpenStudio.OpenStudioDiagnostic> Diagnostics { get; }

        /// <summary>Translation statistics snapshot (source/created/skipped/unsupported counts).</summary>
        public Core.OpenStudio.OpenStudioConversionStatistics Statistics { get; }

        /// <summary>SAM Guid → deterministic OpenStudio object name.</summary>
        public IReadOnlyDictionary<Guid, string> ObjectMap { get; }

        /// <summary>
        /// The design-day basis the conversion actually established (explicit DDY, the design days
        /// embedded in the AnalyticalModel, or none). Snapshotted from the context, so provenance
        /// reporting records what the route did rather than what was supplied to it.
        /// </summary>
        public Core.OpenStudio.OpenStudioDesignDaySource DesignDaySource { get; }

        /// <summary>Creates a result snapshot from a conversion context.</summary>
        /// <param name="openStudioConversionContext">Context to snapshot; required.</param>
        public OpenStudioConversionResult(OpenStudioConversionContext openStudioConversionContext)
        {
            if (openStudioConversionContext == null)
            {
                throw new ArgumentNullException(nameof(openStudioConversionContext));
            }

            Model = openStudioConversionContext.Target;
            Diagnostics = new List<Core.OpenStudio.OpenStudioDiagnostic>(openStudioConversionContext.Diagnostics);
            Statistics = openStudioConversionContext.Statistics.Snapshot();
            ObjectMap = openStudioConversionContext.References.ToNameDictionary();
            DesignDaySource = openStudioConversionContext.DesignDaySource;
        }

        /// <summary>Releases the owned OpenStudio model's native resources. Idempotent.</summary>
        public void Dispose()
        {
            Model?.Dispose();
        }

        /// <summary>
        /// True when a model exists and no diagnostic has Error severity. Warnings do not
        /// invalidate a result.
        /// </summary>
        public bool IsValid
        {
            get
            {
                if (Model == null)
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
    }
}
