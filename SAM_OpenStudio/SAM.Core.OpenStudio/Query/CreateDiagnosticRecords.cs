// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;

namespace SAM.Core.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Pairs every non-null diagnostic with its structured <see cref="Diagnostic"/>, in the original order.
        /// The source index is the position in <paramref name="openStudioDiagnostics"/>. Null entries are skipped.
        /// </summary>
        public static List<OpenStudioDiagnosticRecord> CreateDiagnosticRecords(this IEnumerable<OpenStudioDiagnostic> openStudioDiagnostics)
        {
            List<OpenStudioDiagnosticRecord> result = new List<OpenStudioDiagnosticRecord>();
            if (openStudioDiagnostics == null)
            {
                return result;
            }

            int index = 0;
            foreach (OpenStudioDiagnostic openStudioDiagnostic in openStudioDiagnostics)
            {
                if (openStudioDiagnostic != null)
                {
                    result.Add(new OpenStudioDiagnosticRecord(openStudioDiagnostic, openStudioDiagnostic.ToDiagnostic(index)));
                }

                index++;
            }

            return result;
        }
    }
}
