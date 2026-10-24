// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Analytical.OpenStudio.ResultsReadBack;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text.RegularExpressions;

namespace SAM.Analytical.OpenStudio.InputReadBack.Tests
{
    /// <summary>
    /// S11-C1/C1b (revision-2 change 1): every (zone x required quantity x file) must hold each of the 8760 hours of the
    /// in.idf RunPeriod calendar exactly once, judged per file. An error shared by SQL and ESO, a one-sided error and a
    /// count-preserving shift all BLOCK. For the shared errors the production reader still returns values for the same
    /// files, so the detection comes from the reference, not from the importer.
    /// </summary>
    [TestFixture]
    public class S11CompletenessTests
    {
        public enum Defect
        {
            MissingHourBoth,
            DuplicatedHourBothSameValue,
            DuplicatedHourBothDifferentValue,
            CountPreservingShiftBoth,
            MissingHourSqlOnly,
            MissingHourEsoOnly,
            DuplicatedHourSqlOnly,
            DuplicatedHourEsoOnly,
            SeriesInSqlAbsentFromEso,
            SeriesInEsoAbsentFromSql,
            ZoneMissingFromBoth,
            EsoIdRepeatedInsideOneHourlyBlock,
            SqlSecondHourlyDictionaryEntry,
            EsoSecondHourlyDictionaryEntry,
            ExtraHourBoth,
            February29Both,
            WarmupRowsInRunPeriodSql,
            DesignDayRowsMergedIntoRunPeriodBoth,
            NonAnnualRunPeriod,
            LeapRunPeriod,
            SecondRunPeriodEnvironmentEso,
            DaylightSavingStampBoth,
            SqlExtraTimestepFrequencyZeroValue,
            EsoExtraTimestepFrequencyZeroValue,
            SqlSecondWeatherRunEnvironmentOtherName,
        }

        /// <summary>The defects present identically in both files (the paired production-reader check applies to them).</summary>
        private static readonly Defect[] Shared =
        {
            Defect.MissingHourBoth, Defect.DuplicatedHourBothSameValue, Defect.DuplicatedHourBothDifferentValue, Defect.CountPreservingShiftBoth,
            Defect.ExtraHourBoth, Defect.February29Both, Defect.DesignDayRowsMergedIntoRunPeriodBoth, Defect.SqlSecondWeatherRunEnvironmentOtherName,
        };

        /// <summary>A second weather-run (EnvironmentType 3) environment, named otherwise; the production reader reads every type-3 environment.</summary>
        public const string OtherWeatherRun = "WEATHER FILE RUN PERIOD 2";

        private static void Break(SyntheticRun run, Defect defect)
        {
            string firstKey = SyntheticRun.Key(S11Names.TotalCooling, run.Zones[0].IdealLoads);
            Func<string, bool> one = x => x == firstKey;
            switch (defect)
            {
                case Defect.MissingHourBoth:
                    run.EditSeries(true, true, s => s.RemoveAll(p => p.Month == 7 && p.Day == 7 && p.Hour == 13));
                    break;
                case Defect.DuplicatedHourBothSameValue:
                    run.EditSeries(true, true, s => s.Add(s.First(p => p.Month == 7 && p.Day == 7 && p.Hour == 13).Copy()));
                    break;
                case Defect.DuplicatedHourBothDifferentValue:
                    run.EditSeries(true, true, s => { SyntheticPoint copy = s.First(p => p.Month == 7 && p.Day == 7 && p.Hour == 13).Copy(); copy.Value += 1; s.Add(copy); });
                    break;
                case Defect.CountPreservingShiftBoth:
                    run.EditSeries(true, true, s =>
                    {
                        SyntheticPoint next = s.First(p => p.Month == 7 && p.Day == 7 && p.Hour == 14);
                        next.Hour = 13;
                        Assert.That(s.Count, Is.EqualTo(8760), "the shift keeps the count");
                    });
                    break;
                case Defect.MissingHourSqlOnly:
                    run.EditSeries(true, false, s => s.RemoveAll(p => p.Month == 7 && p.Day == 7 && p.Hour == 13), one);
                    break;
                case Defect.MissingHourEsoOnly:
                    run.EditSeries(false, true, s => s.RemoveAll(p => p.Month == 7 && p.Day == 7 && p.Hour == 13), one);
                    break;
                case Defect.DuplicatedHourSqlOnly:
                    run.EditSeries(true, false, s => s.Add(s.First(p => p.Month == 7 && p.Day == 7 && p.Hour == 13).Copy()), one);
                    break;
                case Defect.DuplicatedHourEsoOnly:
                    run.EditSeries(false, true, s => s.Add(s.First(p => p.Month == 7 && p.Day == 7 && p.Hour == 13).Copy()), one);
                    break;
                case Defect.SeriesInSqlAbsentFromEso:
                    run.Eso.Remove(firstKey);
                    break;
                case Defect.SeriesInEsoAbsentFromSql:
                    run.Sql.Remove(firstKey);
                    break;
                case Defect.ZoneMissingFromBoth:
                    foreach (string key in run.Sql.Keys.Where(x => x.EndsWith("|" + run.Zones[1].IdealLoads, StringComparison.Ordinal) || x.EndsWith("|" + run.Zones[1].Zone, StringComparison.Ordinal)).ToList())
                    {
                        run.Sql.Remove(key);
                        run.Eso.Remove(key);
                    }

                    break;
                case Defect.EsoIdRepeatedInsideOneHourlyBlock:
                    run.EsoEdit = lines =>
                    {
                        int stamp = lines.FindIndex(x => Regex.IsMatch(x, @"^2,\d+, 7, 7, 0,13, 0\.00,60\.00"));
                        lines.Insert(stamp + 2, lines[stamp + 1]);
                        return lines;
                    };
                    break;
                case Defect.SqlSecondHourlyDictionaryEntry:
                    run.SqlEdit = connection =>
                    {
                        using (SQLiteCommand command = connection.CreateCommand())
                        {
                            command.CommandText = "INSERT INTO ReportDataDictionary (IsMeter, Type, IndexGroup, TimestepType, KeyValue, Name, ReportingFrequency, Units) VALUES (0, 'Sum', 'System', 'HVAC System', @k, @n, 'Hourly', 'J')";
                            command.Parameters.AddWithValue("@k", run.Zones[0].IdealLoads);
                            command.Parameters.AddWithValue("@n", S11Names.TotalCooling);
                            command.ExecuteNonQuery();
                        }
                    };
                    break;
                case Defect.EsoSecondHourlyDictionaryEntry:
                    run.EsoEdit = lines =>
                    {
                        lines.Insert(lines.IndexOf("End of Data Dictionary"), "900,1," + run.Zones[0].IdealLoads + "," + S11Names.TotalCooling + " [J] !Hourly");
                        return lines;
                    };
                    break;
                case Defect.ExtraHourBoth:
                    run.EditSeries(true, true, s => s.Add(new SyntheticPoint { Month = 12, Day = 31, Hour = 25, Value = 1, Environment = run.RunPeriodName }));
                    break;
                case Defect.February29Both:
                    run.EditSeries(true, true, s => s.AddRange(Enumerable.Range(1, 24).Select(h => new SyntheticPoint { Month = 2, Day = 29, Hour = h, Value = 2, Environment = run.RunPeriodName })));
                    break;
                case Defect.WarmupRowsInRunPeriodSql:
                    run.EditSeries(true, false, s => s.AddRange(Enumerable.Range(1, 24).Select(h => new SyntheticPoint { Month = 1, Day = 1, Hour = h, Value = 3, Environment = run.RunPeriodName, Warmup = true })));
                    break;
                case Defect.DesignDayRowsMergedIntoRunPeriodBoth:
                    run.EditSeries(true, true, s => s.AddRange(Enumerable.Range(1, 24).Select(h => new SyntheticPoint { Month = 7, Day = 21, Hour = h, Value = 4, Environment = run.RunPeriodName })));
                    break;
                case Defect.NonAnnualRunPeriod:
                    run.Idf = EditRunPeriod(run.Idf, "End Month", "6");
                    break;
                case Defect.LeapRunPeriod:
                    run.Idf = EditRunPeriod(EditRunPeriod(run.Idf, "Begin Year", "2024"), "End Year", "2024");
                    break;
                case Defect.SecondRunPeriodEnvironmentEso:
                    run.EsoEnvironments.Add(run.RunPeriodName);
                    break;
                case Defect.DaylightSavingStampBoth:
                    run.EditSeries(true, true, s => s.First(p => p.Month == 7 && p.Day == 7 && p.Hour == 13).Dst = 1);
                    break;
                case Defect.SqlExtraTimestepFrequencyZeroValue:
                    // The hourly series stays complete; the additional frequency alone (with a zero value) must block it.
                    run.SqlEdit = connection =>
                    {
                        using (SQLiteCommand command = connection.CreateCommand())
                        {
                            command.CommandText = "INSERT INTO ReportDataDictionary (IsMeter, Type, IndexGroup, TimestepType, KeyValue, Name, ReportingFrequency, Units) VALUES (0, 'Sum', 'System', 'HVAC System', @k, @n, 'HVAC System Timestep', 'J')";
                            command.Parameters.AddWithValue("@k", run.Zones[0].IdealLoads);
                            command.Parameters.AddWithValue("@n", S11Names.TotalCooling);
                            command.ExecuteNonQuery();
                            command.CommandText = "INSERT INTO ReportData (TimeIndex, ReportDataDictionaryIndex, Value) VALUES ((SELECT MIN(TimeIndex) FROM Time), (SELECT MAX(ReportDataDictionaryIndex) FROM ReportDataDictionary), 0)";
                            command.ExecuteNonQuery();
                        }
                    };
                    break;
                case Defect.EsoExtraTimestepFrequencyZeroValue:
                    // As the review reproduction: a second dictionary entry at TimeStep frequency and one zero record.
                    run.EsoEdit = lines =>
                    {
                        lines.Insert(lines.IndexOf("End of Data Dictionary"), "999999,1," + run.Zones[0].IdealLoads + "," + S11Names.TotalCooling + " [J] !TimeStep");
                        int stamp = lines.FindIndex(x => Regex.IsMatch(x, @"^2,\d+, 7, 7, 0,13, 0\.00,60\.00"));
                        lines.Insert(stamp + 1, "999999,0");
                        return lines;
                    };
                    break;
                case Defect.SqlSecondWeatherRunEnvironmentOtherName:
                    run.SqlEnvironments.Add(new KeyValuePair<string, int>(OtherWeatherRun, 3));
                    run.EsoEnvironments.Add(OtherWeatherRun);
                    run.EditSeries(true, true, s => s.AddRange(Enumerable.Range(1, 24).Select(h => new SyntheticPoint { Month = 1, Day = 1, Hour = h, Value = 5, Environment = OtherWeatherRun })));
                    break;
            }
        }

        /// <summary>Replaces the value of one labelled RunPeriod field in the IDF text.</summary>
        public static string EditRunPeriod(string idf, string label, string value)
        {
            int start = idf.IndexOf("RunPeriod,", StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            int end = idf.IndexOf(';', start);
            string block = idf.Substring(start, end - start);
            string edited = Regex.Replace(block, @"(\n\s*)[^,\n]*(,\s*!- " + Regex.Escape(label) + @"\b)", "${1}" + value + "${2}");
            Assert.That(edited, Is.Not.EqualTo(block), "RunPeriod field " + label + " not found");
            return idf.Substring(0, start) + edited + idf.Substring(end);
        }

        [Test]
        public void EveryCompletenessDefect_Blocks([Values] Defect defect)
        {
            string model = S11Support.Two();
            SyntheticRun synthetic = SyntheticRun.For(model, "two");
            Break(synthetic, defect);
            S11Case run = S11Support.Evaluate(synthetic, model, "c1-" + defect);

            Assert.That(run.Compare.Verdict, Is.EqualTo("BLOCKED"), run.Summary());
            Assert.That(run.Compare.Failed, Is.Zero, "a completeness defect blocks, it is not a value failure: " + run.Summary());
            string reasons = string.Join(" ", run.Compare.Rows.Where(x => x.Status == "BLOCKED").Select(x => x.Reason)) + " " + string.Join(" ", run.Reference.GlobalBlocks);
            Assert.That(reasons, Does.Contain("S11-C1").Or.Contain("calendar").Or.Contain("eso:").Or.Contain("sql:"), reasons);
            if (defect == Defect.SqlExtraTimestepFrequencyZeroValue || defect == Defect.EsoExtraTimestepFrequencyZeroValue)
            {
                Assert.That(reasons, Does.Contain("unsupported reporting frequency"), reasons);
                Assert.That(run.Row("b1a|" + run.Space(1) + "|Cooling|peakLoad").Status, Is.EqualTo("PASS"), "only the key with the extra frequency blocks");
            }

            if (defect == Defect.SqlSecondWeatherRunEnvironmentOtherName)
            {
                Assert.That(string.Join(" ", run.Reference.GlobalBlocks), Does.Contain("2 SQL environment(s) of type 3"));
            }

            // Rows that do not depend on the broken series keep their verdict (each check blocks only dependent rows).
            if (defect == Defect.MissingHourSqlOnly || defect == Defect.SeriesInSqlAbsentFromEso)
            {
                Assert.That(run.Row("b1a|" + run.Space(0) + "|Heating|peakLoad").Status, Is.EqualTo("PASS"));
                Assert.That(run.Row("b1a|" + run.Space(1) + "|Cooling|peakLoad").Status, Is.EqualTo("PASS"));
                Assert.That(run.Row("b1a|" + run.Space(0) + "|Cooling|peakLoad").Status, Is.EqualTo("BLOCKED"));
                Assert.That(run.Row("b1a|model|Cooling|consumption").Status, Is.EqualTo("BLOCKED"));
            }

            if (Shared.Contains(defect))
            {
                OpenStudioSimulationResultSet production = SyntheticProducer.ResultSet(System.IO.Path.Combine(run.Run, "work", "run", "eplusout.sql"));
                Assert.That(production.AnnualCoolingEnergy.Count, Is.EqualTo(2), "the production reader still returns values for the defective files");
                Assert.That(production.PeakCoolingLoad.Values.All(x => x > 0), Is.True);
            }
        }

        [Test]
        public void CountPreservingShift_IsReportedAsOneDuplicateAndOneMissingHourPerSeriesAndFile()
        {
            string model = S11Support.Model(TestSupport.Box);
            SyntheticRun synthetic = SyntheticRun.For(model, "box");
            Break(synthetic, Defect.CountPreservingShiftBoth);
            S11Case run = S11Support.Evaluate(synthetic, model, "c1-shift-detail");

            Assert.That(run.Reference.Completeness, Has.Count.EqualTo(12), "6 quantities x 2 files");
            foreach (CompletenessResult result in run.Reference.Completeness)
            {
                Assert.That(result.Found, Is.EqualTo(8760));
                Assert.That(result.Missing, Is.EqualTo(1), result.Channel + " " + result.Variable);
                Assert.That(result.Duplicated, Is.EqualTo(1));
                Assert.That(result.Complete, Is.False);
            }
        }
    }
}
