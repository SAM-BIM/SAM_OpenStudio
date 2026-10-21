// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;

namespace SAM.Core.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Information diagnostics that share a result-affecting code with others but are known to be inert for the
        /// simulation (no EnergyPlus field exists, or the value only matters for daylighting / moisture modelling).
        /// Only these are lowered below Summary; any other message under such a code, and any reworded message, stays at
        /// Summary, so a change of wording can only make a line more visible, never hide it. The wording is pinned by
        /// <c>DeclaredDiagnosticsTests</c> (C7), which asserts these exact message fragments against the real converters.
        /// </summary>
        private static readonly (string Code, string MessagePrefix, DiagnosticDetail Detail)[] inertInformation =
        {
            (OpenStudioDiagnosticCodes.MaterialUnsupportedParameter, "Vapour diffusion factor", DiagnosticDetail.Detailed),
            (OpenStudioDiagnosticCodes.InternalConditionUnsupportedParameter, "Occupancy view coefficient", DiagnosticDetail.Detailed),
            (OpenStudioDiagnosticCodes.InternalConditionUnsupportedParameter, "Equipment view coefficient", DiagnosticDetail.Detailed),
            (OpenStudioDiagnosticCodes.ConstructionUnsupportedParameter, "Frame data on an opaque door", DiagnosticDetail.Standard),
        };

        /// <summary>
        /// Lowest <see cref="DiagnosticDetail"/> at which an Information diagnostic is shown. Warnings and Errors are
        /// always shown and return <see cref="DiagnosticDetail.Summary"/>.
        /// <para>
        /// Principle: an Information line that records an approximation, fallback, assumption, dropped input or data
        /// source that can change simulation results is Summary. The producers use one code for several situations
        /// (for example <c>SAM-OS-MAT-002</c> covers both an approximated single-sided optic and an inert vapour
        /// diffusion factor), so the table is an explicit code table, with the few known-inert lines of those codes
        /// lowered by <see cref="inertInformation"/>.
        /// </para>
        /// <list type="bullet">
        /// <item><description>Summary: run and workflow outcomes and basis (<c>RUN-001</c>, <c>SET-001</c>, weather and design-day source and site
        /// overrides <c>RUN-002</c>), fallbacks and approximations (<c>CON-001</c> simple-glazing fallback, <c>SCH-001</c> leap-year profile, <c>IC-002</c> lighting split and default, import
        /// <c>APX-001</c>), data that is not converted (<c>MAT-002</c>, <c>CON-002</c>, <c>IC-001</c>, <c>HVAC-001</c>, import <c>LOAD-001</c>,
        /// <c>WEA-001</c>) and import translation / workflow notes.</description></item>
        /// <item><description>Standard: advisories with no effect on results (<c>GEO-003</c> non-convex casting under PixelCounting, <c>ADJ-001</c>
        /// derived building stories, import <c>ZONE-001</c>), the known-inert lines above, and every code added later.</description></item>
        /// <item><description>Detailed: per-object geometry clean-ups (<c>SAM-OS-GEO-002</c>, <c>SAM-OSI-GEO-002</c>) and inert per-object
        /// parameters.</description></item>
        /// </list>
        /// Severity is never inferred from the text; the level of a Warning or Error is not used.
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
                case OpenStudioDiagnosticCodes.WeatherDataIssue:
                case OpenStudioDiagnosticCodes.SimulationSettingsUnsupported:
                case OpenStudioDiagnosticCodes.ConstructionMissingLayer:
                case OpenStudioDiagnosticCodes.ScheduleMissingProfile:
                case OpenStudioDiagnosticCodes.MaterialUnsupportedParameter:
                case OpenStudioDiagnosticCodes.ConstructionUnsupportedParameter:
                case OpenStudioDiagnosticCodes.InternalConditionUnsupportedParameter:
                case OpenStudioDiagnosticCodes.InternalConditionAssumption:
                case OpenStudioDiagnosticCodes.HvacMissingSetpoints:
                case OpenStudioImportDiagnosticCodes.OsmVersionTranslationFailed:
                case OpenStudioImportDiagnosticCodes.OswEnergyPlusMeasureNotRepresentable:
                case OpenStudioImportDiagnosticCodes.OswWorkflowExecuted:
                case OpenStudioImportDiagnosticCodes.OswNoSimulationResults:
                case OpenStudioImportDiagnosticCodes.SimulationSettingUnsupported:
                case OpenStudioImportDiagnosticCodes.ApproximationApplied:
                case OpenStudioImportDiagnosticCodes.LoadUnsupported:
                case OpenStudioImportDiagnosticCodes.WeatherLimitation:
                    foreach ((string code, string messagePrefix, DiagnosticDetail detail) in inertInformation)
                    {
                        if (code == openStudioDiagnostic.Code && openStudioDiagnostic.Message != null && openStudioDiagnostic.Message.StartsWith(messagePrefix, StringComparison.Ordinal))
                        {
                            return detail;
                        }
                    }

                    return DiagnosticDetail.Summary;

                case OpenStudioDiagnosticCodes.GeometryVerticesCleaned:
                case OpenStudioImportDiagnosticCodes.GeometryVerticesCleaned:
                    return DiagnosticDetail.Detailed;

                default:
                    return DiagnosticDetail.Standard;
            }
        }
    }
}
