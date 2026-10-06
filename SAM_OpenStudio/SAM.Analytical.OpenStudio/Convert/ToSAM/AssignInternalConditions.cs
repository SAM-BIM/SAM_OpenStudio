// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Assigns an <see cref="InternalCondition"/> to every imported SAM space and adds the
        /// zone-level controls — heating/cooling setpoints, humidistat setpoints and the
        /// conditioning state — that only become available once spaces and zones are paired.
        /// <para>
        /// Setpoints live on the ThermalZone, not the SpaceType, so the shared space-type
        /// condition is cloned per space before any zone data is written; otherwise one zone's
        /// thermostat would leak into every space of that type.
        /// </para>
        /// <para>
        /// SAM stores setpoints as profiles whose values are the temperatures themselves, so a
        /// dual-setpoint thermostat maps across without approximation — including setback
        /// schedules, which survive as the profile's hourly variation.
        /// </para>
        /// </summary>
        /// <param name="openStudioImportContext">Import context.</param>
        /// <param name="adjacencyCluster">Topology whose spaces receive the conditions.</param>
        /// <param name="profileLibrary">Library receiving the setpoint profiles created here.</param>
        public static void ToSAM_AssignInternalConditions(this OpenStudioImportContext openStudioImportContext, AdjacencyCluster adjacencyCluster, ProfileLibrary profileLibrary)
        {
            if (openStudioImportContext == null || adjacencyCluster == null)
            {
                return;
            }

            global::OpenStudio.SpaceVector spaceVector;
            try
            {
                spaceVector = openStudioImportContext.Source.getSpaces();
            }
            catch (Exception)
            {
                return;
            }

            if (spaceVector == null)
            {
                return;
            }

            foreach (global::OpenStudio.Space openStudioSpace in spaceVector)
            {
                if (openStudioSpace == null)
                {
                    continue;
                }

                Space space;
                if (!openStudioImportContext.SpaceMap.TryGetValue(openStudioSpace.nameString(), out space) || space == null)
                {
                    continue;
                }

                InternalCondition internalCondition = ResolveInternalCondition(openStudioSpace, openStudioImportContext);
                if (internalCondition == null)
                {
                    continue;
                }

                // Clone before writing zone data: the space-type condition is shared, and zone
                // setpoints are per zone.
                internalCondition = new InternalCondition(Guid.NewGuid(), internalCondition);

                ApplyZoneControls(openStudioSpace, internalCondition, openStudioImportContext, profileLibrary);

                space.InternalCondition = internalCondition;

                // Space.InternalCondition clones again with a new Guid, so the cluster copy must
                // be refreshed for the assignment to be visible on the imported topology.
                adjacencyCluster.AddObject(space);
            }
        }

        /// <summary>
        /// The SAM internal condition for a space: its space type's condition when it has one,
        /// otherwise a bare condition named after the space so the zone controls have somewhere
        /// to live. A space with no space type but with its own loads is reported — those loads
        /// are not imported, because SAM carries loads on the internal condition only.
        /// </summary>
        private static InternalCondition ResolveInternalCondition(global::OpenStudio.Space openStudioSpace, OpenStudioImportContext openStudioImportContext)
        {
            global::OpenStudio.OptionalSpaceType optionalSpaceType = openStudioSpace.spaceType;
            if (optionalSpaceType != null && !optionalSpaceType.isNull())
            {
                string spaceTypeName = optionalSpaceType.get().nameString();

                InternalCondition internalCondition;
                if (openStudioImportContext.InternalConditionMap.TryGetValue(spaceTypeName, out internalCondition))
                {
                    ReportSpaceLevelOverrides(openStudioSpace, openStudioImportContext, spaceTypeName);
                    return internalCondition;
                }
            }

            ReportSpaceLevelOverrides(openStudioSpace, openStudioImportContext, null);

            return new InternalCondition(openStudioSpace.nameString());
        }

        /// <summary>
        /// Reports loads defined directly on a space rather than on its space type. SAM has no
        /// per-space load layer on top of an internal condition, so these are not imported;
        /// saying so is the difference between an incomplete model and a wrong one.
        /// </summary>
        private static void ReportSpaceLevelOverrides(global::OpenStudio.Space openStudioSpace, OpenStudioImportContext openStudioImportContext, string spaceTypeName)
        {
            int count = 0;
            count += openStudioSpace.people()?.Count ?? 0;
            count += openStudioSpace.lights()?.Count ?? 0;
            count += openStudioSpace.electricEquipment()?.Count ?? 0;
            count += openStudioSpace.spaceInfiltrationDesignFlowRates()?.Count ?? 0;

            if (count == 0)
            {
                return;
            }

            string label = OpenStudioImportContext.OpenStudioObjectLabel(openStudioSpace);

            if (spaceTypeName == null)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.LoadUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("The space carries {0} load(s) directly and belongs to no space type; SAM holds loads on the internal condition only, so these loads were not imported", count), label);
            }
            else
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.LoadAssignmentConflict, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("The space carries {0} load(s) directly, in addition to those of space type '{1}'; SAM has no per-space load layer, so only the space type's loads were imported and the space-level ones are missing", count, spaceTypeName), label);
            }

            openStudioImportContext.RegisterSkip();
        }

        /// <summary>
        /// Writes the zone's thermostat and humidistat setpoints onto the space's internal
        /// condition and reports detailed HVAC that is deliberately not imported.
        /// </summary>
        private static void ApplyZoneControls(global::OpenStudio.Space openStudioSpace, InternalCondition internalCondition, OpenStudioImportContext openStudioImportContext, ProfileLibrary profileLibrary)
        {
            global::OpenStudio.OptionalThermalZone optionalThermalZone = openStudioSpace.thermalZone();
            if (optionalThermalZone == null || optionalThermalZone.isNull())
            {
                return;
            }

            global::OpenStudio.ThermalZone thermalZone = optionalThermalZone.get();
            string label = OpenStudioImportContext.OpenStudioObjectLabel(thermalZone);

            global::OpenStudio.OptionalThermostatSetpointDualSetpoint optionalThermostat = thermalZone.thermostatSetpointDualSetpoint();
            if (optionalThermostat != null && !optionalThermostat.isNull())
            {
                global::OpenStudio.ThermostatSetpointDualSetpoint thermostat = optionalThermostat.get();
                AssignProfile(internalCondition, InternalConditionParameter.HeatingProfileName, thermostat.heatingSetpointTemperatureSchedule(), ProfileType.Heating, openStudioImportContext, profileLibrary);
                AssignProfile(internalCondition, InternalConditionParameter.CoolingProfileName, thermostat.coolingSetpointTemperatureSchedule(), ProfileType.Cooling, openStudioImportContext, profileLibrary);
            }

            global::OpenStudio.OptionalZoneControlHumidistat optionalHumidistat = thermalZone.zoneControlHumidistat();
            if (optionalHumidistat != null && !optionalHumidistat.isNull())
            {
                global::OpenStudio.ZoneControlHumidistat humidistat = optionalHumidistat.get();
                AssignProfile(internalCondition, InternalConditionParameter.HumidificationProfileName, humidistat.humidifyingRelativeHumiditySetpointSchedule(), ProfileType.Humidification, openStudioImportContext, profileLibrary);
                AssignProfile(internalCondition, InternalConditionParameter.DehumidificationProfileName, humidistat.dehumidifyingRelativeHumiditySetpointSchedule(), ProfileType.Dehumidification, openStudioImportContext, profileLibrary);
            }

            ReportZoneHvac(thermalZone, openStudioImportContext, label);
        }

        /// <summary>
        /// Reports the zone's HVAC. Ideal Loads is recognised as "conditioned" and needs no
        /// further translation, because SAM's own conversion targets Ideal Loads too. Air loops
        /// and zone equipment are named and explicitly declared not imported — the first release
        /// must never leave the impression that detailed HVAC came across.
        /// </summary>
        private static void ReportZoneHvac(global::OpenStudio.ThermalZone thermalZone, OpenStudioImportContext openStudioImportContext, string label)
        {
            bool idealAirLoads = false;
            try
            {
                idealAirLoads = thermalZone.useIdealAirLoads();
            }
            catch (Exception)
            {
                // an unreadable flag is treated as absent and covered by the equipment report
            }

            if (idealAirLoads)
            {
                return;
            }

            List<string> equipmentTypes = new List<string>();

            try
            {
                global::OpenStudio.ModelObjectVector modelObjectVector = thermalZone.equipment();
                if (modelObjectVector != null)
                {
                    foreach (global::OpenStudio.ModelObject modelObject in modelObjectVector)
                    {
                        string typeName = IddTypeName(modelObject);

                        // ZoneHVAC:IdealLoadsAirSystem is the system SAM itself exports, so its
                        // presence means "conditioned" rather than "unsupported HVAC".
                        if (typeName != null && typeName.IndexOf("IdealLoads", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            continue;
                        }

                        if (!equipmentTypes.Contains(typeName))
                        {
                            equipmentTypes.Add(typeName);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // best effort — the report must not fail the import
            }

            int airLoopCount = 0;
            try
            {
                airLoopCount = thermalZone.airLoopHVACs()?.Count ?? 0;
            }
            catch (Exception)
            {
                // best effort
            }

            if (equipmentTypes.Count == 0 && airLoopCount == 0)
            {
                return;
            }

            openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.HvacUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("The zone is served by detailed HVAC ({0}{1}{2}); this release imports zone setpoints and the conditioning state only - no air loop, plant loop or zone equipment was translated into SAM Systems", equipmentTypes.Count == 0 ? string.Empty : string.Join(", ", equipmentTypes), equipmentTypes.Count > 0 && airLoopCount > 0 ? "; " : string.Empty, airLoopCount > 0 ? string.Format("{0} air loop(s)", airLoopCount) : string.Empty), label);
            openStudioImportContext.RegisterSkip();
        }

    }
}
