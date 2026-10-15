// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;

namespace SAM.Core.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// The legacy diagnostic lines (<see cref="OpenStudioDiagnostic.ToString"/>) kept at <paramref name="detail"/>.
        /// At <see cref="DiagnosticDetail.Detailed"/> (default) this is exactly the list the Grasshopper <c>diagnostics</c>
        /// outputs have always shown, in the same order. Null entries are skipped.
        /// </summary>
        public static List<string> FormatDiagnostics(this IEnumerable<OpenStudioDiagnostic> openStudioDiagnostics, DiagnosticDetail detail = DiagnosticDetail.Detailed)
        {
            return openStudioDiagnostics.CreateDiagnosticRecords().FilterDiagnosticRecords(detail).ConvertAll(x => x.Text);
        }
    }
}
