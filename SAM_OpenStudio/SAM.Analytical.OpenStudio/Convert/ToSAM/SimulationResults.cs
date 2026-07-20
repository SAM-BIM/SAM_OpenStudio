// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Maps an engine-neutral result set to a SAM AnalyticalModelSimulationResult (C5):
        /// annual heating/cooling consumption [kWh], coincident peak loads [kW] with their
        /// hour-of-year, and the model floor area/volume derived from the source model.
        /// </summary>
        /// <param name="openStudioSimulationResultSet">Extracted result set.</param>
        /// <param name="analyticalModel">Source model (floor area/volume derivation); may be null.</param>
        /// <returns>The SAM result, or null when the result set is null.</returns>
        public static AnalyticalModelSimulationResult ToSAM(this OpenStudioSimulationResultSet openStudioSimulationResultSet, AnalyticalModel analyticalModel)
        {
            if (openStudioSimulationResultSet == null)
            {
                return null;
            }

            AnalyticalModelSimulationResult result = new AnalyticalModelSimulationResult(analyticalModel?.Name, Query.Source(), analyticalModel?.Guid.ToString("N"));
            result.SetValue(AnalyticalModelSimulationResultParameter.ConsumptionHeating, openStudioSimulationResultSet.TotalAnnualHeating);
            result.SetValue(AnalyticalModelSimulationResultParameter.ConsumptionCooling, openStudioSimulationResultSet.TotalAnnualCooling);
            result.SetValue(AnalyticalModelSimulationResultParameter.PeakHeatingLoad, openStudioSimulationResultSet.PeakHeatingLoadTotal);
            result.SetValue(AnalyticalModelSimulationResultParameter.PeakHeatingHour, openStudioSimulationResultSet.PeakHeatingHourTotal);
            result.SetValue(AnalyticalModelSimulationResultParameter.PeakCoolingLoad, openStudioSimulationResultSet.PeakCoolingLoadTotal);
            result.SetValue(AnalyticalModelSimulationResultParameter.PeakCoolingHour, openStudioSimulationResultSet.PeakCoolingHourTotal);

            AdjacencyCluster adjacencyCluster = analyticalModel?.AdjacencyCluster;
            if (adjacencyCluster != null)
            {
                double floorArea = 0;
                double volume = 0;
                List<Space> spaces = adjacencyCluster.GetSpaces();
                if (spaces != null)
                {
                    foreach (Space space in spaces)
                    {
                        if (space == null)
                        {
                            continue;
                        }

                        if (space.TryGetValue(SpaceParameter.Area, out double area) && !double.IsNaN(area))
                        {
                            floorArea += area;
                        }

                        if (space.TryGetValue(SpaceParameter.Volume, out double spaceVolume) && !double.IsNaN(spaceVolume))
                        {
                            volume += spaceVolume;
                        }
                    }
                }

                result.SetValue(AnalyticalModelSimulationResultParameter.FloorArea, floorArea);
                result.SetValue(AnalyticalModelSimulationResultParameter.Volume, volume);
            }

            return result;
        }

        /// <summary>
        /// Maps an engine-neutral result set to per-space SAM SpaceSimulationResults (C5), one
        /// per load type (Heating/Cooling, matching the established SAM per-LoadType pattern):
        /// peak load [W] with its hour-of-year and the setpoint not-met hours. Energy and peak
        /// dictionaries key on the Ideal Loads system name; unmet-hour dictionaries key on the
        /// ThermalZone name — both are reconstructed deterministically per SAM space
        /// (SAM_&lt;type&gt;_&lt;space&gt;_&lt;guid8&gt;, uppercased by EnergyPlus). A zone that
        /// matches no space is reported in the result's source string count, never silently
        /// merged.
        /// </summary>
        /// <param name="openStudioSimulationResultSet">Extracted result set.</param>
        /// <param name="analyticalModel">Source model providing the spaces; may be null (keys then stay unmapped).</param>
        /// <returns>Per-space results (possibly empty), or null when the result set is null.</returns>
        public static List<SpaceSimulationResult> ToSAM_SpaceSimulationResults(this OpenStudioSimulationResultSet openStudioSimulationResultSet, AnalyticalModel analyticalModel)
        {
            if (openStudioSimulationResultSet == null)
            {
                return null;
            }

            string source = Query.Source();
            List<SpaceSimulationResult> result = new List<SpaceSimulationResult>();

            List<Space> spaces = analyticalModel?.AdjacencyCluster?.GetSpaces();
            if (spaces == null)
            {
                // No source model: emit keyed-but-unmapped results per energy zone key.
                HashSet<string> zoneKeys = new HashSet<string>(openStudioSimulationResultSet.AnnualHeatingEnergy.Keys, System.StringComparer.OrdinalIgnoreCase);
                zoneKeys.UnionWith(openStudioSimulationResultSet.AnnualCoolingEnergy.Keys);
                foreach (string zoneKey in zoneKeys)
                {
                    AddLoadTypeResult(result, zoneKey, zoneKey, zoneKey, source, LoadType.Heating, openStudioSimulationResultSet.PeakHeatingLoad, openStudioSimulationResultSet.PeakHeatingHour, null);
                    AddLoadTypeResult(result, zoneKey, zoneKey, zoneKey, source, LoadType.Cooling, openStudioSimulationResultSet.PeakCoolingLoad, openStudioSimulationResultSet.PeakCoolingHour, null);
                }

                return result;
            }

            foreach (Space space in spaces)
            {
                if (space == null)
                {
                    continue;
                }

                string idealLoadsKey = Core.OpenStudio.Query.OpenStudioName("IdealLoads", space.Name, space.Guid);
                if (!ContainsKey(openStudioSimulationResultSet.AnnualHeatingEnergy, idealLoadsKey) && !ContainsKey(openStudioSimulationResultSet.AnnualCoolingEnergy, idealLoadsKey))
                {
                    continue;
                }

                string reference = space.Guid.ToString("N");

                AddLoadTypeResult(result, idealLoadsKey, space.Name, reference, source, LoadType.Heating, openStudioSimulationResultSet.PeakHeatingLoad, openStudioSimulationResultSet.PeakHeatingHour, UnmetForZone(openStudioSimulationResultSet.UnmetHeatingHours, idealLoadsKey));
                AddLoadTypeResult(result, idealLoadsKey, space.Name, reference, source, LoadType.Cooling, openStudioSimulationResultSet.PeakCoolingLoad, openStudioSimulationResultSet.PeakCoolingHour, UnmetForZone(openStudioSimulationResultSet.UnmetCoolingHours, idealLoadsKey));
            }

            return result;
        }

        private static bool ContainsKey(IReadOnlyDictionary<string, double> dictionary, string key)
        {
            double value;
            return TryGetValue(dictionary, key, out value);
        }

        private static bool TryGetValue(IReadOnlyDictionary<string, double> dictionary, string key, out double value)
        {
            value = 0;
            if (dictionary == null || key == null)
            {
                return false;
            }

            if (dictionary.TryGetValue(key, out value))
            {
                return true;
            }

            // EnergyPlus uppercases report keys; the deterministic SAM names are mixed case.
            foreach (KeyValuePair<string, double> keyValuePair in dictionary)
            {
                if (string.Equals(keyValuePair.Key, key, System.StringComparison.OrdinalIgnoreCase))
                {
                    value = keyValuePair.Value;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetValue(IReadOnlyDictionary<string, int> dictionary, string key, out int value)
        {
            value = -1;
            if (dictionary == null || key == null)
            {
                return false;
            }

            if (dictionary.TryGetValue(key, out value))
            {
                return true;
            }

            foreach (KeyValuePair<string, int> keyValuePair in dictionary)
            {
                if (string.Equals(keyValuePair.Key, key, System.StringComparison.OrdinalIgnoreCase))
                {
                    value = keyValuePair.Value;
                    return true;
                }
            }

            return false;
        }

        private static double? UnmetForZone(IReadOnlyDictionary<string, double> unmetHours, string thermalZoneKey)
        {
            double unmet;
            return TryGetValue(unmetHours, thermalZoneKey, out unmet) ? unmet : (double?)null;
        }

        private static void AddLoadTypeResult(List<SpaceSimulationResult> results, string zoneKey, string spaceName, string reference, string source, LoadType loadType, IReadOnlyDictionary<string, double> peakLoads, IReadOnlyDictionary<string, int> peakHours, double? unmetHours)
        {
            double peakLoadKilowatts;
            if (!TryGetValue(peakLoads, zoneKey, out peakLoadKilowatts))
            {
                return;
            }

            SpaceSimulationResult spaceSimulationResult = new SpaceSimulationResult(spaceName, source, reference);
            spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.LoadType, loadType.ToString());
            spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.Load, peakLoadKilowatts * 1000.0);

            int peakHour;
            if (TryGetValue(peakHours, zoneKey, out peakHour))
            {
                spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.LoadIndex, peakHour);
            }

            if (unmetHours.HasValue)
            {
                spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.UnmetHours, unmetHours.Value);
            }

            results.Add(spaceSimulationResult);
        }
    }
}
