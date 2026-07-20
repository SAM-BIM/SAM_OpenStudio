using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Create
    {
        public static List<SpaceSimulationResult> SpaceSimulationResults(this string path)
        {
            if(string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            {
                return null;
            }

            List<SpaceSimulationResult> result = null;
            using (SQLiteConnection sQLiteConnection = Core.SQLite.Create.SQLiteConnection(path))
            {
                result = SpaceSimulationResults(sQLiteConnection);
            }

            return result;
        }

        public static List<SpaceSimulationResult> SpaceSimulationResults(this SQLiteConnection sQLiteConnection)
        {
            if(sQLiteConnection == null)
            {
                return null;
            }

            DataTable dataTable = null;

            List<SpaceSimulationResult> result = null;

            dataTable = Core.SQLite.Query.DataTable(sQLiteConnection, "Zones", "ZoneIndex", "ZoneName", "FloorArea", "Volume");
            if (dataTable != null)
            {
                result = dataTable.ToSAM_SpaceSimulationResult();
            }

            DataTable dataTable_Surfaces = Core.SQLite.Query.DataTable(sQLiteConnection, "Surfaces", "SurfaceIndex", "SurfaceName", "ZoneIndex");

            if (result != null && result.Count != 0)
            {
                DataTable dataTable_EnvironmentPeriods = Core.SQLite.Query.DataTable(sQLiteConnection, "EnvironmentPeriods", "EnvironmentPeriodIndex", "EnvironmentName");

                dataTable = Core.SQLite.Query.DataTable(sQLiteConnection, "ZoneSizes", "ZoneName", "LoadType", "CalcDesLoad", "DesDayName", "PeakHrMin", "PeakTemp", "PeakHumRat");
                if (dataTable != null)
                {
                    int index_ZoneName = dataTable.Columns.IndexOf("ZoneName");
                    int index_LoadType = dataTable.Columns.IndexOf("LoadType");
                    if (index_ZoneName != -1 && index_LoadType != -1)
                    {
                        int index_CalcDesLoad = dataTable.Columns.IndexOf("CalcDesLoad");
                        int index_PeakHrMin = dataTable.Columns.IndexOf("PeakHrMin");
                        int index_DesDayName = dataTable.Columns.IndexOf("DesDayName");

                        List<SpaceSimulationResult> spaceSimulationResults = new List<SpaceSimulationResult>();
                        foreach (SpaceSimulationResult spaceSimulationResult in result)
                        {
                            List<int> indexes = Core.Query.FindIndexes(dataTable, "ZoneName", spaceSimulationResult.Name);
                            if (indexes != null)
                            {
                                foreach (int index in indexes)
                                {
                                    DataRow dataRow = dataTable.Rows[index];

                                    SpaceSimulationResult spaceSimulationResult_LoadType = new SpaceSimulationResult(Guid.NewGuid(), spaceSimulationResult);
                                    spaceSimulationResult_LoadType.SetValue(Analytical.SpaceSimulationResultParameter.LoadType, dataRow[index_LoadType]);

                                    if (index_CalcDesLoad != -1)
                                    {
                                        spaceSimulationResult_LoadType.SetValue(Analytical.SpaceSimulationResultParameter.DesignLoad, dataRow[index_CalcDesLoad]);
                                    }

                                    if (index_DesDayName != -1)
                                    {
                                        if (spaceSimulationResult_LoadType.SetValue(SpaceSimulationResultParameter.DesignDayName, dataRow[index_DesDayName]))
                                        {
                                            if (dataTable_EnvironmentPeriods != null)
                                            {
                                                int environmentPeriodIndex = Core.OpenStudio.Query.EnvironmentPeriodIndex(dataTable_EnvironmentPeriods, spaceSimulationResult_LoadType.GetValue<string>(SpaceSimulationResultParameter.DesignDayName));
                                                if (environmentPeriodIndex != -1)
                                                {
                                                    spaceSimulationResult_LoadType.SetValue(SpaceSimulationResultParameter.DesignDayIndex, environmentPeriodIndex);
                                                }
                                            }
                                        }

                                    }

                                    if (index_PeakHrMin != -1)
                                    {
                                        if (Core.Query.TryConvert(dataRow[index_PeakHrMin], out string peakDate))
                                        {
                                            Core.OpenStudio.ShortDateTime shortDateTime = Core.OpenStudio.Create.ShortDateTime(peakDate);
                                            if (shortDateTime != null)
                                            {
                                                spaceSimulationResult_LoadType.SetValue(SpaceSimulationResultParameter.PeakDate, shortDateTime);
                                            }
                                        }
                                    }

                                    spaceSimulationResults.Add(spaceSimulationResult_LoadType);
                                }
                            }
                        }
                        result = spaceSimulationResults;
                    }
                }

                dataTable = Core.SQLite.Query.DataTable(sQLiteConnection, "NominalLighting", "ZoneIndex", "DesignLevel");
                if (dataTable != null)
                {
                    int index_ObjectName = dataTable.Columns.IndexOf("ZoneIndex");
                    int index_DesignLevel = dataTable.Columns.IndexOf("DesignLevel");
                    if (index_ObjectName != -1 && index_DesignLevel != -1)
                    {
                        foreach (SpaceSimulationResult spaceSimulationResult in result)
                        {
                            //if(!spaceSimulationResult.TryGetValue(SpaceSimulationResultParameter.Area, out double area))
                            //{
                            //    continue;
                            //}

                            if (!int.TryParse(spaceSimulationResult.Reference, out int zoneIndex))
                            {
                                continue;
                            }

                            List<int> indexes = Core.Query.FindIndexes(dataTable, "ZoneIndex", zoneIndex);
                            if (indexes == null || indexes.Count == 0)
                            {
                                continue;
                            }

                            if (!Core.Query.TryConvert(dataTable.Rows[indexes[0]][index_DesignLevel], out double designLevel))
                            {
                                continue;
                            }

                            spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.LightingGain, designLevel);
                        }
                    }
                }

                dataTable = Core.SQLite.Query.DataTable(sQLiteConnection, "NominalInfiltration", "ZoneIndex", "DesignLevel");
                if (dataTable != null)
                {
                    int index_ObjectName = dataTable.Columns.IndexOf("ZoneIndex");
                    int index_DesignLevel = dataTable.Columns.IndexOf("DesignLevel");
                    if (index_ObjectName != -1 && index_DesignLevel != -1)
                    {
                        foreach (SpaceSimulationResult spaceSimulationResult in result)
                        {
                            //if(!spaceSimulationResult.TryGetValue(SpaceSimulationResultParameter.Area, out double area))
                            //{
                            //    continue;
                            //}

                            if (!int.TryParse(spaceSimulationResult.Reference, out int zoneIndex))
                            {
                                continue;
                            }

                            List<int> indexes = Core.Query.FindIndexes(dataTable, "ZoneIndex", zoneIndex);
                            if (indexes == null || indexes.Count == 0)
                            {
                                continue;
                            }

                            if (!Core.Query.TryConvert(dataTable.Rows[indexes[0]][index_DesignLevel], out double designLevel))
                            {
                                continue;
                            }

                            spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.InfiltrationGain, designLevel);
                        }
                    }
                }

                dataTable = Core.SQLite.Query.DataTable(sQLiteConnection, "NominalElectricEquipment", "ZoneIndex", "DesignLevel");
                if (dataTable != null)
                {
                    int index_ObjectName = dataTable.Columns.IndexOf("ZoneIndex");
                    int index_DesignLevel = dataTable.Columns.IndexOf("DesignLevel");
                    if (index_ObjectName != -1 && index_DesignLevel != -1)
                    {
                        foreach (SpaceSimulationResult spaceSimulationResult in result)
                        {
                            //if(!spaceSimulationResult.TryGetValue(SpaceSimulationResultParameter.Area, out double area))
                            //{
                            //    continue;
                            //}

                            if (!int.TryParse(spaceSimulationResult.Reference, out int zoneIndex))
                            {
                                continue;
                            }

                            List<int> indexes = Core.Query.FindIndexes(dataTable, "ZoneIndex", zoneIndex);
                            if (indexes == null || indexes.Count == 0)
                            {
                                continue;
                            }

                            if (!Core.Query.TryConvert(dataTable.Rows[indexes[0]][index_DesignLevel], out double designLevel))
                            {
                                continue;
                            }

                            spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.EquipmentSensibleGain, designLevel);
                        }
                    }
                }

                dataTable = Core.SQLite.Query.DataTable(sQLiteConnection, "ReportDataDictionary", "ReportDataDictionaryIndex", "KeyValue", "Name", "Units");
                if (dataTable != null)
                {
                    DataTable dataTable_Time = Core.SQLite.Query.DataTable(sQLiteConnection, "Time", "TimeIndex", "Year", "Month", "Day", "Hour", "Minute", "Dst", "EnvironmentPeriodIndex");
                    DataTable dataTable_ReportData = Core.SQLite.Query.DataTable(sQLiteConnection, "ReportData", "ReportDataDictionaryIndex", "TimeIndex", "Value");

                    foreach (SpaceSimulationResult spaceSimulationResult in result)
                    {
                        if (spaceSimulationResult == null)
                        {
                            continue;
                        }

                        if (!spaceSimulationResult.TryGetValue(SpaceSimulationResultParameter.PeakDate, out Core.OpenStudio.ShortDateTime peakDate))
                        {
                            continue;
                        }

                        if (!spaceSimulationResult.TryGetValue(SpaceSimulationResultParameter.DesignDayIndex, out int designDayIndex))
                        {
                            continue;
                        }

                        LoadType loadType = spaceSimulationResult.LoadType();
                        if (loadType == LoadType.Undefined)
                        {
                            continue;
                        }

                        int timeIndex = Core.OpenStudio.Query.TimeIndex(dataTable_Time, peakDate, designDayIndex);
                        if (timeIndex == -1)
                        {
                            continue;
                        }

                        spaceSimulationResult.SetValue(SpaceSimulationResultParameter.DesignLoadTimeIndex, timeIndex);

                        SortedDictionary<int, DateTime> sortedDictionary_TimeIndex = Core.OpenStudio.Query.TimeIndexDictionary(dataTable_Time, designDayIndex);

                        int minTimeIndex = -1;
                        int maxTimeIndex = -1;
                        if (sortedDictionary_TimeIndex != null)
                        {
                            minTimeIndex = sortedDictionary_TimeIndex.Keys.Min();
                            maxTimeIndex = sortedDictionary_TimeIndex.Keys.Max();
                        }

                        //Infiltration
                        string name_Infiltration = loadType == LoadType.Cooling ? "Zone Infiltration Sensible Heat Gain Energy" : "Zone Infiltration Sensible Heat Loss Energy";
                        int reportDataDictionaryIndex_Infiltration = Core.OpenStudio.Query.ReportDataDictionaryIndex(dataTable, name_Infiltration, spaceSimulationResult.Name);
                        if (reportDataDictionaryIndex_Infiltration != -1)
                        {
                            double value = Core.OpenStudio.Query.ReportData(dataTable_ReportData, reportDataDictionaryIndex_Infiltration, timeIndex);
                            if (double.IsNaN(value))
                            {
                                continue;
                            }

                            value = Core.OpenStudio.Query.ConvertUnit(dataTable, name_Infiltration, spaceSimulationResult.Name, value);

                            if (loadType == LoadType.Heating)
                            {
                                value = -value;
                            }

                            spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.InfiltrationGain, value);
                        }

                        //Equipment Sensible
                        string name_EquipmentRadiant = "Zone Electric Equipment Radiant Heating Rate";
                        string name_EquipmentConvective = "Zone Electric Equipment Convective Heating Rate";
                        int reportDataDictionaryIndex_EquipmentRadiant = Core.OpenStudio.Query.ReportDataDictionaryIndex(dataTable, name_EquipmentRadiant, spaceSimulationResult.Name);
                        int reportDataDictionaryIndex_EquipmentConvective = Core.OpenStudio.Query.ReportDataDictionaryIndex(dataTable, name_EquipmentConvective, spaceSimulationResult.Name);
                        if (reportDataDictionaryIndex_EquipmentConvective != -1 || reportDataDictionaryIndex_EquipmentRadiant != -1)
                        {
                            double value_EquipmentRadiant = Core.OpenStudio.Query.ReportData(dataTable_ReportData, reportDataDictionaryIndex_EquipmentRadiant, timeIndex);
                            double value_EquipmentConvective = Core.OpenStudio.Query.ReportData(dataTable_ReportData, reportDataDictionaryIndex_EquipmentConvective, timeIndex);
                            if (!double.IsNaN(value_EquipmentRadiant) || !double.IsNaN(value_EquipmentConvective))
                            {
                                double value = 0;
                                if (!double.IsNaN(value_EquipmentRadiant))
                                {
                                    value_EquipmentRadiant = Core.OpenStudio.Query.ConvertUnit(dataTable, name_EquipmentRadiant, spaceSimulationResult.Name, value_EquipmentRadiant);
                                    value += value_EquipmentRadiant;
                                }

                                if (!double.IsNaN(value_EquipmentConvective))
                                {
                                    value_EquipmentConvective = Core.OpenStudio.Query.ConvertUnit(dataTable, name_EquipmentRadiant, spaceSimulationResult.Name, value_EquipmentConvective);
                                    value += value_EquipmentConvective;
                                }

                                spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.EquipmentSensibleGain, value);
                            }

                        }

                        //Equipment Latent
                        string name_EquipmentLatent = "Zone Electric Equipment Latent Gain Rate";
                        int reportDataDictionaryIndex_EquipmentLatent = Core.OpenStudio.Query.ReportDataDictionaryIndex(dataTable, name_EquipmentLatent, spaceSimulationResult.Name);
                        if (reportDataDictionaryIndex_EquipmentLatent != -1)
                        {
                            double value = Core.OpenStudio.Query.ReportData(dataTable_ReportData, reportDataDictionaryIndex_EquipmentLatent, timeIndex);
                            if (double.IsNaN(value))
                            {
                                continue;
                            }

                            value = Core.OpenStudio.Query.ConvertUnit(dataTable, name_EquipmentLatent, spaceSimulationResult.Name, value);
                            spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.EquipmentLatentGain, value);
                        }

                        //Lighting
                        string name_Lighting = "Zone Lights Total Heating Rate";
                        int reportDataDictionaryIndex_Lighting = Core.OpenStudio.Query.ReportDataDictionaryIndex(dataTable, name_Lighting, spaceSimulationResult.Name);
                        if (reportDataDictionaryIndex_Lighting != -1)
                        {
                            double value = Core.OpenStudio.Query.ReportData(dataTable_ReportData, reportDataDictionaryIndex_Lighting, timeIndex);
                            if (double.IsNaN(value))
                            {
                                continue;
                            }

                            value = Core.OpenStudio.Query.ConvertUnit(dataTable, name_Lighting, spaceSimulationResult.Name, value);
                            spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.LightingGain, value);
                        }

                        //Relative Humidity
                        string name_RelativeHumidity = "Zone Air Relative Humidity";
                        int reportDataDictionaryIndex_RelativeHumidity = Core.OpenStudio.Query.ReportDataDictionaryIndex(dataTable, name_RelativeHumidity, spaceSimulationResult.Name);
                        if (reportDataDictionaryIndex_RelativeHumidity != -1)
                        {
                            double value = Core.OpenStudio.Query.ReportData(dataTable_ReportData, reportDataDictionaryIndex_RelativeHumidity, timeIndex);
                            if (double.IsNaN(value))
                            {
                                continue;
                            }

                            value = Core.OpenStudio.Query.ConvertUnit(dataTable, name_RelativeHumidity, spaceSimulationResult.Name, value);
                            spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.RelativeHumidity, value);
                        }

                        //Humidity Ratio
                        string name_HumidityRatio = "Zone Mean Air Humidity Ratio";
                        int reportDataDictionaryIndex_HumidityRatio = Core.OpenStudio.Query.ReportDataDictionaryIndex(dataTable, name_HumidityRatio, spaceSimulationResult.Name);
                        if (reportDataDictionaryIndex_HumidityRatio == -1)
                        {
                            name_HumidityRatio = "Zone Air Humidity Ratio";
                            reportDataDictionaryIndex_HumidityRatio = Core.OpenStudio.Query.ReportDataDictionaryIndex(dataTable, name_HumidityRatio, spaceSimulationResult.Name);
                        }
                        if (reportDataDictionaryIndex_HumidityRatio != -1)
                        {
                            double value = Core.OpenStudio.Query.ReportData(dataTable_ReportData, reportDataDictionaryIndex_HumidityRatio, timeIndex);
                            if (double.IsNaN(value))
                            {
                                continue;
                            }

                            value = Core.OpenStudio.Query.ConvertUnit(dataTable, name_HumidityRatio, spaceSimulationResult.Name, value);
                            spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.HumidityRatio, value);
                        }

                        //Resultant Temperature
                        string name_ResultantTemperature = "Zone Operative Temperature";
                        int reportDataDictionaryIndex_ResultantTemperature = Core.OpenStudio.Query.ReportDataDictionaryIndex(dataTable, name_ResultantTemperature, spaceSimulationResult.Name);
                        if (reportDataDictionaryIndex_EquipmentLatent != -1)
                        {
                            double value = Core.OpenStudio.Query.ReportData(dataTable_ReportData, reportDataDictionaryIndex_ResultantTemperature, timeIndex);
                            if (double.IsNaN(value))
                            {
                                continue;
                            }

                            value = Core.OpenStudio.Query.ConvertUnit(dataTable, name_EquipmentLatent, spaceSimulationResult.Name, value);
                            spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.ResultantTemperature, value);
                        }

                        SortedDictionary<int, double> sortedDictionary_DryBulbTemperture = null;

                        //Dry Bulb Temperture
                        string name_DryBulbTemperture = "Zone Mean Air Temperature";
                        int reportDataDictionaryIndex_DryBulbTemperture = Core.OpenStudio.Query.ReportDataDictionaryIndex(dataTable, name_DryBulbTemperture, spaceSimulationResult.Name);
                        if (reportDataDictionaryIndex_DryBulbTemperture == -1)
                        {
                            name_DryBulbTemperture = "Zone Air Temperature";
                            reportDataDictionaryIndex_DryBulbTemperture = Core.OpenStudio.Query.ReportDataDictionaryIndex(dataTable, name_DryBulbTemperture, spaceSimulationResult.Name);
                        }
                        if (reportDataDictionaryIndex_DryBulbTemperture != -1)
                        {
                            sortedDictionary_DryBulbTemperture = Core.OpenStudio.Query.ReportDataDictionary(dataTable_ReportData, reportDataDictionaryIndex_DryBulbTemperture, minTimeIndex, maxTimeIndex);
                            if (sortedDictionary_DryBulbTemperture != null)
                            {
                                if (sortedDictionary_DryBulbTemperture.ContainsKey(timeIndex))
                                {
                                    double value = sortedDictionary_DryBulbTemperture[timeIndex];
                                    if (!double.IsNaN(value))
                                    {
                                        value = Core.OpenStudio.Query.ConvertUnit(dataTable, name_DryBulbTemperture, spaceSimulationResult.Name, value);
                                        spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.DryBulbTempearture, value);
                                    }
                                }

                                // Max Dry Bulb Temperture
                                Core.OpenStudio.Query.Max(sortedDictionary_DryBulbTemperture, out int timeIndex_Max, out double value_Max);
                                spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.MaxDryBulbTemperature, value_Max);
                                spaceSimulationResult.SetValue(SpaceSimulationResultParameter.MaxDryBulbTemperatureTimeIndex, timeIndex_Max);

                                DateTime? dateTime_Max = Core.OpenStudio.Query.DateTime(dataTable_Time, timeIndex_Max);
                                if (dateTime_Max != null && dateTime_Max.HasValue)
                                {
                                    spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.MaxDryBulbTemperatureIndex, Core.OpenStudio.Query.IntervalHourOfYear(dateTime_Max.Value));
                                }

                                // Min Dry Bulb Temperture
                                Core.OpenStudio.Query.Min(sortedDictionary_DryBulbTemperture, out int timeIndex_Min, out double value_Min);
                                spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.MinDryBulbTemperature, value_Min);
                                spaceSimulationResult.SetValue(SpaceSimulationResultParameter.MinDryBulbTemperatureTimeIndex, timeIndex_Min);

                                DateTime? dateTime_Min = Core.OpenStudio.Query.DateTime(dataTable_Time, timeIndex_Min);
                                if (dateTime_Min != null && dateTime_Min.HasValue)
                                {
                                    spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.MinDryBulbTemperatureIndex, Core.OpenStudio.Query.IntervalHourOfYear(dateTime_Min.Value));
                                }
                            }
                        }

                        //Load
                        string name_Load = loadType == LoadType.Cooling ? "Zone Ideal Loads Supply Air Sensible Cooling Rate" : "Zone Ideal Loads Supply Air Sensible Heating Rate";
                        int reportDataDictionaryIndex_Load = Core.OpenStudio.Query.ReportDataDictionaryIndex(dataTable, name_Load, spaceSimulationResult.Name, Core.TextComparisonType.StartsWith);
                        if (reportDataDictionaryIndex_Load != -1)
                        {
                            SortedDictionary<int, double> sortedDictionary = Core.OpenStudio.Query.ReportDataDictionary(dataTable_ReportData, reportDataDictionaryIndex_Load, minTimeIndex, maxTimeIndex);
                            Core.OpenStudio.Query.Max(sortedDictionary, out int timeIndex_Load, out double value);
                            if (double.IsNaN(value))
                            {
                                continue;
                            }

                            spaceSimulationResult.SetValue(SpaceSimulationResultParameter.LoadTimeIndex, timeIndex_Load);

                            value = Core.OpenStudio.Query.ConvertUnit(dataTable, name_Load, spaceSimulationResult.Name, value);
                            spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.Load, value);

                            DateTime? dateTime = Core.OpenStudio.Query.DateTime(dataTable_Time, timeIndex_Load);
                            if (dateTime != null && dateTime.HasValue)
                            {
                                spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.LoadIndex, Core.OpenStudio.Query.IntervalHourOfYear(dateTime.Value));
                            }

                            //if (sortedDictionary_TimeIndex != null && sortedDictionary_TimeIndex.ContainsKey(timeIndex_Temp))
                            //{
                            //    DateTime dateTime = sortedDictionary_TimeIndex[timeIndex_Temp];
                            //    spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.LoadIndex, Core.Query.HourOfYear(dateTime));
                            //}
                        }

                        //People Latent
                        string name_PeopleLatent = "Zone People Latent Gain Rate";
                        int reportDataDictionaryIndex_PeopleLatent = Core.OpenStudio.Query.ReportDataDictionaryIndex(dataTable, name_PeopleLatent, spaceSimulationResult.Name);
                        if (reportDataDictionaryIndex_PeopleLatent != -1)
                        {
                            double value = Core.OpenStudio.Query.ReportData(dataTable_ReportData, reportDataDictionaryIndex_PeopleLatent, timeIndex);
                            if (double.IsNaN(value))
                            {
                                continue;
                            }

                            value = Core.OpenStudio.Query.ConvertUnit(dataTable, name_PeopleLatent, spaceSimulationResult.Name, value);
                            spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.OccupancyLatentGain, value);
                        }

                        SortedDictionary<int, double> sortedDictionary_PeopleSensible = null;

                        //People Sensible
                        string name_PeopleSensible = "Zone People Sensible Heating Rate";
                        int reportDataDictionaryIndex_PeopleSensible = Core.OpenStudio.Query.ReportDataDictionaryIndex(dataTable, name_PeopleSensible, spaceSimulationResult.Name);
                        if (reportDataDictionaryIndex_PeopleSensible != -1)
                        {
                            sortedDictionary_PeopleSensible = Core.OpenStudio.Query.ReportDataDictionary(dataTable_ReportData, reportDataDictionaryIndex_PeopleSensible, minTimeIndex, maxTimeIndex);
                            if (sortedDictionary_PeopleSensible != null && sortedDictionary_PeopleSensible.ContainsKey(timeIndex))
                            {
                                double value = sortedDictionary_PeopleSensible[timeIndex];
                                if (double.IsNaN(value))
                                {
                                    continue;
                                }

                                value = Core.OpenStudio.Query.ConvertUnit(dataTable, name_PeopleSensible, spaceSimulationResult.Name, value);
                                spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.OccupancySensibleGain, value);
                            }
                        }

                        //Occupied Hours
                        if (sortedDictionary_PeopleSensible != null && sortedDictionary_TimeIndex != null)
                        {
                            List<Tuple<DateTime, int>> tuples = new List<Tuple<DateTime, int>>();
                            foreach (KeyValuePair<int, double> keyValuePair in sortedDictionary_PeopleSensible)
                            {
                                if (keyValuePair.Value <= 0)
                                {
                                    continue;
                                }

                                if (!sortedDictionary_TimeIndex.ContainsKey(keyValuePair.Key))
                                {
                                    continue;
                                }

                                DateTime dateTime = sortedDictionary_TimeIndex[keyValuePair.Key];
                                dateTime = new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, 0, 0);
                                if (tuples.Find(x => x.Item1.Year == dateTime.Year && x.Item1.Month == dateTime.Month && x.Item1.Day == dateTime.Day && x.Item1.Hour == dateTime.Hour) == null)
                                {
                                    tuples.Add(new Tuple<DateTime, int>(dateTime, keyValuePair.Key));
                                }
                            }

                            spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.OccupiedHours, tuples.Count);

                            if (sortedDictionary_DryBulbTemperture != null)
                            {
                                List<int> counts = new List<int>() { 0, 0 }; //counts[0] <- 25 //counts[1] <- 28
                                foreach (Tuple<DateTime, int> tuple in tuples)
                                {
                                    if (!sortedDictionary_DryBulbTemperture.TryGetValue(tuple.Item2, out double dryBulbTemperature))
                                    {
                                        continue;
                                    }

                                    if (dryBulbTemperature > 25)
                                    {
                                        counts[0]++;
                                    }

                                    if (dryBulbTemperature > 28)
                                    {
                                        counts[1]++;
                                    }
                                }
                                spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.OccupiedHours25, counts[0]);
                                spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.OccupiedHours28, counts[1]);
                            }
                        }

                        //Solar Gain
                        string name_SolarGain = "Zone Windows Total Transmitted Solar Radiation Rate";
                        int reportDataDictionaryIndex_SolarGain = Core.OpenStudio.Query.ReportDataDictionaryIndex(dataTable, name_SolarGain, spaceSimulationResult.Name);
                        if (reportDataDictionaryIndex_SolarGain != -1)
                        {
                            double value = Core.OpenStudio.Query.ReportData(dataTable_ReportData, reportDataDictionaryIndex_SolarGain, timeIndex);
                            if (double.IsNaN(value))
                            {
                                continue;
                            }

                            value = Core.OpenStudio.Query.ConvertUnit(dataTable, name_SolarGain, spaceSimulationResult.Name, value);
                            spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.SolarGain, value);
                        }

                        //Opaque External Conduction
                        if (dataTable_Surfaces != null && Core.Query.TryConvert(spaceSimulationResult.Reference, out int zoneIndex))
                        {
                            List<string> surfaceNames = Core.OpenStudio.Query.SurfaceNames(dataTable_Surfaces, zoneIndex);
                            if (surfaceNames != null && surfaceNames.Count != 0)
                            {
                                //string name_OpaqueExternalConduction = "Surface Average Face Conduction Heat Transfer Rate";
                                string name_OpaqueExternalConduction = "Surface Inside Face Conduction Heat Transfer Rate";

                                double value = 0;
                                foreach (string surfaceName in surfaceNames)
                                {
                                    int reportDataDictionaryIndex_OpaqueExternalConduction = Core.OpenStudio.Query.ReportDataDictionaryIndex(dataTable, name_OpaqueExternalConduction, surfaceName);
                                    if (reportDataDictionaryIndex_OpaqueExternalConduction == -1)
                                    {
                                        continue;
                                    }

                                    double value_Surface = Core.OpenStudio.Query.ReportData(dataTable_ReportData, reportDataDictionaryIndex_OpaqueExternalConduction, timeIndex);
                                    if (double.IsNaN(value_Surface))
                                    {
                                        continue;
                                    }

                                    value += value_Surface;
                                }

                                spaceSimulationResult.SetValue(Analytical.SpaceSimulationResultParameter.OpaqueExternalConduction, value);
                            }
                        }
                    }
                }

            }

            return result;
        }
    }
}