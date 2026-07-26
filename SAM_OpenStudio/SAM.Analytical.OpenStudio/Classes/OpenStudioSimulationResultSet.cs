// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;

namespace SAM.Analytical.OpenStudio
{
    /// <summary>
    /// Engine-neutral simulation result set extracted from one EnergyPlus run (C5). All annual
    /// energies are kWh over the weather RunPeriod only (sizing periods excluded), peak loads
    /// are kW with their hour-of-year index, unmet hours are hours, gains are annual kWh per
    /// category. All per-zone dictionaries share ONE zone identity: the Ideal Loads system
    /// report key (zone-level and enclosure variables are remapped onto it by their shared SAM
    /// Guid suffix during extraction). The SAM Guid ↔ OpenStudio identity lives on the
    /// conversion result's ObjectMap. Zone temperature/operative/humidity series are populated
    /// only when requested (OpenStudioRunOptions.ExtractTimeSeries). Immutable.
    /// </summary>
    public sealed class OpenStudioSimulationResultSet
    {
        /// <summary>
        /// Zone sizing outcomes read from the SQL <c>ZoneSizes</c> table — one entry per zone and load
        /// type, produced by the DESIGN-DAY sizing periods. Empty when the run performed no sizing (or
        /// reported none), never null. Keyed by the EnergyPlus ThermalZone name, unlike the annual
        /// dictionaries above which key on the Ideal Loads system name: the sizing table is written by
        /// the zone sizing calculation, not by a system output variable. Ordered deterministically by
        /// <see cref="OpenStudioZoneSizingResult.Key"/>.
        /// </summary>
        public IReadOnlyList<OpenStudioZoneSizingResult> ZoneSizing { get; }

        /// <summary>Annual heating energy per zone key [kWh].</summary>
        public IReadOnlyDictionary<string, double> AnnualHeatingEnergy { get; }

        /// <summary>Annual cooling energy per zone key [kWh].</summary>
        public IReadOnlyDictionary<string, double> AnnualCoolingEnergy { get; }

        /// <summary>Peak heating load per zone key [kW] (max hour-average power over the weather run).</summary>
        public IReadOnlyDictionary<string, double> PeakHeatingLoad { get; }

        /// <summary>Peak cooling load per zone key [kW].</summary>
        public IReadOnlyDictionary<string, double> PeakCoolingLoad { get; }

        /// <summary>Hour-of-year (0-based) of the heating peak per zone key.</summary>
        public IReadOnlyDictionary<string, int> PeakHeatingHour { get; }

        /// <summary>Hour-of-year (0-based) of the cooling peak per zone key.</summary>
        public IReadOnlyDictionary<string, int> PeakCoolingHour { get; }

        /// <summary>Building-level (coincident) peak heating load [kW] and its hour-of-year.</summary>
        public double PeakHeatingLoadTotal { get; }

        /// <summary>Hour-of-year of the building-level heating peak.</summary>
        public int PeakHeatingHourTotal { get; }

        /// <summary>Building-level (coincident) peak cooling load [kW] and its hour-of-year.</summary>
        public double PeakCoolingLoadTotal { get; }

        /// <summary>Hour-of-year of the building-level cooling peak.</summary>
        public int PeakCoolingHourTotal { get; }

        /// <summary>Heating setpoint not-met hours per zone key [h].</summary>
        public IReadOnlyDictionary<string, double> UnmetHeatingHours { get; }

        /// <summary>Cooling setpoint not-met hours per zone key [h].</summary>
        public IReadOnlyDictionary<string, double> UnmetCoolingHours { get; }

        /// <summary>Annual people gains per zone key [kWh].</summary>
        public IReadOnlyDictionary<string, double> PeopleGains { get; }

        /// <summary>Annual lighting gains per zone key [kWh].</summary>
        public IReadOnlyDictionary<string, double> LightingGains { get; }

        /// <summary>Annual electric equipment gains per zone key [kWh].</summary>
        public IReadOnlyDictionary<string, double> EquipmentGains { get; }

        /// <summary>Annual transmitted window solar gains per zone key [kWh].</summary>
        public IReadOnlyDictionary<string, double> WindowSolarGains { get; }

        /// <summary>Annual net infiltration gains (gain − loss) per zone key [kWh].</summary>
        public IReadOnlyDictionary<string, double> InfiltrationGains { get; }

        /// <summary>Annual outdoor-air sensible heating energy per zone key [kWh].</summary>
        public IReadOnlyDictionary<string, double> VentilationHeatingEnergy { get; }

        /// <summary>Annual outdoor-air sensible cooling energy per zone key [kWh].</summary>
        public IReadOnlyDictionary<string, double> VentilationCoolingEnergy { get; }

        /// <summary>Hourly zone mean air temperature series per zone key [°C]; null when series were not requested.</summary>
        public IReadOnlyDictionary<string, double[]> TemperatureSeries { get; }

        /// <summary>Hourly zone operative temperature series per zone key [°C]; null when series were not requested.</summary>
        public IReadOnlyDictionary<string, double[]> OperativeTemperatureSeries { get; }

        /// <summary>Hourly zone relative humidity series per zone key [%]; null when series were not requested.</summary>
        public IReadOnlyDictionary<string, double[]> RelativeHumiditySeries { get; }

        /// <summary>CLI wall-clock runtime [s].</summary>
        public double RuntimeSeconds { get; }

        /// <summary>EnergyPlus warning count (eplusout.err).</summary>
        public int WarningCount { get; }

        /// <summary>EnergyPlus severe error count.</summary>
        public int SevereCount { get; }

        /// <summary>EnergyPlus fatal error count.</summary>
        public int FatalCount { get; }

        /// <summary>Creates an immutable result set. Null dictionaries become empty; null series stay null.</summary>
        public OpenStudioSimulationResultSet(
            IReadOnlyDictionary<string, double> annualHeatingEnergy,
            IReadOnlyDictionary<string, double> annualCoolingEnergy,
            IReadOnlyDictionary<string, double> peakHeatingLoad,
            IReadOnlyDictionary<string, double> peakCoolingLoad,
            IReadOnlyDictionary<string, int> peakHeatingHour,
            IReadOnlyDictionary<string, int> peakCoolingHour,
            double peakHeatingLoadTotal,
            int peakHeatingHourTotal,
            double peakCoolingLoadTotal,
            int peakCoolingHourTotal,
            IReadOnlyDictionary<string, double> unmetHeatingHours,
            IReadOnlyDictionary<string, double> unmetCoolingHours,
            IReadOnlyDictionary<string, double> peopleGains,
            IReadOnlyDictionary<string, double> lightingGains,
            IReadOnlyDictionary<string, double> equipmentGains,
            IReadOnlyDictionary<string, double> windowSolarGains,
            IReadOnlyDictionary<string, double> infiltrationGains,
            IReadOnlyDictionary<string, double> ventilationHeatingEnergy,
            IReadOnlyDictionary<string, double> ventilationCoolingEnergy,
            IReadOnlyDictionary<string, double[]> temperatureSeries,
            IReadOnlyDictionary<string, double[]> operativeTemperatureSeries,
            IReadOnlyDictionary<string, double[]> relativeHumiditySeries,
            double runtimeSeconds,
            int warningCount,
            int severeCount,
            int fatalCount,
            IReadOnlyList<OpenStudioZoneSizingResult> zoneSizing = null)
        {
            ZoneSizing = Copy(zoneSizing);
            AnnualHeatingEnergy = Copy(annualHeatingEnergy);
            AnnualCoolingEnergy = Copy(annualCoolingEnergy);
            PeakHeatingLoad = Copy(peakHeatingLoad);
            PeakCoolingLoad = Copy(peakCoolingLoad);
            PeakHeatingHour = Copy(peakHeatingHour);
            PeakCoolingHour = Copy(peakCoolingHour);
            PeakHeatingLoadTotal = peakHeatingLoadTotal;
            PeakHeatingHourTotal = peakHeatingHourTotal;
            PeakCoolingLoadTotal = peakCoolingLoadTotal;
            PeakCoolingHourTotal = peakCoolingHourTotal;
            UnmetHeatingHours = Copy(unmetHeatingHours);
            UnmetCoolingHours = Copy(unmetCoolingHours);
            PeopleGains = Copy(peopleGains);
            LightingGains = Copy(lightingGains);
            EquipmentGains = Copy(equipmentGains);
            WindowSolarGains = Copy(windowSolarGains);
            InfiltrationGains = Copy(infiltrationGains);
            VentilationHeatingEnergy = Copy(ventilationHeatingEnergy);
            VentilationCoolingEnergy = Copy(ventilationCoolingEnergy);
            TemperatureSeries = temperatureSeries == null ? null : Copy(temperatureSeries);
            OperativeTemperatureSeries = operativeTemperatureSeries == null ? null : Copy(operativeTemperatureSeries);
            RelativeHumiditySeries = relativeHumiditySeries == null ? null : Copy(relativeHumiditySeries);
            RuntimeSeconds = runtimeSeconds;
            WarningCount = warningCount;
            SevereCount = severeCount;
            FatalCount = fatalCount;
        }

        /// <summary>Total annual heating energy over all zones [kWh].</summary>
        public double TotalAnnualHeating
        {
            get { return Sum(AnnualHeatingEnergy); }
        }

        /// <summary>Total annual cooling energy over all zones [kWh].</summary>
        public double TotalAnnualCooling
        {
            get { return Sum(AnnualCoolingEnergy); }
        }

        /// <summary>
        /// Defensive copy in a TOTAL, input-order-independent order: by
        /// <see cref="OpenStudioZoneSizingResult.Key"/>, then by
        /// <see cref="OpenStudioZoneSizingResult.SourceIndex"/>. Null entries are dropped.
        /// </summary>
        /// <remarks>
        /// The tie-breaker is load-bearing, not decoration. Two rows sharing a zone and load type have the
        /// same key, so ordering on the key alone would leave their relative order to
        /// <see cref="List{T}.Sort(System.Comparison{T})"/>, which is NOT stable — the row a consumer then
        /// treats as "first" would depend on the order the database returned. Ordering also by the source
        /// index (the SQL <c>ZoneSizesIndex</c>) makes the sequence reproducible for identical data
        /// whatever order it arrived in.
        /// </remarks>
        private static List<OpenStudioZoneSizingResult> Copy(IReadOnlyList<OpenStudioZoneSizingResult> source)
        {
            List<OpenStudioZoneSizingResult> result = new List<OpenStudioZoneSizingResult>();
            if (source != null)
            {
                foreach (OpenStudioZoneSizingResult zoneSizingResult in source)
                {
                    if (zoneSizingResult != null)
                    {
                        result.Add(zoneSizingResult);
                    }
                }
            }

            result.Sort((left, right) =>
            {
                int byKey = string.CompareOrdinal(left.Key, right.Key);
                return byKey != 0 ? byKey : left.SourceIndex.CompareTo(right.SourceIndex);
            });

            return result;
        }

        private static Dictionary<string, double> Copy(IReadOnlyDictionary<string, double> source)
        {
            return source == null ? new Dictionary<string, double>() : new Dictionary<string, double>(System.Linq.Enumerable.ToDictionary(source, x => x.Key, x => x.Value));
        }

        private static Dictionary<string, int> Copy(IReadOnlyDictionary<string, int> source)
        {
            return source == null ? new Dictionary<string, int>() : new Dictionary<string, int>(System.Linq.Enumerable.ToDictionary(source, x => x.Key, x => x.Value));
        }

        private static Dictionary<string, double[]> Copy(IReadOnlyDictionary<string, double[]> source)
        {
            Dictionary<string, double[]> result = new Dictionary<string, double[]>();
            foreach (KeyValuePair<string, double[]> keyValuePair in source)
            {
                result[keyValuePair.Key] = keyValuePair.Value == null ? null : (double[])keyValuePair.Value.Clone();
            }

            return result;
        }

        private static double Sum(IReadOnlyDictionary<string, double> source)
        {
            double result = 0;
            foreach (KeyValuePair<string, double> keyValuePair in source)
            {
                result += keyValuePair.Value;
            }

            return result;
        }
    }
}
