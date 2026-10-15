// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Core.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Converts an <see cref="OpenStudioDiagnostic"/> to a shared <see cref="Diagnostic"/>. Code and message are
        /// carried verbatim, severity is the producer's (see <see cref="ToDiagnosticSeverity"/>), the subject is the SAM
        /// object type and Guid (or the OpenStudio object name when there is no SAM identity). The original is not modified.
        /// </summary>
        /// <param name="openStudioDiagnostic">The diagnostic; null returns null.</param>
        /// <param name="sourceIndex">Position of the diagnostic in its list, when known.</param>
        public static Diagnostic ToDiagnostic(this OpenStudioDiagnostic openStudioDiagnostic, int? sourceIndex = null)
        {
            if (openStudioDiagnostic == null)
            {
                return null;
            }

            string subject = null;
            if (openStudioDiagnostic.SamGuid.HasValue || !string.IsNullOrWhiteSpace(openStudioDiagnostic.SamObjectType))
            {
                subject = string.Format("{0} {1}", openStudioDiagnostic.SamObjectType, openStudioDiagnostic.SamGuid).Trim();
            }
            else if (!string.IsNullOrWhiteSpace(openStudioDiagnostic.OpenStudioObjectName))
            {
                subject = openStudioDiagnostic.OpenStudioObjectName;
            }

            return new Diagnostic(openStudioDiagnostic.Severity.ToDiagnosticSeverity(), openStudioDiagnostic.GetDiagnosticDetail(), subject, openStudioDiagnostic.Message, openStudioDiagnostic.Code, sourceIndex);
        }
    }
}
