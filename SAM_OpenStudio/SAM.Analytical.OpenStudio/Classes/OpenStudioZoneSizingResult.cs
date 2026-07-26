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
        /// <param name="peakTemperature">SQL <c>ZoneSizes.PeakTemp</c> verbatim [C]; see the property for its unsettled scope.</param>
        /// <param name="peakHumidityRatio">Humidity ratio at the peak [kgWater/kgDryAir].</param>
        /// <param name="sourceIndex">
        /// SQL <c>ZoneSizes.ZoneSizesIndex</c> (the table's primary key), or any caller-assigned read
        /// order. It exists solely to make ordering TOTAL: <see cref="Key"/> alone cannot separate two
        /// rows that share a zone and load type, so without this the surviving row of a duplicate pair
        /// would depend on the order the database happened to return.
        /// </param>
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
            double? peakHumidityRatio,
            long sourceIndex = 0)
        {
            SourceIndex = sourceIndex;
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

        /// <summary>
        /// SQL <c>ZoneSizes.PeakTemp</c> [C], carried verbatim and deliberately NOT interpreted.
        /// </summary>
        /// <remarks>
        /// Its scope is unsettled and must not be asserted by this type. The EnergyPlus engineering
        /// reference describes the zone sizing peak temperature as a ZONE value
        /// (<c>ZoneTempAtHeatPeak</c>/<c>ZoneTempAtCoolPeak</c>); however, on the HungaryHouse benchmark
        /// run every row carried <c>-3.20000004768372</c>, bit-identical to the design day's OUTDOOR
        /// maximum dry bulb, where a heated zone would have sat at its setpoint instead. Until the
        /// design-day audit settles which it is, no consumer should map this onto an outdoor or a zone
        /// temperature parameter — which is why <c>Convert.ToSAM_SpaceDesignLoadResults</c> does not.
        /// </remarks>
        public double? PeakTemperature { get; }

        /// <summary>SQL <c>ZoneSizes.PeakHumRat</c> [kgWater/kgDryAir], carried verbatim; same scope caveat as <see cref="PeakTemperature"/>.</summary>
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

        /// <summary>
        /// The row's source order (SQL <c>ZoneSizesIndex</c> where available). Used only as the ordering
        /// tie-breaker for rows sharing a <see cref="Key"/>; it is not part of the result's identity.
        /// </summary>
        public long SourceIndex { get; }

        /// <summary>
        /// The ordinal grouping key <c>ZoneName|LoadType</c>. NOT unique on its own: EnergyPlus writes one
        /// row per zone and load type, but a duplicate pair is possible, which is why ordering uses
        /// <see cref="SortSignature"/> and duplicates are reported rather than silently resolved.
        /// </summary>
        public string Key => (ZoneName ?? string.Empty) + "|" + (LoadType ?? string.Empty);

        /// <summary>
        /// A TOTAL ordinal ordering key: <see cref="Key"/>, then <see cref="SourceIndex"/>, then every
        /// payload field. Ordering on the key and index alone is not enough — a caller that does not
        /// supply a source index leaves duplicates sharing the default, the comparison then reports
        /// equality, and because <see cref="List{T}.Sort(System.Comparison{T})"/> is unstable the row a
        /// consumer treats as "first" would flip with the input order. Including the payload means two
        /// entries can only compare equal when they are genuinely indistinguishable, so which one is
        /// chosen cannot matter.
        /// </summary>
        public string SortSignature => string.Join("|", new string[]
        {
            Key,
            SourceIndex.ToString("D19", System.Globalization.CultureInfo.InvariantCulture),
            Number(UserDesignLoad),
            Number(CalculatedDesignLoad),
            Number(UserDesignFlow),
            Number(CalculatedDesignFlow),
            DesignDayName ?? string.Empty,
            PeakTime ?? string.Empty,
            Number(PeakTemperature),
            Number(PeakHumidityRatio)
        });

        /// <summary>Invariant, round-trippable text for a nullable number; empty for null.</summary>
        private static string Number(double? value)
        {
            return value.HasValue ? value.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
        }
    }
}
