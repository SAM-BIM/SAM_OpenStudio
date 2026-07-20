// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Core;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnalyticalCreate = SAM.Analytical.Create;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// C3: fenestration, frames and constructions — WindowPropertyFrameAndDivider with pane
    /// geometry, frameless fallback on invalid frame data, composite-frame conductance, opaque
    /// vs glazed doors, internal glazed openings, hole diagnostics with Guid + geometry summary,
    /// unsupported-data diagnostics (shades, blinds, TAS heat-transfer percentages) and an
    /// end-to-end EnergyPlus gate for a window with frame.
    /// </summary>
    [TestFixture]
    public class FenestrationTests
    {
        private static string WeatherPath(string extension)
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "resources", "weather", "USA_MA_Boston-Logan.Intl.AP.725090_TMYx.2004-2018" + extension));
        }

        [Test]
        public void Frame_WindowWithFrameData_CreatesFrameAndDivider_OnPaneGeometry()
        {
            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(apertureConstructionOverride: AnalyticalModelFixtures.CreateFramedWindowConstruction(), includeFrameMaterial: true).ToOpenStudio();

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Model.getWindowPropertyFrameAndDividers().Count, Is.EqualTo(1));

            global::OpenStudio.WindowPropertyFrameAndDivider frameAndDivider = result.Model.getWindowPropertyFrameAndDividers()[0];
            Assert.That(frameAndDivider.frameWidth(), Is.EqualTo(0.05).Within(1e-9));
            Assert.That(frameAndDivider.frameConductance(), Is.EqualTo(0.17 / 0.05).Within(1e-9), "U = λ/t of the single PVC frame layer");
            Assert.That(frameAndDivider.frameSolarAbsorptance(), Is.EqualTo(0.6).Within(1e-9), "1 − ExternalSolarReflectance 0.4");
            Assert.That(frameAndDivider.frameVisibleAbsorptance(), Is.EqualTo(0.6).Within(1e-9));

            global::OpenStudio.SubSurface subSurface = result.Model.getSubSurfaces()[0];
            Assert.That(subSurface.windowPropertyFrameAndDivider().isNull(), Is.False);
            Assert.That(subSurface.grossArea(), Is.EqualTo(1.9 * 1.3).Within(1e-4), "SubSurface polygon is the PANE (frame grows outward per the EnergyPlus convention), not the full 2×1.4 m aperture");
        }

        [Test]
        public void Frame_FramelessAperture_KeepsFullPolygon_NoFrameObject()
        {
            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox().ToOpenStudio();

            Assert.That(result.Model.getWindowPropertyFrameAndDividers().Count, Is.EqualTo(0));
            Assert.That(result.Model.getSubSurfaces()[0].grossArea(), Is.EqualTo(2.8).Within(1e-6));
        }

        [Test]
        public void Frame_InvalidFrameData_FallsBack_WithWarning_NeverWrongGeometry()
        {
            ApertureConstruction apertureConstruction = AnalyticalModelFixtures.CreateFramedWindowConstruction(frameMaterialName: "Frame Missing");
            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(apertureConstructionOverride: apertureConstruction, includeFrameMaterial: true).ToOpenStudio();

            Assert.That(result.Model.getWindowPropertyFrameAndDividers().Count, Is.EqualTo(0), "Missing frame material → frameless fallback");
            Assert.That(result.Model.getSubSurfaces()[0].grossArea(), Is.EqualTo(2.8).Within(1e-6), "Full aperture polygon kept");
            Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OS-CON-002" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning && d.Message.Contains("frameless conversion")), Is.True);
        }

        [Test]
        public void Frame_CompositeFrame_ConductanceFromLayerSum()
        {
            ApertureConstruction apertureConstruction = new ApertureConstruction(new System.Guid("22222222-4444-4444-4444-444444444444"), "Composite Frame Window", ApertureType.Window, new List<ConstructionLayer>
            {
                new ConstructionLayer("Glass", 0.006),
                new ConstructionLayer("Air", 0.012),
                new ConstructionLayer("Glass", 0.006),
            }, new List<ConstructionLayer>
            {
                new ConstructionLayer("Frame PVC", 0.03),
                new ConstructionLayer("Brick", 0.02),
            });
            apertureConstruction.SetValue(ApertureConstructionParameter.DefaultFrameWidth, 0.05);

            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(apertureConstructionOverride: apertureConstruction, includeFrameMaterial: true).ToOpenStudio();

            Assert.That(result.Model.getWindowPropertyFrameAndDividers().Count, Is.EqualTo(1));
            double expectedConductance = 1.0 / (0.03 / 0.17 + 0.02 / 0.84);
            Assert.That(result.Model.getWindowPropertyFrameAndDividers()[0].frameConductance(), Is.EqualTo(expectedConductance).Within(1e-9), "U = 1/Σ(t/λ) over the frame layers");
        }

        [Test]
        public void Doors_OpaqueAndGlazed_AreClassifiedByPaneFamily()
        {
            ApertureConstruction opaqueDoorConstruction = new ApertureConstruction(new System.Guid("22222222-5555-5555-5555-555555555555"), "Opaque Door", ApertureType.Door, new List<ConstructionLayer>
            {
                new ConstructionLayer("Plasterboard", 0.0125),
                new ConstructionLayer("Brick", 0.1),
            });

            OpenStudioConversionResult opaqueResult = AnalyticalModelFixtures.SingleBox(apertureConstructionOverride: opaqueDoorConstruction).ToOpenStudio();
            Assert.That(opaqueResult.Model.getSubSurfaces()[0].subSurfaceType(), Is.EqualTo("Door"));

            ApertureConstruction glazedDoorConstruction = new ApertureConstruction(AnalyticalModelFixtures.WindowConstruction, ApertureType.Door);
            OpenStudioConversionResult glazedResult = AnalyticalModelFixtures.SingleBox(apertureConstructionOverride: glazedDoorConstruction).ToOpenStudio();
            Assert.That(glazedResult.Model.getSubSurfaces()[0].subSurfaceType(), Is.EqualTo("GlassDoor"));
        }

        [Test]
        public void InternalGlazedOpening_IsPairedBetweenAdjacentSpaces()
        {
            OpenStudioConversionResult result = AnalyticalModelFixtures.TwoAdjacentBoxes(sharedWallWindow: true).ToOpenStudio();

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Model.getSubSurfaces().Count, Is.EqualTo(3), "Exterior window (1 side) + internal window (2 sides)");

            List<global::OpenStudio.SubSurface> internalSubSurfaces = result.Model.getSubSurfaces().Where(x => x.adjacentSubSurface().isNull() == false).ToList();
            Assert.That(internalSubSurfaces.Count, Is.EqualTo(2), "The internal aperture produces a paired subsurface on each side");
            Assert.That(internalSubSurfaces[0].construction().isNull(), Is.False, "Paired internal glazing carries a construction");
            Assert.That(internalSubSurfaces[1].construction().isNull(), Is.False);
        }

        [Test]
        public void Holes_AreReported_WithGuidAndGeometrySummary_NeverFabricated()
        {
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster();

            Space space = new Space(new System.Guid("77777777-0000-0000-0000-000000000077"), "Space Hole", new Geometry.Spatial.Point3D(2.5, 2, 1.5));
            space.SetValue(SpaceParameter.Area, 20.0);
            space.SetValue(SpaceParameter.Volume, 60.0);
            space.InternalCondition = AnalyticalModelFixtures.CreateOfficeInternalCondition();
            adjacencyCluster.AddObject(space);

            Geometry.Planar.Polygon2D outer2D = new Geometry.Planar.Polygon2D(new List<Geometry.Planar.Point2D>
            {
                new Geometry.Planar.Point2D(0, 0), new Geometry.Planar.Point2D(5, 0), new Geometry.Planar.Point2D(5, 4), new Geometry.Planar.Point2D(0, 4),
            });
            Geometry.Planar.Polygon2D hole2D = new Geometry.Planar.Polygon2D(new List<Geometry.Planar.Point2D>
            {
                new Geometry.Planar.Point2D(2, 1), new Geometry.Planar.Point2D(3, 1), new Geometry.Planar.Point2D(3, 2), new Geometry.Planar.Point2D(2, 2),
            });
            Geometry.Spatial.Face3D floorWithHole = Geometry.Spatial.Face3D.Create(new Geometry.Spatial.Plane(new Geometry.Spatial.Point3D(0, 0, 0), Geometry.Spatial.Vector3D.WorldZ), outer2D, new List<Geometry.Planar.IClosed2D> { hole2D });

            Geometry.Spatial.Point3D P(double x, double y, double z) => new Geometry.Spatial.Point3D(x, y, z);
            Geometry.Spatial.Face3D F(params Geometry.Spatial.Point3D[] point3Ds) => new Geometry.Spatial.Face3D(new Geometry.Spatial.Polygon3D(point3Ds));

            Panel holedFloor = AnalyticalCreate.Panel(AnalyticalModelFixtures.WallConstruction, PanelType.SlabOnGrade, floorWithHole);
            List<Panel> panels = new List<Panel>
            {
                holedFloor,
                AnalyticalCreate.Panel(AnalyticalModelFixtures.WallConstruction, PanelType.Roof, F(P(0, 0, 3), P(5, 0, 3), P(5, 4, 3), P(0, 4, 3))),
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

            AnalyticalModel analyticalModel = new AnalyticalModel("Holed Floor Model", "C3 hole diagnostic fixture", null, null, adjacencyCluster, AnalyticalModelFixtures.CreateMaterialLibrary(), AnalyticalModelFixtures.CreateProfileLibrary());
            OpenStudioConversionResult result = analyticalModel.ToOpenStudio();

            Core.OpenStudio.OpenStudioDiagnostic diagnostic = result.Diagnostics.FirstOrDefault(d => d.Message.Contains("hole"));
            Assert.That(diagnostic, Is.Not.Null, "A hole must be reported");
            Assert.That(diagnostic.Severity, Is.EqualTo(Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning));
            Assert.That(diagnostic.SamGuid, Is.EqualTo(holedFloor.Guid), "The diagnostic carries the panel Guid");
            Assert.That(diagnostic.Message, Does.Contain("1 m²"), "The diagnostic carries the hole area summary");
            Assert.That(result.Model.getSubSurfaces().Count, Is.EqualTo(0), "Holes are never fabricated into windows");
        }

        [Test]
        public void UnsupportedApertureData_RaisesStructuredDiagnostics()
        {
            ApertureConstruction apertureConstruction = new ApertureConstruction(new System.Guid("22222222-6666-6666-6666-666666666666"), AnalyticalModelFixtures.WindowConstruction, "Pane Adjust Window");
            apertureConstruction.SetValue(ApertureConstructionParameter.PaneAdditionalHeatTransfer, 10.0);

            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(apertureConstructionOverride: apertureConstruction).ToOpenStudio();

            Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OS-CON-002" && d.Message.Contains("Pane additional heat transfer")), Is.True);
        }

        [Test]
        public void BlindMaterial_RaisesDiagnostic_ConvertedAsPlainGlazing()
        {
            MaterialLibrary materialLibrary = AnalyticalModelFixtures.CreateMaterialLibrary();
            TransparentMaterial blindGlass = new TransparentMaterial(new System.Guid("33333333-0000-0000-0000-000000000008"), "Blind Glass", "Glass", "Fixture blind glass", 1.0, 2500, 840);
            blindGlass.SetValue(TransparentMaterialParameter.IsBlind, true);
            blindGlass.SetValue(TransparentMaterialParameter.SolarTransmittance, 0.7);
            blindGlass.SetValue(TransparentMaterialParameter.LightTransmittance, 0.8);
            materialLibrary.Add(blindGlass);

            ApertureConstruction blindConstruction = new ApertureConstruction(new System.Guid("22222222-7777-7777-7777-777777777777"), "Blind Window", ApertureType.Window, new List<ConstructionLayer>
            {
                new ConstructionLayer("Blind Glass", 0.006),
            });

            AdjacencyCluster adjacencyCluster = new AdjacencyCluster();
            Space space = new Space(new System.Guid("77777777-0000-0000-0000-000000000078"), "Space Blind", new Geometry.Spatial.Point3D(2.5, 2, 1.5));
            space.SetValue(SpaceParameter.Area, 20.0);
            space.SetValue(SpaceParameter.Volume, 60.0);
            space.InternalCondition = AnalyticalModelFixtures.CreateOfficeInternalCondition();
            adjacencyCluster.AddObject(space);

            Geometry.Spatial.Point3D P(double x, double y, double z) => new Geometry.Spatial.Point3D(x, y, z);
            Geometry.Spatial.Face3D F(params Geometry.Spatial.Point3D[] point3Ds) => new Geometry.Spatial.Face3D(new Geometry.Spatial.Polygon3D(point3Ds));

            List<Panel> panels = new List<Panel>
            {
                AnalyticalCreate.Panel(AnalyticalModelFixtures.WallConstruction, PanelType.SlabOnGrade, F(P(0, 0, 0), P(5, 0, 0), P(5, 4, 0), P(0, 4, 0))),
                AnalyticalCreate.Panel(AnalyticalModelFixtures.WallConstruction, PanelType.Roof, F(P(0, 0, 3), P(5, 0, 3), P(5, 4, 3), P(0, 4, 3))),
                AnalyticalCreate.Panel(AnalyticalModelFixtures.WallConstruction, PanelType.WallExternal, F(P(0, 0, 0), P(5, 0, 0), P(5, 0, 3), P(0, 0, 3))),
                AnalyticalCreate.Panel(AnalyticalModelFixtures.WallConstruction, PanelType.WallExternal, F(P(0, 0, 0), P(0, 4, 0), P(0, 4, 3), P(0, 0, 3))),
                AnalyticalCreate.Panel(AnalyticalModelFixtures.WallConstruction, PanelType.WallExternal, F(P(0, 4, 0), P(5, 4, 0), P(5, 4, 3), P(0, 4, 3))),
                AnalyticalCreate.Panel(AnalyticalModelFixtures.WallConstruction, PanelType.WallExternal, F(P(5, 0, 0), P(5, 4, 0), P(5, 4, 3), P(5, 0, 3))),
            };

            panels[2].AddAperture(AnalyticalCreate.Aperture(blindConstruction, F(P(1, 0, 0.8), P(3, 0, 0.8), P(3, 0, 2.2), P(1, 0, 2.2))));

            foreach (Panel panel in panels)
            {
                adjacencyCluster.AddObject(panel);
                adjacencyCluster.AddRelation(space, panel);
            }

            AnalyticalModel analyticalModel = new AnalyticalModel("Blind Glass Model", "C3 blind diagnostic fixture", null, null, adjacencyCluster, materialLibrary, AnalyticalModelFixtures.CreateProfileLibrary());
            OpenStudioConversionResult result = analyticalModel.ToOpenStudio();

            Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OS-MAT-002" && d.Message.Contains("blind")), Is.True, "The blind flag must be reported");
            Assert.That(result.Model.getSubSurfaces().Count, Is.EqualTo(1), "Converted as plain glazing");
            Assert.That(result.Model.objects().Count(x => !x.to_Blind().isNull()), Is.EqualTo(0), "No WindowMaterial:Blind is fabricated");
        }

        [Test]
        [Category("Simulation")]
        public void FramedWindow_EndToEnd_EnergyPlusRun()
        {
            string epwPath = WeatherPath(".epw");
            Assert.That(File.Exists(epwPath), Is.True, $"Pinned weather fixture missing: {epwPath}");

            string outputDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "c3_framed_window");
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, true);
            }

            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(apertureConstructionOverride: AnalyticalModelFixtures.CreateFramedWindowConstruction(), includeFrameMaterial: true).ToOpenStudio(epwPath, outputDirectory);

            foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in result.Diagnostics)
            {
                TestContext.Out.WriteLine(diagnostic.ToString());
            }

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.RunResult, Is.Not.Null);
            Assert.That(result.RunResult.ExitCode, Is.EqualTo(0));
            Assert.That(result.RunResult.FatalErrors, Is.Empty);
            Assert.That(result.RunResult.SevereErrors, Is.Empty);
            Assert.That(result.RunResult.Success, Is.True);

            string idf = File.ReadAllText(Path.Combine(outputDirectory, "run", "in.idf"));
            Assert.That(idf, Does.Contain("WindowProperty:FrameAndDivider"), "The frame must reach the IDF");

            using (System.Data.SQLite.SQLiteConnection connection = new System.Data.SQLite.SQLiteConnection("Data Source=" + result.RunResult.SqlPath + ";Read Only=True"))
            {
                connection.Open();
                using (System.Data.SQLite.SQLiteCommand command = connection.CreateCommand())
                {
                    // The EnergyPlus glass area is the PANE area (2.47 m²), not the full
                    // aperture (2.8 m²) — proving the pane+frame conversion (not the frameless
                    // full-polygon path) reached the simulation; the frame object itself is
                    // asserted in the IDF above.
                    command.CommandText = "SELECT SurfaceName, ClassName, Area FROM Surfaces WHERE SurfaceName LIKE '%SUBSURFACE%'";
                    using (System.Data.SQLite.SQLiteDataReader reader = command.ExecuteReader())
                    {
                        Assert.That(reader.Read(), Is.True, "The window must appear in the SQL surfaces table");
                        TestContext.Out.WriteLine($"Window row: {reader.GetValue(0)} | {reader.GetValue(1)} | {reader.GetValue(2)}");
                        Assert.That(reader.GetDouble(2), Is.EqualTo(1.9 * 1.3).Within(0.01), "EnergyPlus glass area must be the pane area");
                    }
                }
            }

            Assert.That(result.Loads, Is.Not.Null);
            Assert.That(result.Loads.TotalHeating + result.Loads.TotalCooling, Is.GreaterThan(0));
        }
    }
}
