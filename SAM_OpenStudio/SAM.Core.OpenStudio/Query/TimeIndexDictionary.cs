using System;
using System.Collections.Generic;
using System.Data;

namespace SAM.Core.OpenStudio
{
    public static partial class Query
    {
        public static SortedDictionary<int, DateTime> TimeIndexDictionary(this DataTable dataTable, int environemntPeriodIndex, short year = 2017)
        {
            return TimeIndexDictionary(dataTable, environemntPeriodIndex, year, null);
        }

        /// <summary>
        /// Maps TimeIndex values to normalised interval-end timestamps for one environment.
        /// Raw EnergyPlus rows (hour 0–24, minute 0–60, year 0 on sizing environments) are
        /// normalised through <see cref="TryGetDateTime"/>; rows that cannot form a valid
        /// calendar date are skipped and reported through <paramref name="diagnostics"/>
        /// instead of throwing.
        /// </summary>
        public static SortedDictionary<int, DateTime> TimeIndexDictionary(this DataTable dataTable, int environemntPeriodIndex, short year, IList<string> diagnostics)
        {
            if (dataTable == null || environemntPeriodIndex == -1)
            {
                return null;
            }

            DataColumnCollection dataColumnCollection = dataTable.Columns;
            if (dataColumnCollection == null)
            {
                return null;
            }

            int index_EnvironmentPeriodIndex = dataColumnCollection.IndexOf("EnvironmentPeriodIndex");
            if (index_EnvironmentPeriodIndex == -1)
            {
                return null;
            }

            int index_TimeIndex = dataColumnCollection.IndexOf("TimeIndex");
            if (index_TimeIndex == -1)
            {
                return null;
            }

            int index_Year = dataColumnCollection.IndexOf("Year");
            if (index_Year == -1)
            {
                return null;
            }

            int index_Month = dataColumnCollection.IndexOf("Month");
            if (index_Month == -1)
            {
                return null;
            }

            int index_Day = dataColumnCollection.IndexOf("Day");
            if (index_Day == -1)
            {
                return null;
            }

            int index_Hour = dataColumnCollection.IndexOf("Hour");
            if (index_Hour == -1)
            {
                return null;
            }

            int index_Minute = dataColumnCollection.IndexOf("Minute");
            int index_Second = dataColumnCollection.IndexOf("Dst");

            DataRowCollection dataRowCollection = dataTable.Rows;
            if (dataRowCollection == null)
            {
                return null;
            }

            SortedDictionary<int, DateTime> result = new SortedDictionary<int, DateTime>();
            foreach (DataRow dataRow in dataRowCollection)
            {
                if (!Core.Query.TryConvert(dataRow[index_EnvironmentPeriodIndex], out int environemntPeriodIndex_Temp))
                {
                    continue;
                }

                if (!environemntPeriodIndex.Equals(environemntPeriodIndex_Temp))
                {
                    continue;
                }

                if (!Core.Query.TryConvert(dataRow[index_Year], out int year_Temp))
                {
                    continue;
                }

                if (!Core.Query.TryConvert(dataRow[index_Month], out int month))
                {
                    continue;
                }

                if (!Core.Query.TryConvert(dataRow[index_Day], out int day))
                {
                    continue;
                }

                if (!Core.Query.TryConvert(dataRow[index_Hour], out int hour))
                {
                    continue;
                }

                if (!Core.Query.TryConvert(dataRow[index_TimeIndex], out int timeIndex))
                {
                    continue;
                }

                int minute = 0;
                if (index_Minute != -1)
                {
                    if (!Core.Query.TryConvert(dataRow[index_Minute], out minute))
                    {
                        continue;
                    }
                }

                int second = 0;
                if (index_Second != -1)
                {
                    if (!Core.Query.TryConvert(dataRow[index_Second], out second))
                    {
                        continue;
                    }
                }

                if (!TryGetDateTime(year_Temp, month, day, hour, minute, second, year, out DateTime dateTime, out string diagnostic))
                {
                    diagnostics?.Add(string.Format("TimeIndex {0} (environment {1}): {2}", timeIndex, environemntPeriodIndex, diagnostic));
                    continue;
                }

                result[timeIndex] = dateTime;
            }

            return result;
        }
    }
}