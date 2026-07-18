// SPDX-License-Identifier: LGPL-3.0-only

namespace SAM.Core.OpenStudio
{
    /// <summary>
    /// Severity of a <see cref="OpenStudioDiagnostic"/> raised during SAM → OpenStudio
    /// conversion or simulation.
    /// </summary>
    public enum OpenStudioDiagnosticSeverity
    {
        /// <summary>Informational message; no action required.</summary>
        Information = 0,

        /// <summary>The conversion continued but the output may differ from the SAM source.</summary>
        Warning = 1,

        /// <summary>The affected object could not be converted; the result is not valid.</summary>
        Error = 2,
    }
}
