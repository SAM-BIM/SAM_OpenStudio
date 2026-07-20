// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using System.IO;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Imports design days from a DDY file into the target model (C4). By default only the
        /// heating 99.6% / cooling 0.4% design days are kept (ASHRAE name convention);
        /// OpenStudioConversionOptions.ImportAllDesignDays imports every design day. When the
        /// name filter matches nothing the import falls back to all days with a warning. Sets
        /// OpenStudioConversionContext.DesignDaysImported (drives sizing-period enablement).
        /// </summary>
        /// <param name="openStudioConversionContext">Conversion context.</param>
        /// <param name="ddyPath">DDY file path; null/empty or unreadable → no import (warning when a path was given).</param>
        /// <returns>Number of imported design days.</returns>
        public static int ToOpenStudio_DesignDays(this OpenStudioConversionContext openStudioConversionContext, string ddyPath)
        {
            if (openStudioConversionContext == null)
            {
                return 0;
            }

            if (string.IsNullOrWhiteSpace(ddyPath))
            {
                return 0;
            }

            List<global::OpenStudio.DesignDay> designDays = Query.DesignDays(ddyPath);
            if (designDays == null || designDays.Count == 0)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("DDY file contains no importable design days: {0}", ddyPath));
                return 0;
            }

            List<global::OpenStudio.DesignDay> selected = designDays;
            if (!openStudioConversionContext.Options.ImportAllDesignDays)
            {
                selected = designDays.FindAll(x =>
                {
                    string name = x.nameString();
                    return name.Contains("99.6%") || name.Contains("0.4%");
                });

                if (selected.Count == 0)
                {
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "No heating 99.6% / cooling 0.4% design days found by name convention; all DDY design days were imported");
                    selected = designDays;
                }
            }

            foreach (global::OpenStudio.DesignDay designDay in selected)
            {
                designDay.clone(openStudioConversionContext.Target);
            }

            openStudioConversionContext.DesignDaysImported = selected.Count > 0;
            openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format("Imported {0} design day(s) from {1}", selected.Count, Path.GetFileName(ddyPath)));
            return selected.Count;
        }
    }
}
