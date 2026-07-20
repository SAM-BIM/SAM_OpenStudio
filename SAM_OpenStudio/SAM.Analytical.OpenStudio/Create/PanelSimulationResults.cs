using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Create
    {
        public static List<SurfaceSimulationResult> SurfaceSimulationResults(this string path, IEnumerable<SpaceSimulationResult> spaceSimulationResults = null)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            {
                return null;
            }

            List<SurfaceSimulationResult> result = null;
            using (SQLiteConnection sQLiteConnection = Core.SQLite.Create.SQLiteConnection(path))
            {
                result = SurfaceSimulationResults(sQLiteConnection, spaceSimulationResults);
            }

            return result;
        }

        public static List<SurfaceSimulationResult> SurfaceSimulationResults(this SQLiteConnection sQLiteConnection, IEnumerable<SpaceSimulationResult> spaceSimulationResults = null)
        {
            if(sQLiteConnection == null)
            {
                return null;
            }

            DataTable dataTable = null;

            List<SurfaceSimulationResult> result = null;

            dataTable = Core.SQLite.Query.DataTable(sQLiteConnection, "Surfaces", "SurfaceIndex", "SurfaceName", "Area", "ZoneIndex");
            if (dataTable != null)
            {
                result = dataTable.ToSAM_SurfaceSimulationResult();
            }

            dataTable = Core.SQLite.Query.DataTable(sQLiteConnection, "Zones", "ZoneIndex", "ZoneName");
            if (dataTable != null)
            {
                int index_ZoneIndex = dataTable.Columns.IndexOf("ZoneIndex");
                int index_ZoneName = dataTable.Columns.IndexOf("ZoneName");
                if (index_ZoneName != -1 && index_ZoneIndex != -1)
                {
                    DataRowCollection dataRowCollection = dataTable.Rows;
                    if(dataRowCollection != null)
                    {
                        Dictionary<int, List<SurfaceSimulationResult>> dictionary = new Dictionary<int, List<SurfaceSimulationResult>>();
                        foreach(SurfaceSimulationResult surfaceSimulationResult in result)
                        {
                            if(!surfaceSimulationResult.TryGetValue(SurfaceSimulationResultParameter.ZoneIndex, out int zoneIndex))
                            {
                                continue;
                            }

                            if(!dictionary.TryGetValue(zoneIndex, out List<SurfaceSimulationResult> surfaceSimulationResults))
                            {
                                surfaceSimulationResults = new List<SurfaceSimulationResult>();
                                dictionary[zoneIndex] = surfaceSimulationResults;
                            }

                            surfaceSimulationResults.Add(surfaceSimulationResult);
                        }


                        foreach(DataRow dataRow in dataRowCollection)
                        {
                            if(!Core.Query.TryConvert(dataRow[index_ZoneIndex], out int zoneIndex))
                            {
                                continue;
                            }

                            if(!dictionary.TryGetValue(zoneIndex, out List<SurfaceSimulationResult> surfaceSimulationResults))
                            {
                                continue;
                            }

                            if (!Core.Query.TryConvert(dataRow[index_ZoneName], out string zoneName))
                            {
                                continue;
                            }

                            foreach (SurfaceSimulationResult panelSimulationResult in surfaceSimulationResults)
                            {
                                panelSimulationResult.SetValue(SurfaceSimulationResultParameter.ZoneName, zoneName);
                            }
                        }
                    }
                }
            }

            if (spaceSimulationResults != null && result != null)
            {
                DataTable dataTable_ReportDataDictionary = Core.SQLite.Query.DataTable(sQLiteConnection, "ReportDataDictionary", "ReportDataDictionaryIndex", "KeyValue", "Name", "Units");
                DataTable dataTable_ReportData = Core.SQLite.Query.DataTable(sQLiteConnection, "ReportData", "ReportDataDictionaryIndex", "TimeIndex", "Value");

                string name_InsideConductionHeatTransfer = "Surface Inside Face Conduction Heat Transfer Rate";
                string name_OutsideConductionHeatTransfer = "Surface Outside Face Conduction Heat Transfer Rate";

                List<SurfaceSimulationResult> result_Temp = new List<SurfaceSimulationResult>();
                foreach (SpaceSimulationResult spaceSimulationResult in spaceSimulationResults)
                {
                    if(!spaceSimulationResult.TryGetValue(SpaceSimulationResultParameter.LoadTimeIndex, out int loadTimeIndex))
                    {
                        continue;
                    }

                    List<SurfaceSimulationResult> surfaceSimulationResults_Space = result.SurfaceSimulationResults(spaceSimulationResult);
                    if(surfaceSimulationResults_Space == null || surfaceSimulationResults_Space.Count == 0)
                    {
                        continue;
                    }

                    LoadType loadType = spaceSimulationResult.LoadType();

                    for (int i =0; i < surfaceSimulationResults_Space.Count; i++)
                    {
                        SurfaceSimulationResult surfaceSimulationResult = surfaceSimulationResults_Space[i];
                        if(surfaceSimulationResult == null)
                        {
                            continue;
                        }

                        surfaceSimulationResult = new SurfaceSimulationResult(System.Guid.NewGuid(), surfaceSimulationResult);
                        
                        if(loadType != LoadType.Undefined)
                        {
                            surfaceSimulationResult.SetValue(Analytical.SurfaceSimulationResultParameter.LoadType, spaceSimulationResult.LoadType());
                        }

                        int reportDataDictionaryIndex_InsideConductionHeatTransfer = Core.OpenStudio.Query.ReportDataDictionaryIndex(dataTable_ReportDataDictionary, name_InsideConductionHeatTransfer, surfaceSimulationResult.Name);
                        if (reportDataDictionaryIndex_InsideConductionHeatTransfer != -1)
                        {
                            double value = Core.OpenStudio.Query.ReportData(dataTable_ReportData, reportDataDictionaryIndex_InsideConductionHeatTransfer, loadTimeIndex);
                            if (double.IsNaN(value))
                            {
                                continue;
                            }

                            value = Core.OpenStudio.Query.ConvertUnit(dataTable, name_InsideConductionHeatTransfer, spaceSimulationResult.Name, value);

                            surfaceSimulationResult.SetValue(Analytical.SurfaceSimulationResultParameter.InternalConduction, value);
                        }

                        int reportDataDictionaryIndex_OutsideConductionHeatTransfer = Core.OpenStudio.Query.ReportDataDictionaryIndex(dataTable_ReportDataDictionary, name_OutsideConductionHeatTransfer, surfaceSimulationResult.Name);
                        if (reportDataDictionaryIndex_OutsideConductionHeatTransfer != -1)
                        {
                            double value = Core.OpenStudio.Query.ReportData(dataTable_ReportData, reportDataDictionaryIndex_OutsideConductionHeatTransfer, loadTimeIndex);
                            if (double.IsNaN(value))
                            {
                                continue;
                            }

                            value = Core.OpenStudio.Query.ConvertUnit(dataTable, name_OutsideConductionHeatTransfer, spaceSimulationResult.Name, value);

                            surfaceSimulationResult.SetValue(Analytical.SurfaceSimulationResultParameter.ExternalConduction, value);
                        }

                        result_Temp.Add(surfaceSimulationResult);
                    }
                }

                // Only the peak-enriched copies replace the base list — when no space result
                // carries a load time index (an annual-only SQL, no sizing periods) the
                // per-surface base results must survive: area, zone identity, panel linkage.
                if (result_Temp.Count != 0)
                {
                    result = result_Temp;
                }
            }

            return result;
        }
    }

}
