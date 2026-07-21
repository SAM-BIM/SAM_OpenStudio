// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Core.OpenStudio
{
    /// <summary>
    /// Options controlling OpenStudio → SAM import. Deliberately separate from
    /// <see cref="OpenStudioConversionOptions"/>: the two directions share tolerances but not
    /// intent, and coupling them would make every forward option appear on the import component.
    /// <para>
    /// As in the forward direction, missing OpenStudio data is never silently substituted
    /// regardless of options — the converters raise <see cref="OpenStudioDiagnostic"/> entries.
    /// </para>
    /// </summary>
    public sealed class OpenStudioImportOptions
    {
        /// <summary>
        /// Distance tolerance [m] for vertex equality, duplicate-vertex removal and geometric
        /// adjacency fallback. Default: <see cref="Core.Tolerance.Distance"/>.
        /// </summary>
        public double DistanceTolerance { get; set; } = Core.Tolerance.Distance;

        /// <summary>
        /// Angle tolerance [rad] for collinearity and planarity checks.
        /// Default: <see cref="Core.Tolerance.Angle"/>.
        /// </summary>
        public double AngleTolerance { get; set; } = Core.Tolerance.Angle;

        /// <summary>
        /// Minimum polygon area [m²] below which an imported boundary is rejected as degenerate.
        /// Default: 1e-4 m² (1 cm²).
        /// </summary>
        public double MinimumArea { get; set; } = 0.0001;

        /// <summary>
        /// When true (default) shading surfaces are imported as SAM shade panels.
        /// </summary>
        public bool IncludeShading { get; set; } = true;

        /// <summary>
        /// When true (default) space types, loads, controls and schedules are imported into SAM
        /// InternalConditions and a ProfileLibrary. False imports geometry and constructions only.
        /// </summary>
        public bool IncludeInternalConditions { get; set; } = true;

        /// <summary>
        /// When true (default) constructions and materials are imported into a SAM
        /// MaterialLibrary. False leaves panels with name-only placeholder constructions.
        /// </summary>
        public bool IncludeConstructions { get; set; } = true;

        /// <summary>
        /// When true (default) an interzone pair whose OpenStudio adjacency handles are missing
        /// may still be paired by validated geometric matching (coincident, opposite-facing,
        /// equal-area boundaries), reported as SAM-OSI-ADJ-001. False leaves such surfaces
        /// unpaired and reports SAM-OSI-ADJ-002.
        /// </summary>
        public bool AllowGeometricAdjacencyFallback { get; set; } = true;

        /// <summary>
        /// When true (default) a full SAM Guid found in the OpenStudio AdditionalProperties
        /// (<see cref="OpenStudioIdentityKeys.Guid"/>) is restored onto the imported SAM object.
        /// False always issues fresh Guids — useful when importing the same OSM twice into one
        /// SAM session.
        /// </summary>
        public bool RestoreSAMIdentity { get; set; } = true;

        /// <summary>
        /// When true the OSW workflow is executed through the OpenStudio CLI before import, so
        /// model measures are applied. Default false: an OSW is imported from its seed model and
        /// SAM-OSI-OSW-004 states that measures were not applied.
        /// </summary>
        public bool ExecuteWorkflow { get; set; }

        /// <summary>
        /// Directory used for OSW execution (run folder and artifacts). When null the OSW's own
        /// directory is used.
        /// </summary>
        public string OutputDirectory { get; set; }

        /// <summary>
        /// Unreferenced materials, constructions and profiles are pruned from the produced
        /// libraries when true (default), mirroring the SAM_LadybugTools reverse converter.
        /// </summary>
        public bool PruneUnreferencedLibraryEntries { get; set; } = true;
    }
}
