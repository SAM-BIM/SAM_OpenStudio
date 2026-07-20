// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using System.IO;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Validates the EPW file, assigns it as the model's weather file and populates the Site
        /// (latitude, longitude, time zone, elevation). The EPW is an explicit input.
        /// Precedence (never silently overridden): the SAM model Location overrides the EPW
        /// site coordinates with an information diagnostic; ground temperatures come from the
        /// SAM model WeatherData (nearest-to-surface set) when present, otherwise the EPW
        /// header (imported by OpenStudio itself), otherwise the EnergyPlus 18 °C default is
        /// named in a warning. Failures raise SAM-OS-RUN-001 errors.
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

            Core.Location location = openStudioConversionContext.Source?.Location;
            if (location != null)
            {
                site.setLatitude(location.Latitude);
                site.setLongitude(location.Longitude);
                site.setElevation(location.Elevation);
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format("Site coordinates taken from the SAM model Location (lat {0}, lon {1}, elevation {2} m), overriding the EPW header; the time zone stays with the EPW", location.Latitude, location.Longitude, location.Elevation));
            }

            ApplyGroundTemperatures(openStudioConversionContext, epwPath);
            return true;
        }

        /// <summary>
        /// Ground-temperature precedence: SAM model WeatherData (nearest-to-surface set) →
        /// EPW GROUND TEMPERATURES header (parsed via SAM.Weather's native EPW reader —
        /// OpenStudio's setWeatherFile does NOT import them) → warning naming the EnergyPlus
        /// 18 °C default. STAT parsing is deferred (no parser exists in the SAM ecosystem).
        /// </summary>
        private static void ApplyGroundTemperatures(OpenStudioConversionContext openStudioConversionContext, string epwPath)
        {
            Weather.WeatherData weatherData = null;
            openStudioConversionContext.Source?.TryGetValue(AnalyticalModelParameter.WeatherData, out weatherData);

            if (weatherData != null && weatherData.TryGetValue(Weather.WeatherDataParameter.GroundTemperatures, out Core.SAMCollection<Weather.GroundTemperature> groundTemperatures) && groundTemperatures != null)
            {
                Weather.GroundTemperature nearestToSurface = null;
                foreach (Weather.GroundTemperature groundTemperature in groundTemperatures)
                {
                    if (groundTemperature?.Temperatures == null || groundTemperature.Temperatures.Length < 12)
                    {
                        continue;
                    }

                    if (nearestToSurface == null || (double.IsNaN(nearestToSurface.Depth) && !double.IsNaN(groundTemperature.Depth)) || (!double.IsNaN(groundTemperature.Depth) && groundTemperature.Depth < nearestToSurface.Depth))
                    {
                        nearestToSurface = groundTemperature;
                    }
                }

                if (nearestToSurface != null)
                {
                    SetGroundTemperatures(openStudioConversionContext.Target, nearestToSurface.Temperatures);
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format("Ground temperatures taken from the SAM model WeatherData (depth {0} m set, 12 monthly values)", nearestToSurface.Depth));
                    return;
                }
            }

            List<Weather.GroundTemperature> epwGroundTemperatures = null;
            try
            {
                // OpenStudio's setWeatherFile does NOT import ground temperatures — parse the
                // EPW header with SAM.Weather's native EPW reader.
                string[] lines = File.ReadAllLines(epwPath);
                int index = -1;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].IndexOf("GROUND TEMPERATURES", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        index = i;
                        break;
                    }

                    if (lines[i].StartsWith("DATA PERIODS", System.StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }

                if (index >= 0)
                {
                    Weather.Query.TryGetGroundTemperatures(lines, index, out epwGroundTemperatures);
                }
            }
            catch (System.Exception)
            {
                // header parse is best effort; the EPW itself was already validated
            }

            Weather.GroundTemperature epwNearestToSurface = null;
            if (epwGroundTemperatures != null)
            {
                foreach (Weather.GroundTemperature groundTemperature in epwGroundTemperatures)
                {
                    if (groundTemperature?.Temperatures == null || groundTemperature.Temperatures.Length < 12)
                    {
                        continue;
                    }

                    if (epwNearestToSurface == null || (double.IsNaN(epwNearestToSurface.Depth) && !double.IsNaN(groundTemperature.Depth)) || (!double.IsNaN(groundTemperature.Depth) && groundTemperature.Depth < epwNearestToSurface.Depth))
                    {
                        epwNearestToSurface = groundTemperature;
                    }
                }
            }

            if (epwNearestToSurface != null)
            {
                SetGroundTemperatures(openStudioConversionContext.Target, epwNearestToSurface.Temperatures);
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format("Ground temperatures taken from the EPW GROUND TEMPERATURES header (depth {0} m set, 12 monthly values)", epwNearestToSurface.Depth));
            }
            else
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "No ground temperatures in the SAM model WeatherData or the EPW header; the EnergyPlus 18 °C default ground temperature applies");
            }
        }

        private static void SetGroundTemperatures(global::OpenStudio.Model model, double[] temperatures)
        {
            global::OpenStudio.SiteGroundTemperatureBuildingSurface siteGroundTemperature = model.getSiteGroundTemperatureBuildingSurface();
            siteGroundTemperature.setJanuaryGroundTemperature(temperatures[0]);
            siteGroundTemperature.setFebruaryGroundTemperature(temperatures[1]);
            siteGroundTemperature.setMarchGroundTemperature(temperatures[2]);
            siteGroundTemperature.setAprilGroundTemperature(temperatures[3]);
            siteGroundTemperature.setMayGroundTemperature(temperatures[4]);
            siteGroundTemperature.setJuneGroundTemperature(temperatures[5]);
            siteGroundTemperature.setJulyGroundTemperature(temperatures[6]);
            siteGroundTemperature.setAugustGroundTemperature(temperatures[7]);
            siteGroundTemperature.setSeptemberGroundTemperature(temperatures[8]);
            siteGroundTemperature.setOctoberGroundTemperature(temperatures[9]);
            siteGroundTemperature.setNovemberGroundTemperature(temperatures[10]);
            siteGroundTemperature.setDecemberGroundTemperature(temperatures[11]);
        }
    }
}
