// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;

namespace SAM.Core.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Keeps the records the shared <see cref="Diagnostics"/> rules keep at <paramref name="detail"/>: Information above
        /// the level is dropped, Warnings and Errors are always kept. The original records are returned in their original
        /// order, so the legacy text is untouched.
        /// </summary>
        public static List<OpenStudioDiagnosticRecord> FilterDiagnosticRecords(this IEnumerable<OpenStudioDiagnosticRecord> openStudioDiagnosticRecords, DiagnosticDetail detail)
        {
            List<OpenStudioDiagnosticRecord> result = new List<OpenStudioDiagnosticRecord>();
            if (openStudioDiagnosticRecords == null)
            {
                return result;
            }

            Diagnostics diagnostics = new Diagnostics(detail);
            foreach (OpenStudioDiagnosticRecord openStudioDiagnosticRecord in openStudioDiagnosticRecords)
            {
                if (openStudioDiagnosticRecord?.Diagnostic != null && diagnostics.Add(openStudioDiagnosticRecord.Diagnostic))
                {
                    result.Add(openStudioDiagnosticRecord);
                }
            }

            return result;
        }
    }
}
