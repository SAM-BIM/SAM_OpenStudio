// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Core.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Converts an <see cref="OpenStudioDiagnostic"/> to a shared <see cref="Diagnostic"/>. Code and message are
        /// carried unchanged, severity is the producer's (see <see cref="ToDiagnosticSeverity"/>).
        /// <para>
        /// <see cref="Diagnostic"/> has a single free-text <see cref="Diagnostic.Subject"/>, so both identities go into it:
        /// <c>"{SamObjectType} {SamGuid} (OpenStudio {OpenStudioObjectName})"</c>, the first part only when there is a SAM
        /// identity and the second only when there is an OpenStudio object name (<c>"OpenStudio {name}"</c> alone otherwise).
        /// <see cref="OpenStudioDiagnosticRecord"/> also exposes the three identity values separately.
        /// </para>
        /// <para>
        /// <see cref="Diagnostic"/> stores a null message as an empty string; the original <see cref="OpenStudioDiagnostic.Message"/>
        /// is unchanged and <see cref="OpenStudioDiagnostic.ToString"/> keeps its legacy form.
        /// </para>
        /// </summary>
        /// <param name="openStudioDiagnostic">The diagnostic; null returns null.</param>
        /// <param name="sourceIndex">Position of the diagnostic in its list, when known.</param>
        public static Diagnostic ToDiagnostic(this OpenStudioDiagnostic openStudioDiagnostic, int? sourceIndex = null)
        {
            if (openStudioDiagnostic == null)
            {
                return null;
            }

            return new Diagnostic(openStudioDiagnostic.Severity.ToDiagnosticSeverity(), openStudioDiagnostic.GetDiagnosticDetail(), DiagnosticSubject(openStudioDiagnostic), openStudioDiagnostic.Message, openStudioDiagnostic.Code, sourceIndex);
        }

        private static string DiagnosticSubject(OpenStudioDiagnostic openStudioDiagnostic)
        {
            string sam = null;
            if (openStudioDiagnostic.SamGuid.HasValue || !string.IsNullOrWhiteSpace(openStudioDiagnostic.SamObjectType))
            {
                sam = string.Format("{0} {1}", openStudioDiagnostic.SamObjectType, openStudioDiagnostic.SamGuid).Trim();
            }

            string openStudio = string.IsNullOrWhiteSpace(openStudioDiagnostic.OpenStudioObjectName) ? null : "OpenStudio " + openStudioDiagnostic.OpenStudioObjectName;

            if (sam != null && openStudio != null)
            {
                return string.Format("{0} ({1})", sam, openStudio);
            }

            return sam ?? openStudio;
        }
    }
}
