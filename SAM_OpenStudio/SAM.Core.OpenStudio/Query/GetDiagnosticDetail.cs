// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Core.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Lowest <see cref="DiagnosticDetail"/> at which an Information diagnostic is shown. Warnings and Errors are
        /// always shown and return <see cref="DiagnosticDetail.Summary"/>. The level is decided by the diagnostic's code
        /// only (never by its text): run and workflow outcomes are Summary, per-object deferrals and clean-ups are
        /// Detailed, everything else - including codes added later - is Standard.
        /// </summary>
        public static DiagnosticDetail GetDiagnosticDetail(this OpenStudioDiagnostic openStudioDiagnostic)
        {
            if (openStudioDiagnostic == null || openStudioDiagnostic.Severity != OpenStudioDiagnosticSeverity.Information)
            {
                return DiagnosticDetail.Summary;
            }

            switch (openStudioDiagnostic.Code)
            {
                case OpenStudioDiagnosticCodes.RunCliFailed:
                case OpenStudioDiagnosticCodes.SimulationSettingsUnsupported:
                case OpenStudioImportDiagnosticCodes.OsmVersionTranslationFailed:
                case OpenStudioImportDiagnosticCodes.OswEnergyPlusMeasureNotRepresentable:
                case OpenStudioImportDiagnosticCodes.OswWorkflowExecuted:
                case OpenStudioImportDiagnosticCodes.OswNoSimulationResults:
                case OpenStudioImportDiagnosticCodes.SimulationSettingUnsupported:
                    return DiagnosticDetail.Summary;

                case OpenStudioDiagnosticCodes.GeometryVerticesCleaned:
                case OpenStudioDiagnosticCodes.MaterialUnsupportedParameter:
                case OpenStudioDiagnosticCodes.ConstructionUnsupportedParameter:
                case OpenStudioDiagnosticCodes.InternalConditionUnsupportedParameter:
                case OpenStudioImportDiagnosticCodes.GeometryVerticesCleaned:
                    return DiagnosticDetail.Detailed;

                default:
                    return DiagnosticDetail.Standard;
            }
        }
    }
}
