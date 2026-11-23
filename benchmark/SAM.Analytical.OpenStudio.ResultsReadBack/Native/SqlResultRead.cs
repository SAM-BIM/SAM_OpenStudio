// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.ResultsReadBack
{
    /// <summary>One EnvironmentPeriods row.</summary>
    public sealed class SqlEnvironment
    {
        public long Index { get; set; }
        public string Name { get; set; }
        public long Type { get; set; }
    }

    /// <summary>A facility meter's sum over the RunPeriod environment as one channel reports it.</summary>
    public sealed class MeterSum
    {
        public string Name { get; set; }
        public string Frequency { get; set; }
        public int Rows { get; set; }
        public double Sum { get; set; }
    }

    /// <summary>One ZoneSizes row (design-day sizing outcome).</summary>
    public sealed class ZoneSizeRow
    {
        public string ZoneName { get; set; }
        public string LoadType { get; set; }
        public double CalcDesLoad { get; set; }
    }

    /// <summary>
    /// The SQL channel: eplusout.sql opened read-only with raw System.Data.SQLite (no OpenStudio SqlFile, no production
    /// reader). Every dictionary entry of the S11 variables is judged (any frequency other than Hourly blocks its key), and
    /// every Hourly series, for every key, is read row by row with its Time stamp; nothing is
    /// summed or filtered by SQL, so a missing, repeated or foreign row stays visible to <see cref="Completeness"/>.
    /// </summary>
    public sealed class SqlResultRead
    {
        public string Path { get; set; }
        public string Sha256Before { get; set; }
        public string Sha256After { get; set; }
        public List<string> EnergyPlusVersions { get; set; } = new List<string>();
        public List<SqlEnvironment> Environments { get; set; } = new List<SqlEnvironment>();

        /// <summary>
        /// The RunPeriod environment: the ONLY EnvironmentType 3 (weather-run) environment, which must be named as the in.idf
        /// RunPeriod; otherwise null with <see cref="EnvironmentProblem"/>. The production reader reads every type-3
        /// environment, so a second one (whatever its name) would be summed there and must not be ignored here.
        /// </summary>
        public long? RunPeriodEnvironment { get; set; }
        public string EnvironmentProblem { get; set; }

        /// <summary>Rows of the S11 series outside the RunPeriod environment (design days, sizing): ignored, counted.</summary>
        public int OtherEnvironmentRows { get; set; }

        public Dictionary<string, ChannelSeries> Series { get; set; } = new Dictionary<string, ChannelSeries>(StringComparer.Ordinal);
        public List<MeterSum> Meters { get; set; } = new List<MeterSum>();
        public List<ZoneSizeRow> ZoneSizes { get; set; } = new List<ZoneSizeRow>();
        public string Problem { get; set; }

        public static SqlResultRead Read(string sqlPath, RunCalendar calendar)
        {
            SqlResultRead result = new SqlResultRead { Path = System.IO.Path.GetFullPath(sqlPath), Sha256Before = Sha256.Of(sqlPath) };
            try
            {
                using (SQLiteConnection connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = sqlPath, ReadOnly = true, FailIfMissing = true }.ConnectionString))
                {
                    connection.Open();
                    HashSet<string> tables = new HashSet<string>(Query(connection, "SELECT name FROM sqlite_master WHERE type = 'table'", null, r => r.GetString(0)), StringComparer.OrdinalIgnoreCase);
                    foreach (string table in new[] { "Simulations", "EnvironmentPeriods", "Time", "ReportDataDictionary", "ReportData" })
                    {
                        if (!tables.Contains(table))
                        {
                            result.Problem = "eplusout.sql has no " + table + " table";
                            return result;
                        }
                    }

                    result.EnergyPlusVersions = Query(connection, "SELECT EnergyPlusVersion FROM Simulations ORDER BY SimulationIndex", null, r => r.IsDBNull(0) ? null : r.GetString(0));
                    result.Environments = Query(connection, "SELECT EnvironmentPeriodIndex, EnvironmentName, EnvironmentType FROM EnvironmentPeriods ORDER BY EnvironmentPeriodIndex", null, r => new SqlEnvironment { Index = r.GetInt64(0), Name = r.IsDBNull(1) ? null : r.GetString(1), Type = r.IsDBNull(2) ? -1 : r.GetInt64(2) });

                    List<SqlEnvironment> weatherRuns = result.Environments.Where(x => x.Type == 3).ToList();
                    if (weatherRuns.Count == 1 && calendar.RunPeriodName != null && string.Equals(weatherRuns[0].Name, calendar.RunPeriodName, StringComparison.OrdinalIgnoreCase))
                    {
                        result.RunPeriodEnvironment = weatherRuns[0].Index;
                    }
                    else
                    {
                        result.EnvironmentProblem = weatherRuns.Count + " SQL environment(s) of type 3 (exactly one, named '" + calendar.RunPeriodName + "', is required); environments: " + string.Join(", ", result.Environments.Select(x => x.Name + " (type " + x.Type + ")"));
                    }

                    foreach (string variable in S11Names.IdealLoadsVariables.Concat(S11Names.ZoneVariables))
                    {
                        ReadVariable(connection, result, variable);
                    }

                    foreach (string meter in S11Names.Meters)
                    {
                        ReadMeter(connection, result, meter);
                    }

                    if (tables.Contains("ZoneSizes"))
                    {
                        result.ZoneSizes = Query(connection, "SELECT ZoneName, LoadType, CalcDesLoad FROM ZoneSizes", null, r => new ZoneSizeRow { ZoneName = r.IsDBNull(0) ? null : r.GetString(0), LoadType = r.IsDBNull(1) ? null : r.GetString(1), CalcDesLoad = r.IsDBNull(2) ? double.NaN : r.GetDouble(2) });
                    }
                }
            }
            catch (Exception exception) when (exception is SQLiteException || exception is InvalidCastException || exception is IOException)
            {
                result.Problem = "eplusout.sql could not be read: " + exception.Message;
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
                result.Sha256After = Sha256.Of(sqlPath);
            }

            return result;
        }

        private sealed class DictionaryEntry
        {
            public long Index;
            public string Key;
            public string Units;
            public string Frequency;
        }

        private static void ReadVariable(SQLiteConnection connection, SqlResultRead result, string variable)
        {
            List<DictionaryEntry> entries = Query(connection, "SELECT ReportDataDictionaryIndex, KeyValue, Units, ReportingFrequency FROM ReportDataDictionary WHERE Name = @name COLLATE NOCASE AND IsMeter = 0 ORDER BY ReportDataDictionaryIndex", new Dictionary<string, object> { ["@name"] = variable }, r => new DictionaryEntry { Index = r.GetInt64(0), Key = r.IsDBNull(1) ? string.Empty : r.GetString(1), Units = r.IsDBNull(2) ? null : r.GetString(2), Frequency = r.IsDBNull(3) ? null : r.GetString(3) });
            // Every dictionary entry of a required variable is judged: the Hourly ones are the series, any other reporting
            // frequency for the same key is unsupported and blocks it (the production reader does not filter frequency).
            foreach (IGrouping<string, DictionaryEntry> group in entries.GroupBy(x => x.Key.ToUpperInvariant()))
            {
                List<DictionaryEntry> hourly = group.Where(x => string.Equals(x.Frequency, "Hourly", StringComparison.OrdinalIgnoreCase)).ToList();
                ChannelSeries series = new ChannelSeries
                {
                    Channel = "sql",
                    Variable = variable,
                    Key = group.First().Key,
                    Units = string.Join(",", hourly.Select(x => x.Units).Distinct()),
                    DictionaryEntries = hourly.Count,
                };

                foreach (DictionaryEntry entry in group.Except(hourly))
                {
                    series.Problem("S11-C1", "unsupported reporting frequency '" + entry.Frequency + "' (ReportDataDictionaryIndex " + entry.Index + "; only Hourly is permitted)");
                }

                foreach (DictionaryEntry entry in hourly)
                {
                    ReadRows(connection, result, series, entry.Index);
                }

                result.Series[S11Names.SeriesKey(variable, series.Key)] = series;
            }
        }

        private static void ReadRows(SQLiteConnection connection, SqlResultRead result, ChannelSeries series, long dictionaryIndex)
        {
            HashSet<long> timeIndexes = new HashSet<long>();
            const string sql = "SELECT rd.TimeIndex, t.TimeIndex, t.Month, t.Day, t.Hour, t.Minute, t.Interval, t.Dst, t.WarmupFlag, t.EnvironmentPeriodIndex, rd.Value "
                + "FROM ReportData rd LEFT JOIN Time t ON t.TimeIndex = rd.TimeIndex WHERE rd.ReportDataDictionaryIndex = @index ORDER BY rd.TimeIndex, rd.ReportDataIndex";
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.Parameters.AddWithValue("@index", dictionaryIndex);
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        long timeIndex = reader.GetInt64(0);
                        if (reader.IsDBNull(1))
                        {
                            series.Problem("S11-C1", "ReportData row at TimeIndex " + timeIndex + " has no Time row");
                            continue;
                        }

                        long environment = reader.IsDBNull(9) ? -1 : reader.GetInt64(9);
                        if (result.RunPeriodEnvironment == null || environment != result.RunPeriodEnvironment.Value)
                        {
                            result.OtherEnvironmentRows++;
                            continue;
                        }

                        if (!timeIndexes.Add(timeIndex))
                        {
                            series.Problem("S11-C1", "TimeIndex " + timeIndex + " repeats in ReportData");
                        }

                        int month = Int(reader, 2);
                        int day = Int(reader, 3);
                        int hour = Int(reader, 4);
                        int minute = Int(reader, 5);
                        int interval = Int(reader, 6);
                        int dst = Int(reader, 7);
                        int warmup = Int(reader, 8);
                        string stamp = month + "/" + day + " h" + hour + ":" + minute.ToString("00", CultureInfo.InvariantCulture);
                        if (warmup != 0)
                        {
                            series.Problem("S11-C1", "warmup row in the RunPeriod environment at " + stamp);
                            continue;
                        }

                        if (interval != 60 || minute != 0)
                        {
                            series.Problem("S11-C2", "row at " + stamp + " has Interval " + interval + " and Minute " + minute + " (an hourly row is Interval 60, Minute 0)");
                        }

                        if (dst != 0)
                        {
                            series.Problem("S11-C2", "row at " + stamp + " has Dst " + dst + " (0 is required)");
                        }

                        series.Points.Add(new HourlyPoint { Month = month, Day = day, Hour = hour, Value = reader.IsDBNull(10) ? double.NaN : reader.GetDouble(10) });
                    }
                }
            }
        }

        private static void ReadMeter(SQLiteConnection connection, SqlResultRead result, string meter)
        {
            List<DictionaryEntry> entries = Query(connection, "SELECT ReportDataDictionaryIndex, KeyValue, Units, ReportingFrequency FROM ReportDataDictionary WHERE Name = @name AND IsMeter = 1 ORDER BY ReportDataDictionaryIndex", new Dictionary<string, object> { ["@name"] = meter }, r => new DictionaryEntry { Index = r.GetInt64(0), Key = r.IsDBNull(1) ? string.Empty : r.GetString(1), Units = r.IsDBNull(2) ? null : r.GetString(2), Frequency = r.IsDBNull(3) ? null : r.GetString(3) });

            // The finest reported frequency sums every interval of the run once.
            DictionaryEntry entry = entries.FirstOrDefault(x => string.Equals(x.Frequency, "Zone Timestep", StringComparison.OrdinalIgnoreCase))
                ?? entries.FirstOrDefault(x => string.Equals(x.Frequency, "Hourly", StringComparison.OrdinalIgnoreCase));
            if (entry == null || result.RunPeriodEnvironment == null)
            {
                return;
            }

            MeterSum sum = new MeterSum { Name = meter, Frequency = entry.Frequency };
            const string sql = "SELECT rd.Value FROM ReportData rd JOIN Time t ON t.TimeIndex = rd.TimeIndex WHERE rd.ReportDataDictionaryIndex = @index AND t.EnvironmentPeriodIndex = @environment AND t.WarmupFlag = 0 ORDER BY rd.TimeIndex, rd.ReportDataIndex";
            foreach (double value in Query(connection, sql, new Dictionary<string, object> { ["@index"] = entry.Index, ["@environment"] = result.RunPeriodEnvironment.Value }, r => r.GetDouble(0)))
            {
                sum.Rows++;
                sum.Sum += value;
            }

            result.Meters.Add(sum);
        }

        private static int Int(SQLiteDataReader reader, int column)
        {
            return reader.IsDBNull(column) ? int.MinValue : System.Convert.ToInt32(reader.GetValue(column), CultureInfo.InvariantCulture);
        }

        private static List<T> Query<T>(SQLiteConnection connection, string sql, Dictionary<string, object> parameters, Func<SQLiteDataReader, T> read)
        {
            List<T> result = new List<T>();
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = sql;
                foreach (KeyValuePair<string, object> parameter in parameters ?? new Dictionary<string, object>())
                {
                    command.Parameters.AddWithValue(parameter.Key, parameter.Value);
                }

                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(read(reader));
                    }
                }
            }

            return result;
        }
    }

    /// <summary>SHA-256 of a file in lowercase hexadecimal; null when the file does not exist.</summary>
    public static class Sha256
    {
        public static string Of(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            using (FileStream stream = File.OpenRead(path))
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                return string.Concat(sha.ComputeHash(stream).Select(x => x.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }

        public static string OfBytes(byte[] bytes)
        {
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                return string.Concat(sha.ComputeHash(bytes).Select(x => x.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }
    }
}
