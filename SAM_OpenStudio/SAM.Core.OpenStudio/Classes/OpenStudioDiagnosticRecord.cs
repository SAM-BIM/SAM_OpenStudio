// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;

namespace SAM.Core.OpenStudio
{
    /// <summary>
    /// An <see cref="OpenStudioDiagnostic"/> paired with its structured shared <see cref="SAM.Core.Diagnostic"/>.
    /// The original diagnostic is kept untouched, so the legacy text (<see cref="OpenStudioDiagnostic.ToString"/>) shown
    /// by the Grasshopper <c>diagnostics</c> outputs is unchanged whatever detail level is applied. Runtime value only.
    /// The pair is checked on construction: a record whose <see cref="Diagnostic"/> does not describe its
    /// <see cref="Source"/> cannot exist.
    /// </summary>
    public sealed class OpenStudioDiagnosticRecord
    {
        /// <summary>The producer's original diagnostic (code, severity, message, SAM and OpenStudio identity).</summary>
        public OpenStudioDiagnostic Source { get; }

        /// <summary>The structured diagnostic derived from <see cref="Source"/>.</summary>
        public Diagnostic Diagnostic { get; }

        /// <summary>The legacy single-line text, identical to <c>Source.ToString()</c> (computed once).</summary>
        public string Text { get; }

        /// <summary>Guid of the related SAM object (from <see cref="Source"/>), when applicable.</summary>
        public Guid? SamGuid => Source.SamGuid;

        /// <summary>SAM type name of the related SAM object (from <see cref="Source"/>), when applicable.</summary>
        public string SamObjectType => Source.SamObjectType;

        /// <summary>Name of the related OpenStudio object (from <see cref="Source"/>), when applicable.</summary>
        public string OpenStudioObjectName => Source.OpenStudioObjectName;

        /// <summary>
        /// Creates a record. <paramref name="diagnostic"/> must be what <see cref="Query.ToDiagnostic"/> gives for
        /// <paramref name="source"/> (same code, severity, message and detail level); the source index is free.
        /// </summary>
        /// <exception cref="ArgumentNullException">Either argument is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="diagnostic"/> does not describe <paramref name="source"/>.</exception>
        public OpenStudioDiagnosticRecord(OpenStudioDiagnostic source, Diagnostic diagnostic)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (diagnostic == null)
            {
                throw new ArgumentNullException(nameof(diagnostic));
            }

            if (diagnostic.Code != source.Code ||
                diagnostic.Severity != source.Severity.ToDiagnosticSeverity() ||
                diagnostic.Detail != source.GetDiagnosticDetail() ||
                diagnostic.Message != (source.Message ?? string.Empty))
            {
                throw new ArgumentException("The diagnostic does not describe the source diagnostic (code, severity, detail or message differ); use Query.ToDiagnostic.", nameof(diagnostic));
            }

            Source = source;
            Diagnostic = diagnostic;
            Text = source.ToString();
        }

        public override string ToString()
        {
            return Text;
        }
    }
}
