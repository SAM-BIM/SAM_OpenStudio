// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Creates a dual-setpoint thermostat for a conditioned space's thermal zone from the
        /// InternalCondition's Heating and Cooling profiles. A conditioned zone missing either
        /// setpoint profile raises SAM-OS-HVAC-001 (error). Heating exceeding cooling at any hour
        /// raises SAM-OS-HVAC-001 (error) — there is no override in the MVP.
        /// </summary>
        /// <param name="space">Conditioned SAM space.</param>
        /// <param name="thermalZone">The zone created for the space.</param>
        /// <param name="openStudioConversionContext">Conversion context.</param>
        /// <returns>The thermostat, or null (diagnostic raised).</returns>
        public static global::OpenStudio.ThermostatSetpointDualSetpoint ToOpenStudio_Thermostat(this Space space, global::OpenStudio.ThermalZone thermalZone, OpenStudioConversionContext openStudioConversionContext)
        {
            if (space == null || thermalZone == null || openStudioConversionContext == null)
            {
                return null;
            }

            string name = Core.OpenStudio.Query.OpenStudioName("Thermostat", space.Name, space.Guid);

            InternalCondition internalCondition = space.InternalCondition;
            ProfileLibrary profileLibrary = openStudioConversionContext.Source?.ProfileLibrary;

            Profile heatingProfile = internalCondition == null || profileLibrary == null ? null : internalCondition.GetProfile(ProfileType.Heating, profileLibrary);
            Profile coolingProfile = internalCondition == null || profileLibrary == null ? null : internalCondition.GetProfile(ProfileType.Cooling, profileLibrary);

            if (heatingProfile == null || coolingProfile == null)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.HvacMissingSetpoints, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Conditioned space is missing {0} setpoint profile(s); no thermostat was created", heatingProfile == null && coolingProfile == null ? "heating and cooling" : heatingProfile == null ? "heating" : "cooling"), space, name);
                return null;
            }

            double[] heatingValues = AnnualHourlyValues(heatingProfile, name, openStudioConversionContext);
            double[] coolingValues = AnnualHourlyValues(coolingProfile, name, openStudioConversionContext);
            if (heatingValues != null && coolingValues != null)
            {
                for (int i = 0; i < heatingValues.Length && i < coolingValues.Length; i++)
                {
                    if (heatingValues[i] > coolingValues[i])
                    {
                        openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.HvacMissingSetpoints, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Heating setpoint {0} °C exceeds cooling setpoint {1} °C at hour {2}; no thermostat was created", heatingValues[i], coolingValues[i], i), space, name);
                        return null;
                    }
                }
            }

            global::OpenStudio.Schedule heatingSchedule = heatingProfile.ToOpenStudio(ProfileType.Heating, openStudioConversionContext);
            global::OpenStudio.Schedule coolingSchedule = coolingProfile.ToOpenStudio(ProfileType.Cooling, openStudioConversionContext);
            if (heatingSchedule == null || coolingSchedule == null)
            {
                return null;
            }

            global::OpenStudio.ThermostatSetpointDualSetpoint result = new global::OpenStudio.ThermostatSetpointDualSetpoint(openStudioConversionContext.Target);
            result.setName(name);
            result.setHeatingSetpointTemperatureSchedule(heatingSchedule);
            result.setCoolingSetpointTemperatureSchedule(coolingSchedule);

            thermalZone.setThermostatSetpointDualSetpoint(result);
            return result;
        }
    }
}
