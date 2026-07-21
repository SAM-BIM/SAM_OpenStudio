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
        /// First day of week of the run calendar (1 Jan), used to align day-composed (weekly)
        /// profiles with the simulated weekdays. When null (default) the full-pipeline overload
        /// derives it from the EPW weather file; the geometry-only overload falls back to Monday.
        /// </summary>
        public System.DayOfWeek? FirstDayOfWeek { get; set; }

        /// <summary>
        /// Directory where the OSM/OSW and simulation run directories are written.
        /// When null the caller must provide paths explicitly at save/run time.
        /// </summary>
        public string OutputDirectory { get; set; }

        /// <summary>
        /// Building rotation [degrees clockwise from true North, the TAS/EnergyPlus convention]
        /// applied to OS:Building North Axis. When null (default) the SAM model's
        /// AnalyticalModelParameter.NorthAngle (stored in radians) is used; when neither exists
        /// the North Axis is left at the OpenStudio default (0).
        /// </summary>
        public double? NorthAngleDegrees { get; set; }

        /// <summary>Run-period begin month (default 1).</summary>
        public int? RunPeriodBeginMonth { get; set; }

        /// <summary>Run-period begin day of month (default 1).</summary>
        public int? RunPeriodBeginDay { get; set; }

        /// <summary>Run-period end month (default 12).</summary>
        public int? RunPeriodEndMonth { get; set; }

        /// <summary>Run-period end day of month (default 31).</summary>
        public int? RunPeriodEndDay { get; set; }

        /// <summary>Timesteps per hour (default null → 6, the documented MVP value).</summary>
        public int? TimestepsPerHour { get; set; }

        /// <summary>
        /// OS:Building Solar Distribution value (e.g. "FullExterior", "FullInteriorAndExterior").
        /// Null (default) leaves the OpenStudio default untouched.
        /// </summary>
        public string SolarDistribution { get; set; }

        /// <summary>
        /// OS:ShadowCalculation Calculation Frequency [days]. Null (default) leaves the
        /// OpenStudio default untouched.
        /// </summary>
        public int? ShadowCalculationFrequencyDays { get; set; }

        /// <summary>
        /// OS:ShadowCalculation Shading Calculation Method: "PixelCounting" (default) or
        /// "PolygonClipping". PixelCounting (the GPU-based method) has no limitations related
        /// to zone or shading-surface concavity — PolygonClipping reports every non-convex
        /// casting surface as a severe DetermineShadowingCombinations error — and scales
        /// better with high shading-surface counts; its per-timestep cost only pays off from
        /// roughly a few hundred shading surfaces. Set to "PolygonClipping" to restore the
        /// EnergyPlus default; null/empty leaves the OpenStudio default untouched.
        /// </summary>
        public string ShadingCalculationMethod { get; set; } = "PixelCounting";

        /// <summary>
        /// Paths to OpenStudio measure directories (each containing measure.xml and measure.rb)
        /// applied to the generated OSW as workflow steps, in order, with caller arguments at
        /// their measure defaults. Null/empty (default) writes an empty steps array. Directories
        /// that do not exist or contain no measure.xml are skipped with a warning — never
        /// silently.
        /// </summary>
        public System.Collections.Generic.IList<string> MeasurePaths { get; set; }

        /// <summary>
        /// Advanced use only: complete EnergyPlus objects in IDF format injected into the
        /// workspace after translation through a generated EnergyPlus measure
        /// (sam_additional_idf_objects) appended to the OSW steps. Each entry is one or more
        /// complete IDF objects; an entry that does not parse fails the workflow step, which
        /// fails the run — invalid objects are never silently dropped. Null/empty (default)
        /// generates no measure.
        /// </summary>
        public System.Collections.Generic.IList<string> AdditionalIdfStrings { get; set; }

        /// <summary>OS:YearDescription Calendar Year. Null (default) leaves it unset.</summary>
        public int? CalendarYear { get; set; }

        /// <summary>
        /// When true the run calendar is a leap year: OS:YearDescription Is Leap Year is set and
        /// schedules are generated with 8784 values (explicit ≥8784 profile stores pass through;
        /// 8760 stores repeat 31 Dec; day-composed profiles tile 366 days). Null/false (default)
        /// keeps the documented 365-day policy.
        /// </summary>
        public bool? IsLeapYear { get; set; }

        /// <summary>
        /// Daylight saving time. Default false (energy-model convention; SAM carries no DST
        /// data): any RunPeriodControlDaylightSavingTime object is removed. When true the object
        /// is ensured (EnergyPlus default dates).
        /// </summary>
        public bool DaylightSavingsTime { get; set; } = false;

        /// <summary>
        /// DDY design-day file imported into the model (heating 99.6% / cooling 0.4% by name
        /// convention unless <see cref="ImportAllDesignDays"/>). An explicit
        /// OpenStudioRunOptions.DdyPath takes precedence over this value. When design days are
        /// imported, sizing-period runs are enabled (see <see cref="RunSizingPeriods"/>).
        /// </summary>
        public string DdyPath { get; set; }

        /// <summary>When true every design day in the DDY is imported; default false imports only the 99.6% heating / 0.4% cooling pair (by name convention).</summary>
        public bool ImportAllDesignDays { get; set; } = false;

        /// <summary>
        /// Sizing-period execution: null (default) runs sizing periods when design days were
        /// imported; explicit true/false overrides. Annual results always exclude sizing periods
        /// (environment-period filter in the runner).
        /// </summary>
        public bool? RunSizingPeriods { get; set; }

        /// <summary>Reporting frequency for the requested output variables. Default "Hourly".</summary>
        public string OutputVariableFrequency { get; set; } = "Hourly";

        /// <summary>
        /// Inter-zone air exchange across SAM Air panels (OS:Construction:AirBoundary), in air
        /// changes per hour of the smaller of the two zones — the EnergyPlus basis.
        /// <para>
        /// Default NaN keeps the EnergyPlus default <c>Air Exchange Method = None</c>: the two
        /// zones are still grouped for solar, daylighting and radiant exchange (that is inherent
        /// to an air boundary), but no air moves between them, so their air temperatures are
        /// coupled only by radiation. A finite positive value switches the method to
        /// <c>SimpleMixing</c> at that rate.
        /// </para>
        /// <para>
        /// This is deliberately opt-in and never inferred: SAM carries no per-panel airflow data
        /// (PanelParameter has no airflow member), so any rate is an assumption by the caller and
        /// is named in a diagnostic when applied. EnergyPlus's own documented default for the
        /// field is 0.5 ACH. Ignored when an AirflowNetwork simulation is active (EnergyPlus
        /// rule), which is outside the Ideal Loads scope of this converter.
        /// </para>
        /// </summary>
        public double AirBoundaryAirChangesPerHour { get; set; } = double.NaN;
    }
}
