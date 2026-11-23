// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical.Benchmark;
using SAM.Analytical.OpenStudio.Benchmark;
using SAM.Analytical.OpenStudio.InputReadBack;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.ResultsReadBack.Cli
{
    /// <summary>The record of a negative copy (negative.json): what was mutated and the exit compare must give.</summary>
    public sealed class NegativeRecord
    {
        public string Case { get; set; }
        public string Source { get; set; }
        public int ExpectedExit { get; set; }
        public string Mutation { get; set; }
        public Dictionary<string, string> SourceSha256 { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public DateTime CreatedUtc { get; set; }
    }

    /// <summary>
    /// Disposable negatives on COPIES of a produced run (no extra simulation; the original is never touched):
    /// <list type="bullet">
    /// <item>ctl-unmutated-copy: a plain copy; compare must PASS when the original passes.</item>
    /// <item>neg-delete-hour-both: one hour of every S11 series removed from BOTH the SQL and the ESO copy; must BLOCK.</item>
    /// <item>neg-duplicate-hour-both: one hour of every S11 series repeated in BOTH copies; must BLOCK.</item>
    /// <item>neg-sensible-cooling-substitution: the producer objects (sam.json) and the import (SAM-I) carry the reference
    /// SENSIBLE cooling in place of Total; the B1a copy is re-mapped from those objects, so the closure holds and the
    /// substitution must FAIL on the cooling rows. For the hour cases the read/subject/compare steps run fresh on the copy;
    /// for the substitution the subject is captured here, then its SAM-I cooling values are replaced.</item>
    /// </list>
    /// </summary>
    internal static class NegativeStep
    {
        public static readonly Dictionary<string, int> Cases = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["ctl-unmutated-copy"] = ReadBackExit.Pass,
            ["neg-delete-hour-both"] = ReadBackExit.BlockedTechnical,
            ["neg-duplicate-hour-both"] = ReadBackExit.BlockedTechnical,
            ["neg-sensible-cooling-substitution"] = ReadBackExit.Fail,
        };

        /// <summary>The mutated hour: 0-based hour 4500 of a non-leap year is 7 July, interval 12:00-13:00 (EnergyPlus hour 13).</summary>
        public static readonly HourKey MutatedHour = new HourKey(7, 7, 13);

        public static int Run(string runDirectory, string modelPath, string caseId, string outputDirectory, TextWriter output)
        {
            if (!Cases.TryGetValue(caseId, out int expectedExit))
            {
                throw new ArgumentException("unknown case '" + caseId + "'; cases: " + string.Join(", ", Cases.Keys));
            }

            if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
            {
                throw new IOException("The negative directory must be new or empty: " + outputDirectory);
            }

            RunLayout source = RunLayout.Resolve(runDirectory, out List<string> problems);
            if (problems.Count > 0)
            {
                throw new IOException(string.Join("; ", problems));
            }

            NegativeRecord record = new NegativeRecord { Case = caseId, Source = source.Directory, ExpectedExit = expectedExit, CreatedUtc = DateTime.UtcNow };
            foreach (string file in new[] { source.B1a, source.SamResults, source.Sql, source.Eso, source.Idf })
            {
                record.SourceSha256[Path.GetFileName(file)] = Sha256.Of(file);
            }

            string runFolder = Path.Combine(outputDirectory, "work", "run");
            Directory.CreateDirectory(runFolder);
            File.Copy(source.B1a, Path.Combine(outputDirectory, RunLayout.B1aFile));
            File.Copy(source.SamResults, Path.Combine(outputDirectory, RunLayout.SamResultsFile));
            File.Copy(source.Sql, Path.Combine(runFolder, "eplusout.sql"));
            File.Copy(source.Eso, Path.Combine(runFolder, "eplusout.eso"));
            File.Copy(source.Idf, Path.Combine(runFolder, "in.idf"));
            RunLayout copy = RunLayout.Resolve(outputDirectory, out _);

            switch (caseId)
            {
                case "ctl-unmutated-copy":
                    record.Mutation = "none (control)";
                    break;
                case "neg-delete-hour-both":
                    record.Mutation = "deleted " + MutatedHour + " of every S11 series in SQL (" + SqlDeleteHour(copy.Sql, MutatedHour) + " rows) and ESO (" + EsoEditHour(copy.Eso, MutatedHour, false) + " lines)";
                    break;
                case "neg-duplicate-hour-both":
                    record.Mutation = "repeated " + MutatedHour + " of every S11 series in SQL (" + SqlDuplicateHour(copy.Sql, MutatedHour) + " rows) and ESO (" + EsoEditHour(copy.Eso, MutatedHour, true) + " lines)";
                    break;
                case "neg-sensible-cooling-substitution":
                    record.Mutation = SensibleSubstitution(source, copy, modelPath);
                    break;
            }

            S11Json.Save(record, Path.Combine(outputDirectory, RunLayout.NegativeFile));
            output.WriteLine("negative " + caseId + ": " + record.Mutation + "; compare must exit " + expectedExit);
            return ReadBackExit.Pass;
        }

        // ---- SQL ---------------------------------------------------------------------------------------------------------

        private static List<long> HourlyIndexes(SQLiteConnection connection)
        {
            List<long> result = new List<long>();
            using (SQLiteCommand command = connection.CreateCommand())
            {
                string names = string.Join(",", S11Names.IdealLoadsVariables.Concat(S11Names.ZoneVariables).Select(x => "'" + x + "'"));
                command.CommandText = "SELECT ReportDataDictionaryIndex FROM ReportDataDictionary WHERE ReportingFrequency = 'Hourly' AND IsMeter = 0 AND Name IN (" + names + ")";
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(reader.GetInt64(0));
                    }
                }
            }

            return result;
        }

        private static long HourlyTimeIndex(SQLiteConnection connection, HourKey hour)
        {
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT t.TimeIndex FROM Time t JOIN EnvironmentPeriods e ON e.EnvironmentPeriodIndex = t.EnvironmentPeriodIndex WHERE e.EnvironmentType = 3 AND t.Interval = 60 AND t.Month = @m AND t.Day = @d AND t.Hour = @h AND t.WarmupFlag = 0";
                command.Parameters.AddWithValue("@m", hour.Month);
                command.Parameters.AddWithValue("@d", hour.Day);
                command.Parameters.AddWithValue("@h", hour.Hour);
                object value = command.ExecuteScalar();
                if (value == null)
                {
                    throw new InvalidDataException("no hourly Time row for " + hour);
                }

                return System.Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
        }

        private static SQLiteConnection Open(string path)
        {
            SQLiteConnection connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = path, FailIfMissing = true }.ConnectionString);
            connection.Open();
            return connection;
        }

        internal static int SqlDeleteHour(string sqlPath, HourKey hour)
        {
            try
            {
                using (SQLiteConnection connection = Open(sqlPath))
                using (SQLiteCommand command = connection.CreateCommand())
                {
                    long timeIndex = HourlyTimeIndex(connection, hour);
                    command.CommandText = "DELETE FROM ReportData WHERE TimeIndex = @t AND ReportDataDictionaryIndex IN (" + string.Join(",", HourlyIndexes(connection)) + ")";
                    command.Parameters.AddWithValue("@t", timeIndex);
                    return command.ExecuteNonQuery();
                }
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
            }
        }

        /// <summary>A second hourly Time row with the same stamp, and a copy of every S11 value at it.</summary>
        internal static int SqlDuplicateHour(string sqlPath, HourKey hour)
        {
            try
            {
                using (SQLiteConnection connection = Open(sqlPath))
                using (SQLiteCommand command = connection.CreateCommand())
                {
                    long timeIndex = HourlyTimeIndex(connection, hour);
                    command.CommandText = "SELECT MAX(TimeIndex) + 1 FROM Time";
                    long duplicate = System.Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
                    command.CommandText = "INSERT INTO Time (TimeIndex, Year, Month, Day, Hour, Minute, Dst, Interval, IntervalType, SimulationDays, DayType, EnvironmentPeriodIndex, WarmupFlag) "
                        + "SELECT @n, Year, Month, Day, Hour, Minute, Dst, Interval, IntervalType, SimulationDays, DayType, EnvironmentPeriodIndex, WarmupFlag FROM Time WHERE TimeIndex = @t";
                    command.Parameters.AddWithValue("@n", duplicate);
                    command.Parameters.AddWithValue("@t", timeIndex);
                    command.ExecuteNonQuery();
                    command.CommandText = "INSERT INTO ReportData (TimeIndex, ReportDataDictionaryIndex, Value) SELECT @n, ReportDataDictionaryIndex, Value FROM ReportData WHERE TimeIndex = @t AND ReportDataDictionaryIndex IN (" + string.Join(",", HourlyIndexes(connection)) + ")";
                    return command.ExecuteNonQuery();
                }
            }
            finally
            {
                SQLiteConnection.ClearAllPools();
            }
        }

        // ---- ESO ---------------------------------------------------------------------------------------------------------

        /// <summary>Removes (or repeats) the S11 value lines after the RunPeriod's hourly stamp of <paramref name="hour"/>.</summary>
        internal static int EsoEditHour(string esoPath, HourKey hour, bool duplicate)
        {
            List<string> lines = File.ReadAllLines(esoPath).ToList();
            HashSet<string> wanted = new HashSet<string>(S11Names.IdealLoadsVariables.Concat(S11Names.ZoneVariables), StringComparer.OrdinalIgnoreCase);
            HashSet<int> ids = new HashSet<int>();
            int end = lines.FindIndex(x => x.StartsWith("End of Data Dictionary", StringComparison.Ordinal));
            for (int i = 1; i < end; i++)
            {
                EsoVariable variable = EsoResultRead.DictionaryLine(lines[i]);
                if (variable != null && !variable.IsMeter && wanted.Contains(variable.Name) && string.Equals(variable.Frequency, "Hourly", StringComparison.OrdinalIgnoreCase))
                {
                    ids.Add(variable.Id);
                }
            }

            for (int i = end + 1; i < lines.Count; i++)
            {
                string[] f = lines[i].Split(',');
                if (f[0] != "2" || f.Length < 8)
                {
                    continue;
                }

                if (int.Parse(f[2].Trim(), CultureInfo.InvariantCulture) != hour.Month || int.Parse(f[3].Trim(), CultureInfo.InvariantCulture) != hour.Day || int.Parse(f[5].Trim(), CultureInfo.InvariantCulture) != hour.Hour
                    || double.Parse(f[6].Trim(), CultureInfo.InvariantCulture) != 0 || double.Parse(f[7].Trim(), CultureInfo.InvariantCulture) != 60)
                {
                    continue;
                }

                List<int> block = new List<int>();
                for (int j = i + 1; j < lines.Count; j++)
                {
                    string[] g = lines[j].Split(',');
                    if (!int.TryParse(g[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id <= 6)
                    {
                        break;
                    }

                    if (ids.Contains(id))
                    {
                        block.Add(j);
                    }
                }

                if (duplicate)
                {
                    List<string> copy = new List<string> { lines[i] };
                    copy.AddRange(block.Select(x => lines[x]));
                    int after = block.Count > 0 ? block.Max() + 1 : i + 1;
                    while (after < lines.Count && int.TryParse(lines[after].Split(',')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && id > 6)
                    {
                        after++;
                    }

                    lines.InsertRange(after, copy);
                    File.WriteAllLines(esoPath, lines);
                    return copy.Count;
                }

                for (int k = block.Count - 1; k >= 0; k--)
                {
                    lines.RemoveAt(block[k]);
                }

                File.WriteAllLines(esoPath, lines);
                return block.Count;
            }

            throw new InvalidDataException("no hourly ESO stamp for " + hour);
        }

        // ---- F3 substitution ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Replaces every cooling quantity of the producer objects and of the import by the reference SENSIBLE cooling, and
        /// re-maps the B1a copy from the mutated objects with the producer (so the copy is self-consistent).
        /// </summary>
        private static string SensibleSubstitution(RunLayout source, RunLayout copy, string modelPath)
        {
            if (!File.Exists(source.Reference) || !File.Exists(source.Subject))
            {
                throw new IOException("the source run needs reference.json and subject.json (run read and subject first)");
            }

            ReferenceDocument reference = S11Json.Load<ReferenceDocument>(source.Reference);
            AnalyticalModel model = Program.LoadModel(modelPath);
            ModelLoadReference cooling = reference.ModelOf(LoadKind.Cooling);
            if (!cooling.Sensible.Available || reference.Spaces.Any(x => !x.Quantities[S11Names.SensibleCooling].Available))
            {
                throw new InvalidDataException("the reference has no complete Sensible cooling to substitute");
            }

            OpenStudioBenchmarkInputs inputs = OpenStudioBenchmarkInputs.Read(source.SamResults);
            inputs.ModelResult.SetValue(AnalyticalModelSimulationResultParameter.ConsumptionCooling, cooling.Sensible.ConsumptionKwh);
            inputs.ModelResult.SetValue(AnalyticalModelSimulationResultParameter.PeakCoolingLoad, cooling.Sensible.CoincidentPeakKw);
            inputs.ModelResult.SetValue(AnalyticalModelSimulationResultParameter.PeakCoolingHour, cooling.Sensible.PeakHour);
            foreach (SpaceSimulationResult result in inputs.SpaceResults)
            {
                if (result.TryGetValue(Analytical.SpaceSimulationResultParameter.LoadType, out string loadType) && loadType == LoadType.Cooling.ToString())
                {
                    QuantityReference sensible = reference.Spaces.Single(x => x.SpaceGuid.ToString("N") == result.Reference).Quantities[S11Names.SensibleCooling];
                    result.SetValue(Analytical.SpaceSimulationResultParameter.Load, sensible.PeakWatts);
                    result.SetValue(Analytical.SpaceSimulationResultParameter.LoadIndex, sensible.PeakHour);
                }
            }

            inputs.Write(copy.SamResults);
            BenchmarkDocument produced = BenchmarkSerializer.Deserialize(File.ReadAllText(source.B1a));
            BenchmarkSerializer.Write(copy.B1a, model.ToBenchmark(SubjectStep.Context(produced.Provenance), inputs));

            SubjectDocument subject = SubjectStep.Capture(model, modelPath, copy);
            foreach (SubjectResult result in subject.SamI.Where(x => x.IsAnnual && x.LoadType == LoadType.Cooling.ToString()))
            {
                QuantityReference sensible = reference.Spaces.Single(x => x.SpaceGuid.ToString("N") == result.Reference).Quantities[S11Names.SensibleCooling];
                result.Load = sensible.PeakWatts;
                result.LoadIndex = sensible.PeakHour;
            }

            subject.Mutation = "SAM-I cooling Load/LoadIndex replaced by the reference Sensible cooling peak";
            S11Json.Save(subject, copy.Subject);
            return "producer objects and SAM-I carry the reference Sensible cooling (consumption " + cooling.Sensible.ConsumptionKwh.ToString("R", CultureInfo.InvariantCulture) + " kWh for Total " + cooling.Total.ConsumptionKwh.ToString("R", CultureInfo.InvariantCulture) + " kWh); B1a re-mapped from the mutated objects";
        }
    }
}
