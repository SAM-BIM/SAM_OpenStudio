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

        /// <summary>
        /// Two boxes whose shared wall is two halves: the south half of <see cref="sandwich"/>, the north half of
        /// <see cref="northHalf"/> (the same layers, its own Guid). <paramref name="divergent"/>: the optics library, whose
        /// Plasterboard and Brick faces differ; otherwise the fixture library, whose faces agree.
        /// </summary>
        private static AnalyticalModel TwoBoxesWithTwoSharedHalves(bool bFirst, bool divergent)
        {
            AnalyticalModel analyticalModel = OpaqueFaceOpticsTests.TwoBoxes(OpaqueFaceOpticsTests.Library(divergent), sandwich, bFirst);
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

        private static readonly Construction northHalf = new Construction(new Guid("66666666-0000-0000-0000-000000000004"), "Order Sandwich North", sandwich.ConstructionLayers);

        /// <summary>
        /// Places one shared half's two native constructions: its SAM-written forward construction on the surface in the space
        /// OpenStudio visits second when <paramref name="forwardOnSecond"/>, else first, and the unstamped reverse on the other
        /// side; then removes the forward construction's SAM stamp when <paramref name="unstamp"/>. Every placement is a mirror
        /// pair, as the writer makes them.
        /// </summary>
        private static void Place(global::OpenStudio.Model model, Guid half, bool forwardOnSecond, bool unstamp)
        {
            string second = model.getSpaces().ToList()[1].nameString();
            bool south = half == sandwich.Guid;
            List<global::OpenStudio.Surface> surfaces = model.getSurfaces().Where(x => x.outsideBoundaryCondition() == "Surface" && (x.vertices().Average(y => y.y()) < 2) == south).ToList();
            Assert.That(surfaces.Count, Is.EqualTo(2));
            global::OpenStudio.ConstructionBase forward = surfaces.Select(x => x.construction().get()).Single(x => Core.OpenStudio.Query.TryGetSAMGuid(x, out Guid guid) && guid == half);
            global::OpenStudio.ConstructionBase reverse = surfaces.Select(x => x.construction().get()).Single(x => !Core.OpenStudio.Query.TryGetSAMGuid(x, out Guid _));
            foreach (global::OpenStudio.Surface surface in surfaces)
            {
                bool inSecond = surface.space().get().nameString() == second;
                Assert.That(surface.setConstruction(inSecond == forwardOnSecond ? forward : reverse), Is.True);
            }

            if (unstamp)
            {
                forward.additionalProperties().remove();
            }
        }

        private static string SecondVisited(global::OpenStudio.Model model)
        {
            return model.getSpaces().ToList()[1].nameString().Contains("Space_B") ? "Space B" : "Space A";
        }

        private static int Count(OpenStudioImportResult result, string code, string fragment)
        {
            return result.Diagnostics.Count(d => d.Code == code && d.Message.Contains(fragment));
        }

        /// <summary>
        /// A mixed model, deterministic whatever order OpenStudio visits the spaces in: the stamped south half's forward
        /// construction is on the space visited second, so that space comes first in the imported order; the north half has no
        /// stamp and is built from the space visited first, so it is re-read from its partner (SAM-OSI-APX-001).
        /// <list type="bullet">
        /// <item><description>North reverse construction on the new first space, materials with differing faces: the re-read
        /// would put Brick's internal face where the native model has Brick's external-face object (0.9 / 0.7 / 0.7 written as
        /// 0.5 / 0.4 / 0.4), so the import fails with SAM-OSI-CON-002 naming the north half and returns no model.</description></item>
        /// <item><description>North forward construction on the new first space with differing faces, or equal faces: the
        /// re-read keeps every layer and used face, the import succeeds and the re-export reproduces every surface.</description></item>
        /// </list>
        /// </summary>
        [TestCase(false, true, false, false)]
        [TestCase(true, true, false, false)]
        [TestCase(false, true, true, true)]
        [TestCase(true, true, true, true)]
        [TestCase(false, false, false, true)]
        [TestCase(true, false, false, true)]
        public void AMixedModelWhoseUnstampedHalfIsReRead_KeepsItsUsedFaces_OrFailsNamed(bool bFirst, bool divergent, bool northForwardOnSecond, bool succeeds)
        {
            using (OpenStudioConversionResult conversion = TwoBoxesWithTwoSharedHalves(bFirst, divergent).ToOpenStudio())
            {
                Assert.That(conversion.IsValid, Is.True, string.Join(" | ", conversion.Diagnostics.Where(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)));
                Place(conversion.Model, sandwich.Guid, true, false);
                Place(conversion.Model, northHalf.Guid, northForwardOnSecond, true);
                string expectedFirst = SecondVisited(conversion.Model);
                List<string> before = Signatures(conversion.Model);

                OpenStudioImportResult result = conversion.Model.ToSAM();
                Assert.That(Count(result, "SAM-OSI-APX-001", "is read from that space's surface instead"), Is.EqualTo(1), string.Join(" | ", result.Diagnostics));
                List<Core.OpenStudio.OpenStudioDiagnostic> errors = result.Diagnostics.Where(d => d.Code == "SAM-OSI-CON-002").ToList();
                if (!succeeds)
                {
                    Assert.That(result.Successful, Is.False, "fail closed: no model when a used face cannot be kept");
                    Assert.That(result.AnalyticalModel, Is.Null);
                    Assert.That(errors.Count, Is.EqualTo(1), string.Join(" | ", result.Diagnostics));
                    Assert.That(errors[0].Severity, Is.EqualTo(Core.OpenStudio.OpenStudioDiagnosticSeverity.Error));
                    Assert.That(errors[0].Message, Does.Contain("Order_Sandwich_North").And.Contain("zone face"), errors[0].Message);
                    return;
                }

                Assert.That(result.Successful, Is.True, string.Join(" | ", result.Diagnostics));
                Assert.That(errors, Is.Empty, string.Join(" | ", errors));
                AdjacencyCluster adjacencyCluster = result.AnalyticalModel.AdjacencyCluster;
                Assert.That(adjacencyCluster.GetSpaces()[0].Name, Is.EqualTo(expectedFirst), "the stamped half's forward side decides the space order");
                List<Panel> halves = adjacencyCluster.GetPanels().Where(x => adjacencyCluster.GetSpaces(x)?.Count == 2).ToList();
                Assert.That(halves.Count, Is.EqualTo(2));
                Assert.That(halves.Select(x => adjacencyCluster.GetSpaces(x)[0].Name), Has.All.EqualTo(expectedFirst));

                using (OpenStudioConversionResult reexport = result.AnalyticalModel.ToOpenStudio())
                {
                    Assert.That(reexport.IsValid, Is.True, string.Join(" | ", reexport.Diagnostics.Where(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)));
                    AssertSameSignatures(before, Signatures(reexport.Model));
                }
            }
        }

        /// <summary>
        /// Contradictory forward sides: both halves SAM-written, the south forward on the space visited second and the north
        /// forward on the space visited first, so no space order puts both first (a warning). The visit order is kept and the
        /// south half is read from its other side: with differing faces that would move Brick's internal face onto a zone face,
        /// so the import fails with SAM-OSI-CON-002; with equal faces it keeps every used face and succeeds.
        /// </summary>
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, false, true)]
        public void ContradictoryForwardSides_KeepTheUsedFaces_OrFailNamed(bool bFirst, bool divergent, bool succeeds)
        {
            using (OpenStudioConversionResult conversion = TwoBoxesWithTwoSharedHalves(bFirst, divergent).ToOpenStudio())
            {
                Place(conversion.Model, sandwich.Guid, true, false);
                Place(conversion.Model, northHalf.Guid, false, false);
                List<string> before = Signatures(conversion.Model);

                OpenStudioImportResult result = conversion.Model.ToSAM();
                Assert.That(Count(result, "SAM-OSI-APX-001", "cannot all come first in one space order"), Is.EqualTo(1), string.Join(" | ", result.Diagnostics));
                Assert.That(Count(result, "SAM-OSI-APX-001", "is read from that space's surface instead"), Is.EqualTo(1), string.Join(" | ", result.Diagnostics));
                List<Core.OpenStudio.OpenStudioDiagnostic> errors = result.Diagnostics.Where(d => d.Code == "SAM-OSI-CON-002").ToList();
                if (!succeeds)
                {
                    Assert.That(result.Successful, Is.False, "fail closed");
                    Assert.That(errors.Count, Is.EqualTo(1), string.Join(" | ", result.Diagnostics));
                    Assert.That(errors[0].Message, Does.Contain("Order_Sandwich_Reverse").And.Contain("zone face"), errors[0].Message);
                    return;
                }

                Assert.That(result.Successful, Is.True, string.Join(" | ", result.Diagnostics));
                Assert.That(errors, Is.Empty);
                using (OpenStudioConversionResult reexport = result.AnalyticalModel.ToOpenStudio())
                {
                    Assert.That(reexport.IsValid, Is.True, string.Join(" | ", reexport.Diagnostics.Where(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)));
                    AssertSameSignatures(before, Signatures(reexport.Model));
                }
            }
        }

        /// <summary>
        /// An edited single-sided wall: an unstamped construction puts Brick's external-face object on the zone face, a role
        /// the writer would fill with Brick's internal face (0.5 / 0.4 / 0.4 instead of 0.9 / 0.7 / 0.7), so the import
        /// fails with SAM-OSI-CON-002 rather than change that zone face.
        /// </summary>
        [Test]
        public void AnUnstampedConstructionWithAFaceObjectInTheOtherRole_FailsNamed()
        {
            using (OpenStudioConversionResult conversion = OpaqueFaceOpticsTests.Box(OpaqueFaceOpticsTests.Library(), AnalyticalModelFixtures.WallConstruction).ToOpenStudio())
            {
                global::OpenStudio.Surface wall = OutdoorWall(conversion.Model);
                List<global::OpenStudio.StandardOpaqueMaterial> layers = Layers(wall);
                global::OpenStudio.Construction edited = new global::OpenStudio.Construction(conversion.Model);
                edited.setName("Order Edited Wall");
                global::OpenStudio.MaterialVector materialVector = new global::OpenStudio.MaterialVector();
                materialVector.Add(layers[1]);
                materialVector.Add(layers[0]);
                Assert.That(edited.setLayers(materialVector), Is.True, "Insulation outside, Brick's external-face object on the zone face");
                Assert.That(wall.setConstruction(edited), Is.True);

                OpenStudioImportResult result = conversion.Model.ToSAM();
                Assert.That(result.Successful, Is.False);
                List<Core.OpenStudio.OpenStudioDiagnostic> errors = result.Diagnostics.Where(d => d.Code == "SAM-OSI-CON-002").ToList();
                Assert.That(errors.Count, Is.EqualTo(1), string.Join(" | ", result.Diagnostics));
                Assert.That(errors[0].Message, Does.Contain("Order Edited Wall").And.Contain("zone face"), errors[0].Message);
            }
        }

        /// <summary>The same edit with the fixture library, whose faces agree: every role writes the same value, so the import succeeds and round-trips.</summary>
        [Test]
        public void AnUnstampedConstructionWithEqualFaceObjects_Succeeds()
        {
            using (OpenStudioConversionResult conversion = OpaqueFaceOpticsTests.Box(OpaqueFaceOpticsTests.Library(false), AnalyticalModelFixtures.WallConstruction).ToOpenStudio())
            {
                global::OpenStudio.Surface wall = OutdoorWall(conversion.Model);
                List<global::OpenStudio.StandardOpaqueMaterial> layers = Layers(wall);
                global::OpenStudio.Construction edited = new global::OpenStudio.Construction(conversion.Model);
                edited.setName("Order Edited Wall");
                global::OpenStudio.MaterialVector materialVector = new global::OpenStudio.MaterialVector();
                materialVector.Add(layers[1]);
                materialVector.Add(layers[0]);
                Assert.That(edited.setLayers(materialVector), Is.True);
                Assert.That(wall.setConstruction(edited), Is.True);
                List<string> before = Signatures(conversion.Model);

                OpenStudioImportResult result = conversion.Model.ToSAM();
                Assert.That(result.Successful, Is.True, string.Join(" | ", result.Diagnostics.Where(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)));
                Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OSI-CON-002"), Is.False);
                using (OpenStudioConversionResult reexport = result.AnalyticalModel.ToOpenStudio())
                {
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

        // ---- per-layer consistency of both sides (review of 89a2216) ---------------------------------------------------

        /// <summary>
        /// A copy of a SAM-written opaque object at another thickness: same thermal properties and absorptances, and the same
        /// source-material and face metadata (so it merges with the original), but no SAM identity of its own.
        /// </summary>
        private static global::OpenStudio.StandardOpaqueMaterial CopyAt(global::OpenStudio.Model model, global::OpenStudio.StandardOpaqueMaterial original, double thickness, string name)
        {
            global::OpenStudio.StandardOpaqueMaterial copy = new global::OpenStudio.StandardOpaqueMaterial(model);
            copy.setName(name);
            copy.setThickness(thickness);
            copy.setThermalConductivity(original.thermalConductivity());
            copy.setDensity(original.density());
            copy.setSpecificHeat(original.specificHeat());
            copy.setThermalAbsorptance(original.thermalAbsorptance());
            copy.setSolarAbsorptance(original.solarAbsorptance());
            copy.setVisibleAbsorptance(original.visibleAbsorptance());
            List<string> keys = new List<string> { Core.OpenStudio.OpenStudioIdentityKeys.MaterialGuid, Core.OpenStudio.OpenStudioIdentityKeys.MaterialFace };
            keys.AddRange(new[] { "InternalEmissivity", "ExternalEmissivity", "InternalSolarReflectance", "ExternalSolarReflectance", "InternalLightReflectance", "ExternalLightReflectance" }.Select(x => Core.OpenStudio.OpenStudioIdentityKeys.OpticPrefix + x));
            keys.AddRange(new[] { "ThermalAbsorptance", "SolarAbsorptance", "VisibleAbsorptance" }.Select(x => Core.OpenStudio.OpenStudioIdentityKeys.WrittenPrefix + x));
            foreach (string key in keys)
            {
                if (Core.OpenStudio.Query.TryGetSAMFeature(original, key, out string value))
                {
                    copy.additionalProperties().setFeature(key, value);
                }
            }

            return copy;
        }

        private static global::OpenStudio.Construction InterzoneConstruction(global::OpenStudio.Model model, bool forward)
        {
            return model.getSurfaces().Where(x => x.outsideBoundaryCondition() == "Surface").Select(x => x.construction().get().to_Construction().get()).First(x => x.nameString().Contains(forward ? "_Forward" : "_Reverse"));
        }

        private static void ReplaceLayer(global::OpenStudio.Construction construction, int index, global::OpenStudio.Material material)
        {
            List<global::OpenStudio.Material> layers = construction.layers().ToList();
            global::OpenStudio.MaterialVector materialVector = new global::OpenStudio.MaterialVector();
            for (int i = 0; i < layers.Count; i++)
            {
                materialVector.Add(i == index ? material : layers[i]);
            }

            Assert.That(construction.setLayers(materialVector), Is.True);
        }

        private static void AssertFailsNamed(OpenStudioImportResult result, string fragment)
        {
            Assert.That(result.Successful, Is.False, "fail closed: no model");
            List<Core.OpenStudio.OpenStudioDiagnostic> errors = result.Diagnostics.Where(d => d.Code == "SAM-OSI-CON-002").ToList();
            Assert.That(errors.Count, Is.EqualTo(1), string.Join(" | ", result.Diagnostics));
            Assert.That(errors[0].Severity, Is.EqualTo(Core.OpenStudio.OpenStudioDiagnosticSeverity.Error));
            Assert.That(errors[0].Message, Does.Contain(fragment), errors[0].Message);
        }

        private static void AssertRoundTrips(OpenStudioConversionResult conversion, List<string> before)
        {
            OpenStudioImportResult result = conversion.Model.ToSAM();
            Assert.That(result.Successful, Is.True, string.Join(" | ", result.Diagnostics.Where(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)));
            Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OSI-CON-002"), Is.False);
            using (OpenStudioConversionResult reexport = result.AnalyticalModel.ToOpenStudio())
            {
                Assert.That(reexport.IsValid, Is.True, string.Join(" | ", reexport.Diagnostics.Where(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)));
                AssertSameSignatures(before, Signatures(reexport.Model));
            }
        }

        /// <summary>
        /// Review of 89a2216 (reproduction): the interzone sandwich's 12.5 mm Plasterboard replaced on one side only by a 20 mm
        /// copy with the same source metadata. The two objects merge into one SAM material, so names alone matched and the
        /// import succeeded, re-exporting 12.5 mm on both sides. Each side's per-layer thickness is now compared with the
        /// panel's SAM layers: error SAM-OSI-CON-002 naming the 20 mm layer, and no model. Either side.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void AnInterzoneSideWithAnotherLayerThickness_FailsNamed(bool onForwardSide)
        {
            using (OpenStudioConversionResult conversion = OpaqueFaceOpticsTests.TwoBoxes(OpaqueFaceOpticsTests.Library(), sandwich).ToOpenStudio())
            {
                global::OpenStudio.Construction construction = InterzoneConstruction(conversion.Model, onForwardSide);
                List<global::OpenStudio.Material> layers = construction.layers().ToList();
                Assert.That(layers[1].nameString(), Does.Contain("Plasterboard_12.5mm"));
                ReplaceLayer(construction, 1, CopyAt(conversion.Model, layers[1].to_StandardOpaqueMaterial().get(), 0.02, "Reviewer_Plasterboard_20mm"));

                // The panel is built from the forward side, so the error names the other side's layer: the 20 mm copy when it
                // is on the reverse side, the original 12.5 mm object when the copy replaced the forward side's layer.
                OpenStudioImportResult result = conversion.Model.ToSAM();
                AssertFailsNamed(result, onForwardSide ? "is 0.0125 m thick but the SAM layer is 0.02 m" : "('Reviewer_Plasterboard_20mm') of the surface in space");
                if (!onForwardSide)
                {
                    Assert.That(result.Diagnostics.Single(d => d.Code == "SAM-OSI-CON-002").Message, Does.Contain("is 0.02 m thick but the SAM layer is 0.0125 m"));
                }
            }
        }

        // SAM inside → outside: one source material twice, at two thicknesses.
        private static readonly Construction repeated = new Construction(new Guid("66666666-0000-0000-0000-000000000005"), "Order Repeated", new List<ConstructionLayer>
        {
            new ConstructionLayer("Plasterboard", 0.0125),
            new ConstructionLayer("Insulation", 0.1),
            new ConstructionLayer("Plasterboard", 0.02),
        });

        /// <summary>
        /// One source material twice at different thicknesses in a mirrored interzone construction: valid, so the import keeps
        /// both thicknesses in place and round-trips, in both space orders (the equivalent-mirror control).
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ARepeatedMaterialAtTwoThicknesses_RoundTrips(bool bFirst)
        {
            using (OpenStudioConversionResult conversion = OpaqueFaceOpticsTests.TwoBoxes(OpaqueFaceOpticsTests.Library(), repeated, bFirst).ToOpenStudio())
            {
                Assert.That(conversion.IsValid, Is.True, string.Join(" | ", conversion.Diagnostics.Where(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)));
                List<string> before = Signatures(conversion.Model);
                OpenStudioImportResult result = conversion.Model.ToSAM();
                Assert.That(result.Successful, Is.True, string.Join(" | ", result.Diagnostics.Where(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)));
                AssertLayers(SharedPanel(result.AnalyticalModel).Construction, ("Plasterboard", 0.0125), ("Insulation", 0.1), ("Plasterboard", 0.02));
                AssertRoundTrips(conversion, before);
            }
        }

        /// <summary>
        /// The same repeated-material construction with the two Plasterboard objects swapped on one side only: the names (one
        /// merged SAM material) still match in order, but the thicknesses run the wrong way, so SAM-OSI-CON-002 and no model.
        /// </summary>
        [Test]
        public void ARepeatedMaterialWithSwappedThicknessesOnOneSide_FailsNamed()
        {
            using (OpenStudioConversionResult conversion = OpaqueFaceOpticsTests.TwoBoxes(OpaqueFaceOpticsTests.Library(), repeated).ToOpenStudio())
            {
                global::OpenStudio.Construction construction = InterzoneConstruction(conversion.Model, false);
                List<global::OpenStudio.Material> layers = construction.layers().ToList();
                Assert.That(layers[0].nameString(), Does.Contain("Plasterboard"));
                Assert.That(layers[2].nameString(), Does.Contain("Plasterboard"));
                global::OpenStudio.MaterialVector materialVector = new global::OpenStudio.MaterialVector();
                materialVector.Add(layers[2]);
                materialVector.Add(layers[1]);
                materialVector.Add(layers[0]);
                Assert.That(construction.setLayers(materialVector), Is.True, "the reverse side now runs 20 mm, Insulation, 12.5 mm");

                AssertFailsNamed(conversion.Model.ToSAM(), "m thick but the SAM layer is");
            }
        }

        /// <summary>
        /// Legitimate reuse of one source material at different thicknesses in unrelated constructions: Plasterboard 12.5 mm in
        /// the walls and 15 mm in the interzone construction (and the roof of a box) merge into one SAM material, each layer
        /// keeps its own thickness, and the import round-trips.
        /// </summary>
        [TestCase("box")]
        [TestCase("two")]
        public void OneMaterialAtDifferentThicknessesInUnrelatedConstructions_RoundTrips(string model)
        {
            Construction sandwich15 = new Construction(new Guid("66666666-0000-0000-0000-000000000006"), "Order Sandwich 15", new List<ConstructionLayer>
            {
                new ConstructionLayer("Insulation", 0.1),
                new ConstructionLayer("Plasterboard", 0.015),
                new ConstructionLayer("Brick", 0.1),
            });

            AnalyticalModel analyticalModel = model == "box"
                ? OpaqueFaceOpticsTests.Box(OpaqueFaceOpticsTests.Library(), AnalyticalModelFixtures.WallConstruction, roof: sandwich15)
                : OpaqueFaceOpticsTests.TwoBoxes(OpaqueFaceOpticsTests.Library(), sandwich15);
            using (OpenStudioConversionResult conversion = analyticalModel.ToOpenStudio())
            {
                Assert.That(conversion.Model.getStandardOpaqueMaterials().Count(x => x.nameString().Contains("Plasterboard")), Is.GreaterThanOrEqualTo(2), "a 12.5 mm and a 15 mm object");
                List<string> before = Signatures(conversion.Model);
                OpenStudioImportResult result = conversion.Model.ToSAM();
                Assert.That(result.AnalyticalModel.MaterialLibrary.GetMaterials().OfType<OpaqueMaterial>().Count(x => x.Name.Contains("Plasterboard")), Is.EqualTo(1), "one merged SAM material");
                AssertRoundTrips(conversion, before);
            }
        }

        /// <summary>
        /// The air-gap handling is unchanged: an interzone cavity wall (AirGap layer) imports with no consistency error, its
        /// cavity layer between the same layers, at the thickness the importer has always given a resistance-only layer with no
        /// recorded nominal thickness (0, `LayerThickness`). Its re-export is not asserted: that pre-existing 0 thickness is
        /// refused by the writer (recorded in the PR record, not changed here).
        /// </summary>
        [Test]
        public void AnInterzoneCavityWall_KeepsItsAirGapImport()
        {
            MaterialLibrary materialLibrary = OpaqueFaceOpticsTests.Library();
            materialLibrary.Add(AnalyticalModelFixtures.CreateCavityGasMaterial());
            using (OpenStudioConversionResult conversion = OpaqueFaceOpticsTests.TwoBoxes(materialLibrary, AnalyticalModelFixtures.CavityWallConstruction).ToOpenStudio())
            {
                Assert.That(conversion.IsValid, Is.True, string.Join(" | ", conversion.Diagnostics.Where(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)));
                Assert.That(conversion.Model.getAirGaps().Count, Is.GreaterThan(0));
                OpenStudioImportResult result = conversion.Model.ToSAM();
                Assert.That(result.Successful, Is.True, string.Join(" | ", result.Diagnostics.Where(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)));
                Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OSI-CON-002"), Is.False);
                AssertLayers(SharedPanel(result.AnalyticalModel).Construction, ("Plasterboard", 0.0125), ("Air_Cavity", 0), ("Brick", 0.1));
            }
        }

        /// <summary>
        /// The documented massless handling is unchanged: one massless object on both sides of the interzone wall imports at
        /// its nominal thickness, reported (SAM-OSI-APX-001), with no consistency error.
        /// </summary>
        [Test]
        public void AMasslessInterzoneLayer_KeepsItsDocumentedImport()
        {
            using (OpenStudioConversionResult conversion = OpaqueFaceOpticsTests.TwoBoxes(OpaqueFaceOpticsTests.Library(), sandwich).ToOpenStudio())
            {
                global::OpenStudio.MasslessOpaqueMaterial massless = new global::OpenStudio.MasslessOpaqueMaterial(conversion.Model);
                massless.setName("Order Massless");
                Assert.That(massless.setThermalResistance(2.5), Is.True);
                foreach (bool forward in new[] { true, false })
                {
                    global::OpenStudio.Construction construction = InterzoneConstruction(conversion.Model, forward);
                    int index = construction.layers().ToList().FindIndex(x => x.nameString().Contains("Plasterboard"));
                    ReplaceLayer(construction, index, massless);
                }

                OpenStudioImportResult result = conversion.Model.ToSAM();
                Assert.That(result.Successful, Is.True, string.Join(" | ", result.Diagnostics.Where(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)));
                Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OSI-CON-002"), Is.False);
                Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OSI-APX-001" && d.Message.Contains("Massless material")), Is.True);
                AssertLayers(SharedPanel(result.AnalyticalModel).Construction, ("Insulation", 0.1), ("Order Massless", 0.025), ("Brick", 0.1));
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
