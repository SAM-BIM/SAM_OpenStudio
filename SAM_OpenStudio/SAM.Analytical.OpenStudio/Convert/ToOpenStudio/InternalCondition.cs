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
            // content hash (parameters and profile-name references, Guids excluded) PLUS the
            // per-space computed load densities: identical clones evaluated for equivalent spaces
            // share one SpaceType, while same-named conditions with different content — or a
            // shared condition evaluated for spaces with different densities — never merge
            // silently (the first space's values are never imposed on another space).
            AdjacencyCluster adjacencyCluster = openStudioConversionContext.Source?.AdjacencyCluster;

            double area = double.NaN;
            space.TryGetValue(SpaceParameter.Area, out area);

            double peoplePerArea = Analytical.Query.CalculatedPeoplePerArea(space);
            double occupancyGain = Analytical.Query.OccupancyGain(space);
            double occupancy = Analytical.Query.CalculatedOccupancy(space);
            double lightingGain = Analytical.Query.CalculatedLightingGain(space);
            double equipmentGain = Analytical.Query.CalculatedEquipmentSensibleGain(space);
            double infiltrationAirFlow = Analytical.Query.CalculatedInfiltrationAirFlow(space);

            string name = "SAM_InternalCondition_" + Core.OpenStudio.Query.SanitizeName(internalCondition.Name) + "_" + ContentHash(internalCondition, peoplePerArea, occupancyGain, occupancy, lightingGain, equipmentGain, infiltrationAirFlow, area);

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

            // People
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

            // Electric equipment (latent): a dedicated instance with Fraction Latent = 1 so the
            // sensible and latent gains keep independent profiles (coverage manifest:
            // InternalConditionParameter.EquipmentLatentGain*). Radiant fraction stays 0 —
            // fractions must sum to 1.
            double equipmentLatentGain = Analytical.Query.CalculatedEquipmentLatentGain(space);
            if (!double.IsNaN(equipmentLatentGain) && equipmentLatentGain > 0 && !double.IsNaN(area) && area > 0)
            {
                global::OpenStudio.Schedule equipmentLatentSchedule = RequiredSchedule(internalCondition, profileDictionary, ProfileType.EquipmentLatent, name, openStudioConversionContext);
                if (equipmentLatentSchedule != null)
                {
                    global::OpenStudio.ElectricEquipmentDefinition electricEquipmentLatentDefinition = new global::OpenStudio.ElectricEquipmentDefinition(openStudioConversionContext.Target);
                    electricEquipmentLatentDefinition.setName(name + "_ElectricEquipmentLatentDefinition");
                    electricEquipmentLatentDefinition.setWattsperSpaceFloorArea(equipmentLatentGain / area);
                    electricEquipmentLatentDefinition.setFractionLatent(1.0);
                    electricEquipmentLatentDefinition.setFractionRadiant(0.0);

                    global::OpenStudio.ElectricEquipment electricEquipmentLatent = new global::OpenStudio.ElectricEquipment(electricEquipmentLatentDefinition);
                    electricEquipmentLatent.setName(name + "_ElectricEquipmentLatent");
                    electricEquipmentLatent.setSchedule(equipmentLatentSchedule);
                    electricEquipmentLatent.setSpaceType(result);
                }
            }

            // Infiltration: the native Air Changes per Hour field when the condition carries
            // ACH (coverage manifest: InternalConditionParameter.InfiltrationAirChangesPerHour);
            // the flow-per-exterior-area derivation remains the fallback for other
            // parameterisations.
            double infiltrationAirChangesPerHour = double.NaN;
            internalCondition.TryGetValue(InternalConditionParameter.InfiltrationAirChangesPerHour, out infiltrationAirChangesPerHour);

            if (!double.IsNaN(infiltrationAirChangesPerHour) && infiltrationAirChangesPerHour > 0)
            {
                global::OpenStudio.Schedule infiltrationSchedule = RequiredSchedule(internalCondition, profileDictionary, ProfileType.Infiltration, name, openStudioConversionContext);
                if (infiltrationSchedule != null)
                {
                    global::OpenStudio.SpaceInfiltrationDesignFlowRate spaceInfiltrationDesignFlowRate = new global::OpenStudio.SpaceInfiltrationDesignFlowRate(openStudioConversionContext.Target);
                    spaceInfiltrationDesignFlowRate.setName(name + "_Infiltration");
                    spaceInfiltrationDesignFlowRate.setAirChangesperHour(infiltrationAirChangesPerHour);
                    spaceInfiltrationDesignFlowRate.setSchedule(infiltrationSchedule);
                    spaceInfiltrationDesignFlowRate.setSpaceType(result);
                }
            }
            else if (!double.IsNaN(infiltrationAirFlow) && infiltrationAirFlow > 0 && adjacencyCluster != null)
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
                        openStudioConversionContext.RegisterSkip();
                    }
                }
            }

            // Outdoor air (ventilation): SpaceType-level DesignSpecificationOutdoorAir, method
            // Sum, from the condition's supply parameters (per person / per area / ACH /
            // absolute). SpaceParameter.OutsideSupplyAirFlow still takes precedence — the
            // orchestrator assigns a per-space DSOA which overrides the SpaceType one in
            // EnergyPlus. A named-but-unresolved ventilation profile is an error; without a
            // named profile the rates are unscheduled (fraction 1).
            double supplyAirFlowPerPerson = ParameterValue(internalCondition, InternalConditionParameter.SupplyAirFlowPerPerson);
            double supplyAirFlowPerArea = ParameterValue(internalCondition, InternalConditionParameter.SupplyAirFlowPerArea);
            double supplyAirChangesPerHour = ParameterValue(internalCondition, InternalConditionParameter.SupplyAirChangesPerHour);
            double supplyAirFlow = ParameterValue(internalCondition, InternalConditionParameter.SupplyAirFlow);

            if (supplyAirFlowPerPerson > 0 || supplyAirFlowPerArea > 0 || supplyAirChangesPerHour > 0 || supplyAirFlow > 0)
            {
                global::OpenStudio.DesignSpecificationOutdoorAir designSpecificationOutdoorAir = new global::OpenStudio.DesignSpecificationOutdoorAir(openStudioConversionContext.Target);
                designSpecificationOutdoorAir.setName(name + "_DesignSpecificationOutdoorAir");
                designSpecificationOutdoorAir.setOutdoorAirMethod("Sum");
                designSpecificationOutdoorAir.setOutdoorAirFlowperPerson(supplyAirFlowPerPerson > 0 ? supplyAirFlowPerPerson : 0);
                designSpecificationOutdoorAir.setOutdoorAirFlowperFloorArea(supplyAirFlowPerArea > 0 ? supplyAirFlowPerArea : 0);
                designSpecificationOutdoorAir.setOutdoorAirFlowAirChangesperHour(supplyAirChangesPerHour > 0 ? supplyAirChangesPerHour : 0);
                designSpecificationOutdoorAir.setOutdoorAirFlowRate(supplyAirFlow > 0 ? supplyAirFlow : 0);

                string ventilationProfileName = internalCondition.GetProfileName(ProfileType.Ventilation);
                if (ventilationProfileName != null)
                {
                    global::OpenStudio.Schedule ventilationSchedule = RequiredSchedule(internalCondition, profileDictionary, ProfileType.Ventilation, name, openStudioConversionContext);
                    if (ventilationSchedule != null)
                    {
                        designSpecificationOutdoorAir.setOutdoorAirFlowRateFractionSchedule(ventilationSchedule);
                    }
                }

                result.setDesignSpecificationOutdoorAir(designSpecificationOutdoorAir);
            }

            // Pollutant modelling (generation rates / profile) has no safe EnergyPlus
            // equivalent in scope — reported, never silently dropped.
            double pollutantPerPerson = ParameterValue(internalCondition, InternalConditionParameter.PollutantGenerationPerPerson);
            double pollutantPerArea = ParameterValue(internalCondition, InternalConditionParameter.PollutantGenerationPerArea);
            if (pollutantPerPerson > 0 || pollutantPerArea > 0 || internalCondition.GetProfileName(ProfileType.Pollutant) != null)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.InternalConditionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "Pollutant generation is not converted (EnergyPlus generic contaminant modelling is out of scope)", internalCondition, name);
                openStudioConversionContext.RegisterSkip();
            }

            // TAS ventilation function expressions have no deterministic mapping.
            if (!string.IsNullOrWhiteSpace(internalCondition.GetValue<string>(InternalConditionParameter.VentilationFunction))
                || !double.IsNaN(ParameterValue(internalCondition, InternalConditionParameter.VentilationFunctionFactor))
                || !double.IsNaN(ParameterValue(internalCondition, InternalConditionParameter.VentilationFunctionSetback)))
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.InternalConditionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "Ventilation function (TAS expression/factor/setback) is not converted", internalCondition, name);
                openStudioConversionContext.RegisterSkip();
            }

            return result;
        }

        private static double ParameterValue(InternalCondition internalCondition, InternalConditionParameter internalConditionParameter)
        {
            double value;
            return internalCondition.TryGetValue(internalConditionParameter, out value) ? value : double.NaN;
        }

        private static global::OpenStudio.Schedule RequiredSchedule(InternalCondition internalCondition, Dictionary<ProfileType, Profile> profileDictionary, ProfileType profileType, string openStudioObjectName, OpenStudioConversionContext openStudioConversionContext)
        {
            Profile profile;
            if (!profileDictionary.TryGetValue(profileType, out profile) || profile == null)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ScheduleMissingProfile, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("{0} gains are defined but the {0} profile '{1}' was not found; the load is skipped (never AlwaysOn)", profileType, internalCondition.GetProfileName(profileType)), internalCondition, openStudioObjectName);
                openStudioConversionContext.RegisterSkip();
                return null;
            }

            return profile.ToOpenStudio(profileType, openStudioConversionContext);
        }

        /// <summary>
        /// Deterministic content hash (8 hex chars) of an internal condition's full parameter
        /// content — parameters, profile-name references and nested parameter sets — with every
        /// Guid excluded so clones hash identically regardless of their SAM Guids, extended with
        /// the per-space computed load densities so spaces with different evaluated densities
        /// never share one SpaceType. Two conditions with equal names, equal content and equal
        /// evaluated densities share a hash; any difference changes it.
        /// </summary>
        private static string ContentHash(InternalCondition internalCondition, params double[] spaceDensities)
        {
            JsonObject jsonObject = internalCondition.ToJsonObject();
            StringBuilder stringBuilder = new StringBuilder();
            AppendContent(jsonObject, stringBuilder);

            if (spaceDensities != null)
            {
                foreach (double density in spaceDensities)
                {
                    stringBuilder.Append("density=").Append(density.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(';');
                }
            }

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
