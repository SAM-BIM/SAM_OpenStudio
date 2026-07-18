// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Core.OpenStudio
{
    /// <summary>
    /// Options controlling SAM → OpenStudio model conversion. All geometry tolerances used by the
    /// converters come from this class; converters must not hard-code tolerances. Missing SAM data
    /// (materials, constructions, profiles, internal-condition values) is never silently
    /// substituted regardless of options — converters raise <see cref="OpenStudioDiagnostic"/>
    /// entries instead.
    /// </summary>
    public sealed class OpenStudioConversionOptions
    {
        /// <summary>
        /// Distance tolerance [m] for vertex equality and duplicate-vertex removal.
        /// Default: <see cref="Core.Tolerance.Distance"/> (1e-6 m).
        /// </summary>
        public double DistanceTolerance { get; set; } = Core.Tolerance.Distance;

        /// <summary>
        /// Angle tolerance [rad] for collinearity and planarity checks.
        /// Default: <see cref="Core.Tolerance.Angle"/>.
        /// </summary>
        public double AngleTolerance { get; set; } = Core.Tolerance.Angle;

        /// <summary>
        /// Elevation tolerance [m] used to group SAM spaces into OpenStudio BuildingStories.
        /// Default: <see cref="Core.Tolerance.MacroDistance"/> (1e-3 m).
        /// </summary>
        public double ElevationTolerance { get; set; } = Core.Tolerance.MacroDistance;

        /// <summary>
        /// Minimum polygon area [m²] below which a boundary is rejected as degenerate.
        /// Default: 1e-4 m² (1 cm²).
        /// </summary>
        public double MinimumArea { get; set; } = 0.0001;

        /// <summary>
        /// When true (default) conditioned thermal zones receive a ZoneHVACIdealLoadsAirSystem
        /// and a dual-setpoint thermostat.
        /// </summary>
        public bool AssignIdealLoads { get; set; } = true;

        /// <summary>
        /// When true (default) SAM shading panels are converted to OpenStudio shading surfaces.
        /// </summary>
        public bool IncludeShading { get; set; } = true;

        /// <summary>
        /// Directory where the OSM/OSW and simulation run directories are written.
        /// When null the caller must provide paths explicitly at save/run time.
        /// </summary>
        public string OutputDirectory { get; set; }
    }
}
