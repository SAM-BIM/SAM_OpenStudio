// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SAM.Analytical.OpenStudio.InputReadBack
{
    /// <summary>
    /// What the fresh-process reader found in the saved OSM and the translated IDF (native.json). Objects are kept as raw
    /// field text with the file's own labels; schedules are expanded to hourly values. No SAM object, writer cache or
    /// OpenStudio SDK call is involved.
    /// </summary>
    public sealed class NativeSnapshot
    {
        public string Schema { get; set; } = "s09-native/1";
        public int ProcessId { get; set; }
        public DateTime StartedUtc { get; set; }
        public DateTime FinishedUtc { get; set; }
        public string ReaderAssemblySha256 { get; set; }
        public string OsmSha256Before { get; set; }
        public string OsmSha256After { get; set; }
        public string IdfSha256Before { get; set; }
        public string IdfSha256After { get; set; }

        /// <summary>The run year length from the IDF RunPeriod (365 unless its begin year is a leap year).</summary>
        public int DayCount { get; set; }
        public string DayCountEvidence { get; set; }

        /// <summary>The EPW the OSM references (OS:WeatherFile Url), hashed by this reader; null when absent or missing.</summary>
        public string WeatherFileSha256 { get; set; }
        public string WeatherFileProblem { get; set; }

        public List<IdfObject> Idf { get; set; } = new List<IdfObject>();
        public List<IdfObject> Osm { get; set; } = new List<IdfObject>();
        public List<NativeSchedule> IdfSchedules { get; set; } = new List<NativeSchedule>();
        public List<NativeSchedule> OsmSchedules { get; set; } = new List<NativeSchedule>();

        public void Save(string path)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, Json.Options));
        }

        public static NativeSnapshot Load(string path)
        {
            return JsonSerializer.Deserialize<NativeSnapshot>(File.ReadAllText(path), Json.Options);
        }

        public IEnumerable<IdfObject> IdfOf(string type)
        {
            return Idf.Where(x => string.Equals(x.Type, type, StringComparison.OrdinalIgnoreCase));
        }

        public IEnumerable<IdfObject> OsmOf(string type)
        {
            return Osm.Where(x => string.Equals(x.Type, type, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static class NativeReader
    {
        /// <summary>
        /// IDF classes the comparison reads, plus every class that could add air, heat or geometry SAM never stated (kept so
        /// the comparison can refuse them as native extras).
        /// </summary>
        public static readonly string[] IdfTypes =
        {
            "Building", "GlobalGeometryRules", "Zone", "Space", "SpaceList", "ZoneList",
            "BuildingSurface:Detailed", "FenestrationSurface:Detailed", "Wall:Detailed", "RoofCeiling:Detailed", "Floor:Detailed",
            "Shading:Building:Detailed", "Shading:Site:Detailed", "Shading:Zone:Detailed", "InternalMass",
            "Construction", "Construction:AirBoundary", "Material", "Material:NoMass", "Material:AirGap",
            "WindowMaterial:Glazing", "WindowMaterial:Gas", "WindowMaterial:SimpleGlazingSystem", "WindowProperty:FrameAndDivider",
            "People", "Lights", "ElectricEquipment", "GasEquipment", "HotWaterEquipment", "SteamEquipment", "OtherEquipment",
            "ZoneInfiltration:DesignFlowRate", "ZoneInfiltration:EffectiveLeakageArea", "ZoneInfiltration:FlowCoefficient",
            "ZoneVentilation:DesignFlowRate", "ZoneVentilation:WindandStackOpenArea", "ZoneMixing", "ZoneCrossMixing",
            "AirflowNetwork:SimulationControl", "AirLoopHVAC", "ZoneHVAC:EquipmentConnections", "ZoneHVAC:EquipmentList",
            "DesignSpecification:OutdoorAir", "DesignSpecification:OutdoorAir:SpaceList", "ZoneHVAC:IdealLoadsAirSystem",
            "ZoneControl:Thermostat", "ThermostatSetpoint:DualSetpoint", "ThermostatSetpoint:SingleHeating",
            "ThermostatSetpoint:SingleCooling", "ThermostatSetpoint:SingleHeatingOrCooling", "ZoneControl:Humidistat",
            "Sizing:Zone", "RunPeriod", "Site:Location", "SizingPeriod:DesignDay", "SizingPeriod:WeatherFileDays",
            "SizingPeriod:WeatherFileConditionType", "Site:GroundTemperature:BuildingSurface", "Timestep"
        };

        public static readonly string[] OsmTypes =
        {
            "OS:Space", "OS:SpaceType", "OS:ThermalZone", "OS:DesignSpecification:OutdoorAir", "OS:ZoneHVAC:IdealLoadsAirSystem",
            "OS:WeatherFile", "OS:Site", "OS:RunPeriod", "OS:YearDescription", "OS:Building"
        };

        private static readonly string[] scheduleTypes = { "Schedule:Compact", "Schedule:Constant", "Schedule:File", "Schedule:Year", "Schedule:Day:Interval", "Schedule:FixedInterval" };

        public static NativeSnapshot Read(string osmPath, string idfPath)
        {
            NativeSnapshot result = new NativeSnapshot
            {
                ProcessId = Environment.ProcessId,
                StartedUtc = DateTime.UtcNow,
                ReaderAssemblySha256 = Hashing.Sha256(typeof(NativeReader).Assembly.Location),
                OsmSha256Before = Hashing.Sha256(osmPath),
                IdfSha256Before = Hashing.Sha256(idfPath)
            };

            List<IdfObject> idf = IdfText.Parse(File.ReadAllText(idfPath));
            List<IdfObject> osm = IdfText.Parse(File.ReadAllText(osmPath));

            result.Idf = idf.Where(x => IdfTypes.Contains(x.Type, StringComparer.OrdinalIgnoreCase)).ToList();
            result.Osm = osm.Where(x => OsmTypes.Contains(x.Type, StringComparer.OrdinalIgnoreCase)).ToList();

            result.DayCount = 365;
            result.DayCountEvidence = "no RunPeriod: 365 days assumed";
            IdfObject runPeriod = result.IdfOf("RunPeriod").FirstOrDefault();
            if (runPeriod != null)
            {
                string year = runPeriod.Value(3, "Begin Year", out string problem);
                if (problem == null && int.TryParse(year, NumberStyles.Integer, CultureInfo.InvariantCulture, out int beginYear))
                {
                    result.DayCount = DateTime.IsLeapYear(beginYear) ? 366 : 365;
                    result.DayCountEvidence = "IDF RunPeriod begin year " + beginYear;
                }
                else
                {
                    result.DayCountEvidence = "IDF RunPeriod begin year unreadable ('" + year + "'" + (problem == null ? string.Empty : "; " + problem) + "): 365 days assumed";
                }
            }

            foreach (IdfObject schedule in idf.Where(x => scheduleTypes.Contains(x.Type, StringComparer.OrdinalIgnoreCase)))
            {
                result.IdfSchedules.Add(ScheduleReader.Read(schedule, result.DayCount, "idf"));
            }

            foreach (IdfObject schedule in osm.Where(x => string.Equals(x.Type, "OS:Schedule:FixedInterval", StringComparison.OrdinalIgnoreCase)))
            {
                result.OsmSchedules.Add(ScheduleReader.Read(schedule, result.DayCount, "osm"));
            }

            // Schedule references in the OSM are handles; keep each schedule's handle beside its name.
            foreach (IdfObject schedule in osm.Where(x => string.Equals(x.Type, "OS:Schedule:FixedInterval", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Type, "OS:Schedule:Constant", StringComparison.OrdinalIgnoreCase)))
            {
                result.Osm.Add(new IdfObject { Type = "SAM:OsmScheduleHandle", Fields = new List<IdfField> { new IdfField { Value = schedule.Value(0) }, new IdfField { Value = schedule.Value(1) }, new IdfField { Value = schedule.Type } } });
            }

            IdfObject weatherFile = result.OsmOf("OS:WeatherFile").FirstOrDefault();
            string url = weatherFile?.Value(10, "Url", out string _);
            if (string.IsNullOrWhiteSpace(url))
            {
                result.WeatherFileProblem = "the OSM references no weather file";
            }
            else
            {
                string path = url.StartsWith("file:///", StringComparison.OrdinalIgnoreCase) ? url.Substring(8) : url;
                if (!Path.IsPathRooted(path))
                {
                    path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(osmPath)), path);
                }

                if (File.Exists(path))
                {
                    result.WeatherFileSha256 = Hashing.Sha256(path);
                }
                else
                {
                    result.WeatherFileProblem = "the referenced weather file does not exist";
                }
            }

            result.OsmSha256After = Hashing.Sha256(osmPath);
            result.IdfSha256After = Hashing.Sha256(idfPath);
            result.FinishedUtc = DateTime.UtcNow;
            return result;
        }
    }
}
