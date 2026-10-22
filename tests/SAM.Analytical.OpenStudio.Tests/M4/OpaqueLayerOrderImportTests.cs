// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// Opaque multi-layer layer order on import (SAM_OpenStudio#33 revision 11). OpenStudio stores layers outside first and
    /// SAM inside → outside from the panel's first space, so an opaque layered construction used by a planar surface is
    /// read innermost layer first, and an interzone panel is built from (and first related to) the side carrying the
    /// SAM-written forward construction. Export → import → export must keep every surface's layers, thicknesses, thermal
    /// properties and the absorptances of the two faces EnergyPlus uses. Fenestration, opaque door panes, shading, air
    /// boundaries and non-layered constructions keep OpenStudio's order.
    /// </summary>
    [TestFixture]
    public class OpaqueLayerOrderImportTests
    {
        private static readonly CultureInfo ci = CultureInfo.InvariantCulture;

        // SAM inside → outside: Insulation faces the first space, Brick the second (or outdoors).
        private static readonly Construction sandwich = new Construction(new Guid("66666666-0000-0000-0000-000000000003"), "Order Sandwich", new List<ConstructionLayer>
        {
            new ConstructionLayer("Insulation", 0.1),
            new ConstructionLayer("Plasterboard", 0.0125),
            new ConstructionLayer("Brick", 0.1),
        });

        // ---- physical signature ------------------------------------------------------------------------------------

        private static string N(double value, int digits)
        {
            return (Math.Round(value, digits) + 0.0).ToString("R", ci);
        }

        private static string Layer(global::OpenStudio.Material material)
        {
            global::OpenStudio.OptionalStandardOpaqueMaterial optional = material.to_StandardOpaqueMaterial();
            if (optional.isNull())
            {
                return material.iddObjectType().valueName() + ":" + N(material.thickness(), 9);
            }

            global::OpenStudio.StandardOpaqueMaterial standard = optional.get();
            return string.Join(":", N(standard.thickness(), 9), N(standard.thermalConductivity(), 9), N(standard.density(), 6), N(standard.specificHeat(), 6));
        }

        private static string Face(global::OpenStudio.Material material)
        {
            global::OpenStudio.OptionalStandardOpaqueMaterial optional = material.to_StandardOpaqueMaterial();
            if (optional.isNull())
            {
                return "-";
            }

            global::OpenStudio.StandardOpaqueMaterial standard = optional.get();
            return string.Join("/", N(standard.thermalAbsorptance(), 9), N(standard.solarAbsorptance(), 9), N(standard.visibleAbsorptance(), 9));
        }

        /// <summary>
        /// Per surface (keyed by its absolute centroid, outward normal, type, boundary and area, so the two sides of a
        /// shared wall are told apart by their normals): its layers' thickness and thermal properties outside first, and
        /// the absorptances of its outside face (first layer) and zone face (last layer). Names are deliberately not
        /// compared: an unstamped import renames spaces and materials.
        /// </summary>
        internal static List<string> Signatures(global::OpenStudio.Model model)
        {
            List<string> result = new List<string>();
            foreach (global::OpenStudio.Surface surface in model.getSurfaces())
            {
                global::OpenStudio.Vector3d normal = surface.outwardNormal();
                global::OpenStudio.Space space = surface.space().get();
                List<global::OpenStudio.Point3d> vertices = surface.vertices().ToList();
                string centroid = string.Join(",", N(space.xOrigin() + vertices.Average(x => x.x()), 4), N(space.yOrigin() + vertices.Average(x => x.y()), 4), N(space.zOrigin() + vertices.Average(x => x.z()), 4));
                string key = string.Join("|", centroid, N(normal.x(), 6), N(normal.y(), 6), N(normal.z(), 6), surface.surfaceType(), surface.outsideBoundaryCondition(), N(surface.grossArea(), 6));
                global::OpenStudio.OptionalConstructionBase optionalConstructionBase = surface.construction();
                if (optionalConstructionBase.isNull())
                {
                    result.Add(key + " none");
                    continue;
                }

                global::OpenStudio.OptionalConstruction optionalConstruction = optionalConstructionBase.get().to_Construction();
                if (optionalConstruction.isNull())
                {
                    result.Add(key + " " + optionalConstructionBase.get().iddObjectType().valueName());
                    continue;
                }

                List<global::OpenStudio.Material> layers = optionalConstruction.get().layers().ToList();
                result.Add(key + " layers=" + string.Join(";", layers.Select(Layer)) + " outside=" + Face(layers.First()) + " zone=" + Face(layers.Last()));
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private static void AssertSameSignatures(List<string> before, List<string> after)
        {
            Assert.That(after.Count, Is.EqualTo(before.Count), "surface count");
            List<string> differences = before.Zip(after, (b, a) => b == a ? null : "before: " + b + "\n after: " + a).Where(x => x != null).ToList();
            Assert.That(differences, Is.Empty, string.Join("\n", differences));
        }

        private static List<string> LayerNames(Construction construction)
        {
            return construction.ConstructionLayers.Select(x => x.Name).ToList();
        }

        private static void AssertLayers(Construction construction, params (string Fragment, double Thickness)[] expected)
        {
            List<ConstructionLayer> layers = construction.ConstructionLayers;
            Assert.That(layers.Count, Is.EqualTo(expected.Length), string.Join(", ", LayerNames(construction)));
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(layers[i].Name, Does.Contain(expected[i].Fragment), "layer " + i + ": " + string.Join(", ", LayerNames(construction)));
                Assert.That(layers[i].Thickness, Is.EqualTo(expected[i].Thickness).Within(1e-9), "layer " + i + " thickness");
            }
        }

        private static MaterialLibrary LibraryWithoutPlasterboardLight()
        {
            MaterialLibrary materialLibrary = OpaqueFaceOpticsTests.Library();
            OpaqueMaterial plasterboard = materialLibrary.GetMaterial("Plasterboard") as OpaqueMaterial;
            plasterboard.RemoveValue(OpaqueMaterialParameter.ExternalLightReflectance);
            plasterboard.RemoveValue(OpaqueMaterialParameter.InternalLightReflectance);
            materialLibrary.Add(plasterboard);
            return materialLibrary;
        }

        private static global::OpenStudio.Surface OutdoorWall(global::OpenStudio.Model model)
        {
            return model.getSurfaces().Where(x => x.surfaceType() == "Wall" && x.outsideBoundaryCondition() == "Outdoors").OrderBy(x => x.nameString(), StringComparer.Ordinal).First();
        }

        private static List<global::OpenStudio.StandardOpaqueMaterial> Layers(global::OpenStudio.Surface surface)
        {
            return surface.construction().get().to_Construction().get().layers().Select(x => x.to_StandardOpaqueMaterial().get()).ToList();
        }

        private static Panel SharedPanel(AnalyticalModel analyticalModel)
        {
            AdjacencyCluster adjacencyCluster = analyticalModel.AdjacencyCluster;
            return adjacencyCluster.GetPanels().Single(x => adjacencyCluster.GetSpaces(x)?.Count == 2);
        }

        private static void StripSamMetadata(global::OpenStudio.Model model)
        {
            foreach (global::OpenStudio.ModelObject modelObject in model.modelObjects().ToList())
            {
                if (modelObject.iddObjectType().valueName() != "OS_AdditionalProperties" && modelObject.hasAdditionalProperties())
                {
                    modelObject.additionalProperties().remove();
                }
            }

            Assert.That(model.modelObjects().Count(x => x.iddObjectType().valueName() == "OS_AdditionalProperties"), Is.EqualTo(0));
        }

        // ---- round trips ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Review of 806d5449 (reproduction): Plasterboard light reflectance unstated, its zone-face variant edited to visible
        /// absorptance 0.42. Revision 10 re-exported Plasterboard → Insulation → Brick_Internal with Plasterboard at 0.7. The
        /// import now reads the wall innermost first (Plasterboard, Insulation, Brick at their thicknesses), and the re-export
        /// reproduces every surface's layers and both used faces, including the edited 0.42 zone face.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void AMultiLayerRoundTrip_KeepsLayersThicknessesAndBothUsedFaces(bool edited)
        {
            using (OpenStudioConversionResult conversion = OpaqueFaceOpticsTests.Box(LibraryWithoutPlasterboardLight(), AnalyticalModelFixtures.WallConstruction).ToOpenStudio())
            {
                if (edited)
                {
                    conversion.Model.getStandardOpaqueMaterials().Single(x => x.nameString().Contains("Plasterboard") && x.nameString().Contains("_Internal")).setVisibleAbsorptance(0.42);
                }

                List<string> before = Signatures(conversion.Model);
                OpenStudioImportResult result = conversion.Model.ToSAM();
                Panel wall = result.AnalyticalModel.AdjacencyCluster.GetPanels().First(x => x.PanelType == PanelType.WallExternal);
                AssertLayers(wall.Construction, ("Plasterboard", 0.0125), ("Insulation", 0.1), ("Brick", 0.1));
                Assert.That(wall.Construction.Guid, Is.EqualTo(AnalyticalModelFixtures.WallConstruction.Guid), "the forward construction's SAM Guid is restored");

                using (OpenStudioConversionResult reexport = result.AnalyticalModel.ToOpenStudio())
                {
                    Assert.That(reexport.IsValid, Is.True, string.Join(" | ", reexport.Diagnostics.Where(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)));
                    List<global::OpenStudio.StandardOpaqueMaterial> layers = Layers(OutdoorWall(reexport.Model));
                    Assert.That(layers.Count, Is.EqualTo(3));
                    Assert.That(layers[0].nameString(), Does.Contain("Brick"), "outside first: Brick, Insulation, Plasterboard");
                    Assert.That(layers[1].nameString(), Does.Contain("Insulation"));
                    Assert.That(layers.Last().nameString(), Does.Contain("Plasterboard").And.Contain("_Internal"));
                    Assert.That(layers.Last().visibleAbsorptance(), Is.EqualTo(edited ? 0.42 : 0.7).Within(1e-9), "the zone face");
                    AssertSameSignatures(before, Signatures(reexport.Model));
                }
            }
        }

        /// <summary>
        /// Two boxes sharing a multi-layer wall, in both SAM space orders: the imported shared panel keeps SAM's first space,
        /// reads its layers from that side, and the re-export reproduces both sides' layers and used faces. An edit of the
        /// first space's zone-face layer (a shared equal-face object, revision 10) survives on that face only.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void AnInterzoneMultiLayerRoundTrip_KeepsTheFirstSpaceAndBothSides(bool bFirst, bool edited)
        {
            using (OpenStudioConversionResult conversion = OpaqueFaceOpticsTests.TwoBoxes(OpaqueFaceOpticsTests.Library(), sandwich, bFirst).ToOpenStudio())
            {
                Assert.That(conversion.IsValid, Is.True);
                if (edited)
                {
                    conversion.Model.getStandardOpaqueMaterials().Single(x => x.nameString().Contains("Insulation")).setVisibleAbsorptance(0.42);
                }

                List<string> before = Signatures(conversion.Model);
                OpenStudioImportResult result = conversion.Model.ToSAM();
                Panel shared = SharedPanel(result.AnalyticalModel);
                Assert.That(result.AnalyticalModel.AdjacencyCluster.GetSpaces(shared)[0].Name, Is.EqualTo(bFirst ? "Space B" : "Space A"), "SAM's first space of the shared panel");
                Assert.That(result.AnalyticalModel.AdjacencyCluster.GetSpaces()[0].Name, Is.EqualTo(bFirst ? "Space B" : "Space A"), "first in the cluster's space order, which the writer reads");
                AssertLayers(shared.Construction, ("Insulation", 0.1), ("Plasterboard", 0.0125), ("Brick", 0.1));
                Assert.That(shared.Construction.Guid, Is.EqualTo(sandwich.Guid), "built from the forward side");

                using (OpenStudioConversionResult reexport = result.AnalyticalModel.ToOpenStudio())
                {
                    Assert.That(reexport.IsValid, Is.True, string.Join(" | ", reexport.Diagnostics.Where(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)));
                    AssertSameSignatures(before, Signatures(reexport.Model));
                }
            }
        }

        /// <summary>
        /// Geometric-fallback pairing: the interzone adjacency handles are blanked in the saved OSM, so the two shared-wall
        /// surfaces are paired by geometry. The panel built from the forward side is kept, in both space orders, and the
        /// re-export reproduces the original surfaces.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void AGeometricFallbackPair_KeepsTheForwardSide(bool bFirst)
        {
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SAM_OpenStudio_Order_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            System.IO.Directory.CreateDirectory(directory);
            try
            {
                string path = System.IO.Path.Combine(directory, "dangling.osm");
                List<string> before;
                using (OpenStudioConversionResult conversion = OpaqueFaceOpticsTests.TwoBoxes(OpaqueFaceOpticsTests.Library(), sandwich, bFirst).ToOpenStudio())
                {
                    before = Signatures(conversion.Model);
                    Assert.That(conversion.Model.save(global::OpenStudio.OpenStudioUtilitiesCore.toPath(path), true), Is.True);
                }

                string[] lines = System.IO.File.ReadAllLines(path);
                int edited = 0;
                for (int i = 1; i < lines.Length; i++)
                {
                    if (lines[i].Contains("!- Outside Boundary Condition Object") && lines[i - 1].Trim().StartsWith("Surface,", StringComparison.Ordinal) && lines[i - 1].Contains("!- Outside Boundary Condition"))
                    {
                        lines[i] = "  ,                                       !- Outside Boundary Condition Object";
                        edited++;
                    }
                }

                Assert.That(edited, Is.EqualTo(2), "both sides of the shared wall");
                System.IO.File.WriteAllLines(path, lines);

                OpenStudioImportResult result = Convert.ToSAM(path);
                Assert.That(result.Diagnostics.Count(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.AdjacencyGeometricFallback), Is.EqualTo(1), string.Join(" | ", result.Diagnostics));
                Panel shared = SharedPanel(result.AnalyticalModel);
                Assert.That(result.AnalyticalModel.AdjacencyCluster.GetSpaces(shared)[0].Name, Is.EqualTo(bFirst ? "Space B" : "Space A"));
                Assert.That(result.AnalyticalModel.AdjacencyCluster.GetSpaces()[0].Name, Is.EqualTo(bFirst ? "Space B" : "Space A"), "first in the cluster's space order, which the writer reads");
                AssertLayers(shared.Construction, ("Insulation", 0.1), ("Plasterboard", 0.0125), ("Brick", 0.1));

                using (OpenStudioConversionResult reexport = result.AnalyticalModel.ToOpenStudio())
                {
                    Assert.That(reexport.IsValid, Is.True, string.Join(" | ", reexport.Diagnostics.Where(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)));
                    AssertSameSignatures(before, Signatures(reexport.Model));
                }
            }
            finally
            {
                try
                {
                    System.IO.Directory.Delete(directory, true);
                }
                catch (Exception)
                {
                    // a locked temp directory must not fail an otherwise passing test
                }
            }
        }

        /// <summary>
        /// Unstamped native opaque models (every SAM AdditionalProperties removed): no SAM metadata at all, so layers are
        /// still read innermost first from the surface's zone, the interzone panel is built from the first surface visited,
        /// and the re-export reproduces every surface's layers and used faces, in both space orders.
        /// </summary>
        [TestCase("box", false)]
        [TestCase("two", false)]
        [TestCase("two", true)]
        public void AnUnstampedNativeModel_RoundTripsItsLayersAndUsedFaces(string model, bool bFirst)
        {
            AnalyticalModel analyticalModel = model == "box"
                ? OpaqueFaceOpticsTests.Box(OpaqueFaceOpticsTests.Library(), AnalyticalModelFixtures.WallConstruction)
                : OpaqueFaceOpticsTests.TwoBoxes(OpaqueFaceOpticsTests.Library(), sandwich, bFirst);
            using (OpenStudioConversionResult conversion = analyticalModel.ToOpenStudio())
            {
                StripSamMetadata(conversion.Model);
                List<string> before = Signatures(conversion.Model);
                OpenStudioImportResult result = conversion.Model.ToSAM();
                Panel wall = result.AnalyticalModel.AdjacencyCluster.GetPanels().First(x => x.PanelType == PanelType.WallExternal);
                AssertLayers(wall.Construction, ("Plasterboard", 0.0125), ("Insulation", 0.1), ("Brick", 0.1));
                if (model == "two")
                {
                    Panel shared = SharedPanel(result.AnalyticalModel);
                    // No stamp: the panel is built from the first surface visited, and its layers face that space first.
                    // SAM-authored, Insulation faces the authored first space (B when bFirst), Brick the other.
                    string first = result.AnalyticalModel.AdjacencyCluster.GetSpaces(shared)[0].Name;
                    bool firstIsA = first.Contains("Space_A") || first.Contains("Space A");
                    Assert.That(firstIsA || first.Contains("Space_B") || first.Contains("Space B"), Is.True, first);
                    AssertLayers(shared.Construction, firstIsA != bFirst
                        ? new[] { ("Insulation", 0.1), ("Plasterboard", 0.0125), ("Brick", 0.1) }
                        : new[] { ("Brick", 0.1), ("Plasterboard", 0.0125), ("Insulation", 0.1) });
                    Assert.That(result.AnalyticalModel.AdjacencyCluster.GetSpaces()[0].Name, Is.EqualTo(first), "the panel's first space is also first in the cluster's space order");
                }

                using (OpenStudioConversionResult reexport = result.AnalyticalModel.ToOpenStudio())
                {
                    Assert.That(reexport.IsValid, Is.True, string.Join(" | ", reexport.Diagnostics.Where(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)));
                    AssertSameSignatures(before, Signatures(reexport.Model));
                }
            }
        }

        private static Geometry.Spatial.Face3D Face(params double[] c)
        {
            List<Geometry.Spatial.Point3D> point3Ds = new List<Geometry.Spatial.Point3D>();
            for (int i = 0; i < c.Length; i += 3)
            {
                point3Ds.Add(new Geometry.Spatial.Point3D(c[i], c[i + 1], c[i + 2]));
            }

            return new Geometry.Spatial.Face3D(new Geometry.Spatial.Polygon3D(point3Ds));
        }

        /// <summary>Two boxes whose shared wall is two halves: the south half of <see cref="sandwich"/>, the north half of a copy with its own Guid.</summary>
        private static AnalyticalModel TwoBoxesWithTwoSharedHalves(Construction northHalf, bool bFirst)
        {
            // Equal faces (the fixture library states no internal values): a re-read panel's face roles change with its first
            // space, which only a material whose authored faces differ would show (a recorded limit of the re-read).
            AnalyticalModel analyticalModel = OpaqueFaceOpticsTests.TwoBoxes(OpaqueFaceOpticsTests.Library(false), sandwich, bFirst);
            AdjacencyCluster adjacencyCluster = analyticalModel.AdjacencyCluster;
            Panel shared = SharedPanel(analyticalModel);
            List<Space> spaces = adjacencyCluster.GetSpaces(shared);
            adjacencyCluster.RemoveObject<Panel>(shared.Guid);
            foreach ((Construction construction, Geometry.Spatial.Face3D face3D) in new[]
            {
                (sandwich, Face(5, 0, 0, 5, 2, 0, 5, 2, 3, 5, 0, 3)),
                (northHalf, Face(5, 2, 0, 5, 4, 0, 5, 4, 3, 5, 2, 3)),
            })
            {
                Panel half = Analytical.Create.Panel(construction, PanelType.WallInternal, face3D);
                adjacencyCluster.AddObject(half);
                foreach (Space space in spaces)
                {
                    adjacencyCluster.AddRelation(space, half);
                }
            }

            return new AnalyticalModel(analyticalModel, adjacencyCluster);
        }

        /// <summary>
        /// A mixed model: one shared half SAM-written, the other with its SAM stamps removed. The stamped (south) half's two
        /// constructions are placed so its forward side is the space OpenStudio visits second; the unstamped (north) half is
        /// built from the space visited first. The stamped half puts its forward space first in the space order, so the
        /// unstamped half is re-read from its partner surface in that space (warning SAM-OSI-APX-001), and both halves
        /// round-trip. Deterministic: the placement follows the visit order of the exported model, whatever it is. The
        /// materials' faces agree; with differing authored faces the re-read half's exposed faces would take the new first
        /// space's face roles (recorded limit, an edited model only).
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void AnUnstampedHalfBuiltFromTheSecondSpace_IsReReadFromItsPartner(bool bFirst)
        {
            Construction northHalf = new Construction(new Guid("66666666-0000-0000-0000-000000000004"), "Order Sandwich North", sandwich.ConstructionLayers);
            using (OpenStudioConversionResult conversion = TwoBoxesWithTwoSharedHalves(northHalf, bFirst).ToOpenStudio())
            {
                Assert.That(conversion.IsValid, Is.True, string.Join(" | ", conversion.Diagnostics.Where(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)));
                List<global::OpenStudio.Space> visit = conversion.Model.getSpaces().ToList();
                Assert.That(visit.Count, Is.EqualTo(2));
                string second = visit[1].nameString();

                List<global::OpenStudio.Surface> interzone = conversion.Model.getSurfaces().Where(x => x.outsideBoundaryCondition() == "Surface").ToList();
                bool South(global::OpenStudio.Surface surface) => surface.vertices().Average(x => x.y()) < 2;
                bool InSecond(global::OpenStudio.Surface surface) => surface.space().get().nameString() == second;

                // South half: the stamped forward construction on the second-visited space's surface, the reverse on the other.
                global::OpenStudio.Surface southSecond = interzone.Single(x => South(x) && InSecond(x));
                global::OpenStudio.Surface southFirst = interzone.Single(x => South(x) && !InSecond(x));
                global::OpenStudio.ConstructionBase forward = new[] { southSecond, southFirst }.Select(x => x.construction().get()).Single(x => Core.OpenStudio.Query.TryGetSAMGuid(x, out Guid guid) && guid == sandwich.Guid);
                global::OpenStudio.ConstructionBase reverse = new[] { southSecond, southFirst }.Select(x => x.construction().get()).Single(x => !Core.OpenStudio.Query.TryGetSAMGuid(x, out Guid _));
                Assert.That(southSecond.setConstruction(forward), Is.True);
                Assert.That(southFirst.setConstruction(reverse), Is.True);

                // North half: no SAM stamp at all.
                conversion.Model.getConstructions().Single(x => Core.OpenStudio.Query.TryGetSAMGuid(x, out Guid guid) && guid == northHalf.Guid).additionalProperties().remove();
                global::OpenStudio.Surface northSecond = interzone.Single(x => !South(x) && InSecond(x));
                string Fragment(string name) => new[] { "Insulation", "Plasterboard", "Brick" }.Single(name.Contains);
                List<string> expectedNorth = northSecond.construction().get().to_Construction().get().layers().Select(x => Fragment(x.nameString())).Reverse().ToList();
                List<string> before = Signatures(conversion.Model);

                OpenStudioImportResult result = conversion.Model.ToSAM();
                AdjacencyCluster adjacencyCluster = result.AnalyticalModel.AdjacencyCluster;
                string expectedFirst = second.Contains("Space_B") ? "Space B" : "Space A";
                Assert.That(adjacencyCluster.GetSpaces()[0].Name, Is.EqualTo(expectedFirst), "the stamped half's forward side decides the space order");
                Assert.That(result.Diagnostics.Count(d => d.Code == "SAM-OSI-APX-001" && d.Message.Contains("is read from that space's surface instead")), Is.EqualTo(1), string.Join(" | ", result.Diagnostics));

                List<Panel> halves = adjacencyCluster.GetPanels().Where(x => adjacencyCluster.GetSpaces(x)?.Count == 2).ToList();
                Assert.That(halves.Count, Is.EqualTo(2));
                foreach (Panel half in halves)
                {
                    Assert.That(adjacencyCluster.GetSpaces(half)[0].Name, Is.EqualTo(expectedFirst));
                }

                Panel southPanel = halves.Single(x => x.Construction.Guid == sandwich.Guid);
                AssertLayers(southPanel.Construction, ("Insulation", 0.1), ("Plasterboard", 0.0125), ("Brick", 0.1));
                Panel northPanel = halves.Single(x => !ReferenceEquals(x, southPanel));
                Assert.That(LayerNames(northPanel.Construction).Select(Fragment), Is.EqualTo(expectedNorth), "innermost first from the first space's own surface");

                using (OpenStudioConversionResult reexport = result.AnalyticalModel.ToOpenStudio())
                {
                    Assert.That(reexport.IsValid, Is.True, string.Join(" | ", reexport.Diagnostics.Where(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)));
                    AssertSameSignatures(before, Signatures(reexport.Model));
                }
            }
        }

        /// <summary>The single-layer control: one layer reads the same in either order, and its round trip is unchanged.</summary>
        [Test]
        public void ASingleLayerRoundTrip_IsUnchanged()
        {
            Construction single = new Construction(new Guid("66666666-0000-0000-0000-000000000001"), "Order Single", new List<ConstructionLayer> { new ConstructionLayer("Insulation", 0.1) });
            using (OpenStudioConversionResult conversion = OpaqueFaceOpticsTests.Box(OpaqueFaceOpticsTests.Library(), AnalyticalModelFixtures.WallConstruction, floor: single).ToOpenStudio())
            {
                List<string> before = Signatures(conversion.Model);
                OpenStudioImportResult result = conversion.Model.ToSAM();
                AssertLayers(result.AnalyticalModel.AdjacencyCluster.GetPanels().Single(x => x.PanelType == PanelType.SlabOnGrade).Construction, ("Insulation", 0.1));
                using (OpenStudioConversionResult reexport = result.AnalyticalModel.ToOpenStudio())
                {
                    AssertSameSignatures(before, Signatures(reexport.Model));
                }
            }
        }

        // ---- unchanged paths -----------------------------------------------------------------------------------------

        /// <summary>Glazing keeps OpenStudio's pane order: an asymmetric window (6 mm, cavity, 4 mm panes) imports exactly as stored.</summary>
        [Test]
        public void AGlazingConstruction_KeepsOpenStudiosPaneOrder()
        {
            ApertureConstruction window = new ApertureConstruction(new Guid("66666666-0000-0000-0000-000000000010"), "Order Window", ApertureType.Window, new List<ConstructionLayer>
            {
                new ConstructionLayer("Glass", 0.006),
                new ConstructionLayer("Air", 0.012),
                new ConstructionLayer("Glass", 0.004),
            });

            using (OpenStudioConversionResult conversion = AnalyticalModelFixtures.SingleBox(apertureConstructionOverride: window).ToOpenStudio())
            {
                global::OpenStudio.SubSurface subSurface = conversion.Model.getSubSurfaces().Single();
                List<double> native = subSurface.construction().get().to_Construction().get().layers().Select(x => Math.Round(x.thickness(), 6)).ToList();
                Assert.That(native, Is.Not.EqualTo(Enumerable.Reverse(native).ToList()), "the panes are asymmetric");

                OpenStudioImportResult result = conversion.Model.ToSAM();
                Aperture aperture = result.AnalyticalModel.AdjacencyCluster.GetPanels().SelectMany(x => x.Apertures ?? new List<Aperture>()).Single();
                Assert.That(aperture.ApertureConstruction.PaneConstructionLayers.Select(x => Math.Round(x.Thickness, 6)), Is.EqualTo(native), "OpenStudio's order, as before revision 11");
                Panel wall = result.AnalyticalModel.AdjacencyCluster.GetPanels().First(x => x.PanelType == PanelType.WallExternal);
                Assert.That(LayerNames(wall.Construction).First(), Does.Contain("Plasterboard"), "the opaque wall in the same model is read innermost first");
            }
        }

        /// <summary>
        /// An unstamped opaque construction used only by a subsurface (an opaque door pane) keeps OpenStudio's order, as
        /// before; the same model's walls are read innermost first.
        /// </summary>
        [Test]
        public void AnOpaqueDoorPane_KeepsOpenStudiosOrder()
        {
            using (OpenStudioConversionResult conversion = AnalyticalModelFixtures.SingleBox().ToOpenStudio())
            {
                List<global::OpenStudio.StandardOpaqueMaterial> wallLayers = Layers(OutdoorWall(conversion.Model));
                global::OpenStudio.Construction door = new global::OpenStudio.Construction(conversion.Model);
                door.setName("Order Door");
                global::OpenStudio.MaterialVector materialVector = new global::OpenStudio.MaterialVector();
                materialVector.Add(wallLayers[0]);
                materialVector.Add(wallLayers[1]);
                Assert.That(door.setLayers(materialVector), Is.True);
                Assert.That(conversion.Model.getSubSurfaces().Single().setConstruction(door), Is.True);

                OpenStudioImportResult result = conversion.Model.ToSAM();
                Aperture aperture = result.AnalyticalModel.AdjacencyCluster.GetPanels().SelectMany(x => x.Apertures ?? new List<Aperture>()).Single();
                List<ConstructionLayer> panes = aperture.ApertureConstruction.PaneConstructionLayers;
                Assert.That(panes.Count, Is.EqualTo(2));
                Assert.That(panes[0].Name, Does.Contain("Brick"), "OpenStudio's order: " + string.Join(", ", panes.Select(x => x.Name)));
                Assert.That(panes[1].Name, Does.Contain("Insulation"));
                Panel wall = result.AnalyticalModel.AdjacencyCluster.GetPanels().First(x => x.PanelType == PanelType.WallExternal);
                Assert.That(LayerNames(wall.Construction).First(), Does.Contain("Plasterboard"));
            }
        }

        private static global::OpenStudio.ShadingSurface AddShade(global::OpenStudio.Model model, global::OpenStudio.ConstructionBase construction)
        {
            global::OpenStudio.ShadingSurfaceGroup group = new global::OpenStudio.ShadingSurfaceGroup(model);
            global::OpenStudio.Point3dVector points = new global::OpenStudio.Point3dVector();
            points.Add(new global::OpenStudio.Point3d(0, -1, 3));
            points.Add(new global::OpenStudio.Point3d(0, -2, 3));
            points.Add(new global::OpenStudio.Point3d(5, -2, 3));
            points.Add(new global::OpenStudio.Point3d(5, -1, 3));
            global::OpenStudio.ShadingSurface shade = new global::OpenStudio.ShadingSurface(points, model);
            Assert.That(shade.setShadingSurfaceGroup(group), Is.True);
            Assert.That(shade.setConstruction(construction), Is.True);
            return shade;
        }

        private static Panel Shade(OpenStudioImportResult result)
        {
            return result.AnalyticalModel.AdjacencyCluster.GetPanels().Single(x => x.PanelType == PanelType.Shade);
        }

        /// <summary>A construction used only by a shading surface keeps OpenStudio's order.</summary>
        [Test]
        public void AShadingOnlyConstruction_KeepsOpenStudiosOrder()
        {
            using (OpenStudioConversionResult conversion = OpaqueFaceOpticsTests.Box(OpaqueFaceOpticsTests.Library(), AnalyticalModelFixtures.WallConstruction).ToOpenStudio())
            {
                List<global::OpenStudio.StandardOpaqueMaterial> wallLayers = Layers(OutdoorWall(conversion.Model));
                global::OpenStudio.Construction shadeConstruction = new global::OpenStudio.Construction(conversion.Model);
                shadeConstruction.setName("Order Shade");
                global::OpenStudio.MaterialVector materialVector = new global::OpenStudio.MaterialVector();
                materialVector.Add(wallLayers[0]);
                materialVector.Add(wallLayers[1]);
                Assert.That(shadeConstruction.setLayers(materialVector), Is.True);
                AddShade(conversion.Model, shadeConstruction);

                OpenStudioImportResult result = conversion.Model.ToSAM();
                List<string> layers = LayerNames(Shade(result).Construction);
                Assert.That(layers.Count, Is.EqualTo(2));
                Assert.That(layers[0], Does.Contain("Brick"), "OpenStudio's order: " + string.Join(", ", layers));
                Assert.That(layers[1], Does.Contain("Insulation"));
                Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OSI-APX-001" && d.Message.Contains("planar surfaces and by a subsurface or shading surface")), Is.False);
            }
        }

        /// <summary>
        /// A construction shared by walls and a shading surface is split: the walls get the innermost-first SAM construction
        /// with the restored Guid, the shade a separate one in OpenStudio's order with its own Guid; reported once.
        /// </summary>
        [Test]
        public void AConstructionSharedByWallsAndAShade_IsSplit_AndReported()
        {
            using (OpenStudioConversionResult conversion = OpaqueFaceOpticsTests.Box(OpaqueFaceOpticsTests.Library(), AnalyticalModelFixtures.WallConstruction).ToOpenStudio())
            {
                AddShade(conversion.Model, OutdoorWall(conversion.Model).construction().get());
                List<string> before = Signatures(conversion.Model);

                OpenStudioImportResult result = conversion.Model.ToSAM();
                Panel wall = result.AnalyticalModel.AdjacencyCluster.GetPanels().First(x => x.PanelType == PanelType.WallExternal);
                AssertLayers(wall.Construction, ("Plasterboard", 0.0125), ("Insulation", 0.1), ("Brick", 0.1));
                Assert.That(wall.Construction.Guid, Is.EqualTo(AnalyticalModelFixtures.WallConstruction.Guid));
                Construction shade = Shade(result).Construction;
                Assert.That(LayerNames(shade).First(), Does.Contain("Brick"), "OpenStudio's order: " + string.Join(", ", LayerNames(shade)));
                Assert.That(shade.Guid, Is.Not.EqualTo(wall.Construction.Guid));
                Assert.That(result.Diagnostics.Count(d => d.Code == "SAM-OSI-APX-001" && d.Message.Contains("planar surfaces and by a subsurface or shading surface")), Is.EqualTo(1), string.Join(" | ", result.Diagnostics));

                using (OpenStudioConversionResult reexport = result.AnalyticalModel.ToOpenStudio())
                {
                    AssertSameSignatures(before, Signatures(reexport.Model));
                }
            }
        }

        /// <summary>An air boundary keeps its layer-free SAM construction; the model's opaque walls are read innermost first.</summary>
        [Test]
        public void AnAirBoundary_IsUnchanged()
        {
            using (OpenStudioConversionResult conversion = AnalyticalModelFixtures.TwoAdjacentBoxes(sharedWallIsAir: true).ToOpenStudio())
            {
                OpenStudioImportResult result = conversion.Model.ToSAM();
                Panel air = result.AnalyticalModel.AdjacencyCluster.GetPanels().Single(x => x.PanelType == PanelType.Air);
                Assert.That(air.Construction?.ConstructionLayers ?? new List<ConstructionLayer>(), Is.Empty);
                Panel wall = result.AnalyticalModel.AdjacencyCluster.GetPanels().First(x => x.PanelType == PanelType.WallExternal);
                Assert.That(LayerNames(wall.Construction).First(), Does.Contain("Plasterboard"));
            }
        }

        /// <summary>A non-layered construction (F-factor ground floor) keeps its name-only placeholder, with no invented layers.</summary>
        [Test]
        public void ANonLayeredConstruction_IsUnchanged()
        {
            using (OpenStudioConversionResult conversion = OpaqueFaceOpticsTests.Box(OpaqueFaceOpticsTests.Library(), AnalyticalModelFixtures.WallConstruction).ToOpenStudio())
            {
                global::OpenStudio.FFactorGroundFloorConstruction fFactor = new global::OpenStudio.FFactorGroundFloorConstruction(conversion.Model);
                fFactor.setName("Order F-Factor");
                Assert.That(conversion.Model.getSurfaces().Single(x => x.surfaceType() == "Floor").setConstruction(fFactor), Is.True);

                OpenStudioImportResult result = conversion.Model.ToSAM();
                Panel floor = result.AnalyticalModel.AdjacencyCluster.GetPanels().Single(x => x.PanelType == PanelType.SlabOnGrade);
                Assert.That(floor.Construction.Name, Is.EqualTo("Order F-Factor"));
                Assert.That(floor.Construction.ConstructionLayers ?? new List<ConstructionLayer>(), Is.Empty);
                Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OSI-CON-001" && d.Message.Contains("not a layered construction")), Is.True);
            }
        }
    }
}
