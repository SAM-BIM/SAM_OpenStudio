// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// R6: regressions found importing a real multi-zone SAM-exported building in Rhino.
    /// <para>
    /// Two things the synthetic fixtures did not surface: an opaque-pane aperture construction (a
    /// solid door) is indistinguishable from a wall construction by its layers, so it must be
    /// classified from its stamped SAM type; and a real space's surfaces close only at the
    /// millimetre, so the internal-point derivation must not run at the micron tolerance and must
    /// have a fallback.
    /// </para>
    /// </summary>
    [TestFixture]
    public class RealModelRegressionTests
    {
        private static global::OpenStudio.Point3dVector ToVector(double[][] vertices)
        {
            global::OpenStudio.Point3dVector result = new global::OpenStudio.Point3dVector();
            foreach (double[] vertex in vertices)
            {
                result.Add(new global::OpenStudio.Point3d(vertex[0], vertex[1], vertex[2]));
            }

            return result;
        }

        private static global::OpenStudio.Surface AddSurface(global::OpenStudio.Model model, global::OpenStudio.Space space, string surfaceType, string boundaryCondition, double[][] vertices)
        {
            global::OpenStudio.Surface surface = new global::OpenStudio.Surface(ToVector(vertices), model);
            surface.setSpace(space);
            surface.setSurfaceType(surfaceType);
            surface.setOutsideBoundaryCondition(boundaryCondition);
            return surface;
        }

        /// <summary>A closed box space, optionally with each vertex nudged by a sub-millimetre jitter.</summary>
        private static global::OpenStudio.Space AddBox(global::OpenStudio.Model model, string name, double x0, double jitter)
        {
            global::OpenStudio.Space space = new global::OpenStudio.Space(model);
            space.setName(name);

            global::OpenStudio.ThermalZone thermalZone = new global::OpenStudio.ThermalZone(model);
            thermalZone.setName(name + " Zone");
            space.setThermalZone(thermalZone);

            double w = 4, d = 3, h = 2.7;
            double j = jitter;

            AddSurface(model, space, "Floor", "Ground", new[] { new[] { x0, 0.0, 0.0 }, new[] { x0, d, 0.0 }, new[] { x0 + w, d, 0.0 }, new[] { x0 + w, 0.0, 0.0 } });
            AddSurface(model, space, "RoofCeiling", "Outdoors", new[] { new[] { x0, 0.0, h }, new[] { x0 + w, 0.0, h }, new[] { x0 + w, d, h }, new[] { x0, d, h } });
            // The four walls, each corner independently nudged by +/- jitter so no two walls share
            // a bit-identical vertex — the shell then closes only above the jitter scale.
            AddSurface(model, space, "Wall", "Outdoors", new[] { new[] { x0, 0.0, 0.0 }, new[] { x0 + w, j, 0.0 }, new[] { x0 + w, 0.0, h }, new[] { x0, j, h } });
            AddSurface(model, space, "Wall", "Outdoors", new[] { new[] { x0 + w, 0.0, 0.0 }, new[] { x0 + w, d, j }, new[] { x0 + w, d, h }, new[] { x0 + w, 0.0, h } });
            AddSurface(model, space, "Wall", "Outdoors", new[] { new[] { x0 + w, d, 0.0 }, new[] { x0, d, 0.0 }, new[] { x0 - j, d, h }, new[] { x0 + w, d, h } });
            AddSurface(model, space, "Wall", "Outdoors", new[] { new[] { x0, d, 0.0 }, new[] { x0, 0.0, 0.0 }, new[] { x0, 0.0, h }, new[] { x0, d, h } });

            return space;
        }

        [Test]
        public void OpaquePaneApertureConstruction_RoundTripsAsApertureConstructionWithItsGuid()
        {
            Guid apertureConstructionGuid = Guid.NewGuid();

            using (global::OpenStudio.Model model = new global::OpenStudio.Model())
            {
                // A single free-standing wall (the pattern the R4 tests prove attaches
                // subsurfaces): the box import has its own aperture-containment subtleties that
                // are not what this test is about — this test is about the construction's type.
                global::OpenStudio.Space space = new global::OpenStudio.Space(model);
                space.setName("Room");
                global::OpenStudio.ThermalZone thermalZone = new global::OpenStudio.ThermalZone(model);
                thermalZone.setName("Zone");
                space.setThermalZone(thermalZone);

                global::OpenStudio.Surface wall = AddSurface(model, space, "Wall", "Outdoors", new[] { new[] { 0.0, 0.0, 0.0 }, new[] { 4.0, 0.0, 0.0 }, new[] { 4.0, 0.0, 2.7 }, new[] { 0.0, 0.0, 2.7 } });

                // A solid door: an OS:Construction whose only layer is an OPAQUE material — exactly
                // what the forward direction writes for an opaque-pane ApertureConstruction — and
                // stamped as an ApertureConstruction, which is the only thing distinguishing it
                // from a wall construction.
                global::OpenStudio.StandardOpaqueMaterial timber = new global::OpenStudio.StandardOpaqueMaterial(model, "MediumSmooth", 0.04, 0.15, 600, 1600);
                timber.setName("Door Timber");

                global::OpenStudio.OpaqueMaterialVector layers = new global::OpenStudio.OpaqueMaterialVector();
                layers.Add(timber);
                global::OpenStudio.Construction doorConstruction = new global::OpenStudio.Construction(layers);
                doorConstruction.setName("SAM_Construction_Door_Pane_Forward_deadbeef");
                Core.OpenStudio.Modify.SetSAMIdentity(doorConstruction, apertureConstructionGuid, typeof(ApertureConstruction).Name, "Door Pane");

                // Strictly inside the wall boundary (off the floor edge), so SAM's aperture
                // containment check accepts it — the forward direction offsets edge apertures;
                // this test simply places it clear of the edge.
                global::OpenStudio.SubSurface door = new global::OpenStudio.SubSurface(ToVector(new[] { new[] { 1.0, 0.0, 0.9 }, new[] { 2.0, 0.0, 0.9 }, new[] { 2.0, 0.0, 2.1 }, new[] { 1.0, 0.0, 2.1 } }), model);
                door.setSurface(wall);
                door.setSubSurfaceType("Door");
                door.setConstruction(doorConstruction);

                OpenStudioImportResult result = model.ToSAM();
                foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in result.Diagnostics)
                {
                    TestContext.Out.WriteLine(diagnostic.ToString());
                }

                // The stamped type must be trusted: no "declares type 'ApertureConstruction' but a
                // Construction is being imported" collision.
                Assert.That(result.Diagnostics.Any(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.IdentityCollision), Is.False, "An opaque-pane door stamped as an ApertureConstruction must not trigger an identity collision");

                // The door aperture must keep its real construction, not a placeholder.
                List<Aperture> apertures = new List<Aperture>();
                foreach (Panel panel in result.AnalyticalModel.AdjacencyCluster.GetPanels())
                {
                    if (panel.Apertures != null)
                    {
                        apertures.AddRange(panel.Apertures);
                    }
                }

                Assert.That(apertures.Count, Is.EqualTo(1), "The door must import as one SAM aperture");
                Aperture aperture = apertures[0];

                ApertureConstruction apertureConstruction = aperture.ApertureConstruction;
                Assert.That(apertureConstruction, Is.Not.Null);
                Assert.That(apertureConstruction.Guid, Is.EqualTo(apertureConstructionGuid), "The ApertureConstruction GUID must be restored, not replaced");
                Assert.That(apertureConstruction.PaneConstructionLayers, Is.Not.Null.And.Not.Empty, "The door must keep its pane layer, not fall back to an empty placeholder");
                Assert.That(apertureConstruction.PaneConstructionLayers[0].Name, Is.EqualTo("Door Timber"));
            }
        }

        [Test]
        public void OpaquePaneDoor_WithoutSamMetadata_KeepsItsOpaqueLayers()
        {
            // The same solid door as above, but with NO SAM identity stamp — a third-party model,
            // or the forward-written door before identity was added. The construction pass then
            // classifies it by its layers and files it as an opaque Construction; the aperture
            // resolver must still carry those real layers across instead of an empty placeholder.
            using (global::OpenStudio.Model model = new global::OpenStudio.Model())
            {
                global::OpenStudio.Space space = new global::OpenStudio.Space(model);
                space.setName("Room");
                global::OpenStudio.ThermalZone thermalZone = new global::OpenStudio.ThermalZone(model);
                thermalZone.setName("Zone");
                space.setThermalZone(thermalZone);

                global::OpenStudio.Surface wall = AddSurface(model, space, "Wall", "Outdoors", new[] { new[] { 0.0, 0.0, 0.0 }, new[] { 4.0, 0.0, 0.0 }, new[] { 4.0, 0.0, 2.7 }, new[] { 0.0, 0.0, 2.7 } });

                global::OpenStudio.StandardOpaqueMaterial timber = new global::OpenStudio.StandardOpaqueMaterial(model, "MediumSmooth", 0.04, 0.15, 600, 1600);
                timber.setName("Door Timber");

                global::OpenStudio.OpaqueMaterialVector layers = new global::OpenStudio.OpaqueMaterialVector();
                layers.Add(timber);
                global::OpenStudio.Construction doorConstruction = new global::OpenStudio.Construction(layers);
                doorConstruction.setName("Plain Opaque Door");
                // Deliberately NO Core.OpenStudio.Modify.SetSAMIdentity call.

                global::OpenStudio.SubSurface door = new global::OpenStudio.SubSurface(ToVector(new[] { new[] { 1.0, 0.0, 0.9 }, new[] { 2.0, 0.0, 0.9 }, new[] { 2.0, 0.0, 2.1 }, new[] { 1.0, 0.0, 2.1 } }), model);
                door.setSurface(wall);
                door.setSubSurfaceType("Door");
                door.setConstruction(doorConstruction);

                OpenStudioImportResult result = model.ToSAM();
                foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in result.Diagnostics)
                {
                    TestContext.Out.WriteLine(diagnostic.ToString());
                }

                List<Aperture> apertures = new List<Aperture>();
                foreach (Panel panel in result.AnalyticalModel.AdjacencyCluster.GetPanels())
                {
                    if (panel.Apertures != null)
                    {
                        apertures.AddRange(panel.Apertures);
                    }
                }

                Assert.That(apertures.Count, Is.EqualTo(1), "The door must import as one SAM aperture");

                ApertureConstruction apertureConstruction = apertures[0].ApertureConstruction;
                Assert.That(apertureConstruction, Is.Not.Null);
                Assert.That(apertureConstruction.PaneConstructionLayers, Is.Not.Null.And.Not.Empty, "A metadata-free opaque door must keep its opaque layers, not fall back to an empty placeholder");
                Assert.That(apertureConstruction.PaneConstructionLayers[0].Name, Is.EqualTo("Door Timber"));
                Assert.That(apertureConstruction.ApertureType, Is.EqualTo(ApertureType.Door), "The aperture type comes from the subsurface, not the construction");
            }
        }

        [Test]
        public void SpaceThatClosesOnlyAtMillimetre_StillGetsAnInternalPointLocation()
        {
            using (global::OpenStudio.Model model = new global::OpenStudio.Model())
            {
                // 0.5 mm jitter: below MacroDistance (1 mm) so the shell still closes there, but
                // far above DistanceTolerance (1 micron), which is where the old code failed.
                AddBox(model, "Jittered Room", 0, 0.0005);

                OpenStudioImportResult result = model.ToSAM();
                foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in result.Diagnostics)
                {
                    TestContext.Out.WriteLine(diagnostic.ToString());
                }

                Space space = result.AnalyticalModel.AdjacencyCluster.GetSpaces()[0];
                Assert.That(space.Location, Is.Not.Null, "A space that closes at the millimetre must still get a SAM location");

                double volume;
                Assert.That(space.TryGetValue(SpaceParameter.Volume, out volume), Is.True, "Volume must be derived from the shell");
                Assert.That(volume, Is.EqualTo(4.0 * 3.0 * 2.7).Within(2).Percent);
            }
        }

        [Test]
        public void SpaceThatCannotClose_GetsACentroidLocationAndIsReported()
        {
            using (global::OpenStudio.Model model = new global::OpenStudio.Model())
            {
                global::OpenStudio.Space space = AddBox(model, "Open Room", 0, 0);

                // Delete the roof so the shell genuinely cannot close.
                model.getSurfaces().First(x => x.surfaceType() == "RoofCeiling").remove();

                OpenStudioImportResult result = model.ToSAM();
                foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in result.Diagnostics)
                {
                    TestContext.Out.WriteLine(diagnostic.ToString());
                }

                Space importedSpace = result.AnalyticalModel.AdjacencyCluster.GetSpaces()[0];

                // The key regression: an unclosable space must NOT be left with a null location.
                Assert.That(importedSpace.Location, Is.Not.Null, "An unclosable space must fall back to the vertex centroid, never a null location");

                // And it must say the location is approximate rather than pretend it is exact.
                Assert.That(result.Diagnostics.Any(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied && x.Message.Contains("centroid")), Is.True);

                // The centroid of a 4 x 3 x 2.7 box floor-and-walls sits inside the footprint.
                Assert.That(importedSpace.Location.X, Is.GreaterThan(0).And.LessThan(4));
                Assert.That(importedSpace.Location.Y, Is.GreaterThan(0).And.LessThan(3));
            }
        }

        [Test]
        public void CollinearVertices_AreCleanedAsInformationNotWarning()
        {
            using (global::OpenStudio.Model model = new global::OpenStudio.Model())
            {
                global::OpenStudio.Space space = new global::OpenStudio.Space(model);
                space.setName("Room");
                global::OpenStudio.ThermalZone thermalZone = new global::OpenStudio.ThermalZone(model);
                thermalZone.setName("Zone");
                space.setThermalZone(thermalZone);

                // A rectangle with a redundant vertex at the midpoint of its bottom edge — exactly
                // what a real exported model carries, and what produced ten warnings on the real
                // building. The point lies ON the edge, so the polygon is geometrically identical.
                AddSurface(model, space, "Wall", "Outdoors", new[] { new[] { 0.0, 0.0, 0.0 }, new[] { 2.0, 0.0, 0.0 }, new[] { 4.0, 0.0, 0.0 }, new[] { 4.0, 0.0, 2.7 }, new[] { 0.0, 0.0, 2.7 } });

                OpenStudioImportResult result = model.ToSAM();
                foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in result.Diagnostics)
                {
                    TestContext.Out.WriteLine(diagnostic.ToString());
                }

                Core.OpenStudio.OpenStudioDiagnostic cleaned = result.Diagnostics.FirstOrDefault(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.GeometryVerticesCleaned);
                Assert.That(cleaned, Is.Not.Null, "Vertex cleaning must be reported under its own code");
                Assert.That(cleaned.Severity, Is.EqualTo(Core.OpenStudio.OpenStudioDiagnosticSeverity.Information), "Removing a collinear vertex changes nothing, so it must not be a warning");

                // It must NOT be reported as invalid geometry, which is what buried real failures.
                Assert.That(result.Diagnostics.Any(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.GeometryInvalid), Is.False);

                // And the panel must still be imported, with its true 4 m x 2.7 m area.
                List<Panel> panels = result.AnalyticalModel.AdjacencyCluster.GetPanels();
                Assert.That(panels.Count, Is.EqualTo(1));
                Assert.That(panels[0].GetFace3D().GetArea(), Is.EqualTo(4.0 * 2.7).Within(0.01));
            }
        }

        [Test]
        public void MultiSpaceBuilding_EveryImportedSpaceGetsALocation()
        {
            using (global::OpenStudio.Model model = new global::OpenStudio.Model())
            {
                // Several rooms side by side, each with its own sub-millimetre jitter — a
                // miniature of the real building where every space reported ZONE-002.
                for (int i = 0; i < 5; i++)
                {
                    AddBox(model, "Room " + i, i * 5.0, 0.0004);
                }

                OpenStudioImportResult result = model.ToSAM();

                List<Space> spaces = result.AnalyticalModel.AdjacencyCluster.GetSpaces();
                Assert.That(spaces.Count, Is.EqualTo(5));

                foreach (Space space in spaces)
                {
                    Assert.That(space.Location, Is.Not.Null, $"Space '{space.Name}' must have a location");
                }

                // None should now report the old "location is not set" ZoneIncomplete warning.
                Assert.That(result.Diagnostics.Any(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.ZoneIncomplete && x.Message.Contains("location is not set")), Is.False);
            }
        }
    }
}
