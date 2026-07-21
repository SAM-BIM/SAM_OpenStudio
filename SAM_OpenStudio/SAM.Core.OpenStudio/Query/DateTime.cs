// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Data;

namespace SAM.Core.OpenStudio
{
    public static partial class Query
    {
        public static DateTime? DateTime(this DataTable dataTable, int timeIndex, int deafultYear = 2017)
        {
            if (dataTable == null || timeIndex == -1)
            {
                return null;
            }

            DataColumnCollection dataColumnCollection = dataTable.Columns;
            if (dataColumnCollection == null)
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

            List<Tuple<int, int>> tuples_Minute = new List<Tuple<int, int>>();
            List<Tuple<int, int>> tuples_Second = new List<Tuple<int, int>>();
            foreach (DataRow dataRow in dataRowCollection)
            {
                if (!Core.Query.TryConvert(dataRow[index_TimeIndex], out int timeIndex_Temp))
                {
                    continue;
                }

                if (!timeIndex.Equals(timeIndex_Temp))
                {
                    continue;
                }

                if (!Core.Query.TryConvert(dataRow[index_Year], out int year_Temp))
                {
                    continue;
                }

                if (year_Temp == 0)
                {
                    year_Temp = deafultYear;
                }

                if (!Core.Query.TryConvert(dataRow[index_Month], out int month_Temp))
                {
                    continue;
                }

                if (!Core.Query.TryConvert(dataRow[index_Day], out int day_Temp))
                {
                    continue;
                }

                if (!Core.Query.TryConvert(dataRow[index_Hour], out int hour_Temp))
                {
                    continue;
                }

                int minute_Temp = 0;
                if (index_Minute != -1)
                {
                    if(!Core.Query.TryConvert(dataRow[index_Minute], out minute_Temp))
                    {
                        continue;
                    }
                }

                int second_Temp = 0;
                if (index_Second != -1)
                {
                    if(!Core.Query.TryConvert(dataRow[index_Second], out second_Temp))
                    {
                        continue;
                    }

                }

                // Raw EnergyPlus fields (hour 0–24, minute 0–60, end-of-interval) are
                // normalised centrally; unrepresentable rows return null, never throw.
                if (TryGetDateTime(year_Temp, month_Temp, day_Temp, hour_Temp, minute_Temp, second_Temp, deafultYear, out DateTime dateTime, out string _))
                {
                    return dateTime;
                }
            }

            return null;
        }
    }
}