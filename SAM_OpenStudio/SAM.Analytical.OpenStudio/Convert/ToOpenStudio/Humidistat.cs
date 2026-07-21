// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Creates a ZoneControlHumidistat for a space's thermal zone from the InternalCondition's
        /// Humidification and/or Dehumidification profiles (SAM carries setpoint PROFILES, not
        /// numeric setpoints — coverage manifest: InternalConditionParameter.Humidification/
        /// DehumidificationProfileName). Profiles convert with Percent type limits (0–100 %RH).
        /// A named-but-unresolved profile raises SAM-OS-SCH-001 (error). Returns null when
        /// neither profile is present — the zone then keeps no humidity control.
        /// </summary>
        /// <param name="space">SAM space.</param>
        /// <param name="thermalZone">The zone created for the space.</param>
        /// <param name="openStudioConversionContext">Conversion context.</param>
        /// <returns>The humidistat, or null when no humidity profiles exist (or a diagnostic was raised).</returns>
        public static global::OpenStudio.ZoneControlHumidistat ToOpenStudio_Humidistat(this Space space, global::OpenStudio.ThermalZone thermalZone, OpenStudioConversionContext openStudioConversionContext)
        {
            if (space == null || thermalZone == null || openStudioConversionContext == null)
            {
                return null;
            }

            string name = Core.OpenStudio.Query.OpenStudioName("Humidistat", space.Name, space.Guid);

            InternalCondition internalCondition = space.InternalCondition;
            if (internalCondition == null)
            {
                return null;
            }

            ProfileLibrary profileLibrary = openStudioConversionContext.Source?.ProfileLibrary;

            Profile humidificationProfile = ResolveHumidityProfile(internalCondition, profileLibrary, ProfileType.Humidification, space, name, openStudioConversionContext);
            Profile dehumidificationProfile = ResolveHumidityProfile(internalCondition, profileLibrary, ProfileType.Dehumidification, space, name, openStudioConversionContext);
            if (humidificationProfile == null && dehumidificationProfile == null)
            {
                return null;
            }

            global::OpenStudio.Schedule humidificationSchedule = humidificationProfile?.ToOpenStudio(ProfileType.Humidification, openStudioConversionContext);
            global::OpenStudio.Schedule dehumidificationSchedule = dehumidificationProfile?.ToOpenStudio(ProfileType.Dehumidification, openStudioConversionContext);
            if ((humidificationProfile != null && humidificationSchedule == null) || (dehumidificationProfile != null && dehumidificationSchedule == null))
            {
                return null;
            }

            global::OpenStudio.ZoneControlHumidistat result = new global::OpenStudio.ZoneControlHumidistat(openStudioConversionContext.Target);
            result.setName(name);
            if (humidificationSchedule != null)
            {
                result.setHumidifyingRelativeHumiditySetpointSchedule(humidificationSchedule);
            }

            if (dehumidificationSchedule != null)
            {
                result.setDehumidifyingRelativeHumiditySetpointSchedule(dehumidificationSchedule);
            }

            thermalZone.setZoneControlHumidistat(result);
            return result;
        }

        private static Profile ResolveHumidityProfile(InternalCondition internalCondition, ProfileLibrary profileLibrary, ProfileType profileType, Space space, string openStudioObjectName, OpenStudioConversionContext openStudioConversionContext)
        {
            string profileName = internalCondition.GetProfileName(profileType);
            if (profileName == null)
            {
                return null;
            }

            Profile profile = profileLibrary == null ? null : internalCondition.GetProfile(profileType, profileLibrary);
            if (profile == null)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ScheduleMissingProfile, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("The {0} profile '{1}' was named but not found; the humidity control is incomplete", profileType, profileName), space, openStudioObjectName);
            }

            return profile;
        }
    }
}
