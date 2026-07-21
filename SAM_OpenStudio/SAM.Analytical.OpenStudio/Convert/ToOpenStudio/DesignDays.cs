// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// The ASHRAE annual heating design-day name: "… Ann Htg 99.6% Condns DB". Requiring
        /// "Htg" directly before "99.6%" excludes the wind ("Ann Htg Wind 99.6% Condns
        /// WS=>MCDB") and humidification ("Ann Hum_n 99.6% Condns DP=>MCDB") days that share
        /// the 99.6% figure (review P1-02).
        /// </summary>
        private static readonly Regex HeatingDesignDayName = new Regex(@"Ann\s+Htg\s+99\.6\s*%\s+Condns\s+DB", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// The ASHRAE annual cooling design-day name: "… Ann Clg .4% Condns DB=>MWB" — written
        /// WITHOUT a leading zero in ASHRAE/climate.onebuilding DDY files (accepted with one
        /// too), MWB/MCWB variants both accepted. Restricting to the DB=>M(C)WB condition keeps
        /// the WB=>MDB / DP=>MDB / Enth=>MDB variants and the monthly ".4%" days out.
        /// </summary>
        private static readonly Regex CoolingDesignDayName = new Regex(@"Ann\s+Clg\s+0?\.4\s*%\s+Condns\s+DB\s*=>\s*M(C)?WB", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Imports design days from a DDY file into the target model (C4). By default exactly
        /// the ASHRAE annual pair is kept: the heating 99.6% dry-bulb day and the cooling .4%
        /// DB=>MWB day (review P1-02 — the humidification and wind 99.6% days never match).
        /// A missing side raises a warning; when neither side matches, the import falls back to
        /// all days with a warning. OpenStudioConversionOptions.ImportAllDesignDays imports
        /// every design day. Sets OpenStudioConversionContext.DesignDaysImported (drives
        /// sizing-period enablement).
        /// </summary>
        /// <param name="openStudioConversionContext">Conversion context.</param>
        /// <param name="ddyPath">DDY file path; null/empty or unreadable → no import (warning when a path was given).</param>
        /// <returns>Number of imported design days.</returns>
        public static int ToOpenStudio_DesignDays(this OpenStudioConversionContext openStudioConversionContext, string ddyPath)
        {
            if (openStudioConversionContext == null)
            {
                return 0;
            }

            if (string.IsNullOrWhiteSpace(ddyPath))
            {
                return 0;
            }

            List<global::OpenStudio.DesignDay> designDays = Query.DesignDays(ddyPath);
            if (designDays == null || designDays.Count == 0)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("DDY file contains no importable design days: {0}", ddyPath));
                return 0;
            }

            List<global::OpenStudio.DesignDay> selected = designDays;
            if (!openStudioConversionContext.Options.ImportAllDesignDays)
            {
                List<global::OpenStudio.DesignDay> heatingDays = designDays.FindAll(x => HeatingDesignDayName.IsMatch(x.nameString()));
                List<global::OpenStudio.DesignDay> coolingDays = designDays.FindAll(x => CoolingDesignDayName.IsMatch(x.nameString()));

                if (heatingDays.Count == 0 && coolingDays.Count == 0)
                {
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "No annual heating 99.6% / cooling .4% design days found by name convention; all DDY design days were imported");
                }
                else
                {
                    if (heatingDays.Count == 0)
                    {
                        openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "No annual heating 99.6% dry-bulb design day found by name convention — heating sizing has no design day");
                    }

                    if (coolingDays.Count == 0)
                    {
                        openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "No annual cooling .4% DB=>MWB design day found by name convention — cooling sizing has no design day");
                    }

                    selected = new List<global::OpenStudio.DesignDay>();
                    selected.AddRange(heatingDays);
                    selected.AddRange(coolingDays);
                }
            }

            foreach (global::OpenStudio.DesignDay designDay in selected)
            {
                designDay.clone(openStudioConversionContext.Target);
            }

            openStudioConversionContext.DesignDaysImported = selected.Count > 0;
            openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format("Imported {0} design day(s) from {1}", selected.Count, Path.GetFileName(ddyPath)));
            return selected.Count;
        }

        /// <summary>
        /// Translates the design days embedded in the AnalyticalModel
        /// (AnalyticalModelParameter.HeatingDesignDays / CoolingDesignDays) into OpenStudio
        /// SizingPeriod:DesignDay objects. This is the fallback design-day source when no
        /// explicit DDY path is supplied — never combined with a DDY import in the same model.
        /// SAM design days are hourly WeatherDay profiles (dry bulb, relative humidity, wind,
        /// pressure, solar) with a calendar date; the translation to the EnergyPlus
        /// steady-state design-day object approximates (each approximation is named in a
        /// warning, nothing is silently downgraded): the daily range is the 24 h max-min
        /// spread, humidity becomes a constant dew point at the maximum-dry-bulb hour
        /// (Magnus formula), wind becomes the mean speed and circular-mean direction,
        /// pressure becomes the daily mean, and the solar model is ASHRAEClearSky with
        /// clearness 0.0 for heating days / 1.0 for cooling days (the hourly SAM radiation
        /// profile is not fitted). Heating days become WinterDesignDay, cooling days
        /// SummerDesignDay day types. Sets OpenStudioConversionContext.DesignDaysImported.
        /// </summary>
        /// <param name="openStudioConversionContext">Conversion context.</param>
        /// <param name="heatingDesignDays">Embedded SAM heating design days.</param>
        /// <param name="coolingDesignDays">Embedded SAM cooling design days.</param>
        /// <returns>Number of imported design days.</returns>
        public static int ToOpenStudio_DesignDays(this OpenStudioConversionContext openStudioConversionContext, IEnumerable<DesignDay> heatingDesignDays, IEnumerable<DesignDay> coolingDesignDays)
        {
            if (openStudioConversionContext == null)
            {
                return 0;
            }

            int heatingCount = 0;
            if (heatingDesignDays != null)
            {
                foreach (DesignDay designDay in heatingDesignDays)
                {
                    if (ToOpenStudio_DesignDay(designDay, "WinterDesignDay", 0.0, openStudioConversionContext) != null)
                    {
                        heatingCount++;
                    }
                }
            }

            int coolingCount = 0;
            if (coolingDesignDays != null)
            {
                foreach (DesignDay designDay in coolingDesignDays)
                {
                    if (ToOpenStudio_DesignDay(designDay, "SummerDesignDay", 1.0, openStudioConversionContext) != null)
                    {
                        coolingCount++;
                    }
                }
            }

            int result = heatingCount + coolingCount;
            openStudioConversionContext.DesignDaysImported = result > 0;
            return result;
        }

        private static global::OpenStudio.DesignDay ToOpenStudio_DesignDay(DesignDay designDay, string dayType, double skyClearness, OpenStudioConversionContext openStudioConversionContext)
        {
            if (designDay == null)
            {
                return null;
            }

            string name = string.IsNullOrWhiteSpace(designDay.Name) ? "SAM Design Day" : designDay.Name;

            if (designDay.Month < 1 || designDay.Month > 12 || designDay.Day < 1 || designDay.Day > 31)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Embedded design day '{0}' has an invalid month/day ({1}/{2}); skipped", name, designDay.Month, designDay.Day));
                return null;
            }

            double[] dryBulbTemperatures = designDay[Weather.WeatherDataType.DryBulbTemperature];
            if (dryBulbTemperatures == null || dryBulbTemperatures.Length != 24 || System.Array.TrueForAll(dryBulbTemperatures, double.IsNaN))
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Embedded design day '{0}' has no hourly dry-bulb temperature profile; skipped — a design day is never silently downgraded", name));
                return null;
            }

            int hourMax = -1;
            double maxDryBulb = double.MinValue;
            double minDryBulb = double.MaxValue;
            for (int i = 0; i < 24; i++)
            {
                double value = dryBulbTemperatures[i];
                if (double.IsNaN(value))
                {
                    continue;
                }

                if (value > maxDryBulb)
                {
                    maxDryBulb = value;
                    hourMax = i;
                }

                if (value < minDryBulb)
                {
                    minDryBulb = value;
                }
            }

            global::OpenStudio.DesignDay result = new global::OpenStudio.DesignDay(openStudioConversionContext.Target);
            result.setName(Core.OpenStudio.Query.SanitizeName(name));
            result.setMonth(designDay.Month);
            result.setDayOfMonth(designDay.Day);
            result.setDayType(dayType);
            result.setMaximumDryBulbTemperature(maxDryBulb);
            result.setDailyDryBulbTemperatureRange(maxDryBulb - minDryBulb);

            double[] relativeHumidities = designDay[Weather.WeatherDataType.RelativeHumidity];
            double relativeHumidity = relativeHumidities != null && hourMax != -1 && relativeHumidities.Length == 24 ? relativeHumidities[hourMax] : double.NaN;
            if (!double.IsNaN(relativeHumidity) && relativeHumidity > 0 && relativeHumidity <= 100)
            {
                result.setHumidityIndicatingType("DewPoint");
                result.setHumidityIndicatingConditionsAtMaximumDryBulb(DewPoint(maxDryBulb, relativeHumidity));
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Embedded design day '{0}': the hourly humidity profile was approximated as a constant dew point ({1:0.0} °C) from {2:0}% RH at the maximum-dry-bulb hour", name, DewPoint(maxDryBulb, relativeHumidity), relativeHumidity));
            }
            else
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Embedded design day '{0}' has no usable relative-humidity profile; the EnergyPlus default humidity condition applies", name));
            }

            double[] pressures = designDay[Weather.WeatherDataType.AtmosphericPressure];
            double pressure = Mean(pressures);
            if (!double.IsNaN(pressure) && pressure > 0)
            {
                result.setBarometricPressure(pressure);
            }

            double[] windSpeeds = designDay[Weather.WeatherDataType.WindSpeed];
            double windSpeed = Mean(windSpeeds);
            if (!double.IsNaN(windSpeed) && windSpeed >= 0)
            {
                result.setWindSpeed(windSpeed);
            }

            double[] windDirections = designDay[Weather.WeatherDataType.WindDirection];
            double windDirection = CircularMean(windDirections);
            if (!double.IsNaN(windDirection))
            {
                result.setWindDirection(windDirection);
            }

            result.setSolarModelIndicator("ASHRAEClearSky");
            result.setSkyClearness(skyClearness);
            openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Embedded design day '{0}': the hourly solar profile was approximated by the ASHRAEClearSky model with clearness {1:0.0}; daily dry-bulb range, mean wind/pressure and the {2} day type are derived from the SAM hourly profile", name, skyClearness, dayType));

            return result;
        }

        /// <summary>Magnus (Alduchov–Eskridge) dew point [°C] from dry bulb [°C] and RH [%].</summary>
        private static double DewPoint(double dryBulbTemperature, double relativeHumidity)
        {
            const double a = 17.625;
            const double b = 243.04;

            double gamma = (a * dryBulbTemperature / (b + dryBulbTemperature)) + System.Math.Log(relativeHumidity / 100.0);
            return (b * gamma) / (a - gamma);
        }

        private static double Mean(double[] values)
        {
            if (values == null || values.Length == 0)
            {
                return double.NaN;
            }

            double sum = 0;
            int count = 0;
            foreach (double value in values)
            {
                if (double.IsNaN(value))
                {
                    continue;
                }

                sum += value;
                count++;
            }

            return count == 0 ? double.NaN : sum / count;
        }

        private static double CircularMean(double[] directions)
        {
            if (directions == null || directions.Length == 0)
            {
                return double.NaN;
            }

            double x = 0;
            double y = 0;
            int count = 0;
            foreach (double direction in directions)
            {
                if (double.IsNaN(direction))
                {
                    continue;
                }

                double radians = direction * System.Math.PI / 180.0;
                x += System.Math.Cos(radians);
                y += System.Math.Sin(radians);
                count++;
            }

            if (count == 0 || (System.Math.Abs(x) < 1e-12 && System.Math.Abs(y) < 1e-12))
            {
                return double.NaN;
            }

            double result = System.Math.Atan2(y, x) * 180.0 / System.Math.PI;
            return result < 0 ? result + 360.0 : result;
        }
    }
}
