// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using System.Globalization;
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

                    // S07 §5.1 (T-LIGHT-SPLIT): p is the total radiant share; it is split by the declared short-wave
                    // share s, Return Air Fraction stays unset (0) and the view coefficient is never read here.
                    // An authored p, including 0, is preserved (SAM keeps it within [0, 1]); an absent or NaN p takes
                    // the common default p₀.
                    bool lightingRadiantProportionStated = internalCondition.TryGetValue(InternalConditionParameter.LightingRadiantProportion, out double lightingRadiantProportion) && !double.IsNaN(lightingRadiantProportion);
                    if (!lightingRadiantProportionStated)
                    {
                        lightingRadiantProportion = LightingSplit.ReferenceRadiantProportion;
                    }

                    lightsDefinition.setFractionRadiant(LightingSplit.FractionRadiant(lightingRadiantProportion));
                    lightsDefinition.setFractionVisible(LightingSplit.FractionVisible(lightingRadiantProportion));
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.InternalConditionAssumption, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, LightingSplitMessage(lightingRadiantProportion, lightingRadiantProportionStated), internalCondition, name);

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
            // EnergyPlus. The supply parameters are the requirement; the selected Ventilation
            // profile is the delivery (S07 D-F2). Without a selection, or with a selection that
            // does not resolve, the requirement is kept and an all-zero fraction schedule
            // delivers no outdoor air (never unscheduled).
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
                designSpecificationOutdoorAir.setOutdoorAirFlowRateFractionSchedule(VentilationDeliverySchedule(internalCondition, profileDictionary, designSpecificationOutdoorAir.nameString(), openStudioConversionContext));

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

            // TAS view coefficients and the lighting control function (coverage manifest,
            // Unsupported SAM-OS-IC-001 info; review P1-01; S07 §5.1): a view coefficient weights
            // the emitter's radiation in the TAS mean radiant and resultant temperatures. It is a
            // comfort coefficient, not an energy fraction, and EnergyPlus has no input for it. The
            // TAS control expression has no deterministic daylighting-control mapping. Reported per
            // SpaceType.
            if (!double.IsNaN(ParameterValue(internalCondition, InternalConditionParameter.OccupancyViewCoefficient)))
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.InternalConditionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "Occupancy view coefficient is not converted — it is a TAS comfort (mean radiant and resultant temperature) coefficient, not an energy fraction; OS:People has no input for it", internalCondition, name);
                openStudioConversionContext.RegisterSkip();
            }

            if (!double.IsNaN(ParameterValue(internalCondition, InternalConditionParameter.EquipmentViewCoefficient)))
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.InternalConditionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "Equipment view coefficient is not converted — it is a TAS comfort (mean radiant and resultant temperature) coefficient, not an energy fraction; OS:ElectricEquipment has no input for it", internalCondition, name);
                openStudioConversionContext.RegisterSkip();
            }

            if (!double.IsNaN(ParameterValue(internalCondition, InternalConditionParameter.LightingViewCoefficient)))
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.InternalConditionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "Lighting view coefficient is not converted — it is a TAS comfort (mean radiant and resultant temperature) coefficient, not an energy fraction; OS:Lights has no input for it, and its Fraction Visible comes from the lighting radiant proportion (S07 §5.1)", internalCondition, name);
                openStudioConversionContext.RegisterSkip();
            }

            if (!string.IsNullOrWhiteSpace(internalCondition.GetValue<string>(InternalConditionParameter.LightingControlFunction)))
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.InternalConditionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "Lighting control function (TAS control expression) is not converted — no deterministic daylighting-control mapping", internalCondition, name);
                openStudioConversionContext.RegisterSkip();
            }

            // Deferred detailed-HVAC data (coverage manifest, Deferred SAM-OS-HVAC-001 info;
            // review P1-01): Ideal Loads is a purely convective air system without fan/air-loop
            // equipment, so emitter characteristics and exhaust flows wait for the HVAC
            // programme. One diagnostic per group, naming every present parameter.
            List<string> emitterParameterNames = PresentParameterNames(internalCondition,
                InternalConditionParameter.HeatingEmitterRadiantProportion,
                InternalConditionParameter.HeatingEmitterCoefficient,
                InternalConditionParameter.CoolingEmitterRadiantProportion,
                InternalConditionParameter.CoolingEmitterCoefficient);
            if (emitterParameterNames.Count > 0)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.HvacMissingSetpoints, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format("Emitter characteristics ({0}) are not converted — Ideal Loads is a purely convective air system; emitters are deferred to the detailed-HVAC programme", string.Join(", ", emitterParameterNames)), internalCondition, name);
                openStudioConversionContext.RegisterSkip();
            }

            List<string> exhaustParameterNames = PresentParameterNames(internalCondition,
                InternalConditionParameter.ExhaustAirFlowPerPerson,
                InternalConditionParameter.ExhaustAirChangesPerHour,
                InternalConditionParameter.ExhaustAirFlowPerArea,
                InternalConditionParameter.ExhaustAirFlow);
            if (exhaustParameterNames.Count > 0)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.HvacMissingSetpoints, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format("Exhaust air flows ({0}) are not converted — exhaust needs fan/air-loop equipment, deferred to the detailed-HVAC programme", string.Join(", ", exhaustParameterNames)), internalCondition, name);
                openStudioConversionContext.RegisterSkip();
            }

            return result;
        }

        private static List<string> PresentParameterNames(InternalCondition internalCondition, params InternalConditionParameter[] internalConditionParameters)
        {
            List<string> result = new List<string>();
            foreach (InternalConditionParameter internalConditionParameter in internalConditionParameters)
            {
                if (!double.IsNaN(ParameterValue(internalCondition, internalConditionParameter)))
                {
                    result.Add(internalConditionParameter.ToString());
                }
            }

            return result;
        }

        private static double ParameterValue(InternalCondition internalCondition, InternalConditionParameter internalConditionParameter)
        {
            double value;
            return internalCondition.TryGetValue(internalConditionParameter, out value) ? value : double.NaN;
        }

        /// <summary>
        /// The S07 §5.1 lighting-split note (Information, Summary): the values written, s and its reference category and
        /// limits, and, for an absent p, that the common default p₀ was applied and that its suitability outside the
        /// category is the open owner decision OD-L1.
        /// </summary>
        private static string LightingSplitMessage(double radiantProportion, bool stated)
        {
            string values = string.Format(CultureInfo.InvariantCulture,
                "Fraction Radiant = p·(1 − s) = {0:0.######}, Fraction Visible = p·s = {1:0.######}, Return Air Fraction 0, convective 1 − p = {2:0.######}; s = 0.2415 / 0.576 = {3:0.#######} (short-wave share of the radiant output)",
                LightingSplit.FractionRadiant(radiantProportion), LightingSplit.FractionVisible(radiantProportion), 1 - radiantProportion, LightingSplit.ShortWaveShare);
            const string category = "Reference category: suspended (pendant) LED office lighting with the entire fitting in the conditioned room; a midpoint-derived reference from cited historical ranges (IEA SHC Task 61 / EBC Annex 77 D1, Table 6; ASHRAE Fundamentals 2017, Ch. 18, Table 3), not manufacturer-certified. Not applicable to recessed troffers, downlights, retrofit kits, high-bays or non-LED lamps";

            if (stated)
            {
                return string.Format(CultureInfo.InvariantCulture, "Lighting radiant proportion p = {0:R} (authored) is split under S07 §5.1 T-LIGHT-SPLIT: {1}. s is a declared assumption, not SAM data. {2}.", radiantProportion, values, category);
            }

            return string.Format(CultureInfo.InvariantCulture, "Lighting radiant proportion is not stated; the S07 §5.1 common default p₀ = {0:R} was applied: {1}. {2}. p₀ is not a regulatory, NCM, TM59 or dwelling value, and its suitability outside the reference category is unresolved (owner decision OD-L1); author LightingRadiantProportion where a method or template prescribes it.", radiantProportion, values, category);
        }

        /// <summary>
        /// The outdoor-air delivery schedule of a space's ventilation requirement (S07 D-F2): the selected Ventilation
        /// profile when it resolves (V2); otherwise <see cref="VentilationNotDeliveredScheduleName"/>, an all-zero fraction
        /// schedule, so the requirement stays in the model and no outdoor air is delivered. No selection (V1) is reported
        /// as information; a selection that does not resolve to a Ventilation profile (missing, or another type) is an
        /// invalid input and an error. Used for the SpaceType DSOA and for the per-space OutsideSupplyAirFlow DSOA.
        /// </summary>
        /// <param name="internalCondition">The space's internal condition; null means no selection.</param>
        /// <param name="openStudioConversionContext">Conversion context.</param>
        /// <param name="openStudioObjectName">The DSOA the schedule is for (diagnostics).</param>
        /// <returns>The delivery schedule; never null.</returns>
        public static global::OpenStudio.Schedule ToOpenStudio_VentilationDeliverySchedule(this InternalCondition internalCondition, OpenStudioConversionContext openStudioConversionContext, string openStudioObjectName)
        {
            ProfileLibrary profileLibrary = openStudioConversionContext.Source?.ProfileLibrary;
            Dictionary<ProfileType, Profile> profileDictionary = internalCondition == null || profileLibrary == null ? new Dictionary<ProfileType, Profile>() : internalCondition.GetProfileDictionary(profileLibrary);
            return VentilationDeliverySchedule(internalCondition, profileDictionary, openStudioObjectName, openStudioConversionContext);
        }

        /// <summary>The name of the all-zero fraction schedule that delivers no outdoor air (V1, or an invalid selection).</summary>
        public const string VentilationNotDeliveredScheduleName = "SAM_Schedule_VentilationNotDelivered";

        private static global::OpenStudio.Schedule VentilationDeliverySchedule(InternalCondition internalCondition, Dictionary<ProfileType, Profile> profileDictionary, string openStudioObjectName, OpenStudioConversionContext openStudioConversionContext)
        {
            string selection = internalCondition?.GetProfileName(ProfileType.Ventilation);
            if (string.IsNullOrWhiteSpace(selection))
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.HvacMissingSetpoints, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "No Ventilation profile is selected: the outdoor-air requirement is kept and no outdoor air is delivered (S07 D-F2 V1)", internalCondition, openStudioObjectName);
                return openStudioConversionContext.ToOpenStudio_ConstantSchedule(VentilationNotDeliveredScheduleName, 0, "Fractional");
            }

            global::OpenStudio.Schedule result = null;
            if (profileDictionary != null && profileDictionary.TryGetValue(ProfileType.Ventilation, out Profile profile) && profile != null)
            {
                result = profile.ToOpenStudio(ProfileType.Ventilation, openStudioConversionContext);
                if (result == null)
                {
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ScheduleMissingProfile, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("The selected Ventilation profile '{0}' could not be converted; no outdoor air is delivered (never unscheduled)", selection), internalCondition, openStudioObjectName);
                }
            }
            else
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ScheduleMissingProfile, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("The selected Ventilation profile '{0}' was not found as a Ventilation profile (missing, or of another type): an invalid input; no outdoor air is delivered (never unscheduled)", selection), internalCondition, openStudioObjectName);
            }

            if (result == null)
            {
                openStudioConversionContext.RegisterSkip();
                return openStudioConversionContext.ToOpenStudio_ConstantSchedule(VentilationNotDeliveredScheduleName, 0, "Fractional");
            }

            return result;
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
