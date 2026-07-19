// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using AnalyticalCreate = SAM.Analytical.Create;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// C1: adapter-hardening regression tests — one per fix (review P3 items): culture-safe
    /// conditioned classification, per-(Guid, ProfileType) schedule caching, per-space SpaceType
    /// share keys, story fallback without floor panels, no CLI on conversion errors, no
    /// thermostat on a surfaceless conditioned zone, translation statistics, result disposal,
    /// parameterised SQL extraction and multi-variant object-map behaviour.
    /// </summary>
    [TestFixture]
    public class AdapterHardeningTests
    {
        private static string WeatherPath(string extension)
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "resources", "weather", "USA_MA_Boston-Logan.Intl.AP.725090_TMYx.2004-2018" + extension));
        }

        [Test]
        public void IsConditioned_IsCultureIndependent()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                // tr-TR lowercases 'I' to 'ı' — a culture-sensitive ToLower would turn
                // "UNCONDITIONED" into "uncondıtıoned" and misclassify the space as conditioned.
                CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

                Space unconditioned = new Space("Store");
                unconditioned.InternalCondition = new InternalCondition("UNCONDITIONED STORE");
                Assert.That(unconditioned.IsConditioned(), Is.False, "UNCONDITIONED must stay unconditioned under tr-TR");

                Space external = new Space("Outside");
                external.InternalCondition = new InternalCondition("External Zone");
                Assert.That(external.IsConditioned(), Is.False);

                Space office = new Space("Office");
                office.InternalCondition = new InternalCondition("Office");
                Assert.That(office.IsConditioned(), Is.True);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Test]
        public void ProfileCache_IsKeyedByProfileType()
        {
            double[] values = new double[24];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = 0.5;
            }

            Profile profile = new Profile("Shared Profile", ProfileType.Lighting, values);
            OpenStudioConversionContext context = new OpenStudioConversionContext(null, new global::OpenStudio.Model());

            global::OpenStudio.Schedule fractional = profile.ToOpenStudio(ProfileType.Lighting, context);
            global::OpenStudio.Schedule temperature = profile.ToOpenStudio(ProfileType.Heating, context);
            global::OpenStudio.Schedule fractionalAgain = profile.ToOpenStudio(ProfileType.Lighting, context);

            Assert.That(fractional, Is.Not.Null);
            Assert.That(temperature, Is.Not.Null);
            Assert.That(ReferenceEquals(fractional, temperature), Is.False, "The same profile under a different ProfileType must produce a separate schedule");
            Assert.That(ReferenceEquals(fractional, fractionalAgain), Is.True, "Same profile and type must hit the cache");
            Assert.That(fractional.nameString(), Is.Not.EqualTo(temperature.nameString()));
            Assert.That(context.ScheduleMap.Count, Is.EqualTo(2));
        }

        [Test]
        public void SpaceType_DifferentSpaceDensities_DoNotShare()
        {
            OpenStudioConversionResult shared = AnalyticalModelFixtures.TwoAdjacentBoxes().ToOpenStudio();
            Assert.That(shared.Model.getSpaceTypes().Count, Is.EqualTo(1), "Identical spaces sharing a condition must share one SpaceType");

            OpenStudioConversionResult different = AnalyticalModelFixtures.TwoAdjacentBoxes(spaceBVolume: 30).ToOpenStudio();
            Assert.That(different.Model.getSpaceTypes().Count, Is.EqualTo(2), "Spaces with different computed densities must not share one SpaceType (first-space values are never imposed)");
        }

        [Test]
        public void BuildingStory_FallsBackToSpaceElevation_WithoutFloorPanels()
        {
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster();

            Space space = new Space(new Guid("99999999-0000-0000-0000-000000000099"), "Space NoFloors", new Geometry.Spatial.Point3D(2.5, 2, 1.5));
            space.SetValue(SpaceParameter.Area, 20.0);
            space.SetValue(SpaceParameter.Volume, 60.0);
            space.InternalCondition = AnalyticalModelFixtures.CreateOfficeInternalCondition();
            adjacencyCluster.AddObject(space);

            Geometry.Spatial.Point3D P(double x, double y, double z) => new Geometry.Spatial.Point3D(x, y, z);
            Geometry.Spatial.Face3D F(params Geometry.Spatial.Point3D[] point3Ds) => new Geometry.Spatial.Face3D(new Geometry.Spatial.Polygon3D(point3Ds));

            Panel[] panels = new Panel[]
            {
                AnalyticalCreate.Panel(AnalyticalModelFixtures.WallConstruction, PanelType.WallExternal, F(P(0, 0, 0), P(5, 0, 0), P(5, 4, 0), P(0, 4, 0))),
                AnalyticalCreate.Panel(AnalyticalModelFixtures.WallConstruction, PanelType.WallExternal, F(P(0, 0, 3), P(5, 0, 3), P(5, 4, 3), P(0, 4, 3))),
                AnalyticalCreate.Panel(AnalyticalModelFixtures.WallConstruction, PanelType.WallExternal, F(P(0, 0, 0), P(5, 0, 0), P(5, 0, 3), P(0, 0, 3))),
                AnalyticalCreate.Panel(AnalyticalModelFixtures.WallConstruction, PanelType.WallExternal, F(P(0, 0, 0), P(0, 4, 0), P(0, 4, 3), P(0, 0, 3))),
                AnalyticalCreate.Panel(AnalyticalModelFixtures.WallConstruction, PanelType.WallExternal, F(P(0, 4, 0), P(5, 4, 0), P(5, 4, 3), P(0, 4, 3))),
                AnalyticalCreate.Panel(AnalyticalModelFixtures.WallConstruction, PanelType.WallExternal, F(P(5, 0, 0), P(5, 4, 0), P(5, 4, 3), P(5, 0, 3))),
            };

            foreach (Panel panel in panels)
            {
                adjacencyCluster.AddObject(panel);
                adjacencyCluster.AddRelation(space, panel);
            }

            AnalyticalModel analyticalModel = new AnalyticalModel("No Floor Panels Model", "C1 story fallback fixture", null, null, adjacencyCluster, AnalyticalModelFixtures.CreateMaterialLibrary(), AnalyticalModelFixtures.CreateProfileLibrary());
            OpenStudioConversionResult result = analyticalModel.ToOpenStudio();

            Assert.That(result.Model.getBuildingStorys().Count, Is.EqualTo(1), "A model without floor-group panels must still get a story from the space elevation");
            Assert.That(result.Model.getBuildingStorys()[0].nominalZCoordinate().get(), Is.EqualTo(0).Within(1e-6));
            Assert.That(result.Diagnostics.Any(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Information && d.Message.Contains("space minimum elevations")), Is.True);
        }

        [Test]
        public void Runner_DoesNotRunCli_WhenConversionHasErrors()
        {
            string epwPath = WeatherPath(".epw");
            Assert.That(File.Exists(epwPath), Is.True, $"Pinned weather fixture missing: {epwPath}");

            string outputDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "c1_haserrors_gate");
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, true);
            }

            // DegeneratePanelBox raises a SAM-OS-GEO-001 Error at conversion.
            OpenStudioConversionResult result = AnalyticalModelFixtures.DegeneratePanelBox().ToOpenStudio(epwPath, outputDirectory);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.RunResult, Is.Null, "The CLI must not run when the conversion reported errors");
            Assert.That(result.Loads, Is.Null);
            Assert.That(File.Exists(result.OsmPath), Is.True, "The OSM is still saved for inspection");
            Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OS-RUN-001" && d.Message.Contains("conversion reported errors")), Is.True);
        }

        [Test]
        public void ConditionedSpace_WithoutSurfaces_GetsNoThermostatOrIdealLoads()
        {
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster();

            Space space = new Space(new Guid("99999999-0000-0000-0000-000000000098"), "Space NoPanels", new Geometry.Spatial.Point3D(2.5, 2, 1.5));
            space.SetValue(SpaceParameter.Area, 20.0);
            space.SetValue(SpaceParameter.Volume, 60.0);
            space.InternalCondition = AnalyticalModelFixtures.CreateOfficeInternalCondition();
            adjacencyCluster.AddObject(space);

            AnalyticalModel analyticalModel = new AnalyticalModel("No Panels Model", "C1 surfaceless conditioned zone fixture", null, null, adjacencyCluster, AnalyticalModelFixtures.CreateMaterialLibrary(), AnalyticalModelFixtures.CreateProfileLibrary());
            OpenStudioConversionResult result = analyticalModel.ToOpenStudio();

            Assert.That(result.IsValid, Is.False, "A conditioned zone without surfaces must invalidate the conversion");
            Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OS-HVAC-001" && d.Message.Contains("no valid surfaces")), Is.True);
            Assert.That(result.Model.getThermostatSetpointDualSetpoints().Count, Is.EqualTo(0));
            Assert.That(result.Model.getZoneHVACIdealLoadsAirSystems().Count, Is.EqualTo(0));
        }

        [Test]
        public void TranslationStatistics_ArePopulated_AndSnapshotted()
        {
            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox().ToOpenStudio();

            Core.OpenStudio.OpenStudioConversionStatistics statistics = result.Statistics;
            Assert.That(statistics, Is.Not.Null);
            Assert.That(statistics.SourceObjects, Is.EqualTo(8), "1 space + 6 panels + 1 aperture");
            Assert.That(statistics.CreatedObjects, Is.GreaterThan(0));
            Assert.That(statistics.SkippedObjects, Is.EqualTo(0));
            Assert.That(statistics.ErrorCount, Is.EqualTo(0));

            int createdAtSnapshot = statistics.CreatedObjects;

            OpenStudioConversionResult degenerate = AnalyticalModelFixtures.DegeneratePanelBox().ToOpenStudio();
            Assert.That(degenerate.Statistics.SkippedObjects, Is.GreaterThan(0), "The skipped degenerate panel must be counted");
            Assert.That(degenerate.Statistics.ErrorCount, Is.GreaterThan(0));
            Assert.That(statistics.CreatedObjects, Is.EqualTo(createdAtSnapshot), "The snapshot must not change with later conversions");
        }

        [Test]
        public void ConversionResult_Dispose_ReleasesModel_Idempotently()
        {
            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox().ToOpenStudio();
            global::OpenStudio.Model model = result.Model;

            result.Dispose();
            Assert.DoesNotThrow(() => result.Dispose(), "Dispose must be idempotent");
            Assert.That(() => model.getSpaces(), Throws.Exception, "The owned model must be unusable after disposal");
        }

        [Test]
        public void CreateModel_LoadsThroughVersionTranslator()
        {
            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox().ToOpenStudio();

            string osmPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "c1_version_translator.osm");
            if (File.Exists(osmPath))
            {
                File.Delete(osmPath);
            }

            Assert.That(result.Model.save(global::OpenStudio.OpenStudioUtilitiesCore.toPath(osmPath), true), Is.True);

            global::OpenStudio.Model reloaded = Core.OpenStudio.Create.Model(osmPath);
            Assert.That(reloaded, Is.Not.Null, "VersionTranslator load must succeed");
            Assert.That(reloaded.getSpaces().Count, Is.EqualTo(result.Model.getSpaces().Count));
        }

        [Test]
        public void ReadAnnualEnergy_HandlesKeysAndNamesWithQuotes()
        {
            string sqlPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "c1_parameterised_sql.sql");
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
                    command.CommandText = "CREATE TABLE ReportData (ReportDataIndex INTEGER PRIMARY KEY, ReportDataDictionaryIndex INTEGER, Value REAL)";
                    command.ExecuteNonQuery();
                    command.CommandText = "INSERT INTO ReportDataDictionary VALUES (1, @name, @key)";
                    command.Parameters.AddWithValue("@name", "Zone Ideal Loads Supply Air Total Heating Energy");
                    command.Parameters.AddWithValue("@key", "ZONE O'BRIEN");
                    command.ExecuteNonQuery();
                    command.CommandText = "INSERT INTO ReportData VALUES (1, 1, 3600000.0)";
                    command.ExecuteNonQuery();
                    command.CommandText = "INSERT INTO ReportData VALUES (2, 1, 7200000.0)";
                    command.ExecuteNonQuery();
                }
            }

            System.Collections.Generic.Dictionary<string, double> result = OpenStudioSimulationRunner.ReadAnnualEnergy(sqlPath, "Zone Ideal Loads Supply Air Total Heating Energy");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result["ZONE O'BRIEN"], Is.EqualTo(3.0).Within(1e-9), "J → kWh conversion: 10 800 000 J = 3 kWh");
        }

        [Test]
        public void ObjectMap_ConstructionVariants_RegisterOncePerSamGuid()
        {
            OpenStudioConversionContext context = new OpenStudioConversionContext(AnalyticalModelFixtures.SingleBox(), new global::OpenStudio.Model());

            global::OpenStudio.Construction forward = AnalyticalModelFixtures.WallConstruction.ToOpenStudio(true, context);
            global::OpenStudio.Construction reverse = AnalyticalModelFixtures.WallConstruction.ToOpenStudio(false, context);

            Assert.That(forward, Is.Not.Null);
            Assert.That(reverse, Is.Not.Null);
            Assert.That(context.ConstructionMap.Count, Is.EqualTo(2), "Forward and reverse variants coexist in the variant-aware cache");
            Assert.That(context.References.References.Count(x => x.SamGuid == AnalyticalModelFixtures.WallConstruction.Guid), Is.EqualTo(1), "Both variants trace back to one SAM Guid; the first registration wins and is never overwritten");
        }
    }
}
