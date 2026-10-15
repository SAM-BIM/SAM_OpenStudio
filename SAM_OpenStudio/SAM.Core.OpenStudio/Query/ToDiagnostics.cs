// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;

namespace SAM.Core.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Collects the diagnostics into a shared <see cref="Diagnostics"/> collection at <paramref name="detail"/>
        /// (default <see cref="DiagnosticDetail.Detailed"/>, which keeps everything and can be re-filtered later with
        /// <see cref="Diagnostics.Filter"/>). Warnings and Errors are never dropped. Null entries are skipped.
        /// </summary>
        public static Diagnostics ToDiagnostics(this IEnumerable<OpenStudioDiagnostic> openStudioDiagnostics, DiagnosticDetail detail = DiagnosticDetail.Detailed)
        {
            Diagnostics result = new Diagnostics(detail);
            if (openStudioDiagnostics == null)
            {
                return result;
            }

            int index = 0;
            foreach (OpenStudioDiagnostic openStudioDiagnostic in openStudioDiagnostics)
            {
                if (openStudioDiagnostic != null)
                {
                    result.Add(openStudioDiagnostic.ToDiagnostic(index));
                }

                index++;
            }

            return result;
        }
    }
}
