// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// F-S11-1: zone-level and enclosure report keys (ThermalZone / Space names) are remapped onto
    /// the Ideal Loads energy keys by the identity of the generated name ("&lt;SanitizedName&gt;_&lt;GuidFirst8&gt;"
    /// after SAM_&lt;Type&gt;_), not by the 8-character Guid suffix alone. Two rooms whose Guids share the
    /// first 8 hex digits (the signed-off s08-fix-two FixA/FixB, 5080c000) must keep their own values;
    /// an absent or ambiguous identity is never guessed onto a room and never overwrites one.
    /// Synthetic EnergyPlus-schema SQL, offline.
    /// </summary>
    [TestFixture]
    public class ZoneResultIdentityTests
    {
        private static readonly Guid fixA = Guid.Parse("5080c000-0000-0000-0000-00000000c0a1");
        private static readonly Guid fixB = Guid.Parse("5080c000-0000-0000-0000-00000000c0b1");
        private static readonly Guid other = Guid.Parse("6181d111-0000-0000-0000-00000000c0b1");

        private const string HeatingEnergy = "Zone Ideal Loads Supply Air Total Heating Energy";
        private const string CoolingEnergy = "Zone Ideal Loads Supply Air Total Cooling Energy";
        private const string HeatingNotMet = "Zone Heating Setpoint Not Met Time";
        private const string CoolingNotMet = "Zone Cooling Setpoint Not Met Time";
        private const string People = "Zone People Total Heating Energy";
        private const string Lights = "Zone Lights Total Heating Energy";
        private const string Equipment = "Zone Electric Equipment Total Heating Energy";
        private const string WindowSolar = "Enclosure Windows Total Transmitted Solar Radiation Energy";
        private const string Infiltration = "Zone Infiltration Sensible Heat Gain Energy";
        private const string AirTemperature = "Zone Mean Air Temperature";
        private const string OperativeTemperature = "Zone Operative Temperature";
        private const string RelativeHumidity = "Zone Air Relative Humidity";

        private static string Name(string type, string name, Guid guid)
        {
            // EnergyPlus reports keys uppercased.
            return Core.OpenStudio.Query.OpenStudioName(type, name, guid).ToUpperInvariant();
        }

        /// <summary>Writes one 24-hour annual run period; each (key, variable) series is constant at its value.</summary>
        private static string CreateSql(string fileName, IEnumerable<(string Key, string Variable, double Value)> series)
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
                    void Exec(string sql)
                    {
                        command.CommandText = sql;
                        command.ExecuteNonQuery();
                    }

                    Exec("CREATE TABLE EnvironmentPeriods (EnvironmentPeriodIndex INTEGER PRIMARY KEY, EnvironmentName TEXT, EnvironmentType INTEGER)");
                    Exec("CREATE TABLE Time (TimeIndex INTEGER PRIMARY KEY, Year INTEGER, Month INTEGER, Day INTEGER, Hour INTEGER, Minute INTEGER, Dst INTEGER, EnvironmentPeriodIndex INTEGER)");
                    Exec("CREATE TABLE ReportDataDictionary (ReportDataDictionaryIndex INTEGER PRIMARY KEY, KeyValue TEXT, Name TEXT, Units TEXT)");
                    Exec("CREATE TABLE ReportData (ReportDataIndex INTEGER PRIMARY KEY, ReportDataDictionaryIndex INTEGER, TimeIndex INTEGER, Value REAL)");
                    Exec("INSERT INTO EnvironmentPeriods VALUES (1, 'SAM_RUNPERIOD_ANNUAL', 3)");
                    for (int hour = 1; hour <= 24; hour++)
                    {
                        Exec(string.Format(System.Globalization.CultureInfo.InvariantCulture, "INSERT INTO Time VALUES ({0}, 2006, 1, 1, {0}, 0, 0, 1)", hour));
                    }

                    int dictionaryIndex = 0;
                    int reportDataIndex = 0;
                    foreach ((string key, string variable, double value) in series)
                    {
                        dictionaryIndex++;
                        Exec(string.Format(System.Globalization.CultureInfo.InvariantCulture, "INSERT INTO ReportDataDictionary VALUES ({0}, '{1}', '{2}', '')", dictionaryIndex, key, variable));
                        for (int hour = 1; hour <= 24; hour++)
                        {
                            reportDataIndex++;
                            Exec(string.Format(System.Globalization.CultureInfo.InvariantCulture, "INSERT INTO ReportData VALUES ({0}, {1}, {2}, {3})", reportDataIndex, dictionaryIndex, hour, value));
                        }
                    }
                }
            }

            return sqlPath;
        }

        /// <summary>Every per-zone variable the result set normalises, for one generated room; values scale with <paramref name="scale"/>.</summary>
        private static IEnumerable<(string, string, double)> Room(string name, Guid guid, double scale)
        {
            string idealLoads = Name("IdealLoads", name, guid);
            string thermalZone = Name("ThermalZone", name, guid);
            string space = Name("Space", name, guid);

            yield return (idealLoads, HeatingEnergy, 1000 * scale);
            yield return (idealLoads, CoolingEnergy, 500 * scale);
            // Whole hours: a not-met hour in 1 of every 24 timesteps of value 'scale' gives 24 * scale hours per day.
            yield return (thermalZone, HeatingNotMet, scale);
            yield return (thermalZone, CoolingNotMet, 2 * scale);
            yield return (thermalZone, People, 10 * scale);
            yield return (thermalZone, Lights, 20 * scale);
            yield return (thermalZone, Equipment, 30 * scale);
            yield return (space, WindowSolar, 40 * scale);
            yield return (thermalZone, Infiltration, 50 * scale);
            yield return (thermalZone, AirTemperature, 18 + scale);
            yield return (thermalZone, OperativeTemperature, 19 + scale);
            yield return (thermalZone, RelativeHumidity, 40 + scale);
        }

        private static OpenStudioSimulationResultSet Extract(string sqlPath)
        {
            OpenStudioLoadSummary loadSummary = OpenStudioSimulationRunner.ExtractLoads(sqlPath, out string failureDetail);
            Assert.That(loadSummary, Is.Not.Null, failureDetail);

            OpenStudioSimulationResultSet result = OpenStudioSimulationRunner.ExtractResultSet(sqlPath, loadSummary, true, 0, 0, 0, 0);
            Assert.That(result, Is.Not.Null);
            return result;
        }

        private static void AssertRoom(OpenStudioSimulationResultSet results, string name, Guid guid, double scale)
        {
            string key = Name("IdealLoads", name, guid);
            string label = name + " (" + guid.ToString("N").Substring(0, 8) + ")";

            Assert.That(results.UnmetHeatingHours[key], Is.EqualTo(24 * scale), label + " heating not-met");
            Assert.That(results.UnmetCoolingHours[key], Is.EqualTo(48 * scale), label + " cooling not-met");
            Assert.That(results.PeopleGains[key], Is.EqualTo(Core.OpenStudio.Query.JoulesToKilowattHours(24 * 10 * scale)).Within(1e-12), label + " people");
            Assert.That(results.LightingGains[key], Is.EqualTo(Core.OpenStudio.Query.JoulesToKilowattHours(24 * 20 * scale)).Within(1e-12), label + " lights");
            Assert.That(results.EquipmentGains[key], Is.EqualTo(Core.OpenStudio.Query.JoulesToKilowattHours(24 * 30 * scale)).Within(1e-12), label + " equipment");
            Assert.That(results.WindowSolarGains[key], Is.EqualTo(Core.OpenStudio.Query.JoulesToKilowattHours(24 * 40 * scale)).Within(1e-12), label + " window solar (Space key)");
            Assert.That(results.InfiltrationGains[key], Is.EqualTo(Core.OpenStudio.Query.JoulesToKilowattHours(24 * 50 * scale)).Within(1e-12), label + " infiltration");
            Assert.That(results.TemperatureSeries[key], Is.All.EqualTo(18 + scale), label + " air temperature");
            Assert.That(results.OperativeTemperatureSeries[key], Is.All.EqualTo(19 + scale), label + " operative temperature");
            Assert.That(results.RelativeHumiditySeries[key], Is.All.EqualTo(40 + scale), label + " relative humidity");
        }

        private static Space Space(string name, Guid guid)
        {
            return new Space(guid, name, new Geometry.Spatial.Point3D(0, 0, 1));
        }

        [Test]
        public void SharedGuidPrefix_FixAFixB_EachRoomKeepsItsOwnValues()
        {
            string sqlPath = CreateSql("zone_identity_shared_prefix.sql", Room("FixA", fixA, 1).Concat(Room("FixB", fixB, 3)));
            OpenStudioSimulationResultSet results = Extract(sqlPath);

            Assert.That(results.AnnualHeatingEnergy.Count, Is.EqualTo(2));
            AssertRoom(results, "FixA", fixA, 1);
            AssertRoom(results, "FixB", fixB, 3);
            Assert.That(results.UnmetHeatingHours.Count, Is.EqualTo(2), "no report key left behind or duplicated");

            // Through the SAM mapping: each space carries its own not-met hours.
            List<SpaceSimulationResult> spaceResults = results.ToSAM_SpaceSimulationResults(new List<Space> { Space("FixA", fixA), Space("FixB", fixB) });
            Assert.That(spaceResults.Count, Is.EqualTo(4));
            foreach ((Guid guid, double scale) in new[] { (fixA, 1.0), (fixB, 3.0) })
            {
                SpaceSimulationResult heating = spaceResults.Single(x => x.Reference == guid.ToString("N") && x.LoadType() == LoadType.Heating);
                Assert.That(heating.TryGetValue(Analytical.SpaceSimulationResultParameter.UnmetHours, out double unmet), Is.True);
                Assert.That(unmet, Is.EqualTo(24 * scale));
            }
        }

        [Test]
        public void UniquePrefixes_AndDuplicateDisplayNames_MapAsBefore()
        {
            string sqlPath = CreateSql("zone_identity_unique_prefix.sql", Room("Office", fixA, 1).Concat(Room("Office", other, 2)));
            OpenStudioSimulationResultSet results = Extract(sqlPath);

            AssertRoom(results, "Office", fixA, 1);
            AssertRoom(results, "Office", other, 2);
        }

        [Test]
        public void SanitizedUnderscoreAndEmptyNames_SharedPrefix_Match()
        {
            // "Office 04" sanitizes to Office_04; "Office_04_A" keeps its underscores; an empty name is just the Guid prefix.
            string sqlPath = CreateSql("zone_identity_sanitized.sql", Room("Office 04", fixA, 1).Concat(Room("Office_04_A", fixB, 2)).Concat(Room(string.Empty, Guid.Parse("5080c000-0000-0000-0000-0000000000e1"), 4)));
            OpenStudioSimulationResultSet results = Extract(sqlPath);

            Assert.That(Name("IdealLoads", string.Empty, fixA), Is.EqualTo("SAM_IDEALLOADS_5080C000"));
            AssertRoom(results, "Office 04", fixA, 1);
            AssertRoom(results, "Office_04_A", fixB, 2);
            AssertRoom(results, string.Empty, Guid.Parse("5080c000-0000-0000-0000-0000000000e1"), 4);
        }

        [Test]
        public void SuffixOnlyMatch_IsNotGuessedOntoAnotherRoom()
        {
            // A zone with no Ideal Loads (unconditioned) shares FixA's Guid prefix; the old suffix match
            // overwrote FixA's not-met hours with it.
            List<(string, string, double)> series = Room("FixA", fixA, 1).ToList();
            series.Add((Name("ThermalZone", "Store", fixB), HeatingNotMet, 9));
            series.Add(("RADIANT ENCLOSURE 1", WindowSolar, 9));
            OpenStudioSimulationResultSet results = Extract(CreateSql("zone_identity_unmatched.sql", series));

            AssertRoom(results, "FixA", fixA, 1);
            Assert.That(results.UnmetHeatingHours[Name("ThermalZone", "Store", fixB)], Is.EqualTo(24 * 9), "an unmatched key passes through unchanged");
            Assert.That(results.WindowSolarGains.ContainsKey("RADIANT ENCLOSURE 1"), Is.True, "a non-SAM key passes through unchanged");
        }

        [Test]
        public void AmbiguousIdentity_LeavesKeysUnmapped_NeverOverwrites()
        {
            // Same sanitized name AND same Guid prefix: OpenStudio de-duplicates the second name with " 1".
            string idealLoads = Name("IdealLoads", "FixA", fixA);
            string thermalZone = Name("ThermalZone", "FixA", fixA);
            OpenStudioSimulationResultSet results = Extract(CreateSql("zone_identity_ambiguous.sql", new List<(string, string, double)>
            {
                (idealLoads, HeatingEnergy, 1000),
                (idealLoads + " 1", HeatingEnergy, 3000),
                (thermalZone, HeatingNotMet, 1),
                (thermalZone + " 1", HeatingNotMet, 3),
            }));

            Assert.That(results.UnmetHeatingHours.ContainsKey(idealLoads), Is.False, "an ambiguous identity is never assigned to a room");
            Assert.That(results.UnmetHeatingHours.ContainsKey(idealLoads + " 1"), Is.False);
            Assert.That(results.UnmetHeatingHours[thermalZone], Is.EqualTo(24));
            Assert.That(results.UnmetHeatingHours[thermalZone + " 1"], Is.EqualTo(72));
        }

        [Test]
        public void TwoReportKeysOnOneRoom_NeitherOverwritesTheOther()
        {
            string idealLoads = Name("IdealLoads", "FixA", fixA);
            OpenStudioSimulationResultSet results = Extract(CreateSql("zone_identity_collision.sql", new List<(string, string, double)>
            {
                (idealLoads, HeatingEnergy, 1000),
                (Name("ThermalZone", "FixA", fixA), HeatingNotMet, 1),
                (Name("Space", "FixA", fixA), HeatingNotMet, 2),
            }));

            Assert.That(results.UnmetHeatingHours.ContainsKey(idealLoads), Is.False);
            Assert.That(results.UnmetHeatingHours[Name("ThermalZone", "FixA", fixA)], Is.EqualTo(24));
            Assert.That(results.UnmetHeatingHours[Name("Space", "FixA", fixA)], Is.EqualTo(48));
        }
    }
}
