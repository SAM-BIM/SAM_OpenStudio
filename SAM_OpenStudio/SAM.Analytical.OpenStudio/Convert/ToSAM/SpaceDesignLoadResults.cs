// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Maps the run's zone sizing outcomes (<see cref="OpenStudioSimulationResultSet.ZoneSizing"/>,
        /// read from the SQL <c>ZoneSizes</c> table) onto SAM <see cref="SpaceSimulationResult"/>s
        /// carrying <see cref="Analytical.SpaceSimulationResultParameter.DesignLoad"/>, one per space and
        /// load type. These are SIZING results and are deliberately separate from the annual results
        /// produced by <see cref="ToSAM_SpaceSimulationResults(OpenStudioSimulationResultSet, IEnumerable{Space})"/>:
        /// this mapping never writes <c>Load</c> or <c>LoadIndex</c>, so an annual simulated peak can
        /// never be relabelled as a design load.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="OpenStudioZoneSizingResult.CalculatedDesignLoad"/> populates the design load — the
        /// unaltered thermal load EnergyPlus calculated from the design-day weather and schedules, as the
        /// result contract in <c>docs/SAM_OPENSTUDIO_ANALYTICAL_COVERAGE.md</c> specifies.
        /// <see cref="OpenStudioZoneSizingResult.UserDesignLoad"/> is deliberately NOT emitted: it is the
        /// capacity after sizing factors, so emitting it would fold a user-configured margin into a
        /// cross-engine comparison — on the HungaryHouse run a 1.25 heating factor made every UserDesLoad
        /// exactly 25% above the calculated load. It stays on the result set, with the design flows, for
        /// the sizing-capacity audit, and may later become a metric of its own.
        /// </para>
        /// <para>
        /// Identity: <c>ZoneSizes.ZoneName</c> is the EnergyPlus ThermalZone name, so spaces are matched
        /// on <c>Core.OpenStudio.Query.OpenStudioName("ThermalZone", …)</c> — NOT the Ideal Loads key the
        /// annual dictionaries use. Heating and cooling are matched independently: a zone sized for
        /// heating only yields a heating design load and nothing for cooling. A row with no
        /// <c>UserDesLoad</c> yields no result, so a missing design load stays unavailable and is never
        /// reported as a zero. A genuine sized zero IS emitted as an available zero.
        /// </para>
        /// </remarks>
        /// <param name="openStudioSimulationResultSet">Result set carrying the zone sizing rows.</param>
        /// <param name="spaces">Source SAM spaces to map onto; null yields an empty list.</param>
        /// <returns>Design-load results, ordered by space then load type; never null.</returns>
        public static List<SpaceSimulationResult> ToSAM_SpaceDesignLoadResults(this OpenStudioSimulationResultSet openStudioSimulationResultSet, IEnumerable<Space> spaces)
        {
            return ToSAM_SpaceDesignLoadResults(openStudioSimulationResultSet, spaces, out List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics);
        }

        /// <summary>
        /// As <see cref="ToSAM_SpaceDesignLoadResults(OpenStudioSimulationResultSet, IEnumerable{Space})"/>,
        /// additionally reporting what could not be mapped: EnergyPlus sizing rows matching no source
        /// space, and duplicate <c>(ZoneName, LoadType)</c> rows. Both are silent data loss otherwise.
        /// </summary>
        /// <param name="openStudioSimulationResultSet">Result set carrying the zone sizing rows.</param>
        /// <param name="spaces">Source SAM spaces to map onto; null yields an empty list.</param>
        /// <param name="diagnostics">Unmatched-zone and duplicate-row diagnostics; never null.</param>
        public static List<SpaceSimulationResult> ToSAM_SpaceDesignLoadResults(this OpenStudioSimulationResultSet openStudioSimulationResultSet, IEnumerable<Space> spaces, out List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics)
        {
            diagnostics = new List<Core.OpenStudio.OpenStudioDiagnostic>();
            List<SpaceSimulationResult> result = new List<SpaceSimulationResult>();
            if (openStudioSimulationResultSet?.ZoneSizing == null || openStudioSimulationResultSet.ZoneSizing.Count == 0 || spaces == null)
            {
                return result;
            }

            // Index the sizing rows by zone name + load type. A duplicate pair is a contract surprise
            // (EnergyPlus writes one row per zone/load type): the FIRST row in the result set's
            // deterministic order wins and the collision is named, rather than one being picked
            // silently or the pair being dropped.
            var byKey = new Dictionary<string, OpenStudioZoneSizingResult>(StringComparer.OrdinalIgnoreCase);
            foreach (OpenStudioZoneSizingResult zoneSizingResult in openStudioSimulationResultSet.ZoneSizing)
            {
                if (zoneSizingResult == null || string.IsNullOrWhiteSpace(zoneSizingResult.ZoneName))
                {
                    continue;
                }

                string key = zoneSizingResult.Key;
                if (byKey.TryGetValue(key, out OpenStudioZoneSizingResult existing))
                {
                    diagnostics.Add(new Core.OpenStudio.OpenStudioDiagnostic(
                        Core.OpenStudio.OpenStudioDiagnosticCodes.ResultExtractionLimitation,
                        Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning,
                        string.Format(System.Globalization.CultureInfo.InvariantCulture, "Duplicate zone sizing row for zone '{0}' load type '{1}': the first row (design load {2}) is used and the later one (design load {3}) is ignored", zoneSizingResult.ZoneName, zoneSizingResult.LoadType, Text(existing.CalculatedDesignLoad), Text(zoneSizingResult.CalculatedDesignLoad)),
                        openStudioObjectName: zoneSizingResult.ZoneName));
                    continue;
                }

                byKey[key] = zoneSizingResult;
            }

            var matchedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Space space in spaces)
            {
                if (space == null)
                {
                    continue;
                }

                string thermalZoneName = Core.OpenStudio.Query.OpenStudioName("ThermalZone", space.Name, space.Guid);
                string reference = space.Guid.ToString("N");

                AddDesignLoadResult(result, matchedKeys, byKey, thermalZoneName, space.Name, reference, LoadType.Heating);
                AddDesignLoadResult(result, matchedKeys, byKey, thermalZoneName, space.Name, reference, LoadType.Cooling);
            }

            // Sizing rows that belong to no source space: the model and the engine disagree about zone
            // identity, which would otherwise silently lose a design load.
            foreach (KeyValuePair<string, OpenStudioZoneSizingResult> keyValuePair in byKey)
            {
                if (matchedKeys.Contains(keyValuePair.Key))
                {
                    continue;
                }

                diagnostics.Add(new Core.OpenStudio.OpenStudioDiagnostic(
                    Core.OpenStudio.OpenStudioDiagnosticCodes.ResultExtractionLimitation,
                    Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning,
                    string.Format("EnergyPlus reported a {0} zone sizing result for zone '{1}', which matches no source SAM space; its design load is not mapped", keyValuePair.Value.LoadType, keyValuePair.Value.ZoneName),
                    openStudioObjectName: keyValuePair.Value.ZoneName));
            }

            return result;
        }

        private static void AddDesignLoadResult(
            List<SpaceSimulationResult> result,
            HashSet<string> matchedKeys,
            Dictionary<string, OpenStudioZoneSizingResult> byKey,
            string thermalZoneName,
            string spaceName,
            string reference,
            LoadType loadType)
        {
            string key = thermalZoneName + "|" + loadType.ToString();
            if (!byKey.TryGetValue(key, out OpenStudioZoneSizingResult zoneSizingResult))
            {
                return;
            }

            matchedKeys.Add(key);

            // No sized load for this zone/load type: leave it unavailable. A sized ZERO is a real
            // result and IS emitted (double? distinguishes the two).
            if (!zoneSizingResult.CalculatedDesignLoad.HasValue)
            {
                return;
            }

            SpaceSimulationResult spaceSimulationResult = new SpaceSimulationResult(spaceName, Query.Source(), reference);

            // LoadType is written as text, exactly as the annual mapping does, so both result families
            // are selected by the same predicate downstream.
            spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.LoadType, loadType.ToString());
            spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.DesignLoad, zoneSizingResult.CalculatedDesignLoad.Value);
            spaceSimulationResult.SetValue(SpaceSimulationResultParameter.ZoneName, zoneSizingResult.ZoneName);

            if (!string.IsNullOrWhiteSpace(zoneSizingResult.DesignDayName))
            {
                spaceSimulationResult.SetValue(SpaceSimulationResultParameter.DesignDayName, zoneSizingResult.DesignDayName);
            }

            // ZoneSizes.PeakTemp is deliberately NOT written to DesignDayTemperature. Its scope is not
            // settled: the EnergyPlus engineering reference describes the zone sizing peak temperature as
            // a ZONE value (ZoneTempAtHeatPeak/ZoneTempAtCoolPeak), while on a real run here every row
            // carried -3.20000004768372, bit-identical to the design day's OUTDOOR maximum dry bulb.
            // Writing an unsettled value into a parameter that names it an outdoor design-day temperature
            // would be exactly the silent relabelling this mapping exists to avoid, so the value stays on
            // OpenStudioZoneSizingResult verbatim for the design-day audit to interpret.

            if (!string.IsNullOrWhiteSpace(zoneSizingResult.PeakTime))
            {
                Core.OpenStudio.ShortDateTime shortDateTime = Core.OpenStudio.Create.ShortDateTime(zoneSizingResult.PeakTime);
                if (shortDateTime != null)
                {
                    spaceSimulationResult.SetValue(SpaceSimulationResultParameter.PeakDate, shortDateTime);
                }
            }

            result.Add(spaceSimulationResult);
        }

        private static string Text(double? value)
        {
            return value.HasValue ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unavailable";
        }
    }
}
