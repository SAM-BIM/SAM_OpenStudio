// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// Review P1-03: the SQL hour-of-year computation used a fixed non-leap reference year, so
    /// in a leap-year run every Feb 29 row clamped onto Feb 28 (duplicate hour indices — the
    /// coincident peak double-counts) and every date after February landed one day early.
    /// Synthetic EnergyPlus-schema SQLite exercises the internal reader directly.
    /// </summary>
    [TestFixture]
    public class LeapYearIndexingTests
    {
        private const string VariableName = "Zone Ideal Loads Supply Air Total Heating Energy";
        private const string KeyName = "ZONE LEAP";

        /// <summary>
        /// Minimal EnergyPlus-schema SQL: one hourly variable for one zone over the given
        /// (month, day, hour) rows, all in the annual weather environment (EnvironmentType 3).
        /// Values encode the row order (1-based) so indices can be traced back.
        /// </summary>
        private static string CreateSql(string fileName, List<(int Month, int Day, int Hour)> rows)
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
                    command.CommandText = "CREATE TABLE ReportDataDictionary (ReportDataDictionaryIndex INTEGER PRIMARY KEY, Name TEXT, KeyValue TEXT)";
                    command.ExecuteNonQuery();
                    command.CommandText = "CREATE TABLE ReportData (ReportDataIndex INTEGER PRIMARY KEY, ReportDataDictionaryIndex INTEGER, TimeIndex INTEGER, Value REAL)";
                    command.ExecuteNonQuery();
                    command.CommandText = "CREATE TABLE Time (TimeIndex INTEGER PRIMARY KEY, Month INTEGER, Day INTEGER, Hour INTEGER, EnvironmentPeriodIndex INTEGER)";
                    command.ExecuteNonQuery();
                    command.CommandText = "CREATE TABLE EnvironmentPeriods (EnvironmentPeriodIndex INTEGER PRIMARY KEY, EnvironmentName TEXT, EnvironmentType INTEGER)";
                    command.ExecuteNonQuery();

                    command.CommandText = "INSERT INTO EnvironmentPeriods VALUES (1, 'ANNUAL', 3)";
                    command.ExecuteNonQuery();
                    command.CommandText = "INSERT INTO ReportDataDictionary VALUES (1, @name, @key)";
                    command.Parameters.AddWithValue("@name", VariableName);
                    command.Parameters.AddWithValue("@key", KeyName);
                    command.ExecuteNonQuery();

                    int timeIndex = 0;
                    foreach ((int month, int day, int hour) in rows)
                    {
                        timeIndex++;
                        command.Parameters.Clear();
                        command.CommandText = string.Format(System.Globalization.CultureInfo.InvariantCulture, "INSERT INTO Time VALUES ({0}, {1}, {2}, {3}, 1)", timeIndex, month, day, hour);
                        command.ExecuteNonQuery();
                        command.CommandText = string.Format(System.Globalization.CultureInfo.InvariantCulture, "INSERT INTO ReportData VALUES ({0}, 1, {0}, {1})", timeIndex, (double)timeIndex);
                        command.ExecuteNonQuery();
                    }
                }
            }

            return sqlPath;
        }

        [Test]
        public void LeapYearSql_Feb29GetsOwnHourIndices_NoDuplicates()
        {
            // Feb 28 (24 h) + Feb 29 (24 h) + Mar 1 hour 1 + Dec 31 hour 24 of a leap year.
            List<(int, int, int)> rows = new List<(int, int, int)>();
            for (int hour = 1; hour <= 24; hour++)
            {
                rows.Add((2, 28, hour));
            }

            for (int hour = 1; hour <= 24; hour++)
            {
                rows.Add((2, 29, hour));
            }

            rows.Add((3, 1, 1));
            rows.Add((12, 31, 24));

            string sqlPath = CreateSql("c5_leap_year.sql", rows);
            Dictionary<string, List<KeyValuePair<int, double>>> series = OpenStudioSimulationRunner.ReadHourlyValueSeries(sqlPath, VariableName);

            Assert.That(series, Is.Not.Null);
            Assert.That(series.ContainsKey(KeyName), Is.True);

            List<KeyValuePair<int, double>> points = series[KeyName];
            Assert.That(points.Count, Is.EqualTo(50));

            List<int> indices = points.Select(x => x.Key).ToList();
            Assert.That(indices.Distinct().Count(), Is.EqualTo(50), "Feb 29 must produce its own 24 hour indices — duplicates double-count the coincident peak");
            Assert.That(indices[0], Is.EqualTo(1392), "Feb 28 hour 1 (day-of-year 59)");
            Assert.That(indices[24], Is.EqualTo(1416), "Feb 29 hour 1 (day-of-year 60, leap)");
            Assert.That(indices[48], Is.EqualTo(1440), "Mar 1 hour 1 starts at 1440 in a leap year");
            Assert.That(indices[49], Is.EqualTo(8783), "Dec 31 hour 24 is the 8784th leap-year hour (index 8783)");
        }

        [Test]
        public void NonLeapSql_IndexingUnchanged()
        {
            // No Feb 29 rows → the non-leap calendar applies: Mar 1 follows Feb 28 directly.
            List<(int, int, int)> rows = new List<(int, int, int)>
            {
                (2, 28, 24),
                (3, 1, 1),
                (12, 31, 24),
            };

            string sqlPath = CreateSql("c5_non_leap_year.sql", rows);
            Dictionary<string, List<KeyValuePair<int, double>>> series = OpenStudioSimulationRunner.ReadHourlyValueSeries(sqlPath, VariableName);

            Assert.That(series, Is.Not.Null);
            List<KeyValuePair<int, double>> points = series[KeyName];
            Assert.That(points.Count, Is.EqualTo(3));
            Assert.That(points[0].Key, Is.EqualTo(1415), "Feb 28 hour 24 (day-of-year 59, non-leap)");
            Assert.That(points[1].Key, Is.EqualTo(1416), "Mar 1 hour 1 starts at 1416 in a non-leap year");
            Assert.That(points[2].Key, Is.EqualTo(8759), "Dec 31 hour 24 is the 8760th hour (index 8759)");
        }
    }
}
