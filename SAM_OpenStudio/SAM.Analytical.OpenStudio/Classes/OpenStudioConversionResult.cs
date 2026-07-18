// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;

namespace SAM.Analytical.OpenStudio
{
    /// <summary>
    /// Immutable snapshot of a completed SAM → OpenStudio conversion: the produced model, file
    /// paths, run outcome, diagnostics and the SAM Guid → OpenStudio name map. Snapshotting means
    /// later mutation of the conversion context does not affect an already created result.
    /// </summary>
    public sealed class OpenStudioConversionResult
    {
        /// <summary>The produced OpenStudio model (null when conversion could not start).</summary>
        public global::OpenStudio.Model Model { get; }

        /// <summary>Path of the saved OSM file, when saved.</summary>
        public string OsmPath { get; internal set; }

        /// <summary>Path of the generated OSW workflow file, when generated.</summary>
        public string OswPath { get; internal set; }

        /// <summary>Outcome of the simulation run, when one was executed.</summary>
        public Core.OpenStudio.OpenStudioRunResult RunResult { get; internal set; }

        /// <summary>Diagnostics recorded up to the moment this result was created.</summary>
        public IReadOnlyList<Core.OpenStudio.OpenStudioDiagnostic> Diagnostics { get; }

        /// <summary>SAM Guid → deterministic OpenStudio object name.</summary>
        public IReadOnlyDictionary<Guid, string> ObjectMap { get; }

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
            ObjectMap = openStudioConversionContext.References.ToNameDictionary();
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
