// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.IO;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Validates the EPW file, assigns it as the model's weather file and populates the Site
        /// (latitude, longitude, time zone, elevation) from it. The EPW is an explicit input —
        /// SAM weather data is not consulted in the MVP. Failures raise SAM-OS-RUN-001 errors.
        /// </summary>
        /// <param name="openStudioConversionContext">Conversion context.</param>
        /// <param name="epwPath">Path to the EPW weather file.</param>
        /// <returns>True when the weather file was assigned.</returns>
        public static bool ToOpenStudio_Weather(this OpenStudioConversionContext openStudioConversionContext, string epwPath)
        {
            if (openStudioConversionContext == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(epwPath) || !File.Exists(epwPath))
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("EPW weather file not found: {0}", epwPath));
                return false;
            }

            global::OpenStudio.OptionalEpwFile optionalEpwFile = global::OpenStudio.EpwFile.load(global::OpenStudio.OpenStudioUtilitiesCore.toPath(epwPath));
            if (optionalEpwFile == null || optionalEpwFile.isNull())
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("EPW weather file could not be parsed: {0}", epwPath));
                return false;
            }

            global::OpenStudio.EpwFile epwFile = optionalEpwFile.get();

            global::OpenStudio.OptionalWeatherFile optionalWeatherFile = global::OpenStudio.WeatherFile.setWeatherFile(openStudioConversionContext.Target, epwFile);
            if (optionalWeatherFile == null || optionalWeatherFile.isNull())
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "OpenStudio rejected the EPW weather file");
                return false;
            }

            global::OpenStudio.Site site = openStudioConversionContext.Target.getSite();
            site.setName("SAM_Site_" + Core.OpenStudio.Query.SanitizeName(Path.GetFileNameWithoutExtension(epwPath)));
            site.setLatitude(epwFile.latitude());
            site.setLongitude(epwFile.longitude());
            site.setTimeZone(epwFile.timeZone());
            site.setElevation(epwFile.elevation());

            return true;
        }
    }
}
