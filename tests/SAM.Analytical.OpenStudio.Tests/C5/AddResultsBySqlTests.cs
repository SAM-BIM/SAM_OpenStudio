// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// Human-Rhino validation, fix 3: SAMAnalytical.AddResultsBySQL returned no space/panel
    /// results — the annual family was never read (only design-day ZoneSizes data was mapped),
    /// surface results were discarded whenever no space result carried a load time index, and
    /// EnergyPlus names (SAM_&lt;type&gt;_&lt;name&gt;_&lt;guid8&gt;) were matched against full
    /// 32-character Guids, so nothing ever related back to a SAM space or panel. Synthetic
    /// EnergyPlus-schema SQL proves the mapping end to end: per-space results, per-surface
    /// results with panel identity, internal-panel two-surface handling, zero-vs-missing,
    /// rerun deduplication, annual/design-day environment separation and SAM serialization.
    /// </summary>
    [TestFixture]
    public class AddResultsBySqlTests
    {
        /// <param name="hostSurfaceIndexes">SurfaceIndex -> BaseSurfaceIndex for subsurface rows; a surface not listed points at itself, like an EnergyPlus base surface.</param>
        private static string CreateResultsSql(string fileName, IReadOnlyList<(string ZoneName, string IdealLoadsKey, double FloorArea, double Volume)> zones, IReadOnlyList<(string SurfaceName, double Area, int ZoneIndex)> surfaces, double heatingPeakJoules, double coolingPeakJoules, bool withDesignDayEnvironment, double? zeroCoolingZone = null, IReadOnlyDictionary<int, int> hostSurfaceIndexes = null)
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
                    Exec("CREATE TABLE Zones (ZoneIndex INTEGER PRIMARY KEY, ZoneName TEXT, FloorArea REAL, Volume REAL)");
                    Exec("CREATE TABLE Surfaces (SurfaceIndex INTEGER PRIMARY KEY, SurfaceName TEXT, Area REAL, ZoneIndex INTEGER, BaseSurfaceIndex INTEGER)");
                    Exec("CREATE TABLE ZoneSizes (ZoneName TEXT, LoadType TEXT, CalcDesLoad REAL, DesDayName TEXT, PeakHrMin TEXT, PeakTemp REAL, PeakHumRat REAL)");
                    Exec("CREATE TABLE NominalLighting (ZoneIndex INTEGER, DesignLevel REAL)");
                    Exec("CREATE TABLE NominalInfiltration (ZoneIndex INTEGER, DesignLevel REAL)");
                    Exec("CREATE TABLE NominalElectricEquipment (ZoneIndex INTEGER, DesignLevel REAL)");

                    Exec("INSERT INTO EnvironmentPeriods VALUES (1, 'SAM_RUNPERIOD_ANNUAL', 3)");
                    if (withDesignDayEnvironment)
                    {
                        Exec("INSERT INTO EnvironmentPeriods VALUES (2, 'BOSTON ANN HTG 99.6% CONDNS DB', 1)");
                        Exec("INSERT INTO EnvironmentPeriods VALUES (3, 'BOSTON ANN CLG .4% CONDNS DB=>MWB', 2)");
                    }

                    int timeIndex = 0;
                    int reportDataIndex = 0;
                    for (int hour = 1; hour <= 24; hour++)
                    {
                        timeIndex++;
                        Exec(string.Format(System.Globalization.CultureInfo.InvariantCulture, "INSERT INTO Time VALUES ({0}, 2006, 1, 1, {1}, 0, 0, 1)", timeIndex, hour));
                    }

                    int annualTimeIndexCount = timeIndex;
                    if (withDesignDayEnvironment)
                    {
                        for (int hour = 1; hour <= 24; hour++)
                        {
                            timeIndex++;
                            Exec(string.Format(System.Globalization.CultureInfo.InvariantCulture, "INSERT INTO Time VALUES ({0}, 0, 1, 21, {1}, 0, 0, 2)", timeIndex, hour));
                        }

                        for (int hour = 1; hour <= 24; hour++)
                        {
                            timeIndex++;
                            Exec(string.Format(System.Globalization.CultureInfo.InvariantCulture, "INSERT INTO Time VALUES ({0}, 0, 7, 21, {1}, 0, 0, 3)", timeIndex, hour));
                        }
                    }

                    int dictionaryIndex = 0;
                    void Variable(string key, string name, string units)
                    {
                        dictionaryIndex++;
                        Exec(string.Format(System.Globalization.CultureInfo.InvariantCulture, "INSERT INTO ReportDataDictionary VALUES ({0}, '{1}', '{2}', '{3}')", dictionaryIndex, key, name, units));
                    }

                    void Values(string name, string key, double v1, double vPeak, int peakHour, int environmentOffset)
                    {
                        for (int hour = 1; hour <= 24; hour++)
                        {
                            reportDataIndex++;
                            double value = hour == peakHour ? vPeak : v1;
                            Exec(string.Format(System.Globalization.CultureInfo.InvariantCulture, "INSERT INTO ReportData VALUES ({0}, (SELECT ReportDataDictionaryIndex FROM ReportDataDictionary WHERE KeyValue = '{1}' AND Name = '{2}'), {3}, {4})", reportDataIndex, key, name, environmentOffset + hour, value));
                        }
                    }

                    for (int i = 0; i < zones.Count; i++)
                    {
                        (string zoneName, string idealLoadsKey, double floorArea, double volume) = zones[i];
                        Exec(string.Format(System.Globalization.CultureInfo.InvariantCulture, "INSERT INTO Zones VALUES ({0}, '{1}', {2}, {3})", i + 1, zoneName, floorArea, volume));

                        Variable(idealLoadsKey, "Zone Ideal Loads Supply Air Total Heating Energy", "J");
                        Variable(idealLoadsKey, "Zone Ideal Loads Supply Air Total Cooling Energy", "J");
                        Variable(zoneName, "Zone Heating Setpoint Not Met Time", "hr");
                        Variable(zoneName, "Zone Cooling Setpoint Not Met Time", "hr");

                        // Heating peaks at hour 10, cooling at hour 15 — except the designated
                        // zero-cooling zone, whose cooling series is a genuine all-zero.
                        double coolingPeak = zeroCoolingZone.HasValue && i == zones.Count - 1 && zeroCoolingZone.Value == 0 ? 0 : coolingPeakJoules;
                        Values("Zone Ideal Loads Supply Air Total Heating Energy", idealLoadsKey, heatingPeakJoules / 100.0, heatingPeakJoules, 10, 0);
                        Values("Zone Ideal Loads Supply Air Total Cooling Energy", idealLoadsKey, coolingPeak > 0 ? coolingPeak / 100.0 : 0, coolingPeak, 15, 0);
                        Values("Zone Heating Setpoint Not Met Time", zoneName, 0, 1, 12, 0);
                        Values("Zone Cooling Setpoint Not Met Time", zoneName, 0, zeroCoolingZone.HasValue && i == zones.Count - 1 ? 0 : 2, 16, 0);

                        if (withDesignDayEnvironment)
                        {
                            Exec(string.Format(System.Globalization.CultureInfo.InvariantCulture, "INSERT INTO ZoneSizes VALUES ('{0}', 'Heating', {1}, 'BOSTON ANN HTG 99.6% CONDNS DB', ' 1/21 10:00', -10.0, 0.001)", zoneName, heatingPeakJoules / 3600.0));
                            Exec(string.Format(System.Globalization.CultureInfo.InvariantCulture, "INSERT INTO ZoneSizes VALUES ('{0}', 'Cooling', {1}, 'BOSTON ANN CLG .4% CONDNS DB=>MWB', ' 7/21 15:00', 33.0, 0.01)", zoneName, coolingPeakJoules / 3600.0));
                        }
                    }

                    for (int i = 0; i < surfaces.Count; i++)
                    {
                        int surfaceIndex = i + 1;
                        int baseSurfaceIndex = surfaceIndex;
                        if (hostSurfaceIndexes != null && hostSurfaceIndexes.TryGetValue(surfaceIndex, out int hostSurfaceIndex))
                        {
                            baseSurfaceIndex = hostSurfaceIndex;
                        }

                        Exec(string.Format(System.Globalization.CultureInfo.InvariantCulture, "INSERT INTO Surfaces VALUES ({0}, '{1}', {2}, {3}, {4})", surfaceIndex, surfaces[i].SurfaceName, surfaces[i].Area, surfaces[i].ZoneIndex, baseSurfaceIndex));
                    }
                }
            }

            return sqlPath;
        }

        private static string ZoneName(Space space)
        {
            return Core.OpenStudio.Query.OpenStudioName("ThermalZone", space.Name, space.Guid).ToUpperInvariant();
        }

        private static string IdealLoadsKey(Space space)
        {
            return Core.OpenStudio.Query.OpenStudioName("IdealLoads", space.Name, space.Guid).ToUpperInvariant();
        }

        private static string SurfaceName(Panel panel, int spaceIndex)
        {
            return Core.OpenStudio.Query.OpenStudioName("Surface", string.Format("{0}_{1}", panel.Name, spaceIndex), panel.Guid).ToUpperInvariant();
        }

        private static string SubSurfaceName(Aperture aperture, int spaceIndex)
        {
            return Core.OpenStudio.Query.OpenStudioName("SubSurface", string.Format("{0}_{1}", aperture.Name, spaceIndex), aperture.Guid).ToUpperInvariant();
        }

        [Test]
        public void OneZone_SpaceAndPanelResults_AttachedAndRelated()
        {
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.SingleBox();
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster(analyticalModel.AdjacencyCluster);
            Space space = adjacencyCluster.GetSpaces().Single();
            List<Panel> panels = adjacencyCluster.GetPanels();

            string sqlPath = CreateResultsSql(
                "c5_addresults_onezone.sql",
                new[] { (ZoneName(space), IdealLoadsKey(space), 20.0, 60.0) },
                panels.Select((p, i) => (SurfaceName(p, 0), 20.0 - i, 1)).ToArray(),
                3_600_000.0, 7_200_000.0, false);

            List<Result> results = Modify.AddResults(adjacencyCluster, sqlPath, out List<string> diagnostics);

            List<Result> spaceResults = results.FindAll(x => x is SpaceSimulationResult);
            List<Result> panelResults = results.FindAll(x => x is SurfaceSimulationResult);
            Assert.That(spaceResults.Count, Is.EqualTo(2), "Heating + cooling per-LoadType space results: " + string.Join(" | ", diagnostics));
            Assert.That(panelResults.Count, Is.EqualTo(panels.Count), "One surface result per engine surface");

            foreach (Result result in spaceResults)
            {
                List<SpaceSimulationResult> related = adjacencyCluster.GetResults<SpaceSimulationResult>(space);
                Assert.That(related.Any(x => x.Guid == result.Guid), Is.True, "Space relation attached for " + result.Name);
            }

            foreach (Panel panel in panels)
            {
                List<SurfaceSimulationResult> related = adjacencyCluster.GetResults<SurfaceSimulationResult>(panel);
                Assert.That(related, Is.Not.Null.And.Count.EqualTo(1), $"Panel {panel.Name} keeps its own surface result (Guid mapping, not display name)");
            }

            SpaceSimulationResult heating = spaceResults.Cast<SpaceSimulationResult>().Single(x => x.TryGetValue(Analytical.SpaceSimulationResultParameter.LoadType, out string loadType) && loadType == LoadType.Heating.ToString());
            Assert.That(heating.TryGetValue(Analytical.SpaceSimulationResultParameter.Load, out double load), Is.True);
            Assert.That(load, Is.GreaterThan(0), "Peak heating load present [W]");
            Assert.That(heating.TryGetValue(Analytical.SpaceSimulationResultParameter.LoadIndex, out double loadIndex), Is.True);
            Assert.That(loadIndex, Is.EqualTo(9), "Peak at hour 10 -> interval index 9");
            Assert.That(heating.TryGetValue(Analytical.SpaceSimulationResultParameter.UnmetHours, out double unmet), Is.True);
            Assert.That(unmet, Is.EqualTo(1.0).Within(1e-9));

            Assert.That(diagnostics.Count(d => d.Contains("matched no SAM")), Is.EqualTo(0), "Every SQL name resolved: " + string.Join(" | ", diagnostics));
        }

        [Test]
        public void SubSurfaceResults_AttachToHostPanel()
        {
            // Human-Rhino validation: every window row of the live eplusout.sql
            // (SAM_SUBSURFACE_<name>_<guid8>) was reported as "matched no SAM panel; left
            // unattached" — the EnergyPlus Surfaces table lists subsurfaces alongside base
            // surfaces, and a subsurface carries the APERTURE Guid, which no panel Guid can
            // ever equal. The window result belongs to the panel hosting the aperture.
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.SingleBox();
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster(analyticalModel.AdjacencyCluster);
            Space space = adjacencyCluster.GetSpaces().Single();
            List<Panel> panels = adjacencyCluster.GetPanels();
            Panel glazedPanel = panels.Single(x => x.HasApertures);
            Aperture aperture = glazedPanel.Apertures.Single();

            List<(string SurfaceName, double Area, int ZoneIndex)> surfaces = panels.Select((p, i) => (SurfaceName(p, 0), 20.0 - i, 1)).ToList();
            surfaces.Add((SubSurfaceName(aperture, 0), 2.64, 1));

            string sqlPath = CreateResultsSql(
                "c5_addresults_subsurface.sql",
                new[] { (ZoneName(space), IdealLoadsKey(space), 20.0, 60.0) },
                surfaces,
                3_600_000.0, 7_200_000.0, false);

            List<Result> results = Modify.AddResults(adjacencyCluster, sqlPath, out List<string> diagnostics);

            Assert.That(diagnostics.Where(d => d.Contains("matched no SAM panel")), Is.Empty, "A window row is not an orphan: " + string.Join(" | ", diagnostics));

            List<SurfaceSimulationResult> glazedResults = adjacencyCluster.GetResults<SurfaceSimulationResult>(glazedPanel);
            Assert.That(glazedResults, Is.Not.Null.And.Count.EqualTo(2), "The host panel keeps its own opaque surface result AND the hosted window result");
            Assert.That(glazedResults.Any(x => x.Name == SubSurfaceName(aperture, 0)), Is.True, "The window result is related to the panel hosting the aperture, its SQL identity preserved");

            // Never leaks onto a neighbouring panel: only the host receives it.
            foreach (Panel panel in panels.Where(x => !x.HasApertures))
            {
                Assert.That(adjacencyCluster.GetResults<SurfaceSimulationResult>(panel), Is.Not.Null.And.Count.EqualTo(1), $"Panel {panel.Name} keeps only its own surface result");
            }

            Assert.That(results.Count(x => x is SurfaceSimulationResult), Is.EqualTo(panels.Count + 1), "One result per engine surface, subsurfaces included");
        }

        [Test]
        public void SubSurfaceResult_UnknownAperture_StillResolvesThroughItsHostSurface()
        {
            // Second Rhino validation round: one window of twelve still reported "matched no SAM
            // panel" because its aperture Guid was not in the cluster the results were attached
            // to (apertures are re-created - trimmed, merged, re-hosted - so a Guid that existed
            // at conversion time need not survive). EnergyPlus records the base surface each
            // subsurface sits in, and THAT name carries the panel Guid, so the window resolves
            // regardless of what happened to the aperture.
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.SingleBox();
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster(analyticalModel.AdjacencyCluster);
            Space space = adjacencyCluster.GetSpaces().Single();
            List<Panel> panels = adjacencyCluster.GetPanels();
            Panel glazedPanel = panels.Single(x => x.HasApertures);

            // A subsurface whose Guid suffix belongs to no SAM object at all, hosted by the
            // glazed panel's engine surface (SurfaceIndex 1 in the list below).
            int hostIndex = panels.IndexOf(glazedPanel) + 1;
            List<(string SurfaceName, double Area, int ZoneIndex)> surfaces = panels.Select((p, i) => (SurfaceName(p, 0), 20.0 - i, 1)).ToList();
            surfaces.Add(("SAM_SUBSURFACE_ORPHANED_GLZ_0_6F64626E", 2.64, 1));

            string sqlPath = CreateResultsSql(
                "c5_addresults_orphaned_subsurface.sql",
                new[] { (ZoneName(space), IdealLoadsKey(space), 20.0, 60.0) },
                surfaces,
                3_600_000.0, 7_200_000.0, false,
                hostSurfaceIndexes: new Dictionary<int, int> { { surfaces.Count, hostIndex } });

            Modify.AddResults(adjacencyCluster, sqlPath, out List<string> diagnostics);

            Assert.That(diagnostics.Where(d => d.Contains("matched no SAM panel")), Is.Empty, "The host surface resolves the window even with an unknown aperture: " + string.Join(" | ", diagnostics));

            List<SurfaceSimulationResult> glazedResults = adjacencyCluster.GetResults<SurfaceSimulationResult>(glazedPanel);
            Assert.That(glazedResults, Is.Not.Null.And.Count.EqualTo(2), "The host panel keeps its own surface result AND the orphaned window");
            SurfaceSimulationResult window = glazedResults.Single(x => x.Name == "SAM_SUBSURFACE_ORPHANED_GLZ_0_6F64626E");
            Assert.That(window.TryGetValue(SurfaceSimulationResultParameter.HostSurfaceName, out string hostSurfaceName), Is.True);
            Assert.That(hostSurfaceName, Is.EqualTo(SurfaceName(glazedPanel, 0)), "The EnergyPlus host link is recorded on the result");
        }

        [Test]
        public void Results_Reference_CarriesTheMatchedSamGuid()
        {
            // Requested after the Rhino review: a consumer holding only the result list must be
            // able to map back to the model, so Reference carries the SAM Space / Panel Guid.
            // The engine identity it replaces stays available as parameters.
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.SingleBox();
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster(analyticalModel.AdjacencyCluster);
            Space space = adjacencyCluster.GetSpaces().Single();
            List<Panel> panels = adjacencyCluster.GetPanels();

            string sqlPath = CreateResultsSql(
                "c5_addresults_reference.sql",
                new[] { (ZoneName(space), IdealLoadsKey(space), 20.0, 60.0) },
                panels.Select((p, i) => (SurfaceName(p, 0), 20.0 - i, 1)).ToArray(),
                3_600_000.0, 7_200_000.0, true);

            List<Result> results = Modify.AddResults(adjacencyCluster, sqlPath, out List<string> diagnostics);

            // Both space families - annual (named after the SAM space) and design-day (named
            // after the EnergyPlus zone) - now reference the same SAM Space Guid.
            List<SpaceSimulationResult> spaceResults = results.OfType<SpaceSimulationResult>().ToList();
            Assert.That(spaceResults, Is.Not.Empty);
            Assert.That(spaceResults.Select(x => x.Name).Distinct().Count(), Is.EqualTo(2), "The two families keep their own names (SAM space vs EnergyPlus zone)");
            Assert.That(spaceResults.All(x => x.Reference == space.Guid.ToString("N")), Is.True, "Every space result references the SAM Space Guid: " + string.Join(" | ", spaceResults.Select(x => x.Name + "=" + x.Reference)));

            SpaceSimulationResult designDayResult = spaceResults.First(x => x.TryGetValue(Analytical.SpaceSimulationResultParameter.DesignLoad, out double _));
            Assert.That(designDayResult.TryGetValue(SpaceSimulationResultParameter.ZoneIndex, out int zoneIndex), Is.True, "The SQL ZoneIndex survives the Reference change");
            Assert.That(zoneIndex, Is.EqualTo(1));
            Assert.That(designDayResult.TryGetValue(SpaceSimulationResultParameter.ZoneName, out string zoneName), Is.True);
            Assert.That(zoneName, Is.EqualTo(ZoneName(space)));

            foreach (Panel panel in panels)
            {
                SurfaceSimulationResult surfaceResult = adjacencyCluster.GetResults<SurfaceSimulationResult>(panel).Single();
                Assert.That(surfaceResult.Reference, Is.EqualTo(panel.Guid.ToString("N")), $"Panel {panel.Name} result references the SAM Panel Guid");
                Assert.That(surfaceResult.TryGetValue(SurfaceSimulationResultParameter.SurfaceIndex, out int surfaceIndex), Is.True, "The SQL SurfaceIndex survives the Reference change");
                Assert.That(surfaceIndex, Is.EqualTo(panels.IndexOf(panel) + 1));
            }

            // Rebuilding through JSON must not lose anything already asserted elsewhere.
            SpaceSimulationResult annual = spaceResults.First(x => x.TryGetValue(Analytical.SpaceSimulationResultParameter.LoadIndex, out double _));
            Assert.That(annual.Source, Is.EqualTo(Query.Source()), "Source survives");
            Assert.That(annual.TryGetValue(Analytical.SpaceSimulationResultParameter.UnmetHours, out double unmet), Is.True, "Parameters survive");
            Assert.That(unmet, Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void TwoZones_InternalPanels_KeepSourceIdentity()
        {
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.TwoAdjacentBoxes();
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster(analyticalModel.AdjacencyCluster);
            List<Space> spaces = adjacencyCluster.GetSpaces();
            Space spaceA = spaces.Single(x => x.Name == "Space A");
            Space spaceB = spaces.Single(x => x.Name == "Space B");
            List<Panel> panels = adjacencyCluster.GetPanels();
            Panel sharedWall = panels.Single(x => x.PanelType == PanelType.WallInternal);

            // The shared wall appears as TWO engine surfaces (one per zone), like EnergyPlus
            // writes it; everything else appears once.
            List<(string SurfaceName, double Area, int ZoneIndex)> surfaces = new List<(string, double, int)>();
            foreach (Panel panel in panels)
            {
                if (panel == sharedWall)
                {
                    surfaces.Add((SurfaceName(panel, 0), 12.0, 1));
                    surfaces.Add((SurfaceName(panel, 1), 12.0, 2));
                }
                else
                {
                    surfaces.Add((SurfaceName(panel, 0), 10.0, adjacencyCluster.GetPanels(spaceA).Any(x => x.Guid == panel.Guid) ? 1 : 2));
                }
            }

            string sqlPath = CreateResultsSql(
                "c5_addresults_twozones.sql",
                new[] { (ZoneName(spaceA), IdealLoadsKey(spaceA), 20.0, 60.0), (ZoneName(spaceB), IdealLoadsKey(spaceB), 20.0, 60.0) },
                surfaces,
                3_600_000.0, 7_200_000.0, false);

            List<Result> results = Modify.AddResults(adjacencyCluster, sqlPath, out List<string> diagnostics);

            foreach (Space space in spaces)
            {
                List<SpaceSimulationResult> related = adjacencyCluster.GetResults<SpaceSimulationResult>(space);
                Assert.That(related, Is.Not.Null.And.Count.EqualTo(2), $"Space '{space.Name}' receives its own heating+cooling results");
            }

            List<SurfaceSimulationResult> sharedWallResults = adjacencyCluster.GetResults<SurfaceSimulationResult>(sharedWall);
            Assert.That(sharedWallResults, Is.Not.Null.And.Count.EqualTo(2), "An internal panel represented by two engine surfaces receives two results, never a collision");
            Assert.That(sharedWallResults.All(x => x.Reference == sharedWall.Guid.ToString("N")), Is.True, "Both engine surfaces reference the one SAM panel they belong to");
            Assert.That(sharedWallResults.Select(x => x.GetValue<int>(SurfaceSimulationResultParameter.SurfaceIndex)).Distinct().Count(), Is.EqualTo(2), "Engine-surface identity is retained per result (SQL SurfaceIndex), values never summed");

            // No result related to the wrong space: B-side results must not relate to A.
            List<SpaceSimulationResult> resultsA = adjacencyCluster.GetResults<SpaceSimulationResult>(spaceA);
            Assert.That(resultsA.All(x => x.Reference == spaceA.Guid.ToString("N")), Is.True, "Space A carries only its own results");
        }

        [Test]
        public void DuplicateSanitizedNames_GuidMappingStaysCorrect()
        {
            // Two spaces whose names sanitize identically; only the Guid suffix separates them.
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.TwoAdjacentBoxes();
            AdjacencyCluster renamedCluster = new AdjacencyCluster(analyticalModel.AdjacencyCluster);
            List<Space> renamedSpaces = renamedCluster.GetSpaces();
            Space spaceA = renamedSpaces.Single(x => x.Name == "Space A");
            Space spaceB = renamedSpaces.Single(x => x.Name == "Space B");
            spaceB.Name = "Space A";

            string sqlPath = CreateResultsSql(
                "c5_addresults_dupnames.sql",
                new[] { (ZoneName(spaceA), IdealLoadsKey(spaceA), 20.0, 60.0), (ZoneName(spaceB), IdealLoadsKey(spaceB), 20.0, 60.0) },
                Array.Empty<(string, double, int)>(),
                3_600_000.0, 7_200_000.0, false);

            Modify.AddResults(renamedCluster, sqlPath, out List<string> diagnostics);

            foreach (Space space in renamedSpaces)
            {
                List<SpaceSimulationResult> related = renamedCluster.GetResults<SpaceSimulationResult>(space);
                Assert.That(related, Is.Not.Null.And.Count.EqualTo(2), $"Guid mapping holds under duplicate display names ({space.Guid.ToString("N").Substring(0, 8)})");
                Assert.That(related.All(x => x.Reference == space.Guid.ToString("N")), Is.True);
            }
        }

        [Test]
        public void ZeroResult_IsValid_MissingResult_IsDiagnosed()
        {
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.TwoAdjacentBoxes();
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster(analyticalModel.AdjacencyCluster);
            List<Space> spaces = adjacencyCluster.GetSpaces();
            Space spaceA = spaces.Single(x => x.Name == "Space A");
            Space spaceB = spaces.Single(x => x.Name == "Space B");

            // Only zone A is in the SQL; B is genuinely missing (not zero).
            string sqlPath = CreateResultsSql(
                "c5_addresults_zeromissing.sql",
                new[] { (ZoneName(spaceA), IdealLoadsKey(spaceA), 20.0, 60.0) },
                Array.Empty<(string, double, int)>(),
                3_600_000.0, 0.0, false, 0.0);

            List<Result> results = Modify.AddResults(adjacencyCluster, sqlPath, out List<string> diagnostics);

            SpaceSimulationResult cooling = adjacencyCluster.GetResults<SpaceSimulationResult>(spaceA).Single(x => x.TryGetValue(Analytical.SpaceSimulationResultParameter.LoadType, out string loadType) && loadType == LoadType.Cooling.ToString());
            Assert.That(cooling.TryGetValue(Analytical.SpaceSimulationResultParameter.Load, out double load), Is.True);
            Assert.That(load, Is.EqualTo(0.0), "A genuine zero peak stays a valid result");

            Assert.That(adjacencyCluster.GetResults<SpaceSimulationResult>(spaceB), Is.Null.Or.Empty, "Space B is not in the SQL — no fabricated result");
            Assert.That(diagnostics.Any(d => d.Contains("No simulation result found for space") && d.Contains(spaceB.Guid.ToString("N").Substring(0, 8).ToUpperInvariant())), Is.True, "The missing result is diagnosed: " + string.Join(" | ", diagnostics));
        }

        [Test]
        public void Rerun_NoDuplicateResults()
        {
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.SingleBox();
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster(analyticalModel.AdjacencyCluster);
            Space space = adjacencyCluster.GetSpaces().Single();
            List<Panel> panels = adjacencyCluster.GetPanels();

            string sqlPath = CreateResultsSql(
                "c5_addresults_rerun.sql",
                new[] { (ZoneName(space), IdealLoadsKey(space), 20.0, 60.0) },
                panels.Select((p, i) => (SurfaceName(p, 0), 20.0 - i, 1)).ToArray(),
                3_600_000.0, 7_200_000.0, false);

            Modify.AddResults(adjacencyCluster, sqlPath, out _);
            Modify.AddResults(adjacencyCluster, sqlPath, out _);

            Assert.That(adjacencyCluster.GetResults<SpaceSimulationResult>().Count, Is.EqualTo(2), "Rerunning must not duplicate space results");
            Assert.That(adjacencyCluster.GetResults<SurfaceSimulationResult>().Count, Is.EqualTo(panels.Count), "Rerunning must not duplicate surface results");
        }

        [Test]
        public void DesignDayPlusAnnualSql_AnnualResultsExcludeSizingPeriods()
        {
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.SingleBox();
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster(analyticalModel.AdjacencyCluster);
            Space space = adjacencyCluster.GetSpaces().Single();

            string sqlPath = CreateResultsSql(
                "c5_addresults_designday.sql",
                new[] { (ZoneName(space), IdealLoadsKey(space), 20.0, 60.0) },
                Array.Empty<(string, double, int)>(),
                3_600_000.0, 7_200_000.0, true);

            Modify.AddResults(adjacencyCluster, sqlPath, out List<string> diagnostics);

            List<SpaceSimulationResult> related = adjacencyCluster.GetResults<SpaceSimulationResult>(space);
            Assert.That(related, Is.Not.Null.And.Count.EqualTo(4), "Annual heating/cooling + design-day heating/cooling: " + string.Join(" | ", related.Select(x => x.Name)));

            // The annual family peaks come from the annual environment only (hour 10), never
            // from the sizing environments — the C5 reader filters EnvironmentType 3.
            SpaceSimulationResult annualHeating = related.Single(x => x.TryGetValue(Analytical.SpaceSimulationResultParameter.LoadType, out string lt) && lt == LoadType.Heating.ToString() && x.TryGetValue(Analytical.SpaceSimulationResultParameter.LoadIndex, out _));
            Assert.That(annualHeating.TryGetValue(Analytical.SpaceSimulationResultParameter.LoadIndex, out double loadIndex), Is.True);
            Assert.That(loadIndex, Is.EqualTo(9), "Annual peak hour is not contaminated by the design-day environments");

            // The design-day family carries the ZoneSizes design load.
            SpaceSimulationResult designDayHeating = related.Single(x => x.TryGetValue(Analytical.SpaceSimulationResultParameter.LoadType, out string lt) && lt == LoadType.Heating.ToString() && x.TryGetValue(Analytical.SpaceSimulationResultParameter.DesignLoad, out _));
            Assert.That(designDayHeating.TryGetValue(Analytical.SpaceSimulationResultParameter.DesignLoad, out double designLoad), Is.True);
            Assert.That(designLoad, Is.EqualTo(1000.0).Within(1e-6), "ZoneSizes design load mapped (3.6 MJ / 3600 s)");
        }

        [Test]
        public void SaveReload_ResultsStayAttached()
        {
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.SingleBox();
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster(analyticalModel.AdjacencyCluster);
            Space space = adjacencyCluster.GetSpaces().Single();
            List<Panel> panels = adjacencyCluster.GetPanels();

            string sqlPath = CreateResultsSql(
                "c5_addresults_savereload.sql",
                new[] { (ZoneName(space), IdealLoadsKey(space), 20.0, 60.0) },
                panels.Select((p, i) => (SurfaceName(p, 0), 20.0 - i, 1)).ToArray(),
                3_600_000.0, 7_200_000.0, false);

            Modify.AddResults(adjacencyCluster, sqlPath, out _);
            AnalyticalModel withResults = new AnalyticalModel(analyticalModel, adjacencyCluster);

            Assert.That(withResults.AdjacencyCluster.GetResults<SpaceSimulationResult>()?.Count ?? 0, Is.EqualTo(2), "Space results attached before serialization");
            Assert.That(withResults.GetResults<SpaceSimulationResult>(withResults.AdjacencyCluster.GetSpaces().Single())?.Count ?? 0, Is.EqualTo(2), "Relations attached before serialization");

            string json = Core.Convert.ToString(withResults);
            AnalyticalModel reloaded = Core.Convert.ToSAM<AnalyticalModel>(json)?.FirstOrDefault();
            Assert.That(reloaded, Is.Not.Null, "The model round-trips through SAM JSON");

            Space reloadedSpace = reloaded.AdjacencyCluster.GetSpaces().Single();
            Assert.That(reloaded.AdjacencyCluster.GetResults<SpaceSimulationResult>(reloadedSpace), Is.Not.Null.And.Count.EqualTo(2), "Space results survive serialization");
            Assert.That(reloaded.AdjacencyCluster.GetResults<SurfaceSimulationResult>().Count, Is.EqualTo(panels.Count), "Surface results survive serialization");
        }

        [Test]
        public void RealSqlFixture_SpaceAndSurfaceResults_NoThrow()
        {
            // Human-validation fixture (not committed): the annual-only eplusout.sql that
            // produced empty spaceSimulationResults/panelSimulationResults in Grasshopper.
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

            AnalyticalModel analyticalModel = AnalyticalModelFixtures.SingleBox();
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster(analyticalModel.AdjacencyCluster);

            List<Result> results = null;
            List<string> diagnostics = null;
            Assert.DoesNotThrow(() => results = Modify.AddResults(adjacencyCluster, sqlPath, out diagnostics));

            Assert.That(results, Is.Not.Null);
            Assert.That(results.Count(x => x is SurfaceSimulationResult), Is.EqualTo(6), "The fixture's six engine surfaces produce surface results (the base list must survive an annual-only SQL)");
            Assert.That(results.Count(x => x is SpaceSimulationResult), Is.EqualTo(0), "The fixture model does not correspond to the SQL — nothing is fabricated");
            Assert.That(diagnostics.Any(d => d.Contains("matched no SAM space")), Is.True, "The unmatched SQL zone is named");
            TestContext.Out.WriteLine(string.Join("\n", diagnostics));
        }

        [Test]
        public void ComponentPath_AnalyticalModelInput_IsNotMutated()
        {
            // The Grasshopper contract: the input model's own cluster must stay result-free.
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.SingleBox();
            AdjacencyCluster clonedCluster = new AdjacencyCluster(analyticalModel.AdjacencyCluster);
            Space space = clonedCluster.GetSpaces().Single();

            string sqlPath = CreateResultsSql(
                "c5_addresults_nonmutating.sql",
                new[] { (ZoneName(space), IdealLoadsKey(space), 20.0, 60.0) },
                Array.Empty<(string, double, int)>(),
                3_600_000.0, 7_200_000.0, false);

            Modify.AddResults(clonedCluster, sqlPath, out _);
            AnalyticalModel returned = new AnalyticalModel(analyticalModel, clonedCluster);

            Assert.That(analyticalModel.AdjacencyCluster.GetResults<SpaceSimulationResult>(), Is.Null.Or.Empty, "The source model was not mutated");
            Assert.That(returned.AdjacencyCluster.GetResults<SpaceSimulationResult>().Count, Is.EqualTo(2), "The returned model carries the results");
        }
    }
}
