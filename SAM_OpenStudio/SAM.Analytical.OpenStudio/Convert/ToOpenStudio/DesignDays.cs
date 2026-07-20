// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// The ASHRAE annual heating design-day name: "… Ann Htg 99.6% Condns DB". Requiring
        /// "Htg" directly before "99.6%" excludes the wind ("Ann Htg Wind 99.6% Condns
        /// WS=>MCDB") and humidification ("Ann Hum_n 99.6% Condns DP=>MCDB") days that share
        /// the 99.6% figure (review P1-02).
        /// </summary>
        private static readonly Regex HeatingDesignDayName = new Regex(@"Ann\s+Htg\s+99\.6\s*%\s+Condns\s+DB", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// The ASHRAE annual cooling design-day name: "… Ann Clg .4% Condns DB=>MWB" — written
        /// WITHOUT a leading zero in ASHRAE/climate.onebuilding DDY files (accepted with one
        /// too), MWB/MCWB variants both accepted. Restricting to the DB=>M(C)WB condition keeps
        /// the WB=>MDB / DP=>MDB / Enth=>MDB variants and the monthly ".4%" days out.
        /// </summary>
        private static readonly Regex CoolingDesignDayName = new Regex(@"Ann\s+Clg\s+0?\.4\s*%\s+Condns\s+DB\s*=>\s*M(C)?WB", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Imports design days from a DDY file into the target model (C4). By default exactly
        /// the ASHRAE annual pair is kept: the heating 99.6% dry-bulb day and the cooling .4%
        /// DB=>MWB day (review P1-02 — the humidification and wind 99.6% days never match).
        /// A missing side raises a warning; when neither side matches, the import falls back to
        /// all days with a warning. OpenStudioConversionOptions.ImportAllDesignDays imports
        /// every design day. Sets OpenStudioConversionContext.DesignDaysImported (drives
        /// sizing-period enablement).
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
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("DDY file contains no importable design days: {0}", ddyPath));
                return 0;
            }

            List<global::OpenStudio.DesignDay> selected = designDays;
            if (!openStudioConversionContext.Options.ImportAllDesignDays)
            {
                List<global::OpenStudio.DesignDay> heatingDays = designDays.FindAll(x => HeatingDesignDayName.IsMatch(x.nameString()));
                List<global::OpenStudio.DesignDay> coolingDays = designDays.FindAll(x => CoolingDesignDayName.IsMatch(x.nameString()));

                if (heatingDays.Count == 0 && coolingDays.Count == 0)
                {
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "No annual heating 99.6% / cooling .4% design days found by name convention; all DDY design days were imported");
                }
                else
                {
                    if (heatingDays.Count == 0)
                    {
                        openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "No annual heating 99.6% dry-bulb design day found by name convention — heating sizing has no design day");
                    }

                    if (coolingDays.Count == 0)
                    {
                        openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "No annual cooling .4% DB=>MWB design day found by name convention — cooling sizing has no design day");
                    }

                    selected = new List<global::OpenStudio.DesignDay>();
                    selected.AddRange(heatingDays);
                    selected.AddRange(coolingDays);
                }
            }

            foreach (global::OpenStudio.DesignDay designDay in selected)
            {
                designDay.clone(openStudioConversionContext.Target);
            }

            openStudioConversionContext.DesignDaysImported = selected.Count > 0;
            openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format("Imported {0} design day(s) from {1}", selected.Count, Path.GetFileName(ddyPath)));
            return selected.Count;
        }
    }
}
