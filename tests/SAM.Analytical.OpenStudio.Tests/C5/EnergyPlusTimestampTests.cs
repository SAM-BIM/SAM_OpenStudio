// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// Human-Rhino validation, fix 2: EnergyPlus SQL Time rows use the end-of-interval
    /// convention (hour 0–24, minute 0–60, year 0 on sizing environments) and must be
    /// normalised centrally — never passed raw into a DateTime constructor. Reproduces the
    /// Grasshopper "Hour, Minute, and Second parameters describe an un-representable DateTime"
    /// failure (live SQL carries hour-0 sub-hourly rows and hour-24 rows) and pins the
    /// day/month/year rollover behaviour of the normalisation helper.
    /// </summary>
    [TestFixture]
    public class EnergyPlusTimestampTests
    {
        [Test]
        public void TryGetDateTime_OrdinaryHourlyTimestamp_IsIntervalEnd()
        {
            Assert.That(Core.OpenStudio.Query.TryGetDateTime(2006, 7, 15, 1, 0, 0, 2017, out DateTime dateTime, out string diagnostic), Is.True);
            Assert.That(diagnostic, Is.Null);
            Assert.That(dateTime, Is.EqualTo(new DateTime(2006, 7, 15, 1, 0, 0)), "Hour 1 minute 0 is the end of the first hourly interval");
        }

        [Test]
        public void TryGetDateTime_Hour24_RollsToNextDay()
        {
            Assert.That(Core.OpenStudio.Query.TryGetDateTime(2006, 1, 1, 24, 0, 0, 2017, out DateTime dateTime, out _), Is.True);
            Assert.That(dateTime, Is.EqualTo(new DateTime(2006, 1, 2, 0, 0, 0)), "24:00 is midnight at the end of the day, never clamped to 23:00");
        }

        [Test]
        public void TryGetDateTime_Minute60_RollsToNextHour()
        {
            Assert.That(Core.OpenStudio.Query.TryGetDateTime(2006, 1, 1, 1, 60, 0, 2017, out DateTime dateTime, out _), Is.True);
            Assert.That(dateTime, Is.EqualTo(new DateTime(2006, 1, 1, 2, 0, 0)), "1:60 is the end of hour 2, never clamped to 1:59");
        }

        [Test]
        public void TryGetDateTime_Hour24PlusMinute60_RollsToNextDayPlusHour()
        {
            Assert.That(Core.OpenStudio.Query.TryGetDateTime(2006, 1, 1, 24, 60, 0, 2017, out DateTime dateTime, out _), Is.True);
            Assert.That(dateTime, Is.EqualTo(new DateTime(2006, 1, 2, 1, 0, 0)));
        }

        [Test]
        public void TryGetDateTime_Hour0_SubHourlyRow_SameDayEarlyInterval()
        {
            // Live SQL rows: timestep data starts the day at Hour = 0 (00:10 … 00:50).
            Assert.That(Core.OpenStudio.Query.TryGetDateTime(2006, 1, 1, 0, 10, 0, 2017, out DateTime dateTime, out _), Is.True);
            Assert.That(dateTime, Is.EqualTo(new DateTime(2006, 1, 1, 0, 10, 0)), "The hour-0 sub-hourly row that threw the Grasshopper exception");
        }

        [Test]
        public void TryGetDateTime_Year0_UsesDefaultYear_SizingEnvironment()
        {
            Assert.That(Core.OpenStudio.Query.TryGetDateTime(0, 1, 21, 13, 0, 0, 2017, out DateTime dateTime, out _), Is.True);
            Assert.That(dateTime, Is.EqualTo(new DateTime(2017, 1, 21, 13, 0, 0)), "Sizing/design-day environments write Year = 0");
        }

        [Test]
        public void TryGetDateTime_February28_Valid()
        {
            Assert.That(Core.OpenStudio.Query.TryGetDateTime(2017, 2, 28, 24, 0, 0, 2017, out DateTime dateTime, out _), Is.True);
            Assert.That(dateTime, Is.EqualTo(new DateTime(2017, 3, 1, 0, 0, 0)), "Non-leap February rolls into March");
        }

        [Test]
        public void TryGetDateTime_February29_LeapYear_Valid()
        {
            Assert.That(Core.OpenStudio.Query.TryGetDateTime(2020, 2, 29, 24, 0, 0, 2017, out DateTime dateTime, out _), Is.True);
            Assert.That(dateTime, Is.EqualTo(new DateTime(2020, 3, 1, 0, 0, 0)), "Leap-day 24:00 rolls into March of the leap year");
        }

        [Test]
        public void TryGetDateTime_February29_NonLeapCalendar_DiagnosticNotException()
        {
            Assert.That(Core.OpenStudio.Query.TryGetDateTime(2017, 2, 29, 12, 0, 0, 2017, out _, out string diagnostic), Is.False);
            Assert.That(diagnostic, Does.Contain("2017").And.Contain("leap"), "The diagnostic names the calendar conflict");
        }

        [Test]
        public void TryGetDateTime_February29_Year0NonLeapDefault_DiagnosticNotException()
        {
            Assert.That(Core.OpenStudio.Query.TryGetDateTime(0, 2, 29, 12, 0, 0, 2017, out _, out string diagnostic), Is.False);
            Assert.That(diagnostic, Is.Not.Null);
        }

        [Test]
        public void TryGetDateTime_December31_FinalTimestep_RollsYear()
        {
            Assert.That(Core.OpenStudio.Query.TryGetDateTime(2006, 12, 31, 24, 0, 0, 2017, out DateTime dateTime, out _), Is.True);
            Assert.That(dateTime, Is.EqualTo(new DateTime(2007, 1, 1, 0, 0, 0)), "The year's final interval ends on 1 Jan of the next year");
        }

        [Test]
        public void TryGetDateTime_Month0_WarmupRow_DiagnosticNotException()
        {
            Assert.That(Core.OpenStudio.Query.TryGetDateTime(2006, 0, 1, 1, 0, 0, 2017, out _, out string diagnostic), Is.False);
            Assert.That(diagnostic, Is.Not.Null);
        }

        [Test]
        public void IntervalHourOfYear_MatchesLegacyHourlyConvention()
        {
            Assert.That(Core.OpenStudio.Query.IntervalHourOfYear(new DateTime(2006, 1, 1, 1, 0, 0)), Is.EqualTo(0), "End of interval 0");
            Assert.That(Core.OpenStudio.Query.IntervalHourOfYear(new DateTime(2006, 1, 2, 0, 0, 0)), Is.EqualTo(23), "24:00 ends interval 23");
            Assert.That(Core.OpenStudio.Query.IntervalHourOfYear(new DateTime(2006, 12, 31, 23, 0, 0)), Is.EqualTo(8758));
            Assert.That(Core.OpenStudio.Query.IntervalHourOfYear(new DateTime(2007, 1, 1, 0, 0, 0)), Is.EqualTo(8759), "Dec 31 24:00 (normalised to 1 Jan) ends interval 8759");
        }

        [Test]
        public void TimeIndexDictionary_LiveEdgeRows_NoThrow_NoCollapsedTimestamps()
        {
            // Mirrors the live eplusout.sql Time table: sub-hourly hour-0 rows, hour-24 rows
            // and the duplicate hour-boundary rows EnergyPlus writes per reporting frequency.
            DataTable dataTable = new DataTable();
            dataTable.Columns.Add("TimeIndex", typeof(int));
            dataTable.Columns.Add("Year", typeof(int));
            dataTable.Columns.Add("Month", typeof(int));
            dataTable.Columns.Add("Day", typeof(int));
            dataTable.Columns.Add("Hour", typeof(int));
            dataTable.Columns.Add("Minute", typeof(int));
            dataTable.Columns.Add("Dst", typeof(int));
            dataTable.Columns.Add("EnvironmentPeriodIndex", typeof(int));

            void Row(int timeIndex, int hour, int minute)
            {
                dataTable.Rows.Add(timeIndex, 2006, 1, 1, hour, minute, 0, 1);
            }

            Row(1, 0, 10);
            Row(2, 0, 20);
            Row(3, 0, 30);
            Row(4, 0, 40);
            Row(5, 0, 50);
            Row(6, 1, 0);
            Row(7, 1, 0); // duplicate hour-boundary row (timestep + hourly frequencies)
            Row(8, 24, 0);

            List<string> diagnostics = new List<string>();
            SortedDictionary<int, DateTime> result = Core.OpenStudio.Query.TimeIndexDictionary(dataTable, 1, 2017, diagnostics);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(8), "Every TimeIndex survives — duplicates are keyed by index, never collapsed");
            Assert.That(result[1], Is.EqualTo(new DateTime(2006, 1, 1, 0, 10, 0)));
            Assert.That(result[6], Is.EqualTo(new DateTime(2006, 1, 1, 1, 0, 0)));
            Assert.That(result[8], Is.EqualTo(new DateTime(2006, 1, 2, 0, 0, 0)), "Hour 24 rolls into the next day");
            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public void TimeIndexDictionary_MalformedRows_SkippedWithDiagnostics()
        {
            DataTable dataTable = new DataTable();
            dataTable.Columns.Add("TimeIndex", typeof(int));
            dataTable.Columns.Add("Year", typeof(int));
            dataTable.Columns.Add("Month", typeof(int));
            dataTable.Columns.Add("Day", typeof(int));
            dataTable.Columns.Add("Hour", typeof(int));
            dataTable.Columns.Add("Minute", typeof(int));
            dataTable.Columns.Add("Dst", typeof(int));
            dataTable.Columns.Add("EnvironmentPeriodIndex", typeof(int));

            dataTable.Rows.Add(1, 2017, 2, 29, 12, 0, 0, 1); // Feb 29 in a non-leap calendar
            dataTable.Rows.Add(2, 2006, 1, 1, 1, 0, 0, 1);

            List<string> diagnostics = new List<string>();
            SortedDictionary<int, DateTime> result = Core.OpenStudio.Query.TimeIndexDictionary(dataTable, 1, 2017, diagnostics);

            Assert.That(result.Count, Is.EqualTo(1), "The malformed row is skipped, the valid row kept");
            Assert.That(diagnostics.Count, Is.EqualTo(1));
            Assert.That(diagnostics[0], Does.Contain("TimeIndex 1"));
        }

        [Test]
        public void CreateDesignDays_SizingEnvironment_Reads24HoursAndClassifies()
        {
            string sqlPath = CreateDesignDaySql("c5_designdays.sql");

            List<DesignDay> designDays = Create.DesignDays(sqlPath, out List<string> diagnostics);

            Assert.That(designDays, Is.Not.Null);
            Assert.That(designDays.Count, Is.EqualTo(2), "Only the two sizing environments — the annual environment is not a design day");

            DesignDay heating = designDays.FirstOrDefault(x => x.Name.IndexOf("HTG", StringComparison.OrdinalIgnoreCase) >= 0);
            DesignDay cooling = designDays.FirstOrDefault(x => x.Name.IndexOf("CLG", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.That(heating, Is.Not.Null);
            Assert.That(cooling, Is.Not.Null);
            Assert.That(heating.Month, Is.EqualTo((byte)1));
            Assert.That(heating.Day, Is.EqualTo((byte)21));
            Assert.That(heating[Weather.WeatherDataType.DryBulbTemperature, 0], Is.EqualTo(-10.0).Within(1e-9));
            Assert.That(heating[Weather.WeatherDataType.DryBulbTemperature, 23], Is.EqualTo(-9.77).Within(1e-9), "Interval-end 24:00 maps to hourly index 23");
            Assert.That(cooling[Weather.WeatherDataType.DryBulbTemperature, 13], Is.EqualTo(33.0).Within(1e-9), "Interval-end 14:00 maps to hourly index 13");
        }

        [Test]
        public void CreateDesignDays_AnnualOnlySql_NoThrow_NoDesignDays_StructuredDiagnostic()
        {
            // The reproduction fixture: an annual-only SQL whose Time table carries the hour-0
            // sub-hourly rows that threw the un-representable DateTime exception.
            string sqlPath = CreateAnnualOnlySql("c5_annual_only.sql");

            List<DesignDay> designDays = null;
            List<string> diagnostics = null;
            Assert.DoesNotThrow(() => designDays = Create.DesignDays(sqlPath, out diagnostics));
            Assert.That(designDays, Is.Not.Null);
            Assert.That(designDays.Count, Is.EqualTo(0), "An annual environment is not a design day");
            Assert.That(diagnostics.Any(d => d.Contains("No design-day environments")), Is.True, "The empty result is explained");
        }

        [Test]
        public void CreateDesignDays_RealSqlReproductionFixture_NoThrow()
        {
            // Human-validation fixture (not committed — 30 MB): the annual-only eplusout.sql
            // whose hour-0 sub-hourly and hour-24 Time rows threw the un-representable
            // DateTime exception in Grasshopper. Drop the file at tests/resources/sql or set
            // SAM_OPENSTUDIO_TEST_SQL to run this gate.
            string sqlPath = System.Environment.GetEnvironmentVariable("SAM_OPENSTUDIO_TEST_SQL");
            if (string.IsNullOrWhiteSpace(sqlPath))
            {
                string candidate = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "resources", "sql", "eplusout.sql"));
                if (File.Exists(candidate))
                {
                    sqlPath = candidate;
                }
            }

            if (string.IsNullOrWhiteSpace(sqlPath) || !File.Exists(sqlPath))
            {
                Assert.Ignore("Real SQL fixture not present (set SAM_OPENSTUDIO_TEST_SQL or tests/resources/sql/eplusout.sql)");
            }

            List<DesignDay> designDays = null;
            List<string> diagnostics = null;
            Assert.DoesNotThrow(() => designDays = Create.DesignDays(sqlPath, out diagnostics), "The live reproduction fixture must never throw the un-representable DateTime exception");
            Assert.That(designDays, Is.Not.Null);
            TestContext.Out.WriteLine($"Design days: {designDays.Count}; diagnostics: {string.Join(" | ", diagnostics)}");
        }

        /// <summary>
        /// Synthetic SQL with a heating and a cooling sizing environment (EnvironmentType 1,
        /// Year = 0, hourly rows ending 01:00…24:00) plus an annual environment that must be
        /// ignored. Dry-bulb values encode the hour so interval mapping is provable.
        /// </summary>
        private static string CreateDesignDaySql(string fileName)
        {
            string sqlPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, fileName);
            if (File.Exists(sqlPath))
            {
                File.Delete(sqlPath);
            }

            using (System.Data.SQLite.SQLiteConnection connection = new System.Data.SQLite.SQLiteConnection("Data Source=" + sqlPath))
            {
                connection.Open();
                using (System.Data.SQLite.SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "CREATE TABLE EnvironmentPeriods (EnvironmentPeriodIndex INTEGER PRIMARY KEY, EnvironmentName TEXT, EnvironmentType INTEGER)";
                    command.ExecuteNonQuery();
                    command.CommandText = "CREATE TABLE Time (TimeIndex INTEGER PRIMARY KEY, Year INTEGER, Month INTEGER, Day INTEGER, Hour INTEGER, Minute INTEGER, Dst INTEGER, EnvironmentPeriodIndex INTEGER)";
                    command.ExecuteNonQuery();
                    command.CommandText = "CREATE TABLE ReportDataDictionary (ReportDataDictionaryIndex INTEGER PRIMARY KEY, KeyValue TEXT, Name TEXT, Units TEXT)";
                    command.ExecuteNonQuery();
                    command.CommandText = "CREATE TABLE ReportData (ReportDataIndex INTEGER PRIMARY KEY, ReportDataDictionaryIndex INTEGER, TimeIndex INTEGER, Value REAL)";
                    command.ExecuteNonQuery();

                    command.CommandText = "INSERT INTO EnvironmentPeriods VALUES (1, 'BOSTON ANN HTG 99.6% CONDNS DB', 1)";
                    command.ExecuteNonQuery();
                    command.CommandText = "INSERT INTO EnvironmentPeriods VALUES (2, 'BOSTON ANN CLG .4% CONDNS DB=>MWB', 2)";
                    command.ExecuteNonQuery();
                    command.CommandText = "INSERT INTO EnvironmentPeriods VALUES (3, 'SAM_RUNPERIOD_ANNUAL', 3)";
                    command.ExecuteNonQuery();
                    command.CommandText = "INSERT INTO ReportDataDictionary VALUES (1, 'Environment', 'Site Outdoor Air Drybulb Temperature', 'C')";
                    command.ExecuteNonQuery();

                    int timeIndex = 0;
                    for (int hour = 1; hour <= 24; hour++)
                    {
                        timeIndex++;
                        command.CommandText = string.Format(System.Globalization.CultureInfo.InvariantCulture, "INSERT INTO Time VALUES ({0}, 0, 1, 21, {1}, 0, 0, 1)", timeIndex, hour);
                        command.ExecuteNonQuery();
                        command.CommandText = string.Format(System.Globalization.CultureInfo.InvariantCulture, "INSERT INTO ReportData VALUES ({0}, 1, {0}, {1})", timeIndex, -10.0 + (hour - 1) * 0.01);
                        command.ExecuteNonQuery();
                    }

                    for (int hour = 1; hour <= 24; hour++)
                    {
                        timeIndex++;
                        command.CommandText = string.Format(System.Globalization.CultureInfo.InvariantCulture, "INSERT INTO Time VALUES ({0}, 0, 7, 21, {1}, 0, 0, 2)", timeIndex, hour);
                        command.ExecuteNonQuery();
                        command.CommandText = string.Format(System.Globalization.CultureInfo.InvariantCulture, "INSERT INTO ReportData VALUES ({0}, 1, {0}, {1})", timeIndex, 20.0 + hour - 1);
                        command.ExecuteNonQuery();
                    }

                    // A few annual rows including the hour-0 sub-hourly edge — must be ignored
                    // with the environment, never parsed into a design day.
                    for (int minute = 10; minute <= 50; minute += 10)
                    {
                        timeIndex++;
                        command.CommandText = string.Format(System.Globalization.CultureInfo.InvariantCulture, "INSERT INTO Time VALUES ({0}, 2006, 1, 1, 0, {1}, 0, 3)", timeIndex, minute);
                        command.ExecuteNonQuery();
                    }
                }
            }

            return sqlPath;
        }

        private static string CreateAnnualOnlySql(string fileName)
        {
            string sqlPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, fileName);
            if (File.Exists(sqlPath))
            {
                File.Delete(sqlPath);
            }

            using (System.Data.SQLite.SQLiteConnection connection = new System.Data.SQLite.SQLiteConnection("Data Source=" + sqlPath))
            {
                connection.Open();
                using (System.Data.SQLite.SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "CREATE TABLE EnvironmentPeriods (EnvironmentPeriodIndex INTEGER PRIMARY KEY, EnvironmentName TEXT, EnvironmentType INTEGER)";
                    command.ExecuteNonQuery();
                    command.CommandText = "CREATE TABLE Time (TimeIndex INTEGER PRIMARY KEY, Year INTEGER, Month INTEGER, Day INTEGER, Hour INTEGER, Minute INTEGER, Dst INTEGER, EnvironmentPeriodIndex INTEGER)";
                    command.ExecuteNonQuery();
                    command.CommandText = "CREATE TABLE ReportDataDictionary (ReportDataDictionaryIndex INTEGER PRIMARY KEY, KeyValue TEXT, Name TEXT, Units TEXT)";
                    command.ExecuteNonQuery();
                    command.CommandText = "CREATE TABLE ReportData (ReportDataIndex INTEGER PRIMARY KEY, ReportDataDictionaryIndex INTEGER, TimeIndex INTEGER, Value REAL)";
                    command.ExecuteNonQuery();

                    command.CommandText = "INSERT INTO EnvironmentPeriods VALUES (1, 'SAM_RUNPERIOD_ANNUAL', 3)";
                    command.ExecuteNonQuery();

                    int timeIndex = 0;
                    for (int minute = 10; minute <= 50; minute += 10)
                    {
                        timeIndex++;
                        command.CommandText = string.Format(System.Globalization.CultureInfo.InvariantCulture, "INSERT INTO Time VALUES ({0}, 2006, 1, 1, 0, {1}, 0, 1)", timeIndex, minute);
                        command.ExecuteNonQuery();
                    }

                    timeIndex++;
                    command.CommandText = string.Format(System.Globalization.CultureInfo.InvariantCulture, "INSERT INTO Time VALUES ({0}, 2006, 1, 1, 24, 0, 0, 1)", timeIndex);
                    command.ExecuteNonQuery();
                }
            }

            return sqlPath;
        }
    }
}
