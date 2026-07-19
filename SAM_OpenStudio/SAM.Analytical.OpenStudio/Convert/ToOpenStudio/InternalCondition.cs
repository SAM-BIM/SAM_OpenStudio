// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Converts a SAM InternalCondition to an OpenStudio SpaceType with People, Lights,
        /// ElectricEquipment, SpaceInfiltrationDesignFlowRate and DesignSpecificationOutdoorAir,
        /// following docs/SAM_OPENSTUDIO_INTERNAL_CONDITION_MAPPING.md. One SpaceType is created
        /// per unique condition identity — sanitized name plus a deterministic content hash over
        /// the condition's parameters and profile references (cached); load densities come from
        /// the established SAM queries evaluated for the given space. A load whose gains exist
        /// but whose profile is missing raises SAM-OS-SCH-001 and the load is skipped — never
        /// silently AlwaysOn. Heating/cooling setpoint profiles are not part of the SpaceType
        /// (thermostats, M6).
        /// </summary>
        /// <param name="internalCondition">SAM internal condition.</param>
        /// <param name="space">Space used to evaluate SAM's calculated load densities.</param>
        /// <param name="openStudioConversionContext">Conversion context.</param>
        /// <returns>The SpaceType, or null when inputs are null.</returns>
        public static global::OpenStudio.SpaceType ToOpenStudio(this InternalCondition internalCondition, Space space, OpenStudioConversionContext openStudioConversionContext)
        {
            if (internalCondition == null || space == null || openStudioConversionContext == null)
            {
                return null;
            }

            // SAM's Space.InternalCondition setter clones the condition with a NEW Guid
            // (SAM.Analytical\Classes\Space.cs), so per-space Guids differ by design and cannot
            // identify shared conditions. Deduplication is by sanitized name PLUS a deterministic
            // content hash (parameters and profile-name references, Guids excluded): identical
            // clones share one SpaceType, while same-named conditions with different content
            // never merge silently.
            string name = "SAM_InternalCondition_" + Core.OpenStudio.Query.SanitizeName(internalCondition.Name) + "_" + ContentHash(internalCondition);

            global::OpenStudio.OptionalSpaceType existing = openStudioConversionContext.Target.getSpaceTypeByName(name);
            if (existing != null && !existing.isNull())
            {
                openStudioConversionContext.RegisterModelObject(internalCondition, existing.get());
                return existing.get();
            }

            global::OpenStudio.SpaceType result = new global::OpenStudio.SpaceType(openStudioConversionContext.Target);
            result.setName(name);
            openStudioConversionContext.RegisterModelObject(internalCondition, result);

            ProfileLibrary profileLibrary = openStudioConversionContext.Source?.ProfileLibrary;
            Dictionary<ProfileType, Profile> profileDictionary = profileLibrary == null ? new Dictionary<ProfileType, Profile>() : internalCondition.GetProfileDictionary(profileLibrary);

            AdjacencyCluster adjacencyCluster = openStudioConversionContext.Source?.AdjacencyCluster;

            // People
            double peoplePerArea = Analytical.Query.CalculatedPeoplePerArea(space);
            if (!double.IsNaN(peoplePerArea) && peoplePerArea > 0)
            {
                global::OpenStudio.Schedule occupancySchedule = RequiredSchedule(internalCondition, profileDictionary, ProfileType.Occupancy, name, openStudioConversionContext);
                if (occupancySchedule != null)
                {
                    global::OpenStudio.PeopleDefinition peopleDefinition = new global::OpenStudio.PeopleDefinition(openStudioConversionContext.Target);
                    peopleDefinition.setName(name + "_PeopleDefinition");
                    peopleDefinition.setPeopleperSpaceFloorArea(peoplePerArea);

                    if (!internalCondition.TryGetValue(InternalConditionParameter.OccupancyRadiantProportion, out double occupancyRadiantProportion) || double.IsNaN(occupancyRadiantProportion))
                    {
                        occupancyRadiantProportion = 0.3;
                    }

                    peopleDefinition.setFractionRadiant(occupancyRadiantProportion);

                    double sensibleGain = Analytical.Query.OccupancySensibleGain(space);
                    double latentGain = Analytical.Query.OccupancyLatentGain(space);
                    if (!double.IsNaN(sensibleGain) && !double.IsNaN(latentGain) && sensibleGain + latentGain > 0)
                    {
                        peopleDefinition.setSensibleHeatFraction(sensibleGain / (sensibleGain + latentGain));
                    }

                    double occupancyGain = Analytical.Query.OccupancyGain(space);
                    double occupancy = Analytical.Query.CalculatedOccupancy(space);
                    double activityLevel = 0;
                    if (!double.IsNaN(occupancyGain) && !double.IsNaN(occupancy) && occupancy > 0)
                    {
                        activityLevel = occupancyGain / occupancy;
                    }
                    else
                    {
                        openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.InternalConditionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "Occupancy activity level could not be derived; 0 W/person used", internalCondition, name);
                    }

                    global::OpenStudio.Schedule activitySchedule = openStudioConversionContext.ToOpenStudio_ConstantSchedule(name + "_ActivityLevel", activityLevel, "ActivityLevel");

                    global::OpenStudio.People people = new global::OpenStudio.People(peopleDefinition);
                    people.setName(name + "_People");
                    people.setNumberofPeopleSchedule(occupancySchedule);
                    people.setActivityLevelSchedule(activitySchedule);
                    people.setSpaceType(result);
                }
            }

            // Lights
            double area = double.NaN;
            space.TryGetValue(SpaceParameter.Area, out area);

            double lightingGain = Analytical.Query.CalculatedLightingGain(space);
            if (!double.IsNaN(lightingGain) && lightingGain > 0 && !double.IsNaN(area) && area > 0)
            {
                global::OpenStudio.Schedule lightingSchedule = RequiredSchedule(internalCondition, profileDictionary, ProfileType.Lighting, name, openStudioConversionContext);
                if (lightingSchedule != null)
                {
                    global::OpenStudio.LightsDefinition lightsDefinition = new global::OpenStudio.LightsDefinition(openStudioConversionContext.Target);
                    lightsDefinition.setName(name + "_LightsDefinition");
                    lightsDefinition.setWattsperSpaceFloorArea(lightingGain / area);

                    if (!internalCondition.TryGetValue(InternalConditionParameter.LightingRadiantProportion, out double lightingRadiantProportion) || double.IsNaN(lightingRadiantProportion))
                    {
                        lightingRadiantProportion = 0.32;
                    }

                    if (!internalCondition.TryGetValue(InternalConditionParameter.LightingViewCoefficient, out double lightingViewCoefficient) || double.IsNaN(lightingViewCoefficient))
                    {
                        lightingViewCoefficient = 0.25;
                    }

                    lightsDefinition.setFractionRadiant(lightingRadiantProportion);
                    lightsDefinition.setFractionVisible(lightingViewCoefficient);

                    global::OpenStudio.Lights lights = new global::OpenStudio.Lights(lightsDefinition);
                    lights.setName(name + "_Lights");
                    lights.setSchedule(lightingSchedule);
                    lights.setSpaceType(result);
                }
            }

            // Electric equipment (sensible)
            double equipmentGain = Analytical.Query.CalculatedEquipmentSensibleGain(space);
            if (!double.IsNaN(equipmentGain) && equipmentGain > 0 && !double.IsNaN(area) && area > 0)
            {
                global::OpenStudio.Schedule equipmentSchedule = RequiredSchedule(internalCondition, profileDictionary, ProfileType.EquipmentSensible, name, openStudioConversionContext);
                if (equipmentSchedule != null)
                {
                    global::OpenStudio.ElectricEquipmentDefinition electricEquipmentDefinition = new global::OpenStudio.ElectricEquipmentDefinition(openStudioConversionContext.Target);
                    electricEquipmentDefinition.setName(name + "_ElectricEquipmentDefinition");
                    electricEquipmentDefinition.setWattsperSpaceFloorArea(equipmentGain / area);

                    if (!internalCondition.TryGetValue(InternalConditionParameter.EquipmentRadiantProportion, out double equipmentRadiantProportion) || double.IsNaN(equipmentRadiantProportion))
                    {
                        equipmentRadiantProportion = 0;
                    }

                    electricEquipmentDefinition.setFractionRadiant(equipmentRadiantProportion);

                    global::OpenStudio.ElectricEquipment electricEquipment = new global::OpenStudio.ElectricEquipment(electricEquipmentDefinition);
                    electricEquipment.setName(name + "_ElectricEquipment");
                    electricEquipment.setSchedule(equipmentSchedule);
                    electricEquipment.setSpaceType(result);
                }
            }

            if (internalCondition.GetProfileName(ProfileType.EquipmentLatent) != null)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.InternalConditionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "Latent equipment gains are not converted in the MVP", internalCondition, name);
            }

            // Infiltration
            double infiltrationAirFlow = Analytical.Query.CalculatedInfiltrationAirFlow(space);
            if (!double.IsNaN(infiltrationAirFlow) && infiltrationAirFlow > 0 && adjacencyCluster != null)
            {
                global::OpenStudio.Schedule infiltrationSchedule = RequiredSchedule(internalCondition, profileDictionary, ProfileType.Infiltration, name, openStudioConversionContext);
                if (infiltrationSchedule != null)
                {
                    double exteriorArea = 0;
                    List<Panel> panels = adjacencyCluster.GetPanels(space);
                    if (panels != null)
                    {
                        foreach (Panel panel in panels)
                        {
                            // Exception isolation: SAM's geometry kernel can throw on
                            // pathological panels — skip them (they are diagnosed by the
                            // no-silent-drop check) instead of crashing the conversion.
                            try
                            {
                                if (panel == null || !adjacencyCluster.ExposedToSun(panel))
                                {
                                    continue;
                                }

                                double panelArea = panel.GetArea();
                                if (!double.IsNaN(panelArea))
                                {
                                    exteriorArea += panelArea;
                                }
                            }
                            catch (System.Exception)
                            {
                                continue;
                            }
                        }
                    }

                    if (exteriorArea > 0)
                    {
                        global::OpenStudio.SpaceInfiltrationDesignFlowRate spaceInfiltrationDesignFlowRate = new global::OpenStudio.SpaceInfiltrationDesignFlowRate(openStudioConversionContext.Target);
                        spaceInfiltrationDesignFlowRate.setName(name + "_Infiltration");
                        spaceInfiltrationDesignFlowRate.setFlowperExteriorSurfaceArea(infiltrationAirFlow / exteriorArea);
                        spaceInfiltrationDesignFlowRate.setSchedule(infiltrationSchedule);
                        spaceInfiltrationDesignFlowRate.setSpaceType(result);
                    }
                    else
                    {
                        openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.InternalConditionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "Space has no sun-exposed panels; infiltration skipped", internalCondition, name);
                    }
                }
            }

            // Outdoor air is a space-level SAM parameter (SpaceParameter.OutsideSupplyAirFlow)
            // and is assigned per OpenStudio Space by the orchestrator, not on the SpaceType.

            return result;
        }

        private static global::OpenStudio.Schedule RequiredSchedule(InternalCondition internalCondition, Dictionary<ProfileType, Profile> profileDictionary, ProfileType profileType, string openStudioObjectName, OpenStudioConversionContext openStudioConversionContext)
        {
            Profile profile;
            if (!profileDictionary.TryGetValue(profileType, out profile) || profile == null)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ScheduleMissingProfile, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("{0} gains are defined but the {0} profile '{1}' was not found; the load is skipped (never AlwaysOn)", profileType, internalCondition.GetProfileName(profileType)), internalCondition, openStudioObjectName);
                return null;
            }

            return profile.ToOpenStudio(profileType, openStudioConversionContext);
        }

        /// <summary>
        /// Deterministic content hash (8 hex chars) of an internal condition's full parameter
        /// content — parameters, profile-name references and nested parameter sets — with every
        /// Guid excluded so clones hash identically regardless of their SAM Guids. Two conditions
        /// with equal names and equal content share a hash; any content difference changes it.
        /// </summary>
        private static string ContentHash(InternalCondition internalCondition)
        {
            JsonObject jsonObject = internalCondition.ToJsonObject();
            StringBuilder stringBuilder = new StringBuilder();
            AppendContent(jsonObject, stringBuilder);

            using (MD5 mD5 = MD5.Create())
            {
                byte[] hash = mD5.ComputeHash(Encoding.UTF8.GetBytes(stringBuilder.ToString()));
                StringBuilder result = new StringBuilder(8);
                for (int i = 0; i < 4; i++)
                {
                    result.Append(hash[i].ToString("x2"));
                }

                return result.ToString();
            }
        }

        private static void AppendContent(JsonNode jsonNode, StringBuilder stringBuilder)
        {
            if (jsonNode is JsonObject jsonObject)
            {
                foreach (KeyValuePair<string, JsonNode> keyValuePair in jsonObject)
                {
                    if (keyValuePair.Key == "Guid")
                    {
                        continue;
                    }

                    stringBuilder.Append(keyValuePair.Key).Append('=');
                    AppendContent(keyValuePair.Value, stringBuilder);
                    stringBuilder.Append(';');
                }

                return;
            }

            if (jsonNode is JsonArray jsonArray)
            {
                foreach (JsonNode item in jsonArray)
                {
                    AppendContent(item, stringBuilder);
                    stringBuilder.Append(';');
                }

                return;
            }

            stringBuilder.Append(jsonNode?.ToJsonString());
        }
    }
}
