// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Analytical.OpenStudio
{
    /// <summary>
    /// One row of the EnergyPlus SQL <c>ZoneSizes</c> table: the zone sizing outcome for a single
    /// zone and load type, produced by the design-day sizing periods (NOT by the annual weather run).
    /// Immutable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the route's genuinely sizing-derived load, which is what METRICS.md defines a design
    /// load to be — distinct from the annual simulated peak (<c>Load</c>/<c>LoadIndex</c>), which comes
    /// from the weather RunPeriod and must never be relabelled as a design load.
    /// </para>
    /// <para>
    /// Every column is preserved for auditability even though only <see cref="UserDesignLoad"/>
    /// populates the v1 benchmark <c>designLoad</c> metric: <see cref="CalculatedDesignLoad"/> is the
    /// unaltered calculated zone load, while <see cref="UserDesignLoad"/> is the value after sizing
    /// factors and other user adjustments — the load actually used to size components, and therefore
    /// the one comparable with the TAS route's TBD <c>maxHeatingLoad</c>/<c>maxCoolingLoad</c>.
    /// </para>
    /// </remarks>
    public sealed class OpenStudioZoneSizingResult
    {
        /// <summary>Creates an immutable zone-sizing result.</summary>
        /// <param name="zoneName">EnergyPlus zone name (SQL <c>ZoneSizes.ZoneName</c>).</param>
        /// <param name="loadType">Sizing load type as reported by EnergyPlus (<c>Heating</c>/<c>Cooling</c>).</param>
        /// <param name="calculatedDesignLoad">Unaltered calculated zone design load [W].</param>
        /// <param name="userDesignLoad">Design load after sizing factors/user adjustments [W].</param>
        /// <param name="calculatedDesignFlow">Unaltered calculated design air flow rate [m3/s].</param>
        /// <param name="userDesignFlow">Design air flow rate after sizing factors/user adjustments [m3/s].</param>
        /// <param name="designDayName">Name of the design day that produced the peak.</param>
        /// <param name="peakTime">EnergyPlus peak time stamp as reported (<c>M/D HH:MM:SS</c>).</param>
        /// <param name="peakTemperature">Outdoor dry-bulb temperature at the peak [C].</param>
        /// <param name="peakHumidityRatio">Humidity ratio at the peak [kgWater/kgDryAir].</param>
        public OpenStudioZoneSizingResult(
            string zoneName,
            string loadType,
            double? calculatedDesignLoad,
            double? userDesignLoad,
            double? calculatedDesignFlow,
            double? userDesignFlow,
            string designDayName,
            string peakTime,
            double? peakTemperature,
            double? peakHumidityRatio)
        {
            ZoneName = zoneName;
            LoadType = loadType;
            CalculatedDesignLoad = calculatedDesignLoad;
            UserDesignLoad = userDesignLoad;
            CalculatedDesignFlow = calculatedDesignFlow;
            UserDesignFlow = userDesignFlow;
            DesignDayName = designDayName;
            PeakTime = peakTime;
            PeakTemperature = peakTemperature;
            PeakHumidityRatio = peakHumidityRatio;
        }

        /// <summary>EnergyPlus zone name the row was reported for.</summary>
        public string ZoneName { get; }

        /// <summary>Sizing load type as EnergyPlus reported it (<c>Heating</c> or <c>Cooling</c>).</summary>
        public string LoadType { get; }

        /// <summary>Unaltered calculated zone design load [W]; kept for audit, not emitted as the benchmark metric.</summary>
        public double? CalculatedDesignLoad { get; }

        /// <summary>Design load after sizing factors/user adjustments [W]: the value used to size components.</summary>
        public double? UserDesignLoad { get; }

        /// <summary>Unaltered calculated design air flow rate [m3/s].</summary>
        public double? CalculatedDesignFlow { get; }

        /// <summary>Design air flow rate after sizing factors/user adjustments [m3/s].</summary>
        public double? UserDesignFlow { get; }

        /// <summary>Name of the design day that produced this peak.</summary>
        public string DesignDayName { get; }

        /// <summary>EnergyPlus peak time stamp exactly as reported (<c>M/D HH:MM:SS</c>).</summary>
        public string PeakTime { get; }

        /// <summary>Outdoor dry-bulb temperature at the peak [C].</summary>
        public double? PeakTemperature { get; }

        /// <summary>Humidity ratio at the peak [kgWater/kgDryAir].</summary>
        public double? PeakHumidityRatio { get; }

        /// <summary>The SAM <see cref="Analytical.LoadType"/> this row's <see cref="LoadType"/> text maps to.</summary>
        public LoadType SamLoadType
        {
            get
            {
                if (string.Equals(LoadType, "Heating", System.StringComparison.OrdinalIgnoreCase))
                {
                    return Analytical.LoadType.Heating;
                }

                return string.Equals(LoadType, "Cooling", System.StringComparison.OrdinalIgnoreCase)
                    ? Analytical.LoadType.Cooling
                    : Analytical.LoadType.Undefined;
            }
        }

        /// <summary>A stable ordinal sort/dedup key: <c>ZoneName|LoadType</c>.</summary>
        public string Key => (ZoneName ?? string.Empty) + "|" + (LoadType ?? string.Empty);
    }
}
