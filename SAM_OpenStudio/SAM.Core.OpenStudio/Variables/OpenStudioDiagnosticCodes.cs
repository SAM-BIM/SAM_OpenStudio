// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Core.OpenStudio
{
    /// <summary>
    /// Stable diagnostic codes used across the SAM → OpenStudio conversion, defined by the
    /// MVP implementation plan (docs/SAM_OpenStudio_MVP_Implementation_Plan.md, section 6).
    /// </summary>
    public static class OpenStudioDiagnosticCodes
    {
        /// <summary>Invalid or non-planar boundary.</summary>
        public const string GeometryInvalidBoundary = "SAM-OS-GEO-001";

        /// <summary>Duplicate or collinear vertices removed.</summary>
        public const string GeometryVerticesCleaned = "SAM-OS-GEO-002";

        /// <summary>Missing adjacent surface.</summary>
        public const string AdjacencyMissingSurface = "SAM-OS-ADJ-001";

        /// <summary>Unsupported material.</summary>
        public const string MaterialUnsupported = "SAM-OS-MAT-001";

        /// <summary>Unsupported material parameter (e.g. blind flag without slat geometry).</summary>
        public const string MaterialUnsupportedParameter = "SAM-OS-MAT-002";

        /// <summary>Missing construction layer.</summary>
        public const string ConstructionMissingLayer = "SAM-OS-CON-001";

        /// <summary>Unsupported construction/aperture parameter (frame, shade, opening data).</summary>
        public const string ConstructionUnsupportedParameter = "SAM-OS-CON-002";

        /// <summary>Missing profile.</summary>
        public const string ScheduleMissingProfile = "SAM-OS-SCH-001";

        /// <summary>Unsupported internal-condition parameter.</summary>
        public const string InternalConditionUnsupportedParameter = "SAM-OS-IC-001";

        /// <summary>Conditioned zone missing setpoints, or HVAC-dependent data (emitter characteristics, exhaust flows) deferred to the detailed-HVAC programme.</summary>
        public const string HvacMissingSetpoints = "SAM-OS-HVAC-001";

        /// <summary>OpenStudio CLI failed.</summary>
        public const string RunCliFailed = "SAM-OS-RUN-001";

        /// <summary>Weather/design-day data issue or documented fallback (ground temperatures, design days).</summary>
        public const string WeatherDataIssue = "SAM-OS-RUN-002";

        /// <summary>Result-extraction limitation (e.g. peaks/series assume hourly reporting under a non-hourly OutputVariableFrequency).</summary>
        public const string ResultExtractionLimitation = "SAM-OS-RUN-003";

        /// <summary>Unsupported or rejected simulation-setting value (e.g. shading calculation method); workflow issues such as unusable measure directories.</summary>
        public const string SimulationSettingsUnsupported = "SAM-OS-SET-001";

        /// <summary>EnergyPlus severe error.</summary>
        public const string EnergyPlusSevereError = "SAM-OS-EPLUS-001";
    }
}
