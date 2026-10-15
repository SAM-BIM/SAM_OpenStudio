// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Core.OpenStudio
{
    /// <summary>
    /// An <see cref="OpenStudioDiagnostic"/> paired with its structured shared <see cref="SAM.Core.Diagnostic"/>.
    /// The original diagnostic is kept untouched, so the legacy text (<see cref="OpenStudioDiagnostic.ToString"/>) shown
    /// by the Grasshopper <c>diagnostics</c> outputs is unchanged whatever detail level is applied. Runtime value only.
    /// </summary>
    public sealed class OpenStudioDiagnosticRecord
    {
        /// <summary>The producer's original diagnostic (code, severity, message, SAM and OpenStudio identity).</summary>
        public OpenStudioDiagnostic Source { get; }

        /// <summary>The structured diagnostic derived from <see cref="Source"/>.</summary>
        public Diagnostic Diagnostic { get; }

        /// <summary>The legacy single-line text, identical to <c>Source.ToString()</c>.</summary>
        public string Text => Source?.ToString() ?? string.Empty;

        /// <summary>Creates a record from a source diagnostic and its structured counterpart.</summary>
        public OpenStudioDiagnosticRecord(OpenStudioDiagnostic source, Diagnostic diagnostic)
        {
            Source = source;
            Diagnostic = diagnostic;
        }

        public override string ToString()
        {
            return Text;
        }
    }
}
