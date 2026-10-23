// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Attaches a ZoneHVACIdealLoadsAirSystem to a conditioned thermal zone (one system per
        /// zone). Settings are the EnergyPlus object defaults (no capacity or air-flow limits,
        /// no outdoor-air economizer, no heat recovery) except humidity control: when the zone
        /// carries a ZoneControlHumidistat with a humidifying and/or dehumidifying schedule, the
        /// matching control type is set to Humidistat (coverage manifest:
        /// InternalConditionParameter.Humidification/DehumidificationProfileName).
        /// </summary>
        /// <param name="thermalZone">Conditioned thermal zone.</param>
        /// <param name="space">Source SAM space (naming and diagnostics).</param>
        /// <param name="openStudioConversionContext">Conversion context.</param>
        /// <returns>The attached system, or null (diagnostic raised).</returns>
        public static global::OpenStudio.ZoneHVACIdealLoadsAirSystem ToOpenStudio_IdealLoads(this global::OpenStudio.ThermalZone thermalZone, Space space, OpenStudioConversionContext openStudioConversionContext)
        {
            if (thermalZone == null || space == null || openStudioConversionContext == null)
            {
                return null;
            }

            global::OpenStudio.ZoneHVACIdealLoadsAirSystem result = new global::OpenStudio.ZoneHVACIdealLoadsAirSystem(openStudioConversionContext.Target);
            result.setName(Core.OpenStudio.Query.OpenStudioName("IdealLoads", space.Name, space.Guid));

            global::OpenStudio.OptionalZoneControlHumidistat optionalHumidistat = thermalZone.zoneControlHumidistat();
            if (optionalHumidistat != null && !optionalHumidistat.isNull())
            {
                global::OpenStudio.ZoneControlHumidistat humidistat = optionalHumidistat.get();
                if (!humidistat.humidifyingRelativeHumiditySetpointSchedule().isNull())
                {
                    result.setHumidificationControlType("Humidistat");
                }

                if (!humidistat.dehumidifyingRelativeHumiditySetpointSchedule().isNull())
                {
                    result.setDehumidificationControlType("Humidistat");
                }
            }

            // Outdoor-air controls (S07 D-F2, T-OA-SYS): stated explicitly rather than left to defaults, so the delivered
            // outdoor air is exactly the space's effective DSOA (its per-space override, else the inherited SpaceType one)
            // times its fraction schedule, at the design occupancy: no demand-controlled ventilation, no economizer and
            // no heat recovery. A space without a DSOA (no requirement, V0) leaves the link empty: no outdoor air.
            result.setDemandControlledVentilationType("None");
            result.setOutdoorAirEconomizerType("NoEconomizer");
            result.setHeatRecoveryType("None");
            if (openStudioConversionContext.TryGetModelObject(space.Guid, out global::OpenStudio.Space openStudioSpace))
            {
                global::OpenStudio.OptionalDesignSpecificationOutdoorAir designSpecificationOutdoorAir = openStudioSpace.designSpecificationOutdoorAir();
                if (designSpecificationOutdoorAir != null && !designSpecificationOutdoorAir.isNull())
                {
                    result.setDesignSpecificationOutdoorAirObject(designSpecificationOutdoorAir.get());
                }
            }

            if (!result.addToThermalZone(thermalZone))
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.HvacMissingSetpoints, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "Ideal Loads air system could not be attached to the thermal zone", space, thermalZone.nameString());
                result.remove();
                return null;
            }

            return result;
        }
    }
}
