// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// R4: shading, unsupported objects and approximations. Every category the reverse coverage
    /// manifest declares as Unsupported or Approximated must be able to emit its diagnostic —
    /// otherwise "nothing is silently dropped" is a claim rather than a property.
    /// </summary>
    [TestFixture]
    public class ImportDiagnosticsTests
    {
        /// <summary>
        /// A minimal model with one space and one outdoor wall, built through the OpenStudio API
        /// so each test can attach exactly the object under test.
        /// </summary>
        private static global::OpenStudio.Model BuildModel(out global::OpenStudio.Space space, out global::OpenStudio.Surface wall)
        {
            global::OpenStudio.Model model = new global::OpenStudio.Model();

            space = new global::OpenStudio.Space(model);
            space.setName("Room");

            global::OpenStudio.ThermalZone thermalZone = new global::OpenStudio.ThermalZone(model);
            thermalZone.setName("Zone");
            space.setThermalZone(thermalZone);

            wall = AddSurface(model, space, "Wall", "Outdoors", new[] { new[] { 0.0, 0.0, 0.0 }, new[] { 4.0, 0.0, 0.0 }, new[] { 4.0, 0.0, 2.7 }, new[] { 0.0, 0.0, 2.7 } });
            return model;
        }

        private static global::OpenStudio.Surface AddSurface(global::OpenStudio.Model model, global::OpenStudio.Space space, string surfaceType, string boundaryCondition, double[][] vertices)
        {
            global::OpenStudio.Surface surface = new global::OpenStudio.Surface(ToVector(vertices), model);
            surface.setSpace(space);
            surface.setSurfaceType(surfaceType);
            surface.setOutsideBoundaryCondition(boundaryCondition);
            return surface;
        }

        private static global::OpenStudio.Point3dVector ToVector(double[][] vertices)
        {
            global::OpenStudio.Point3dVector result = new global::OpenStudio.Point3dVector();
            foreach (double[] vertex in vertices)
            {
                result.Add(new global::OpenStudio.Point3d(vertex[0], vertex[1], vertex[2]));
            }

            return result;
        }

        private static List<Core.OpenStudio.OpenStudioDiagnostic> Import(global::OpenStudio.Model model, out OpenStudioImportResult result)
        {
            result = model.ToSAM();
            foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in result.Diagnostics)
            {
                TestContext.Out.WriteLine(diagnostic.ToString());
            }

            return new List<Core.OpenStudio.OpenStudioDiagnostic>(result.Diagnostics);
        }

        private static void AssertHasCode(IEnumerable<Core.OpenStudio.OpenStudioDiagnostic> diagnostics, string code)
        {
            Assert.That(diagnostics.Any(x => x.Code == code), Is.True, $"Expected a {code} diagnostic");
        }

        [Test]
        public void SiteShading_ImportsAsShadePanelsCarryingTheirGroup()
        {
            global::OpenStudio.Space space;
            global::OpenStudio.Surface wall;
            using (global::OpenStudio.Model model = BuildModel(out space, out wall))
            {
                global::OpenStudio.ShadingSurfaceGroup shadingSurfaceGroup = new global::OpenStudio.ShadingSurfaceGroup(model);
                shadingSurfaceGroup.setName("Site Shades");
                shadingSurfaceGroup.setShadingSurfaceType("Site");

                global::OpenStudio.ShadingSurface shadingSurface = new global::OpenStudio.ShadingSurface(ToVector(new[] { new[] { 0.0, -1.0, 3.0 }, new[] { 4.0, -1.0, 3.0 }, new[] { 4.0, -2.0, 3.0 }, new[] { 0.0, -2.0, 3.0 } }), model);
                shadingSurface.setName("Canopy");
                shadingSurface.setShadingSurfaceGroup(shadingSurfaceGroup);

                OpenStudioImportResult result;
                Import(model, out result);

                List<Panel> shades = result.AnalyticalModel.AdjacencyCluster.GetPanels().FindAll(x => x.PanelType == PanelType.Shade);
                Assert.That(shades.Count, Is.EqualTo(1));

                string groupType;
                Assert.That(shades[0].TryGetValue(OpenStudioSourceParameter.ShadingGroupType, out groupType), Is.True);
                Assert.That(groupType, Is.EqualTo("Site"));

                string groupName;
                Assert.That(shades[0].TryGetValue(OpenStudioSourceParameter.ShadingGroupName, out groupName), Is.True);
                Assert.That(groupName, Is.EqualTo("Site Shades"));

                Assert.That(shades[0].GetFace3D().GetArea(), Is.EqualTo(4.0).Within(0.01), "The shade must keep its real geometry");
            }
        }

        [Test]
        public void SpaceShading_IsTransformedByTheOwningSpace()
        {
            global::OpenStudio.Space space;
            global::OpenStudio.Surface wall;
            using (global::OpenStudio.Model model = BuildModel(out space, out wall))
            {
                // A space that is NOT at the origin: the shade's coordinates are relative to its
                // group, which is relative to the space, so both transformations must compose.
                space.setXOrigin(100);

                global::OpenStudio.ShadingSurfaceGroup shadingSurfaceGroup = new global::OpenStudio.ShadingSurfaceGroup(model);
                shadingSurfaceGroup.setName("Space Shades");
                shadingSurfaceGroup.setSpace(space);

                global::OpenStudio.ShadingSurface shadingSurface = new global::OpenStudio.ShadingSurface(ToVector(new[] { new[] { 0.0, -1.0, 3.0 }, new[] { 1.0, -1.0, 3.0 }, new[] { 1.0, -2.0, 3.0 }, new[] { 0.0, -2.0, 3.0 } }), model);
                shadingSurface.setShadingSurfaceGroup(shadingSurfaceGroup);

                OpenStudioImportResult result;
                Import(model, out result);

                List<Panel> shades = result.AnalyticalModel.AdjacencyCluster.GetPanels().FindAll(x => x.PanelType == PanelType.Shade);
                Assert.That(shades.Count, Is.EqualTo(1));

                Geometry.Spatial.Point3D point3D = shades[0].GetFace3D().GetInternalPoint3D(Core.Tolerance.Distance);
                Assert.That(point3D.X, Is.GreaterThan(99), "The space's x-origin of 100 m must have been applied to the shade");
            }
        }

        [Test]
        public void SpaceGeometry_IsTransformedByTheSpaceOrigin()
        {
            global::OpenStudio.Space space;
            global::OpenStudio.Surface wall;
            using (global::OpenStudio.Model model = BuildModel(out space, out wall))
            {
                space.setXOrigin(50);
                space.setZOrigin(10);

                OpenStudioImportResult result;
                Import(model, out result);

                Panel panel = result.AnalyticalModel.AdjacencyCluster.GetPanels()[0];
                Geometry.Spatial.Point3D point3D = panel.GetFace3D().GetInternalPoint3D(Core.Tolerance.Distance);

                Assert.That(point3D.X, Is.GreaterThan(49), "Surface vertices are space-relative and must be transformed on import");
                Assert.That(point3D.Z, Is.GreaterThan(9));
            }
        }

        [Test]
        public void UnsupportedBoundaryCondition_IsReported()
        {
            global::OpenStudio.Space space;
            global::OpenStudio.Surface wall;
            using (global::OpenStudio.Model model = BuildModel(out space, out wall))
            {
                global::OpenStudio.SurfacePropertyOtherSideCoefficients otherSideCoefficients = new global::OpenStudio.SurfacePropertyOtherSideCoefficients(model);
                wall.setSurfacePropertyOtherSideCoefficients(otherSideCoefficients);

                OpenStudioImportResult result;
                List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = Import(model, out result);

                AssertHasCode(diagnostics, Core.OpenStudio.OpenStudioImportDiagnosticCodes.BoundaryConditionUnsupported);

                Panel panel = result.AnalyticalModel.AdjacencyCluster.GetPanels()[0];
                bool adiabatic;
                Assert.That(panel.TryGetValue(PanelParameter.Adiabatic, out adiabatic) && adiabatic, Is.True, "The documented fallback for an unrepresentable boundary is adiabatic");
            }
        }

        [Test]
        public void UnpairedInterzoneSurface_IsReportedAndMarkedAdiabatic()
        {
            // OpenStudio refuses to set a "Surface" boundary in memory without an adjacent
            // surface, so this state only reaches the importer through a file - which is exactly
            // how a third-party OSM produces it. The model is therefore saved, edited as text and
            // re-imported.
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SAM_OpenStudio_Adj_" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
            System.IO.Directory.CreateDirectory(directory);

            try
            {
                string path = System.IO.Path.Combine(directory, "dangling.osm");

                using (OpenStudioConversionResult conversionResult = AnalyticalModelFixtures.TwoAdjacentBoxes().ToOpenStudio())
                {
                    Assert.That(conversionResult.Model.save(global::OpenStudio.OpenStudioUtilitiesCore.toPath(path), true), Is.True);
                }

                // Break one side of the shared wall's adjacency: the boundary condition still says
                // "Surface", but the partner handle no longer resolves.
                string[] lines = System.IO.File.ReadAllLines(path);
                bool edited = false;
                for (int i = 0; i < lines.Length && !edited; i++)
                {
                    if (!lines[i].Trim().StartsWith("Surface,", System.StringComparison.Ordinal) || !lines[i].Contains("!- Outside Boundary Condition"))
                    {
                        continue;
                    }

                    // The next field is the Outside Boundary Condition Object; blank it out.
                    if (i + 1 < lines.Length && lines[i + 1].Contains("!- Outside Boundary Condition Object"))
                    {
                        lines[i + 1] = "  ,                                       !- Outside Boundary Condition Object";
                        edited = true;
                    }
                }

                Assert.That(edited, Is.True, "The test fixture must contain a surface with a resolvable adjacency to break");
                System.IO.File.WriteAllLines(path, lines);

                OpenStudioImportResult result = Convert.ToSAM(path);
                foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in result.Diagnostics)
                {
                    TestContext.Out.WriteLine(diagnostic.ToString());
                }

                Assert.That(result.Successful, Is.True, "A dangling adjacency degrades the topology, not the import");

                // Either the geometric fallback pairs it (reported as an inference) or it stays
                // unpaired and is demoted to adiabatic - both are reported, neither is silent.
                Assert.That(
                    result.Diagnostics.Any(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.AdjacencyPairingFailed || x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.AdjacencyGeometricFallback),
                    Is.True,
                    "An interzone surface whose partner handle does not resolve must be reported");
            }
            finally
            {
                try
                {
                    System.IO.Directory.Delete(directory, true);
                }
                catch (System.Exception)
                {
                    // a locked temp directory must not fail an otherwise passing test
                }
            }
        }

        [Test]
        public void OperableWindow_IsImportedAsAWindowAndReportedAsAnApproximation()
        {
            global::OpenStudio.Space space;
            global::OpenStudio.Surface wall;
            using (global::OpenStudio.Model model = BuildModel(out space, out wall))
            {
                global::OpenStudio.SubSurface subSurface = new global::OpenStudio.SubSurface(ToVector(new[] { new[] { 1.0, 0.0, 0.9 }, new[] { 3.0, 0.0, 0.9 }, new[] { 3.0, 0.0, 2.1 }, new[] { 1.0, 0.0, 2.1 } }), model);
                subSurface.setSurface(wall);
                subSurface.setSubSurfaceType("OperableWindow");

                OpenStudioImportResult result;
                List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = Import(model, out result);

                AssertHasCode(diagnostics, Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied);

                Panel panel = result.AnalyticalModel.AdjacencyCluster.GetPanels()[0];
                Assert.That(panel.Apertures.Count, Is.EqualTo(1));
                Assert.That(panel.Apertures[0].ApertureType, Is.EqualTo(ApertureType.Window));
            }
        }

        [Test]
        public void GlassDoor_ImportsAsADoor()
        {
            global::OpenStudio.Space space;
            global::OpenStudio.Surface wall;
            using (global::OpenStudio.Model model = BuildModel(out space, out wall))
            {
                global::OpenStudio.SubSurface subSurface = new global::OpenStudio.SubSurface(ToVector(new[] { new[] { 1.0, 0.0, 0.0 }, new[] { 2.0, 0.0, 0.0 }, new[] { 2.0, 0.0, 2.1 }, new[] { 1.0, 0.0, 2.1 } }), model);
                subSurface.setSurface(wall);
                subSurface.setSubSurfaceType("GlassDoor");

                OpenStudioImportResult result;
                Import(model, out result);

                Panel panel = result.AnalyticalModel.AdjacencyCluster.GetPanels()[0];
                Assert.That(panel.Apertures[0].ApertureType, Is.EqualTo(ApertureType.Door));
            }
        }

        [Test]
        public void SubSurfaceMultiplier_IsReported()
        {
            global::OpenStudio.Space space;
            global::OpenStudio.Surface wall;
            using (global::OpenStudio.Model model = BuildModel(out space, out wall))
            {
                global::OpenStudio.SubSurface subSurface = new global::OpenStudio.SubSurface(ToVector(new[] { new[] { 1.0, 0.0, 0.9 }, new[] { 2.0, 0.0, 0.9 }, new[] { 2.0, 0.0, 2.1 }, new[] { 1.0, 0.0, 2.1 } }), model);
                subSurface.setSurface(wall);
                subSurface.setSubSurfaceType("FixedWindow");
                subSurface.setMultiplier(3);

                OpenStudioImportResult result;
                List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = Import(model, out result);

                Assert.That(diagnostics.Any(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied && x.Message.Contains("multiplier")), Is.True);
            }
        }

        [Test]
        public void SimpleGlazing_IsImportedAsAnApproximation()
        {
            global::OpenStudio.Space space;
            global::OpenStudio.Surface wall;
            using (global::OpenStudio.Model model = BuildModel(out space, out wall))
            {
                global::OpenStudio.SimpleGlazing simpleGlazing = new global::OpenStudio.SimpleGlazing(model, 1.8, 0.4);
                simpleGlazing.setName("Simple Glazing");

                global::OpenStudio.FenestrationMaterialVector materials = new global::OpenStudio.FenestrationMaterialVector();
                materials.Add(simpleGlazing);
                global::OpenStudio.Construction construction = new global::OpenStudio.Construction(materials);
                construction.setName("Simple Window Construction");

                global::OpenStudio.SubSurface subSurface = new global::OpenStudio.SubSurface(ToVector(new[] { new[] { 1.0, 0.0, 0.9 }, new[] { 3.0, 0.0, 0.9 }, new[] { 3.0, 0.0, 2.1 }, new[] { 1.0, 0.0, 2.1 } }), model);
                subSurface.setSurface(wall);
                subSurface.setSubSurfaceType("FixedWindow");
                subSurface.setConstruction(construction);

                OpenStudioImportResult result;
                List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = Import(model, out result);

                Assert.That(diagnostics.Any(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied && x.Message.Contains("SimpleGlazing")), Is.True);

                Core.TransparentMaterial transparentMaterial = result.AnalyticalModel.MaterialLibrary.GetMaterials().OfType<Core.TransparentMaterial>().FirstOrDefault();
                Assert.That(transparentMaterial, Is.Not.Null, "SimpleGlazing must still produce a usable SAM glazing material");
            }
        }

        [Test]
        public void MasslessMaterial_IsImportedPreservingItsResistance()
        {
            global::OpenStudio.Space space;
            global::OpenStudio.Surface wall;
            using (global::OpenStudio.Model model = BuildModel(out space, out wall))
            {
                const double thermalResistance = 2.5;

                global::OpenStudio.MasslessOpaqueMaterial masslessOpaqueMaterial = new global::OpenStudio.MasslessOpaqueMaterial(model, "MediumSmooth", thermalResistance);
                masslessOpaqueMaterial.setName("Insulation Board");

                global::OpenStudio.OpaqueMaterialVector materials = new global::OpenStudio.OpaqueMaterialVector();
                materials.Add(masslessOpaqueMaterial);
                global::OpenStudio.Construction construction = new global::OpenStudio.Construction(materials);
                construction.setName("Massless Construction");
                wall.setConstruction(construction);

                OpenStudioImportResult result;
                List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = Import(model, out result);

                AssertHasCode(diagnostics, Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied);

                Core.OpaqueMaterial opaqueMaterial = result.AnalyticalModel.MaterialLibrary.GetMaterials().OfType<Core.OpaqueMaterial>().FirstOrDefault(x => x.Name == "Insulation Board");
                Assert.That(opaqueMaterial, Is.Not.Null);

                Construction importedConstruction = result.AnalyticalModel.AdjacencyCluster.GetConstructions().Find(x => x.Name == "Massless Construction");
                Assert.That(importedConstruction, Is.Not.Null);

                ConstructionLayer constructionLayer = importedConstruction.ConstructionLayers[0];

                // The whole point of the nominal-thickness reconstruction: R is preserved exactly.
                Assert.That(constructionLayer.Thickness / opaqueMaterial.ThermalConductivity, Is.EqualTo(thermalResistance).Within(1e-9));
            }
        }

        [Test]
        public void SurfaceWithoutConstruction_IsReported()
        {
            global::OpenStudio.Space space;
            global::OpenStudio.Surface wall;
            using (global::OpenStudio.Model model = BuildModel(out space, out wall))
            {
                OpenStudioImportResult result;
                List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = Import(model, out result);

                AssertHasCode(diagnostics, Core.OpenStudio.OpenStudioImportDiagnosticCodes.ConstructionUnsupported);

                Panel panel = result.AnalyticalModel.AdjacencyCluster.GetPanels()[0];
                Assert.That(panel.Construction, Is.Not.Null, "A missing construction produces a named placeholder, not a null");
                Assert.That(panel.Construction.ConstructionLayers == null || panel.Construction.ConstructionLayers.Count == 0, Is.True, "No layers may be invented for a construction that does not exist");
            }
        }

        [Test]
        public void UnsupportedLoadKind_IsReported()
        {
            global::OpenStudio.Space space;
            global::OpenStudio.Surface wall;
            using (global::OpenStudio.Model model = BuildModel(out space, out wall))
            {
                global::OpenStudio.SpaceType spaceType = new global::OpenStudio.SpaceType(model);
                spaceType.setName("Office");
                space.setSpaceType(spaceType);

                global::OpenStudio.GasEquipmentDefinition gasEquipmentDefinition = new global::OpenStudio.GasEquipmentDefinition(model);
                gasEquipmentDefinition.setWattsperSpaceFloorArea(5);
                global::OpenStudio.GasEquipment gasEquipment = new global::OpenStudio.GasEquipment(gasEquipmentDefinition);
                gasEquipment.setSpaceType(spaceType);

                OpenStudioImportResult result;
                List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = Import(model, out result);

                Assert.That(diagnostics.Any(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.LoadUnsupported && x.Message.Contains("GasEquipment")), Is.True);
            }
        }

        [Test]
        public void DetailedHvac_IsReportedAsNotImported()
        {
            global::OpenStudio.Space space;
            global::OpenStudio.Surface wall;
            using (global::OpenStudio.Model model = BuildModel(out space, out wall))
            {
                global::OpenStudio.ThermalZone thermalZone = space.thermalZone().get();

                global::OpenStudio.ScheduleConstant availability = new global::OpenStudio.ScheduleConstant(model);
                availability.setValue(1);

                global::OpenStudio.ZoneHVACBaseboardConvectiveElectric baseboard = new global::OpenStudio.ZoneHVACBaseboardConvectiveElectric(model);
                baseboard.addToThermalZone(thermalZone);

                OpenStudioImportResult result;
                List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = Import(model, out result);

                AssertHasCode(diagnostics, Core.OpenStudio.OpenStudioImportDiagnosticCodes.HvacUnsupported);
            }
        }

        [Test]
        public void MultiSpaceThermalZone_IsReportedAndNeverCollapsed()
        {
            global::OpenStudio.Space space;
            global::OpenStudio.Surface wall;
            using (global::OpenStudio.Model model = BuildModel(out space, out wall))
            {
                global::OpenStudio.Space secondSpace = new global::OpenStudio.Space(model);
                secondSpace.setName("Second Room");
                secondSpace.setThermalZone(space.thermalZone().get());
                AddSurface(model, secondSpace, "Wall", "Outdoors", new[] { new[] { 0.0, 5.0, 0.0 }, new[] { 4.0, 5.0, 0.0 }, new[] { 4.0, 5.0, 2.7 }, new[] { 0.0, 5.0, 2.7 } });

                OpenStudioImportResult result;
                List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = Import(model, out result);

                AssertHasCode(diagnostics, Core.OpenStudio.OpenStudioImportDiagnosticCodes.ZoneMultiSpace);
                Assert.That(result.AnalyticalModel.AdjacencyCluster.GetSpaces().Count, Is.EqualTo(2), "Two spaces in one zone stay two SAM spaces");
            }
        }

        [Test]
        public void SiteAndDesignDays_ImportWithTheirLimitationsStated()
        {
            global::OpenStudio.Space space;
            global::OpenStudio.Surface wall;
            using (global::OpenStudio.Model model = BuildModel(out space, out wall))
            {
                global::OpenStudio.Site site = model.getSite();
                site.setName("Boston Logan");
                site.setLatitude(42.36);
                site.setLongitude(-71.01);
                site.setElevation(6);

                global::OpenStudio.DesignDay heatingDesignDay = new global::OpenStudio.DesignDay(model);
                heatingDesignDay.setName("Boston Heating 99.6%");
                heatingDesignDay.setDayType("WinterDesignDay");
                heatingDesignDay.setMonth(1);
                heatingDesignDay.setDayOfMonth(21);

                global::OpenStudio.DesignDay coolingDesignDay = new global::OpenStudio.DesignDay(model);
                coolingDesignDay.setName("Boston Cooling 0.4%");
                coolingDesignDay.setDayType("SummerDesignDay");
                coolingDesignDay.setMonth(7);
                coolingDesignDay.setDayOfMonth(21);

                OpenStudioImportResult result;
                List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = Import(model, out result);

                Core.Location location = result.AnalyticalModel.Location;
                Assert.That(location, Is.Not.Null);
                Assert.That(location.Latitude, Is.EqualTo(42.36).Within(1e-6));
                Assert.That(location.Longitude, Is.EqualTo(-71.01).Within(1e-6));

                Core.SAMCollection<DesignDay> heatingDesignDays;
                Assert.That(result.AnalyticalModel.TryGetValue(AnalyticalModelParameter.HeatingDesignDays, out heatingDesignDays), Is.True);
                Assert.That(heatingDesignDays.Count, Is.EqualTo(1));

                Core.SAMCollection<DesignDay> coolingDesignDays;
                Assert.That(result.AnalyticalModel.TryGetValue(AnalyticalModelParameter.CoolingDesignDays, out coolingDesignDays), Is.True);
                Assert.That(coolingDesignDays.Count, Is.EqualTo(1));

                AssertHasCode(diagnostics, Core.OpenStudio.OpenStudioImportDiagnosticCodes.WeatherLimitation);
            }
        }

        [Test]
        public void UnnamedSiteAtTheOrigin_ProducesNoLocation()
        {
            global::OpenStudio.Space space;
            global::OpenStudio.Surface wall;
            using (global::OpenStudio.Model model = BuildModel(out space, out wall))
            {
                OpenStudioImportResult result;
                Import(model, out result);

                Assert.That(result.AnalyticalModel.Location, Is.Null, "An unset site must not become a location at the equator");
            }
        }

        [Test]
        public void SurfaceBelowTheMinimumArea_IsRejectedWithAGeometryDiagnostic()
        {
            global::OpenStudio.Space space;
            global::OpenStudio.Surface wall;
            using (global::OpenStudio.Model model = BuildModel(out space, out wall))
            {
                // OpenStudio refuses to construct a truly degenerate surface, so the minimum-area
                // rejection is exercised through the option that exists for it: a real 0.2 x 0.2 m
                // surface against a 1 m2 minimum.
                global::OpenStudio.Surface small = new global::OpenStudio.Surface(ToVector(new[] { new[] { 0.0, 1.0, 0.0 }, new[] { 0.2, 1.0, 0.0 }, new[] { 0.2, 1.0, 0.2 }, new[] { 0.0, 1.0, 0.2 } }), model);
                small.setSpace(space);
                small.setSurfaceType("Wall");
                small.setOutsideBoundaryCondition("Outdoors");

                OpenStudioImportResult result = model.ToSAM(new Core.OpenStudio.OpenStudioImportOptions { MinimumArea = 1.0 });
                foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in result.Diagnostics)
                {
                    TestContext.Out.WriteLine(diagnostic.ToString());
                }

                AssertHasCode(result.Diagnostics, Core.OpenStudio.OpenStudioImportDiagnosticCodes.GeometryInvalid);
                Assert.That(result.Statistics.SkippedObjects, Is.GreaterThan(0), "A rejected surface must be counted, not silently absent");
                Assert.That(result.AnalyticalModel.AdjacencyCluster.GetPanels()?.Count ?? 0, Is.EqualTo(1), "Only the 10.8 m2 wall survives; the 0.04 m2 surface is rejected");
            }
        }

        [Test]
        public void ScheduleRuleset_ExpandsToAnnualHourlyValuesHonouringItsRules()
        {
            global::OpenStudio.Space space;
            global::OpenStudio.Surface wall;
            using (global::OpenStudio.Model model = BuildModel(out space, out wall))
            {
                global::OpenStudio.SpaceType spaceType = new global::OpenStudio.SpaceType(model);
                spaceType.setName("Office");
                space.setSpaceType(spaceType);

                global::OpenStudio.ScheduleRuleset scheduleRuleset = new global::OpenStudio.ScheduleRuleset(model, 0.1);
                scheduleRuleset.setName("Office Lighting");

                // A weekend rule at a distinct value: the expansion must reproduce both values,
                // which a flattened constant could not.
                global::OpenStudio.ScheduleRule weekendRule = new global::OpenStudio.ScheduleRule(scheduleRuleset);
                weekendRule.setApplySaturday(true);
                weekendRule.setApplySunday(true);
                weekendRule.daySchedule().addValue(new global::OpenStudio.Time(0, 24, 0, 0), 0.9);

                global::OpenStudio.LightsDefinition lightsDefinition = new global::OpenStudio.LightsDefinition(model);
                lightsDefinition.setWattsperSpaceFloorArea(8);
                global::OpenStudio.Lights lights = new global::OpenStudio.Lights(lightsDefinition);
                lights.setSpaceType(spaceType);
                lights.setSchedule(scheduleRuleset);

                OpenStudioImportResult result;
                Import(model, out result);

                InternalCondition internalCondition = result.AnalyticalModel.AdjacencyCluster.GetSpaces()[0].InternalCondition;

                string lightingProfileName;
                Assert.That(internalCondition.TryGetValue(InternalConditionParameter.LightingProfileName, out lightingProfileName), Is.True);

                Profile profile = result.AnalyticalModel.ProfileLibrary.GetProfiles().Find(x => x.Name == lightingProfileName);
                Assert.That(profile, Is.Not.Null);
                Assert.That(profile.Max, Is.EqualTo(8759), "A ruleset must expand to a full annual hourly profile");

                HashSet<double> distinctValues = new HashSet<double>();
                for (int i = 0; i <= profile.Max; i++)
                {
                    distinctValues.Add(System.Math.Round(profile[i], 4));
                }

                Assert.That(distinctValues.Contains(0.1), Is.True, "The default day value must be present");
                Assert.That(distinctValues.Contains(0.9), Is.True, "The weekend rule value must be present - the schedule must not be flattened");
            }
        }

        [Test]
        public void UnsupportedScheduleKind_IsReportedAndLeavesTheLoadWithoutAProfile()
        {
            global::OpenStudio.Space space;
            global::OpenStudio.Surface wall;
            using (global::OpenStudio.Model model = BuildModel(out space, out wall))
            {
                global::OpenStudio.SpaceType spaceType = new global::OpenStudio.SpaceType(model);
                spaceType.setName("Office");
                space.setSpaceType(spaceType);

                global::OpenStudio.ScheduleCompact scheduleCompact = new global::OpenStudio.ScheduleCompact(model);
                scheduleCompact.setName("Compact Lighting");

                global::OpenStudio.LightsDefinition lightsDefinition = new global::OpenStudio.LightsDefinition(model);
                lightsDefinition.setWattsperSpaceFloorArea(8);
                global::OpenStudio.Lights lights = new global::OpenStudio.Lights(lightsDefinition);
                lights.setSpaceType(spaceType);
                lights.setSchedule(scheduleCompact);

                OpenStudioImportResult result;
                List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = Import(model, out result);

                AssertHasCode(diagnostics, Core.OpenStudio.OpenStudioImportDiagnosticCodes.ScheduleUnsupported);

                InternalCondition internalCondition = result.AnalyticalModel.AdjacencyCluster.GetSpaces()[0].InternalCondition;

                string lightingProfileName;
                Assert.That(internalCondition.TryGetValue(InternalConditionParameter.LightingProfileName, out lightingProfileName), Is.False, "An unexpandable schedule must leave the load without a profile, not with an invented one");
            }
        }
    }
}
