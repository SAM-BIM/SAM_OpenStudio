// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Create
    {
        public static List<DesignDay> DesignDays(this string path)
        {
            return DesignDays(path, out _);
        }

        /// <summary>
        /// Reads the design-day environments of an EnergyPlus SQLite output into SAM hourly
        /// DesignDay objects. Annual weather-run environments (EnvironmentType 3) are not
        /// design days and are skipped when the schema carries the type column. Raw SQL
        /// timestamps (hour 0–24, minute 0–60, year 0 on sizing environments) are normalised
        /// through the central <see cref="Core.OpenStudio.Query.TryGetDateTime"/> helper —
        /// malformed rows are skipped and reported through <paramref name="diagnostics"/>,
        /// never thrown as unrepresentable-DateTime exceptions. Each day is classified
        /// heating/cooling from its environment name (Htg/Clg).
        /// </summary>
        /// <param name="path">EnergyPlus SQLite output path.</param>
        /// <param name="diagnostics">Structured per-environment/per-row diagnostics; empty when everything read cleanly.</param>
        /// <returns>The design days (possibly empty), or null when the file does not exist.</returns>
        public static List<DesignDay> DesignDays(this string path, out List<string> diagnostics)
        {
            diagnostics = new List<string>();

            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            {
                return null;
            }

            List<DesignDay> result = null;
            using (SQLiteConnection sQLiteConnection = Core.SQLite.Create.SQLiteConnection(path))
            {
                DataTable dataTable_EnvironmentPeriods = Core.SQLite.Query.DataTable(sQLiteConnection, "EnvironmentPeriods", "EnvironmentPeriodIndex", "EnvironmentName", "EnvironmentType");
                DataTable dataTable_ReportDataDictionary = Core.SQLite.Query.DataTable(sQLiteConnection, "ReportDataDictionary", "ReportDataDictionaryIndex", "KeyValue", "Name", "Units");
                DataTable dataTable_Time = Core.SQLite.Query.DataTable(sQLiteConnection, "Time", "TimeIndex", "Year", "Month", "Day", "Hour", "Minute", "Dst", "EnvironmentPeriodIndex");
                DataTable dataTable_ReportData = Core.SQLite.Query.DataTable(sQLiteConnection, "ReportData", "ReportDataDictionaryIndex", "TimeIndex", "Value");

                if (dataTable_EnvironmentPeriods == null || dataTable_ReportDataDictionary == null || dataTable_Time == null || dataTable_ReportData == null)
                {
                    diagnostics.Add("The SQL output misses one or more required tables (EnvironmentPeriods, ReportDataDictionary, Time, ReportData)");
                    return new List<DesignDay>();
                }

                int index_EnvironmentType = dataTable_EnvironmentPeriods.Columns.IndexOf("EnvironmentType");

                result = new List<DesignDay>();
                foreach (DataRow dataRow in dataTable_EnvironmentPeriods.Rows)
                {
                    if (!Core.Query.TryConvert(dataRow["EnvironmentName"], out string environmentName))
                    {
                        continue;
                    }

                    if (!Core.Query.TryConvert(dataRow["EnvironmentPeriodIndex"], out int environmentPeriodIndex))
                    {
                        continue;
                    }

                    if (index_EnvironmentType != -1 && Core.Query.TryConvert(dataRow[index_EnvironmentType], out int environmentType) && environmentType == 3)
                    {
                        // A weather-run period is an annual environment, not a design day.
                        continue;
                    }

                    List<string> diagnostics_Timestamps = new List<string>();
                    SortedDictionary<int, DateTime> timeIndexDictionary = Core.OpenStudio.Query.TimeIndexDictionary(dataTable_Time, environmentPeriodIndex, 2017, diagnostics_Timestamps);
                    if (diagnostics_Timestamps.Count != 0)
                    {
                        diagnostics.Add(string.Format("{0}: {1} malformed Time row(s) skipped, first: {2}", environmentName, diagnostics_Timestamps.Count, diagnostics_Timestamps[0]));
                    }

                    List<Tuple<int, DateTime>> tuples = new List<Tuple<int, DateTime>>();
                    foreach (KeyValuePair<int, DateTime> keyValuePair in timeIndexDictionary)
                    {
                        tuples.Add(new Tuple<int, DateTime>(keyValuePair.Key, keyValuePair.Value));
                    }

                    if (tuples.Count == 0)
                    {
                        diagnostics.Add(string.Format("{0}: no usable Time rows", environmentName));
                        continue;
                    }

                    tuples.Sort((x, y) => x.Item2.CompareTo(y.Item2));

                    DateTime date = tuples[0].Item2.Date;
                    DesignDay designDay = new DesignDay(environmentName, (short)date.Year, (byte)date.Month, (byte)date.Day);

                    LoadType loadType = LoadType.Undefined;
                    if (environmentName.IndexOf("htg", StringComparison.OrdinalIgnoreCase) >= 0 || environmentName.IndexOf("heating", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        loadType = LoadType.Heating;
                    }
                    else if (environmentName.IndexOf("clg", StringComparison.OrdinalIgnoreCase) >= 0 || environmentName.IndexOf("cooling", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        loadType = LoadType.Cooling;
                    }

                    if (loadType != LoadType.Undefined)
                    {
                        designDay = new DesignDay(designDay, loadType);
                    }

                    // Hourly index of each interval within its day: end-of-interval timestamps
                    // map onto 0–23 by stepping one tick back into the interval (01:00 → 0,
                    // 24:00 → 23, sub-hourly rows onto their containing hour).
                    int reportDataDictionaryIndex = -1;

                    List<Tuple<Weather.WeatherDataType, string>> tuples_WeatherDataType = new List<Tuple<Weather.WeatherDataType, string>>();
                    tuples_WeatherDataType.Add(new Tuple<Weather.WeatherDataType, string>(Weather.WeatherDataType.CloudCover, "Site Total Sky Cover"));
                    tuples_WeatherDataType.Add(new Tuple<Weather.WeatherDataType, string>(Weather.WeatherDataType.DirectSolarRadiation, "Site Direct Solar Radiation Rate per Area"));
                    tuples_WeatherDataType.Add(new Tuple<Weather.WeatherDataType, string>(Weather.WeatherDataType.DiffuseSolarRadiation, "Site Diffuse Solar Radiation Rate per Area"));
                    tuples_WeatherDataType.Add(new Tuple<Weather.WeatherDataType, string>(Weather.WeatherDataType.WindDirection, "Site Wind Direction"));
                    tuples_WeatherDataType.Add(new Tuple<Weather.WeatherDataType, string>(Weather.WeatherDataType.WindSpeed, "Site Wind Speed"));
                    tuples_WeatherDataType.Add(new Tuple<Weather.WeatherDataType, string>(Weather.WeatherDataType.RelativeHumidity, "Site Outdoor Air Relative Humidity"));
                    tuples_WeatherDataType.Add(new Tuple<Weather.WeatherDataType, string>(Weather.WeatherDataType.DryBulbTemperature, "Site Outdoor Air Drybulb Temperature"));

                    foreach (Tuple<Weather.WeatherDataType, string> tuple in tuples_WeatherDataType)
                    {
                        double factor = 1;
                        if (tuple.Item1 == Weather.WeatherDataType.CloudCover)
                        {
                            factor = 10;
                        }

                        reportDataDictionaryIndex = Core.OpenStudio.Query.ReportDataDictionaryIndex(dataTable_ReportDataDictionary, tuple.Item2, "Environment");
                        if (reportDataDictionaryIndex == -1)
                        {
                            continue;
                        }

                        SortedDictionary<int, double> reportDataDictionary = Core.OpenStudio.Query.ReportDataDictionary(dataTable_ReportData, reportDataDictionaryIndex);
                        if (reportDataDictionary == null)
                        {
                            continue;
                        }

                        foreach (Tuple<int, DateTime> tuple_Time in tuples)
                        {
                            // Interval-start semantics: the 24:00 row ends the design day even
                            // though its interval-end timestamp rolls to the next calendar date.
                            if (tuple_Time.Item2.AddTicks(-1).Date != date)
                            {
                                continue;
                            }

                            int hourIndex = (int)tuple_Time.Item2.AddTicks(-1).TimeOfDay.TotalHours;
                            if (hourIndex < 0 || hourIndex > 23)
                            {
                                continue;
                            }

                            if (!reportDataDictionary.TryGetValue(tuple_Time.Item1, out double value))
                            {
                                continue;
                            }

                            designDay[tuple.Item1, hourIndex] = value / factor;
                        }
                    }

                    result.Add(designDay);
                }
            }

            if (result.Count == 0)
            {
                diagnostics.Add("No design-day environments found in the SQL output (annual weather-run environments are not design days)");
            }

            return result;
        }
    }
}
