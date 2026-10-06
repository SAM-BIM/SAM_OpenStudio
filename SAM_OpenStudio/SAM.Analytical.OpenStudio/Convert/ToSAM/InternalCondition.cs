// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Converts every OpenStudio SpaceType into a deduplicated SAM
        /// <see cref="InternalCondition"/>, caching them on the context by space-type name.
        /// <para>
        /// Deduplication is by space type, which is exactly how OpenStudio itself shares loads:
        /// N spaces of one space type produce one SAM internal condition, and SAM's own
        /// per-space cloning (Space.InternalCondition assigns a clone with a new Guid) then
        /// applies it. Per-space overrides are handled separately in
        /// <see cref="ToSAM_AssignInternalConditions"/>.
        /// </para>
        /// </summary>
        /// <param name="openStudioImportContext">Import context.</param>
        /// <param name="profileLibrary">Library receiving the schedules the loads reference.</param>
        public static void ToSAM_InternalConditions(this OpenStudioImportContext openStudioImportContext, ProfileLibrary profileLibrary)
        {
            if (openStudioImportContext == null || !openStudioImportContext.Options.IncludeInternalConditions)
            {
                return;
            }

            global::OpenStudio.SpaceTypeVector spaceTypeVector;
            try
            {
                spaceTypeVector = openStudioImportContext.Source.getSpaceTypes();
            }
            catch (Exception)
            {
                return;
            }

            if (spaceTypeVector == null)
            {
                return;
            }

            openStudioImportContext.Statistics.SourceObjects += spaceTypeVector.Count;

            foreach (global::OpenStudio.SpaceType spaceType in spaceTypeVector)
            {
                if (spaceType == null)
                {
                    continue;
                }

                InternalCondition internalCondition = spaceType.ToSAM(openStudioImportContext, profileLibrary);
                if (internalCondition != null)
                {
                    openStudioImportContext.InternalConditionMap[spaceType.nameString()] = internalCondition;
                }
            }
        }

        /// <summary>
        /// Converts one OpenStudio SpaceType into a SAM <see cref="InternalCondition"/>: people,
        /// lights, electric equipment, infiltration and outdoor-air requirements, each with its
        /// schedule expanded into a SAM profile.
        /// <para>
        /// Gains are imported per unit area or per person exactly as OpenStudio stores them. They
        /// are deliberately NOT multiplied out to absolute watts here: a space type is shared by
        /// spaces of different sizes, and resolving the density against one space's floor area
        /// would impose that space's loads on all the others.
        /// </para>
        /// <para>
        /// Load kinds SAM cannot carry (gas, steam and hot-water equipment, luminaires, IT
        /// equipment, internal mass, leakage-area and flow-coefficient infiltration) are reported
        /// through SAM-OSI-LOAD-001 rather than dropped.
        /// </para>
        /// </summary>
        /// <param name="spaceType">OpenStudio space type; null returns null.</param>
        /// <param name="openStudioImportContext">Import context.</param>
        /// <param name="profileLibrary">Library receiving the converted schedules.</param>
        /// <returns>The SAM internal condition, or null.</returns>
        public static InternalCondition ToSAM(this global::OpenStudio.SpaceType spaceType, OpenStudioImportContext openStudioImportContext, ProfileLibrary profileLibrary)
        {
            if (spaceType == null || openStudioImportContext == null)
            {
                return null;
            }

            string name = spaceType.nameString();
            string label = OpenStudioImportContext.OpenStudioObjectLabel(spaceType);

            InternalCondition cached;
            if (openStudioImportContext.InternalConditionMap.TryGetValue(name, out cached))
            {
                return cached;
            }

            string samName;
            if (!Core.OpenStudio.Query.TryGetSAMName(spaceType, out samName) || string.IsNullOrWhiteSpace(samName))
            {
                samName = name;
            }

            InternalCondition result = new InternalCondition(openStudioImportContext.ResolveGuid(spaceType, typeof(InternalCondition).Name), samName);

            ToSAM_People(spaceType, result, openStudioImportContext, profileLibrary, label);
            ToSAM_Lights(spaceType, result, openStudioImportContext, profileLibrary, label);
            ToSAM_ElectricEquipment(spaceType, result, openStudioImportContext, profileLibrary, label);
            ToSAM_Infiltration(spaceType, result, openStudioImportContext, profileLibrary, label);
            ToSAM_OutdoorAir(spaceType, result, openStudioImportContext, profileLibrary, label);

            ReportUnsupportedLoads(spaceType, openStudioImportContext, label);

            Modify.SetOpenStudioSource(result, spaceType);

            openStudioImportContext.InternalConditionMap[name] = result;
            openStudioImportContext.RegisterCreated();
            return result;
        }

        /// <summary>
        /// People → occupancy density, per-person sensible/latent split and occupancy profile.
        /// The sensible/latent split is reconstructed from the activity-level schedule and the
        /// definition's sensible heat fraction — the exact inverse of the forward direction,
        /// which derives both from those same two values.
        /// </summary>
        private static void ToSAM_People(global::OpenStudio.SpaceType spaceType, InternalCondition internalCondition, OpenStudioImportContext openStudioImportContext, ProfileLibrary profileLibrary, string label)
        {
            global::OpenStudio.PeopleVector peopleVector = spaceType.people();
            if (peopleVector == null || peopleVector.Count == 0)
            {
                return;
            }

            if (peopleVector.Count > 1)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.LoadAssignmentConflict, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Space type carries {0} People loads; SAM holds one occupancy definition, so only the first was imported", peopleVector.Count), label, internalCondition);
                openStudioImportContext.RegisterSkip();
            }

            global::OpenStudio.People people = peopleVector[0];
            global::OpenStudio.PeopleDefinition peopleDefinition = people.peopleDefinition();

            double peoplePerArea = OptionalValue(peopleDefinition.peopleperSpaceFloorArea());
            if (!double.IsNaN(peoplePerArea) && peoplePerArea > 0)
            {
                internalCondition.SetValue(InternalConditionParameter.AreaPerPerson, 1.0 / peoplePerArea);
            }
            else
            {
                double areaPerPerson = OptionalValue(peopleDefinition.spaceFloorAreaperPerson());
                if (!double.IsNaN(areaPerPerson) && areaPerPerson > 0)
                {
                    internalCondition.SetValue(InternalConditionParameter.AreaPerPerson, areaPerPerson);
                }
                else
                {
                    // "Number of People" is an absolute count for one space; a space type shared
                    // by several spaces cannot carry it as a density without inventing an area.
                    openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.LoadUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("People load uses the '{0}' calculation method, which carries no occupancy density; SAM's area-per-person was left unset rather than derived from an assumed floor area", peopleDefinition.numberofPeopleCalculationMethod()), label, internalCondition);
                    openStudioImportContext.RegisterSkip();
                }
            }

            double activityLevel = ConstantScheduleValue(people.activityLevelSchedule());
            if (!double.IsNaN(activityLevel) && activityLevel > 0)
            {
                double sensibleHeatFraction = OptionalValue(peopleDefinition.sensibleHeatFraction());
                if (double.IsNaN(sensibleHeatFraction))
                {
                    // Autocalculated by EnergyPlus from the zone air temperature: no single value
                    // exists to import, so the whole metabolic rate is recorded as sensible and
                    // the omission is stated.
                    internalCondition.SetValue(InternalConditionParameter.OccupancySensibleGainPerPerson, activityLevel);
                    openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format(CultureInfo.InvariantCulture, "The People definition autocalculates its sensible heat fraction, so no fixed split exists; the whole metabolic rate ({0:G4} W/person) was imported as sensible and the latent gain left at zero", activityLevel), label, internalCondition);
                }
                else
                {
                    internalCondition.SetValue(InternalConditionParameter.OccupancySensibleGainPerPerson, activityLevel * sensibleHeatFraction);
                    internalCondition.SetValue(InternalConditionParameter.OccupancyLatentGainPerPerson, activityLevel * (1.0 - sensibleHeatFraction));
                }
            }
            else if (!double.IsNaN(activityLevel))
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.LoadUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "The People activity level is zero or varies through the year; SAM carries a single per-person gain, so no occupancy gain was imported", label, internalCondition);
            }

            double fractionRadiant = peopleDefinition.fractionRadiant();
            if (!double.IsNaN(fractionRadiant))
            {
                internalCondition.SetValue(InternalConditionParameter.OccupancyRadiantProportion, fractionRadiant);
            }

            AssignProfile(internalCondition, InternalConditionParameter.OccupancyProfileName, people.numberofPeopleSchedule(), ProfileType.Occupancy, openStudioImportContext, profileLibrary);
        }

        /// <summary>Lights → lighting gain per area, radiant/visible fractions and lighting profile.</summary>
        private static void ToSAM_Lights(global::OpenStudio.SpaceType spaceType, InternalCondition internalCondition, OpenStudioImportContext openStudioImportContext, ProfileLibrary profileLibrary, string label)
        {
            global::OpenStudio.LightsVector lightsVector = spaceType.lights();
            if (lightsVector == null || lightsVector.Count == 0)
            {
                return;
            }

            if (lightsVector.Count > 1)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.LoadAssignmentConflict, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Space type carries {0} Lights loads; SAM holds one lighting definition, so only the first was imported", lightsVector.Count), label, internalCondition);
                openStudioImportContext.RegisterSkip();
            }

            global::OpenStudio.Lights lights = lightsVector[0];
            global::OpenStudio.LightsDefinition lightsDefinition = lights.lightsDefinition();

            double wattsPerArea = OptionalValue(lightsDefinition.wattsperSpaceFloorArea());
            if (!double.IsNaN(wattsPerArea))
            {
                internalCondition.SetValue(InternalConditionParameter.LightingGainPerArea, wattsPerArea);
            }
            else
            {
                double wattsPerPerson = OptionalValue(lightsDefinition.wattsperPerson());
                if (!double.IsNaN(wattsPerPerson))
                {
                    internalCondition.SetValue(InternalConditionParameter.LightingGainPerPerson, wattsPerPerson);
                }
                else
                {
                    openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.LoadUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Lights load uses the '{0}' calculation method (an absolute level), which cannot be carried by a shared SAM internal condition; no lighting gain was imported", lightsDefinition.designLevelCalculationMethod()), label, internalCondition);
                    openStudioImportContext.RegisterSkip();
                }
            }

            internalCondition.SetValue(InternalConditionParameter.LightingRadiantProportion, lightsDefinition.fractionRadiant());
            internalCondition.SetValue(InternalConditionParameter.LightingViewCoefficient, lightsDefinition.fractionVisible());

            AssignProfile(internalCondition, InternalConditionParameter.LightingProfileName, lights.schedule(), ProfileType.Lighting, openStudioImportContext, profileLibrary);
        }

        /// <summary>
        /// ElectricEquipment → sensible and latent equipment gains.
        /// <para>
        /// The forward direction writes two instances — one sensible, one with Fraction Latent =
        /// 1 — so each side keeps its own profile. That split is recognised here by the latent
        /// fraction, which makes the round trip exact; a third-party model with a single
        /// part-latent instance has its gain apportioned by that fraction, reported as an
        /// approximation because the two halves then share one schedule.
        /// </para>
        /// </summary>
        private static void ToSAM_ElectricEquipment(global::OpenStudio.SpaceType spaceType, InternalCondition internalCondition, OpenStudioImportContext openStudioImportContext, ProfileLibrary profileLibrary, string label)
        {
            global::OpenStudio.ElectricEquipmentVector electricEquipmentVector = spaceType.electricEquipment();
            if (electricEquipmentVector == null || electricEquipmentVector.Count == 0)
            {
                return;
            }

            bool sensibleAssigned = false;
            bool latentAssigned = false;

            foreach (global::OpenStudio.ElectricEquipment electricEquipment in electricEquipmentVector)
            {
                global::OpenStudio.ElectricEquipmentDefinition definition = electricEquipment.electricEquipmentDefinition();

                double wattsPerArea = OptionalValue(definition.wattsperSpaceFloorArea());
                if (double.IsNaN(wattsPerArea))
                {
                    openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.LoadUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Electric equipment '{0}' uses the '{1}' calculation method, which carries no per-area density; no equipment gain was imported from it", electricEquipment.nameString(), definition.designLevelCalculationMethod()), label, internalCondition);
                    openStudioImportContext.RegisterSkip();
                    continue;
                }

                double fractionLatent = definition.fractionLatent();

                if (fractionLatent >= 1.0)
                {
                    if (AssignEquipmentGain(internalCondition, InternalConditionParameter.EquipmentLatentGainPerArea, wattsPerArea, latentAssigned, electricEquipment, openStudioImportContext, label))
                    {
                        latentAssigned = true;
                        AssignProfile(internalCondition, InternalConditionParameter.EquipmentLatentProfileName, electricEquipment.schedule(), ProfileType.EquipmentLatent, openStudioImportContext, profileLibrary);
                    }

                    continue;
                }

                if (AssignEquipmentGain(internalCondition, InternalConditionParameter.EquipmentSensibleGainPerArea, wattsPerArea * (1.0 - fractionLatent), sensibleAssigned, electricEquipment, openStudioImportContext, label))
                {
                    sensibleAssigned = true;
                    internalCondition.SetValue(InternalConditionParameter.EquipmentRadiantProportion, definition.fractionRadiant());
                    AssignProfile(internalCondition, InternalConditionParameter.EquipmentSensibleProfileName, electricEquipment.schedule(), ProfileType.EquipmentSensible, openStudioImportContext, profileLibrary);
                }

                if (fractionLatent > 0 && !latentAssigned)
                {
                    internalCondition.SetValue(InternalConditionParameter.EquipmentLatentGainPerArea, wattsPerArea * fractionLatent);
                    latentAssigned = true;
                    AssignProfile(internalCondition, InternalConditionParameter.EquipmentLatentProfileName, electricEquipment.schedule(), ProfileType.EquipmentLatent, openStudioImportContext, profileLibrary);

                    openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format(CultureInfo.InvariantCulture, "Electric equipment '{0}' is {1:P0} latent; the gain was split into SAM's separate sensible and latent parameters, which then share the one source schedule", electricEquipment.nameString(), fractionLatent), label, internalCondition);
                }
            }
        }

        /// <summary>
        /// Writes an equipment gain unless that slot is already taken, in which case the extra
        /// instance is reported instead of silently overwriting the first.
        /// </summary>
        private static bool AssignEquipmentGain(InternalCondition internalCondition, InternalConditionParameter parameter, double value, bool alreadyAssigned, global::OpenStudio.ElectricEquipment electricEquipment, OpenStudioImportContext openStudioImportContext, string label)
        {
            if (alreadyAssigned)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.LoadAssignmentConflict, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Electric equipment '{0}' would overwrite an equipment gain already imported for this space type; SAM holds one gain per kind, so it was not imported", electricEquipment.nameString()), label, internalCondition);
                openStudioImportContext.RegisterSkip();
                return false;
            }

            internalCondition.SetValue(parameter, value);
            return true;
        }

        /// <summary>
        /// SpaceInfiltrationDesignFlowRate → air changes per hour.
        /// <para>
        /// Only the native ACH parameterisation converts directly. The flow-based methods depend
        /// on the space's floor area, exterior area or volume, none of which a shared space type
        /// has — converting them here would silently attach one space's geometry to every space
        /// of the type — so they are reported instead.
        /// </para>
        /// </summary>
        private static void ToSAM_Infiltration(global::OpenStudio.SpaceType spaceType, InternalCondition internalCondition, OpenStudioImportContext openStudioImportContext, ProfileLibrary profileLibrary, string label)
        {
            global::OpenStudio.SpaceInfiltrationDesignFlowRateVector vector = spaceType.spaceInfiltrationDesignFlowRates();
            if (vector == null || vector.Count == 0)
            {
                return;
            }

            global::OpenStudio.SpaceInfiltrationDesignFlowRate infiltration = vector[0];

            if (vector.Count > 1)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.LoadAssignmentConflict, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Space type carries {0} infiltration objects; SAM holds one infiltration rate, so only the first was imported", vector.Count), label, internalCondition);
                openStudioImportContext.RegisterSkip();
            }

            double airChangesPerHour = OptionalValue(infiltration.airChangesperHour());
            if (!double.IsNaN(airChangesPerHour))
            {
                internalCondition.SetValue(InternalConditionParameter.InfiltrationAirChangesPerHour, airChangesPerHour);
                AssignProfile(internalCondition, InternalConditionParameter.InfiltrationProfileName, infiltration.schedule(), ProfileType.Infiltration, openStudioImportContext, profileLibrary);
                return;
            }

            openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.LoadUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Infiltration uses the '{0}' calculation method; converting it to SAM's air changes per hour needs the space's floor/exterior area or volume, which a shared space type does not have, so no infiltration rate was imported", infiltration.designFlowRateCalculationMethod()), label, internalCondition);
            openStudioImportContext.RegisterSkip();
        }

        /// <summary>
        /// DesignSpecificationOutdoorAir → SAM supply-air parameters, using whichever of the
        /// per-person, per-area, ACH or absolute fields the outdoor-air method actually populates.
        /// </summary>
        private static void ToSAM_OutdoorAir(global::OpenStudio.SpaceType spaceType, InternalCondition internalCondition, OpenStudioImportContext openStudioImportContext, ProfileLibrary profileLibrary, string label)
        {
            global::OpenStudio.OptionalDesignSpecificationOutdoorAir optional = spaceType.designSpecificationOutdoorAir();
            if (optional == null || optional.isNull())
            {
                return;
            }

            global::OpenStudio.DesignSpecificationOutdoorAir designSpecificationOutdoorAir = optional.get();

            AssignIfPositive(internalCondition, InternalConditionParameter.SupplyAirFlowPerPerson, designSpecificationOutdoorAir.outdoorAirFlowperPerson());
            AssignIfPositive(internalCondition, InternalConditionParameter.SupplyAirFlowPerArea, designSpecificationOutdoorAir.outdoorAirFlowperFloorArea());
            AssignIfPositive(internalCondition, InternalConditionParameter.SupplyAirChangesPerHour, designSpecificationOutdoorAir.outdoorAirFlowAirChangesperHour());
            AssignIfPositive(internalCondition, InternalConditionParameter.SupplyAirFlow, designSpecificationOutdoorAir.outdoorAirFlowRate());

            AssignProfile(internalCondition, InternalConditionParameter.VentilationProfileName, designSpecificationOutdoorAir.outdoorAirFlowRateFractionSchedule(), ProfileType.Ventilation, openStudioImportContext, profileLibrary);
        }

        /// <summary>
        /// Reports the load kinds present on the space type that SAM has no representation for.
        /// Each is named once per model so a template with fifty space types does not produce
        /// fifty identical warnings.
        /// </summary>
        private static void ReportUnsupportedLoads(global::OpenStudio.SpaceType spaceType, OpenStudioImportContext openStudioImportContext, string label)
        {
            ReportUnsupportedLoad(spaceType.gasEquipment()?.Count ?? 0, "GasEquipment", openStudioImportContext, label);
            ReportUnsupportedLoad(spaceType.steamEquipment()?.Count ?? 0, "SteamEquipment", openStudioImportContext, label);
            ReportUnsupportedLoad(spaceType.hotWaterEquipment()?.Count ?? 0, "HotWaterEquipment", openStudioImportContext, label);
            ReportUnsupportedLoad(spaceType.otherEquipment()?.Count ?? 0, "OtherEquipment", openStudioImportContext, label);
            ReportUnsupportedLoad(spaceType.luminaires()?.Count ?? 0, "Luminaire", openStudioImportContext, label);
            ReportUnsupportedLoad(spaceType.electricEquipmentITEAirCooled()?.Count ?? 0, "ElectricEquipment:ITE:AirCooled", openStudioImportContext, label);
            ReportUnsupportedLoad(spaceType.internalMass()?.Count ?? 0, "InternalMass", openStudioImportContext, label);
            ReportUnsupportedLoad(spaceType.spaceInfiltrationEffectiveLeakageAreas()?.Count ?? 0, "SpaceInfiltration:EffectiveLeakageArea", openStudioImportContext, label);
            ReportUnsupportedLoad(spaceType.spaceInfiltrationFlowCoefficients()?.Count ?? 0, "SpaceInfiltration:FlowCoefficient", openStudioImportContext, label);
        }

        private static void ReportUnsupportedLoad(int count, string kind, OpenStudioImportContext openStudioImportContext, string label)
        {
            if (count <= 0 || !openStudioImportContext.RegisterOnce("SAM-OSI-LOAD-001:" + kind))
            {
                return;
            }

            openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.LoadUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("{0} loads have no SAM equivalent and were not imported; their gains are absent from the SAM model", kind), label);
            openStudioImportContext.RegisterSkip();
        }

        /// <summary>
        /// Converts a schedule and records its SAM profile name on the internal condition. A
        /// schedule that cannot be expanded leaves the profile name unset — the load then has no
        /// profile, which is visible, rather than an invented always-on one.
        /// </summary>
        private static void AssignProfile(InternalCondition internalCondition, InternalConditionParameter parameter, global::OpenStudio.OptionalSchedule optionalSchedule, ProfileType profileType, OpenStudioImportContext openStudioImportContext, ProfileLibrary profileLibrary)
        {
            if (optionalSchedule == null || optionalSchedule.isNull())
            {
                return;
            }

            Profile profile = optionalSchedule.get().ToSAM(profileType, openStudioImportContext, profileLibrary);
            if (profile != null)
            {
                internalCondition.SetValue(parameter, profile.Name);
            }
        }

        /// <summary>
        /// Value of a schedule that is constant across the year, or NaN when it varies. Used for
        /// quantities SAM stores as a single number (the People activity level), where a varying
        /// schedule genuinely has no single equivalent.
        /// </summary>
        private static double ConstantScheduleValue(global::OpenStudio.OptionalSchedule optionalSchedule)
        {
            if (optionalSchedule == null || optionalSchedule.isNull())
            {
                return double.NaN;
            }

            global::OpenStudio.OptionalScheduleConstant optionalScheduleConstant = global::OpenStudio.OpenStudioModelResources.toScheduleConstant(optionalSchedule.get());
            if (optionalScheduleConstant != null && !optionalScheduleConstant.isNull())
            {
                return optionalScheduleConstant.get().value();
            }

            // A ruleset whose default day is a single constant value is equally constant; that is
            // how OpenStudio templates usually express an activity level.
            global::OpenStudio.OptionalScheduleRuleset optionalScheduleRuleset = global::OpenStudio.OpenStudioModelCore.toScheduleRuleset(optionalSchedule.get());
            if (optionalScheduleRuleset == null || optionalScheduleRuleset.isNull())
            {
                return double.NaN;
            }

            global::OpenStudio.ScheduleRuleset scheduleRuleset = optionalScheduleRuleset.get();
            if (scheduleRuleset.scheduleRules() != null && scheduleRuleset.scheduleRules().Count > 0)
            {
                return double.NaN;
            }

            global::OpenStudio.DoubleVector values = scheduleRuleset.defaultDaySchedule()?.values();
            if (values == null || values.Count == 0)
            {
                return double.NaN;
            }

            double first = values[0];
            for (int i = 1; i < values.Count; i++)
            {
                if (Math.Abs(values[i] - first) > 1e-9)
                {
                    return double.NaN;
                }
            }

            return first;
        }

        private static void AssignIfPositive(InternalCondition internalCondition, InternalConditionParameter parameter, double value)
        {
            if (!double.IsNaN(value) && value > 0)
            {
                internalCondition.SetValue(parameter, value);
            }
        }

        /// <summary>Value of an OpenStudio optional double, or NaN when absent.</summary>
        private static double OptionalValue(global::OpenStudio.OptionalDouble optionalDouble)
        {
            return optionalDouble == null || optionalDouble.isNull() ? double.NaN : optionalDouble.get();
        }
    }
}
