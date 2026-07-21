// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Core.OpenStudio
{
    /// <summary>
    /// Stable diagnostic codes used by the reverse OpenStudio → SAM import, defined by
    /// docs/openstudio-to-sam-import-audit.md §5. The <c>SAM-OSI-*</c> prefix ("OpenStudio
    /// import") keeps them disjoint from the forward <c>SAM-OS-*</c> codes in
    /// <see cref="OpenStudioDiagnosticCodes"/>, so a consumer can tell which direction produced a
    /// diagnostic from the code alone and neither list has to be renumbered when the other grows.
    /// </summary>
    public static class OpenStudioImportDiagnosticCodes
    {
        /// <summary>Input path is null, empty or does not exist.</summary>
        public const string InputPathInvalid = "SAM-OSI-IN-001";

        /// <summary>Input file extension is neither .osm nor .osw.</summary>
        public const string InputExtensionUnsupported = "SAM-OSI-IN-002";

        /// <summary>The OSM could not be loaded.</summary>
        public const string OsmLoadFailed = "SAM-OSI-OSM-001";

        /// <summary>OSM version translation failed, or the VersionTranslator reported errors/warnings.</summary>
        public const string OsmVersionTranslationFailed = "SAM-OSI-OSM-002";

        /// <summary>The OSW JSON could not be parsed.</summary>
        public const string OswParseFailed = "SAM-OSI-OSW-001";

        /// <summary>The OSW declares no seed_file.</summary>
        public const string OswSeedMissing = "SAM-OSI-OSW-002";

        /// <summary>The OSW seed_file could not be resolved on disk.</summary>
        public const string OswSeedNotFound = "SAM-OSI-OSW-003";

        /// <summary>The workflow was not executed, so its measures were not applied to the imported model.</summary>
        public const string OswWorkflowNotExecuted = "SAM-OSI-OSW-004";

        /// <summary>Workflow execution failed.</summary>
        public const string OswExecutionFailed = "SAM-OSI-OSW-005";

        /// <summary>The final post-model-measure OSM could not be located after the run.</summary>
        public const string OswFinalOsmNotFound = "SAM-OSI-OSW-006";

        /// <summary>The workflow contains EnergyPlus measures whose changes exist only in the generated IDF and cannot be represented in the OSM.</summary>
        public const string OswEnergyPlusMeasureNotRepresentable = "SAM-OSI-OSW-007";

        /// <summary>Geometry could not be converted (degenerate, non-planar, too few vertices, below minimum area, aperture not contained by its host).</summary>
        public const string GeometryInvalid = "SAM-OSI-GEO-001";

        /// <summary>Adjacency was resolved by validated geometric matching because no OpenStudio adjacency handle was available.</summary>
        public const string AdjacencyGeometricFallback = "SAM-OSI-ADJ-001";

        /// <summary>Adjacency pairing failed; the surface was not merged with a partner.</summary>
        public const string AdjacencyPairingFailed = "SAM-OSI-ADJ-002";

        /// <summary>Unsupported outside boundary condition.</summary>
        public const string BoundaryConditionUnsupported = "SAM-OSI-BC-001";

        /// <summary>Unsupported subsurface type.</summary>
        public const string SubSurfaceUnsupported = "SAM-OSI-SUB-001";

        /// <summary>Unsupported material family.</summary>
        public const string MaterialUnsupported = "SAM-OSI-MAT-001";

        /// <summary>Unsupported construction kind.</summary>
        public const string ConstructionUnsupported = "SAM-OSI-CON-001";

        /// <summary>Unsupported schedule kind, or a schedule that could not be expanded to hourly values.</summary>
        public const string ScheduleUnsupported = "SAM-OSI-SCH-001";

        /// <summary>Unsupported load or control object.</summary>
        public const string LoadUnsupported = "SAM-OSI-LOAD-001";

        /// <summary>Conflicting space-type and space-level load assignment.</summary>
        public const string LoadAssignmentConflict = "SAM-OSI-LOAD-002";

        /// <summary>Detailed HVAC object encountered and deliberately not imported.</summary>
        public const string HvacUnsupported = "SAM-OSI-HVAC-001";

        /// <summary>A thermal zone contains more than one OpenStudio space.</summary>
        public const string ZoneMultiSpace = "SAM-OSI-ZONE-001";

        /// <summary>Orphaned space, or a space/zone with incomplete geometry.</summary>
        public const string ZoneIncomplete = "SAM-OSI-ZONE-002";

        /// <summary>Invalid or duplicate SAM identity metadata; a new SAM Guid was issued.</summary>
        public const string IdentityCollision = "SAM-OSI-ID-001";

        /// <summary>Weather, site or design-day limitation.</summary>
        public const string WeatherLimitation = "SAM-OSI-WEA-001";

        /// <summary>Simulation setting with no SAM equivalent.</summary>
        public const string SimulationSettingUnsupported = "SAM-OSI-SET-001";

        /// <summary>A documented approximation was applied.</summary>
        public const string ApproximationApplied = "SAM-OSI-APX-001";
    }
}
