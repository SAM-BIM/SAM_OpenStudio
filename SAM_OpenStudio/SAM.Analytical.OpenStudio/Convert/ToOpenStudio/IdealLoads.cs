// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Attaches a ZoneHVACIdealLoadsAirSystem to a conditioned thermal zone (one system per
        /// zone). MVP settings are the EnergyPlus object defaults: no capacity or air-flow
        /// limits, no outdoor-air economizer, no heat recovery, no humidity control — documented
        /// deliberately (plan §12).
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
