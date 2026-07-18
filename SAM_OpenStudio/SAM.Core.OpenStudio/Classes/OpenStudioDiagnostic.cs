// SPDX-License-Identifier: LGPL-3.0-only

using System;

namespace SAM.Core.OpenStudio
{
    /// <summary>
    /// Structured message describing an event, approximation or failure during SAM → OpenStudio
    /// conversion or simulation. Diagnostics replace silent substitution: converters must raise a
    /// diagnostic instead of guessing missing data. Immutable.
    /// </summary>
    public sealed class OpenStudioDiagnostic
    {
        /// <summary>Stable machine-readable code, see <see cref="OpenStudioDiagnosticCodes"/>.</summary>
        public string Code { get; }

        /// <summary>Severity of the diagnostic.</summary>
        public OpenStudioDiagnosticSeverity Severity { get; }

        /// <summary>Human-readable description of what happened and which value was affected.</summary>
        public string Message { get; }

        /// <summary>Guid of the SAM object the diagnostic refers to, when applicable.</summary>
        public Guid? SamGuid { get; }

        /// <summary>SAM type name of the object the diagnostic refers to, when applicable.</summary>
        public string SamObjectType { get; }

        /// <summary>Name of the related OpenStudio object, when one was created.</summary>
        public string OpenStudioObjectName { get; }

        /// <summary>Creates an immutable diagnostic.</summary>
        /// <param name="code">Stable machine-readable code, see <see cref="OpenStudioDiagnosticCodes"/>.</param>
        /// <param name="severity">Severity of the diagnostic.</param>
        /// <param name="message">Human-readable description.</param>
        /// <param name="samGuid">Guid of the related SAM object, when applicable.</param>
        /// <param name="samObjectType">SAM type name of the related object, when applicable.</param>
        /// <param name="openStudioObjectName">Name of the related OpenStudio object, when applicable.</param>
        public OpenStudioDiagnostic(string code, OpenStudioDiagnosticSeverity severity, string message, Guid? samGuid = null, string samObjectType = null, string openStudioObjectName = null)
        {
            Code = code;
            Severity = severity;
            Message = message;
            SamGuid = samGuid;
            SamObjectType = samObjectType;
            OpenStudioObjectName = openStudioObjectName;
        }

        /// <summary>Formats the diagnostic as a single log line.</summary>
        public override string ToString()
        {
            string result = string.Format("[{0}] {1}: {2}", Code, Severity, Message);
            if (SamGuid.HasValue || !string.IsNullOrWhiteSpace(SamObjectType))
            {
                result += string.Format(" (SAM {0} {1})", SamObjectType, SamGuid);
            }

            if (!string.IsNullOrWhiteSpace(OpenStudioObjectName))
            {
                result += string.Format(" → {0}", OpenStudioObjectName);
            }

            return result;
        }
    }
}
