// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Analytical.Benchmark;
using SAM.Analytical.OpenStudio.Benchmark;
using SAM.Analytical.OpenStudio.ResultsReadBack;
using SAM.Analytical.Tas.DirectT3D.Validation.ReadBack;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SAM.Analytical.OpenStudio.InputReadBack.Tests
{
    /// <summary>One synthetic hourly value as a file will stamp it.</summary>
    internal sealed class SyntheticPoint
    {
        public int Month;
        public int Day;
        public int Hour;
        public double Value;
        public string Environment;
        public bool Warmup;
        public int Dst;

        public SyntheticPoint Copy() => (SyntheticPoint)MemberwiseClone();
    }

    /// <summary>One identified zone of the exported fixture IDF: its SAM space, native Zone and Ideal Loads names.</summary>
    internal sealed class SyntheticZone
    {
        public Guid SpaceGuid;
        public string Zone;
        public string IdealLoads;
    }

    /// <summary>
    /// A synthetic EnergyPlus run for S11 offline tests: the S09 export of a fixture (OpenStudio SDK translation only, no
    /// EnergyPlus) gives the in.idf; deterministic hourly series keyed by that IDF's native names are written as a
    /// real-schema eplusout.sql and an eplusout.eso. Each file holds its own copy of every series, so a test can break one
    /// file, both, or one series. Values: heating in the cold months and cooling in summer, both with a latent part of at
    /// least 10 % of Total, and a Total peak at a different hour from the Sensible peak (F3 discrimination).
    /// </summary>
    internal sealed class SyntheticRun
    {
        private static readonly int[] DaysInMonth = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
        private static readonly CultureInfo ci = CultureInfo.InvariantCulture;
        private static readonly Dictionary<string, string> exports = new Dictionary<string, string>(StringComparer.Ordinal);

        public string Idf;
        public string RunPeriodName;
        public List<SyntheticZone> Zones = new List<SyntheticZone>();
        public Dictionary<string, List<SyntheticPoint>> Sql = new Dictionary<string, List<SyntheticPoint>>(StringComparer.Ordinal);
        public Dictionary<string, List<SyntheticPoint>> Eso = new Dictionary<string, List<SyntheticPoint>>(StringComparer.Ordinal);
        public List<KeyValuePair<string, int>> SqlEnvironments = new List<KeyValuePair<string, int>>();
        public List<string> EsoEnvironments = new List<string>();
        public string SqlVersion = "EnergyPlus, Version 25.1.0-1c11a3d85f, YMD=2026.10.24 08:00";
        public string EsoVersion = "Program Version,EnergyPlus, Version 25.1.0-1c11a3d85f, YMD=2026.10.24 08:00";
        public bool Meters = true;
        public List<ZoneSizeRow> ZoneSizes = new List<ZoneSizeRow>();

        /// <summary>Extra SQL edits after the file is written (e.g. a second dictionary entry).</summary>
        public Action<SQLiteConnection> SqlEdit;

        /// <summary>Extra ESO text edits after the file is written (e.g. a repeated value line).</summary>
        public Func<List<string>, List<string>> EsoEdit;

        public static string Key(string variable, string key) => S11Names.SeriesKey(variable, key);

        /// <summary>The exported in.idf of a model (cached per model file hash); the export runs os-input-readback in its own process.</summary>
        public static string ExportIdf(string modelPath, string label)
        {
            string hash = Hashing.Sha256(modelPath);
            lock (exports)
            {
                if (!exports.TryGetValue(hash, out string idf))
                {
                    string run = TestSupport.NewDirectory("s11-export-" + label);
                    int exit = TestSupport.Cli(out string output, "export", "--model", modelPath, "--epw", TestSupport.Budapest(), "--out", run, "--label", label);
                    Assert.That(exit, Is.EqualTo(0), output);
                    idf = File.ReadAllText(Path.Combine(run, "in.idf"));
                    exports[hash] = idf;
                }

                return idf;
            }
        }

        /// <summary>A run for the model at <paramref name="modelPath"/>, every series complete and consistent in both files.</summary>
        public static SyntheticRun For(string modelPath, string label, bool fractionalUnmet = false)
        {
            AnalyticalModel model = Core.Convert.ToSAM<AnalyticalModel>(modelPath).Single();
            SyntheticRun result = new SyntheticRun { Idf = ExportIdf(modelPath, label) };
            List<IdfObject> idf = IdfText.Parse(result.Idf).Where(x => NativeReader.IdfTypes.Contains(x.Type, StringComparer.OrdinalIgnoreCase)).ToList();
            result.RunPeriodName = idf.Single(x => x.Type == "RunPeriod").Name.ToUpperInvariant();
            NativeModel native = new NativeModel(new NativeSnapshot { Idf = idf });
            foreach (NativeZoneIdentityEntry entry in NativeZoneIdentity.Resolve(native, ReadBackExpected.Build(model, null, label)))
            {
                Assert.That(entry.Problem, Is.Null, "the exported fixture must have a clean identity");
                result.Zones.Add(new SyntheticZone { SpaceGuid = entry.SpaceGuid, Zone = entry.Zone.ToUpperInvariant(), IdealLoads = entry.IdealLoads.ToUpperInvariant() });
            }

            result.SqlEnvironments.Add(new KeyValuePair<string, int>(result.RunPeriodName, 3));
            result.EsoEnvironments.Add(result.RunPeriodName);
            for (int z = 0; z < result.Zones.Count; z++)
            {
                SyntheticZone zone = result.Zones[z];
                foreach (string variable in S11Names.IdealLoadsVariables.Concat(S11Names.ZoneVariables))
                {
                    string key = S11Names.ZoneVariables.Contains(variable) ? zone.Zone : zone.IdealLoads;
                    List<SyntheticPoint> points = new List<SyntheticPoint>();
                    int hourOfYear = 0;
                    for (int month = 1; month <= 12; month++)
                    {
                        for (int day = 1; day <= DaysInMonth[month - 1]; day++)
                        {
                            for (int hour = 1; hour <= 24; hour++)
                            {
                                points.Add(new SyntheticPoint { Month = month, Day = day, Hour = hour, Value = Value(variable, z, month, day, hour, hourOfYear, fractionalUnmet), Environment = result.RunPeriodName });
                                hourOfYear++;
                            }
                        }
                    }

                    result.Sql[Key(variable, key)] = points;
                    result.Eso[Key(variable, key)] = points.Select(x => x.Copy()).ToList();
                }
            }

            return result;
        }

        /// <summary>
        /// Deterministic, non-round values [J per hour or hr]. Cooling: May-September, EnergyPlus hours 10-19 (1530 hours).
        /// Sensible cooling peaks on 15 July h15; Total peaks on 10 August h14 where the latent is large. Heating likewise in
        /// November-March with a latent (humidification) part. Zone index shifts the magnitudes. Not-met time is whole hours
        /// (29 heating, 20 cooling, equal in every zone) unless <paramref name="fractionalUnmet"/>: EnergyPlus reports
        /// timestep fractions, here binary-exact quarter hours that differ per zone so the annual sums are exact and a swap
        /// between zones is visible. Zone 0: heating 29 x 0.5 = 14.5 h, cooling 19 x 0.75 + 0.5 = 14.75 h; every other
        /// zone: heating 29 x 0.25 = 7.25 h, cooling 19 x 0.25 + 0.5 = 5.25 h (0.5 on 28 September).
        /// </summary>
        public static double Value(string variable, int zone, int month, int day, int hour, int hourOfYear, bool fractionalUnmet = false)
        {
            double scale = 1 + 0.37 * zone;
            bool cooling = month >= 5 && month <= 9 && hour >= 10 && hour <= 19;
            bool heating = (month >= 11 || month <= 3) && (hour <= 9 || hour >= 18);
            double wave = 0.5 + 0.5 * Math.Sin(hourOfYear * 0.0137 + zone);
            double sensibleCooling = cooling ? scale * (1_000_000.123 + 400_000.0 * wave) : 0;
            double sensibleHeating = heating ? scale * (1_500_000.321 + 500_000.0 * wave) : 0;
            if (month == 7 && day == 15 && hour == 15) sensibleCooling = scale * 2_000_000.5;
            if (month == 1 && day == 20 && hour == 6) sensibleHeating = scale * 2_600_000.25;
            double latentCooling = sensibleCooling * 0.15;
            double latentHeating = sensibleHeating * 0.12;
            if (month == 8 && day == 10 && hour == 14) { sensibleCooling = scale * 1_950_000.75; latentCooling = scale * 600_000.0; }
            if (month == 2 && day == 3 && hour == 7) { sensibleHeating = scale * 2_550_000.5; latentHeating = scale * 700_000.0; }

            switch (variable)
            {
                case S11Names.SensibleCooling: return sensibleCooling;
                case S11Names.TotalCooling: return sensibleCooling + latentCooling;
                case S11Names.SensibleHeating: return sensibleHeating;
                case S11Names.TotalHeating: return sensibleHeating + latentHeating;
                case S11Names.UnmetHeating: return heating && hour == 7 && day % 5 == 0 ? (fractionalUnmet ? (zone == 0 ? 0.5 : 0.25) : 1.0) : 0;
                case S11Names.UnmetCooling: return cooling && hour == 16 && day % 7 == 0 ? (fractionalUnmet ? (month == 9 && day == 28 ? 0.5 : zone == 0 ? 0.75 : 0.25) : 1.0) : 0;
                default: throw new ArgumentException(variable);
            }
        }

        /// <summary>Applies <paramref name="edit"/> to every series of the chosen file(s).</summary>
        public void EditSeries(bool sql, bool eso, Action<List<SyntheticPoint>> edit, Func<string, bool> which = null)
        {
            foreach (Dictionary<string, List<SyntheticPoint>> file in new[] { sql ? Sql : null, eso ? Eso : null }.Where(x => x != null))
            {
                foreach (KeyValuePair<string, List<SyntheticPoint>> series in file.Where(x => which == null || which(x.Key)).ToList())
                {
                    edit(series.Value);
                }
            }
        }

        // ---- writing ---------------------------------------------------------------------------------------------------

        /// <summary>Writes run/{in.idf, eplusout.sql, eplusout.eso} below <paramref name="runDirectory"/>.</summary>
        public void Write(string runDirectory)
        {
            string folder = Path.Combine(runDirectory, "work", "run");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "in.idf"), Idf);
            WriteSql(Path.Combine(folder, "eplusout.sql"));
            WriteEso(Path.Combine(folder, "eplusout.eso"));
        }

        private void WriteSql(string path)
        {
            using (SQLiteConnection connection = new SQLiteConnection("Data Source=" + path))
            {
                connection.Open();
                using (SQLiteTransaction transaction = connection.BeginTransaction())
                using (SQLiteCommand command = connection.CreateCommand())
                {
                    void Exec(string sql)
                    {
                        command.CommandText = sql;
                        command.ExecuteNonQuery();
                    }

                    Exec("CREATE TABLE Simulations (SimulationIndex INTEGER PRIMARY KEY, EnergyPlusVersion TEXT, TimeStamp TEXT, NumTimestepsPerHour INTEGER, Completed BOOL, CompletedSuccessfully BOOL)");
                    Exec("CREATE TABLE EnvironmentPeriods (EnvironmentPeriodIndex INTEGER PRIMARY KEY, SimulationIndex INTEGER, EnvironmentName TEXT, EnvironmentType INTEGER)");
                    Exec("CREATE TABLE Time (TimeIndex INTEGER PRIMARY KEY, Year INTEGER, Month INTEGER, Day INTEGER, Hour INTEGER, Minute INTEGER, Dst INTEGER, Interval INTEGER, IntervalType INTEGER, SimulationDays INTEGER, DayType TEXT, EnvironmentPeriodIndex INTEGER, WarmupFlag INTEGER)");
                    Exec("CREATE TABLE ReportDataDictionary (ReportDataDictionaryIndex INTEGER PRIMARY KEY, IsMeter INTEGER, Type TEXT, IndexGroup TEXT, TimestepType TEXT, KeyValue TEXT, Name TEXT, ReportingFrequency TEXT, ScheduleName TEXT, Units TEXT)");
                    Exec("CREATE TABLE ReportData (ReportDataIndex INTEGER PRIMARY KEY, TimeIndex INTEGER, ReportDataDictionaryIndex INTEGER, Value REAL)");
                    Exec("CREATE TABLE Zones (ZoneIndex INTEGER PRIMARY KEY, ZoneName TEXT, Volume REAL, FloorArea REAL)");
                    Exec("CREATE TABLE Surfaces (SurfaceIndex INTEGER PRIMARY KEY, SurfaceName TEXT, ConstructionIndex INTEGER, ClassName TEXT, Area REAL, GrossArea REAL, Perimeter REAL, Azimuth REAL, Height REAL, Reveal REAL, Shape INTEGER, Sides INTEGER, Tilt REAL, Width REAL, HeatTransferSurf INTEGER, BaseSurfaceIndex INTEGER, ZoneIndex INTEGER, ExtBoundCond INTEGER, ExtSolar INTEGER, ExtWind INTEGER)");
                    Exec("CREATE TABLE ZoneSizes (ZoneSizesIndex INTEGER PRIMARY KEY, ZoneName TEXT, LoadType TEXT, CalcDesLoad REAL, UserDesLoad REAL, CalcDesFlow REAL, UserDesFlow REAL, DesDayName TEXT, PeakHrMin TEXT, PeakTemp REAL, PeakHumRat REAL, CalcOutsideAirFlow REAL, DOASHeatAddRate REAL)");
                    Exec("CREATE TABLE NominalLighting (NominalLightingIndex INTEGER PRIMARY KEY, ObjectName TEXT, ZoneIndex INTEGER, ScheduleIndex INTEGER, DesignLevel REAL)");
                    Exec("CREATE TABLE NominalInfiltration (NominalInfiltrationIndex INTEGER PRIMARY KEY, ObjectName TEXT, ZoneIndex INTEGER, ScheduleIndex INTEGER, DesignLevel REAL)");
                    Exec("CREATE TABLE NominalElectricEquipment (NominalElectricEquipmentIndex INTEGER PRIMARY KEY, ObjectName TEXT, ZoneIndex INTEGER, ScheduleIndex INTEGER, DesignLevel REAL)");

                    Parameter(command, "INSERT INTO Simulations VALUES (1, @v, 'YMD=2026.10.24 08:00', 6, 1, 1)", "@v", SqlVersion);
                    Dictionary<string, int> environments = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < SqlEnvironments.Count; i++)
                    {
                        environments[SqlEnvironments[i].Key] = i + 1;
                        Parameter(command, "INSERT INTO EnvironmentPeriods VALUES (" + (i + 1) + ", 1, @n, " + SqlEnvironments[i].Value + ")", "@n", SqlEnvironments[i].Key);
                    }

                    for (int i = 0; i < Zones.Count; i++)
                    {
                        Parameter(command, "INSERT INTO Zones VALUES (" + (i + 1) + ", @n, 60, 20)", "@n", Zones[i].Zone);
                    }

                    foreach (ZoneSizeRow row in ZoneSizes)
                    {
                        Parameter(command, "INSERT INTO ZoneSizes (ZoneName, LoadType, CalcDesLoad, UserDesLoad, DesDayName, PeakHrMin) VALUES (@n, '" + row.LoadType + "', " + row.CalcDesLoad.ToString("R", ci) + ", " + (row.CalcDesLoad * 1.25).ToString("R", ci) + ", 'DESIGN DAY', ' 1/21 06:00')", "@n", row.ZoneName);
                    }

                    // Time rows: one per distinct stamp and occurrence (a repeated stamp gets its own Time row, as a merged
                    // environment would), in calendar order per environment.
                    Dictionary<string, long> times = new Dictionary<string, long>(StringComparer.Ordinal);
                    long timeIndex = 0;
                    long Time(SyntheticPoint point, int occurrence)
                    {
                        string stamp = string.Join("|", point.Environment, point.Month, point.Day, point.Hour, point.Warmup, point.Dst, occurrence);
                        if (!times.TryGetValue(stamp, out long index))
                        {
                            index = ++timeIndex;
                            times[stamp] = index;
                            int environment = environments.TryGetValue(point.Environment, out int e) ? e : 99;
                            Exec(string.Format(ci, "INSERT INTO Time VALUES ({0}, 2006, {1}, {2}, {3}, 0, {4}, 60, 1, 1, 'Monday', {5}, {6})", index, point.Month, point.Day, point.Hour, point.Dst, environment, point.Warmup ? 1 : 0));
                        }

                        return index;
                    }

                    long dictionary = 0;
                    long data = 0;
                    Dictionary<string, double[]> meterTotals = new Dictionary<string, double[]>(StringComparer.Ordinal);
                    foreach (KeyValuePair<string, List<SyntheticPoint>> series in Sql)
                    {
                        string[] parts = series.Key.Split('|');
                        bool zoneVariable = S11Names.ZoneVariables.Contains(parts[0]);
                        dictionary++;
                        Parameters(command, "INSERT INTO ReportDataDictionary VALUES (" + dictionary + ", 0, 'Sum', @g, @t, @k, @n, 'Hourly', '', @u)", ("@g", zoneVariable ? "Zone" : "System"), ("@t", zoneVariable ? "Zone" : "HVAC System"), ("@k", parts[1]), ("@n", parts[0]), ("@u", S11Names.UnitOf(parts[0])));
                        Dictionary<string, int> occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
                        foreach (SyntheticPoint point in series.Value)
                        {
                            string stamp = string.Join("|", point.Environment, point.Month, point.Day, point.Hour, point.Warmup, point.Dst);
                            occurrences.TryGetValue(stamp, out int occurrence);
                            occurrences[stamp] = occurrence + 1;
                            long time = Time(point, occurrence);
                            data++;
                            Exec(string.Format(ci, "INSERT INTO ReportData VALUES ({0}, {1}, {2}, {3})", data, time, dictionary, point.Value.ToString("R", ci)));
                        }
                    }

                    if (Meters)
                    {
                        foreach (LoadKind kind in new[] { LoadKind.Heating, LoadKind.Cooling })
                        {
                            dictionary++;
                            Parameters(command, "INSERT INTO ReportDataDictionary VALUES (" + dictionary + ", 1, 'Sum', 'Facility', 'Zone', '', @n, 'Hourly', '', 'J')", ("@n", S11Names.Meter(kind)));
                            foreach (SyntheticPoint point in MeterSeries(kind))
                            {
                                data++;
                                Exec(string.Format(ci, "INSERT INTO ReportData VALUES ({0}, {1}, {2}, {3})", data, Time(point, 0), dictionary, point.Value.ToString("R", ci)));
                            }
                        }
                    }

                    transaction.Commit();
                }

                SqlEdit?.Invoke(connection);
            }

            SQLiteConnection.ClearAllPools();
        }

        /// <summary>
        /// A facility meter: per stamp, the sum of the zones' Total energy in the SQL series of the RunPeriod (as EnergyPlus
        /// meters the Ideal Loads), so a test that edits the series edits the meter with them.
        /// </summary>
        private List<SyntheticPoint> MeterSeries(LoadKind kind)
        {
            Dictionary<string, SyntheticPoint> sums = new Dictionary<string, SyntheticPoint>(StringComparer.Ordinal);
            List<SyntheticPoint> result = new List<SyntheticPoint>();
            foreach (SyntheticZone zone in Zones)
            {
                if (!Sql.TryGetValue(Key(S11Names.Total(kind), zone.IdealLoads), out List<SyntheticPoint> series))
                {
                    continue;
                }

                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (SyntheticPoint point in series.Where(x => x.Environment == RunPeriodName && !x.Warmup))
                {
                    string stamp = point.Month + "|" + point.Day + "|" + point.Hour;
                    if (!seen.Add(stamp))
                    {
                        continue;
                    }

                    if (!sums.TryGetValue(stamp, out SyntheticPoint sum))
                    {
                        sum = new SyntheticPoint { Month = point.Month, Day = point.Day, Hour = point.Hour, Environment = RunPeriodName };
                        sums[stamp] = sum;
                        result.Add(sum);
                    }

                    sum.Value += point.Value;
                }
            }

            return result;
        }

        private void WriteEso(string path)
        {
            List<string> lines = new List<string> { EsoVersion };
            lines.Add("1,5,Environment Title[],Latitude[deg],Longitude[deg],Time Zone[],Elevation[m]");
            lines.Add("2,8,Day of Simulation[],Month[],Day of Month[],DST Indicator[1=yes 0=no],Hour[],StartMinute[],EndMinute[],DayType");
            lines.Add("3,5,Cumulative Day of Simulation[],Month[],Day of Month[],DST Indicator[1=yes 0=no],DayType  ! When Daily Report Variables Requested");
            lines.Add("4,2,Cumulative Days of Simulation[],Month[]  ! When Monthly Report Variables Requested");
            lines.Add("5,1,Cumulative Days of Simulation[] ! When Run Period Report Variables Requested");
            lines.Add("6,1,Calendar Year of Simulation[] ! When Annual Report Variables Requested");
            Dictionary<string, int> ids = new Dictionary<string, int>(StringComparer.Ordinal);
            int id = 7;
            foreach (string series in Eso.Keys)
            {
                string[] parts = series.Split('|');
                ids[series] = id;
                lines.Add(id + ",1," + parts[1] + "," + parts[0] + " [" + S11Names.UnitOf(parts[0]) + "] !Hourly");
                id++;
            }

            Dictionary<string, int> meterIds = new Dictionary<string, int>(StringComparer.Ordinal);
            if (Meters)
            {
                foreach (LoadKind kind in new[] { LoadKind.Heating, LoadKind.Cooling })
                {
                    meterIds[S11Names.Meter(kind)] = id;
                    lines.Add(id + ",1," + S11Names.Meter(kind) + " [J] !Hourly");
                    id++;
                }
            }

            lines.Add("End of Data Dictionary");
            foreach (string environment in EsoEnvironments)
            {
                lines.Add("1," + environment + ",  47.52,  19.03,   1.00, 129.00");

                // Stamps in first-appearance order over all series of this environment, each occurrence its own block.
                List<string> order = new List<string>();
                Dictionary<string, List<KeyValuePair<int, SyntheticPoint>>> blocks = new Dictionary<string, List<KeyValuePair<int, SyntheticPoint>>>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, List<SyntheticPoint>> series in Eso)
                {
                    Dictionary<string, int> occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (SyntheticPoint point in series.Value.Where(x => string.Equals(x.Environment, environment, StringComparison.OrdinalIgnoreCase)))
                    {
                        string stamp = string.Join("|", point.Month, point.Day, point.Hour, point.Dst);
                        occurrences.TryGetValue(stamp, out int occurrence);
                        occurrences[stamp] = occurrence + 1;
                        string block = stamp + "|" + occurrence;
                        if (!blocks.TryGetValue(block, out List<KeyValuePair<int, SyntheticPoint>> list))
                        {
                            list = new List<KeyValuePair<int, SyntheticPoint>>();
                            blocks[block] = list;
                            order.Add(block);
                        }

                        list.Add(new KeyValuePair<int, SyntheticPoint>(ids[series.Key], point));
                    }
                }

                Dictionary<string, double> meters = new Dictionary<string, double>(StringComparer.Ordinal);
                if (Meters && environment == RunPeriodName)
                {
                    foreach (LoadKind kind in new[] { LoadKind.Heating, LoadKind.Cooling })
                    {
                        foreach (SyntheticPoint point in MeterSeries(kind))
                        {
                            meters[S11Names.Meter(kind) + "|" + point.Month + "|" + point.Day + "|" + point.Hour] = point.Value;
                        }
                    }
                }

                int dayOfSimulation = 0;
                string lastDay = null;
                foreach (string block in order)
                {
                    SyntheticPoint first = blocks[block][0].Value;
                    string dayKey = first.Month + "/" + first.Day;
                    if (dayKey != lastDay)
                    {
                        dayOfSimulation++;
                        lastDay = dayKey;
                    }

                    lines.Add(string.Format(ci, "2,{0},{1,2},{2,2},{3,2},{4,2}, 0.00,60.00,Monday", dayOfSimulation, first.Month, first.Day, first.Dst, first.Hour));
                    foreach (KeyValuePair<int, SyntheticPoint> value in blocks[block])
                    {
                        lines.Add(value.Key.ToString(ci) + "," + value.Value.Value.ToString("R", ci));
                    }

                    if (block.EndsWith("|0", StringComparison.Ordinal))
                    {
                        foreach (KeyValuePair<string, int> meter in meterIds)
                        {
                            if (meters.TryGetValue(meter.Key + "|" + first.Month + "|" + first.Day + "|" + first.Hour, out double value))
                            {
                                lines.Add(meter.Value.ToString(ci) + "," + value.ToString("R", ci));
                            }
                        }
                    }
                }
            }

            lines.Add("End of Data");
            lines.Add(" Number of Records Written=      " + lines.Count);
            if (EsoEdit != null)
            {
                lines = EsoEdit(lines);
            }

            File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
        }

        private static void Parameter(SQLiteCommand command, string sql, string name, object value)
        {
            Parameters(command, sql, (name, value));
        }

        private static void Parameters(SQLiteCommand command, string sql, params (string Name, object Value)[] values)
        {
            command.CommandText = sql;
            command.Parameters.Clear();
            foreach ((string name, object value) in values)
            {
                command.Parameters.AddWithValue(name, value);
            }

            command.ExecuteNonQuery();
            command.Parameters.Clear();
        }
    }

    /// <summary>
    /// The producer side of a synthetic run, offline: the production C5 extraction of the SQL (OpenStudioSimulationRunner,
    /// reached by reflection because it is internal; the route's own Program would call it after the simulation), then the
    /// benchmark producer's BenchmarkInputs/ToBenchmark, writing b1a.json and sam.json exactly as benchmark-openstudio does.
    /// </summary>
    internal static class SyntheticProducer
    {
        public static OpenStudioSimulationResultSet ResultSet(string sqlPath)
        {
            Type runner = typeof(OpenStudioSimulationResultSet).Assembly.GetType("SAM.Analytical.OpenStudio.OpenStudioSimulationRunner", true);
            MethodInfo extractLoads = runner.GetMethod("ExtractLoads", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo extractResultSet = runner.GetMethod("ExtractResultSet", BindingFlags.NonPublic | BindingFlags.Static);
            object[] arguments = { sqlPath, null };
            object summary = extractLoads.Invoke(null, arguments);
            Assert.That(summary, Is.Not.Null, "production ExtractLoads failed: " + arguments[1]);
            return (OpenStudioSimulationResultSet)extractResultSet.Invoke(null, new object[] { sqlPath, summary, false, 12.5, 0, 0, 0 });
        }

        public static OpenStudioBenchmarkContext Context(AnalyticalModel model, OpenStudioSimulationResultSet resultSet, DesignDaySource designDaySource = DesignDaySource.None)
        {
            OpenStudioBenchmarkContext context = new OpenStudioBenchmarkContext
            {
                SourceModelName = model.Name ?? "model",
                SourceModelGuid = model.Guid.ToString("N"),
                SourceFileHash = BenchmarkHash.ComputeSha256(new byte[] { 1, 2, 3 }),
                CanonicalModelHash = BenchmarkCanonicalJson.ComputeSha256("{\"s11\":\"synthetic\"}"),
                CanonicalizationVersion = BenchmarkCanonicalization.CurrentVersion,
                SamCommit = "0123456789abcdef",
                RunnerCommit = "fedcba9876543210",
                EngineName = "EnergyPlus",
                EngineVersion = "25.1.0",
                SdkVersion = "3.10.0",
                WeatherIdentity = "budapest",
                WeatherHash = BenchmarkHash.ComputeSha256(new byte[] { 4, 5, 6 }),
                DesignDaySource = designDaySource,
                RunTimestampUtc = new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero),
                DurationSeconds = 12.5,
                State = RunState.Success,
                ResultSet = resultSet,
            };

            context.SpaceDesignLoadResults = resultSet.ToSAM_SpaceDesignLoadResults(model.AdjacencyCluster.GetSpaces(), out List<Core.OpenStudio.OpenStudioDiagnostic> _);
            return context;
        }

        /// <summary>Writes b1a.json and sam.json for the run in <paramref name="runDirectory"/>; returns the producer objects.</summary>
        public static OpenStudioBenchmarkInputs Produce(AnalyticalModel model, string runDirectory, DesignDaySource designDaySource = DesignDaySource.None, Action<OpenStudioBenchmarkInputs> mutate = null)
        {
            OpenStudioSimulationResultSet resultSet = ResultSet(Path.Combine(runDirectory, "work", "run", "eplusout.sql"));
            OpenStudioBenchmarkContext context = Context(model, resultSet, designDaySource);
            OpenStudioBenchmarkInputs inputs = model.BenchmarkInputs(context);
            mutate?.Invoke(inputs);
            BenchmarkDocument document = model.ToBenchmark(context, inputs);
            Assert.That(BenchmarkValidator.Validate(document).IsValid, Is.True, "the produced B1a must validate");
            BenchmarkSerializer.Write(Path.Combine(runDirectory, RunLayout.B1aFile), document);
            inputs.Write(Path.Combine(runDirectory, RunLayout.SamResultsFile));
            return inputs;
        }
    }
}
