// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Core.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Maps an <see cref="OpenStudioDiagnosticSeverity"/> to the shared <see cref="DiagnosticSeverity"/> one to one
        /// (Information to Info). An undefined value maps to Error, never to a milder severity.
        /// </summary>
        public static DiagnosticSeverity ToDiagnosticSeverity(this OpenStudioDiagnosticSeverity openStudioDiagnosticSeverity)
        {
            switch (openStudioDiagnosticSeverity)
            {
                case OpenStudioDiagnosticSeverity.Information:
                    return DiagnosticSeverity.Info;

                case OpenStudioDiagnosticSeverity.Warning:
                    return DiagnosticSeverity.Warning;

                default:
                    return DiagnosticSeverity.Error;
            }
        }
    }
}
