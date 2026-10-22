// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using AnalyticalCreate = SAM.Analytical.Create;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// Opaque face optics (SAM_OpenStudio#33 revision 8). EnergyPlus opaque materials are single-sided and use only
    /// the outermost layer's outside face and the innermost layer's zone face. SAM stores layers inside → outside from
    /// the panel's first space, so SAM's first layer carries the internal-face optics (an <c>_Internal</c> variant
    /// when its faces differ) in both directions, every other layer the external ones. A single-layer construction
    /// whose faces differ is supported on the ground or an adiabatic boundary (zone face only) and is a named error
    /// (SAM-OS-MAT-003, unsuccessful conversion) on an outdoor or interzone surface. Every opaque object carries its
    /// source material Guid, face and authored face optics, so an import recovers both faces.
    /// </summary>
    [TestFixture]
    public class OpaqueFaceOpticsTests
    {
        private const string MAT_FACE = "SAM-OS-MAT-003";

        // Plasterboard: external 0.9 / 0.5 / 0.5 (ε, ρs, ρv; fixture), internal 0.8 / 0.7 / 0.75.
        // Brick: external 0.9 / 0.3 / 0.3 (fixture), internal 0.5 / 0.6 / 0.6.
        private static readonly Construction single = new Construction(new Guid("55555555-0000-0000-0000-000000000001"), "Single Plasterboard", new List<ConstructionLayer> { new ConstructionLayer("Plasterboard", 0.0125) });
        private static readonly Construction singleAgreeing = new Construction(new Guid("55555555-0000-0000-0000-000000000002"), "Single Insulation", new List<ConstructionLayer> { new ConstructionLayer("Insulation", 0.1) });
        private static readonly Construction sandwich = new Construction(new Guid("55555555-0000-0000-0000-000000000003"), "Sandwich", new List<ConstructionLayer>
        {
            new ConstructionLayer("Insulation", 0.1),
            new ConstructionLayer("Plasterboard", 0.0125),
            new ConstructionLayer("Brick", 0.1),
        });

        private static Geometry.Spatial.Face3D F(params double[] c)
        {
            List<Geometry.Spatial.Point3D> point3Ds = new List<Geometry.Spatial.Point3D>();
            for (int i = 0; i < c.Length; i += 3)
            {
                point3Ds.Add(new Geometry.Spatial.Point3D(c[i], c[i + 1], c[i + 2]));
            }

            return new Geometry.Spatial.Face3D(new Geometry.Spatial.Polygon3D(point3Ds));
        }

        internal static MaterialLibrary Library(bool divergent = true)
        {
            MaterialLibrary result = AnalyticalModelFixtures.CreateMaterialLibrary();
            if (!divergent)
            {
                return result;
            }

            OpaqueMaterial plasterboard = result.GetMaterial("Plasterboard") as OpaqueMaterial;
            plasterboard.SetValue(OpaqueMaterialParameter.InternalEmissivity, 0.8);
            plasterboard.SetValue(OpaqueMaterialParameter.InternalSolarReflectance, 0.7);
            plasterboard.SetValue(OpaqueMaterialParameter.InternalLightReflectance, 0.75);
            result.Add(plasterboard);

            OpaqueMaterial brick = result.GetMaterial("Brick") as OpaqueMaterial;
            brick.SetValue(OpaqueMaterialParameter.InternalEmissivity, 0.5);
            brick.SetValue(OpaqueMaterialParameter.InternalSolarReflectance, 0.6);
            brick.SetValue(OpaqueMaterialParameter.InternalLightReflectance, 0.6);
            result.Add(brick);
            return result;
        }

        /// <summary>One box (x 0–5, y 0–4, z 0–3): walls, floor (ground unless adiabatic) and roof constructions given separately.</summary>
        internal static AnalyticalModel Box(MaterialLibrary materialLibrary, Construction walls, Construction floor = null, Construction roof = null, bool adiabaticRoof = false, bool roofFirst = false)
        {
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster();
            Space space = new Space(new Guid("cccccccc-0000-0000-0000-0000000000ee"), "Space Optics", new Geometry.Spatial.Point3D(2.5, 2, 1.5));
            space.SetValue(SpaceParameter.Area, 20.0);
            space.SetValue(SpaceParameter.Volume, 60.0);
            space.InternalCondition = AnalyticalModelFixtures.CreateOfficeInternalCondition();
            adjacencyCluster.AddObject(space);

            Panel floorPanel = AnalyticalCreate.Panel(floor ?? walls, PanelType.SlabOnGrade, F(0, 0, 0, 5, 0, 0, 5, 4, 0, 0, 4, 0));
            Panel roofPanel = AnalyticalCreate.Panel(roof ?? walls, PanelType.Roof, F(0, 0, 3, 5, 0, 3, 5, 4, 3, 0, 4, 3));
            if (adiabaticRoof)
            {
                roofPanel.SetValue(PanelParameter.Adiabatic, true);
            }

            List<Panel> panels = roofFirst ? new List<Panel> { roofPanel, floorPanel } : new List<Panel> { floorPanel, roofPanel };
            panels.Add(AnalyticalCreate.Panel(walls, PanelType.WallExternal, F(0, 0, 0, 5, 0, 0, 5, 0, 3, 0, 0, 3)));
            panels.Add(AnalyticalCreate.Panel(walls, PanelType.WallExternal, F(0, 0, 0, 0, 4, 0, 0, 4, 3, 0, 0, 3)));
            panels.Add(AnalyticalCreate.Panel(walls, PanelType.WallExternal, F(0, 4, 0, 5, 4, 0, 5, 4, 3, 0, 4, 3)));
            panels.Add(AnalyticalCreate.Panel(walls, PanelType.WallExternal, F(5, 0, 0, 5, 4, 0, 5, 4, 3, 5, 0, 3)));
            foreach (Panel panel in panels)
            {
                adjacencyCluster.AddObject(panel);
                adjacencyCluster.AddRelation(space, panel);
            }

            return new AnalyticalModel("Optics Box", "revision 8 fixture", null, null, adjacencyCluster, materialLibrary, AnalyticalModelFixtures.CreateProfileLibrary());
        }

        /// <summary>
        /// Two boxes sharing the wall at x = 5 (construction <paramref name="shared"/>); <paramref name="bFirst"/> adds space B
        /// to the cluster first.
        /// </summary>
        internal static AnalyticalModel TwoBoxes(MaterialLibrary materialLibrary, Construction shared, bool bFirst = false)
        {
            Construction wall = AnalyticalModelFixtures.WallConstruction;
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster();
            Space spaceA = new Space(new Guid("aaaaaaaa-0000-0000-0000-0000000000ee"), "Space A", new Geometry.Spatial.Point3D(2.25, 2, 1.5));
            Space spaceB = new Space(new Guid("bbbbbbbb-0000-0000-0000-0000000000ee"), "Space B", new Geometry.Spatial.Point3D(7.5, 2, 1.5));
            foreach (Space space in new[] { spaceA, spaceB })
            {
                space.SetValue(SpaceParameter.Area, 20.0);
                space.SetValue(SpaceParameter.Volume, 60.0);
                space.InternalCondition = AnalyticalModelFixtures.CreateOfficeInternalCondition();
            }

            adjacencyCluster.AddObject(bFirst ? spaceB : spaceA);
            adjacencyCluster.AddObject(bFirst ? spaceA : spaceB);

            Panel sharedWall = AnalyticalCreate.Panel(shared, PanelType.WallInternal, F(5, 0, 0, 5, 4, 0, 5, 4, 3, 5, 0, 3));
            List<Panel> panelsA = new List<Panel>
            {
                AnalyticalCreate.Panel(wall, PanelType.SlabOnGrade, F(0, 0, 0, 5, 0, 0, 5, 4, 0, 0, 4, 0)),
                AnalyticalCreate.Panel(wall, PanelType.Roof, F(0, 0, 3, 5, 0, 3, 5, 4, 3, 0, 4, 3)),
                AnalyticalCreate.Panel(wall, PanelType.WallExternal, F(0, 0, 0, 5, 0, 0, 5, 0, 3, 0, 0, 3)),
                AnalyticalCreate.Panel(wall, PanelType.WallExternal, F(0, 0, 0, 0, 4, 0, 0, 4, 3, 0, 0, 3)),
                AnalyticalCreate.Panel(wall, PanelType.WallExternal, F(0, 4, 0, 5, 4, 0, 5, 4, 3, 0, 4, 3)),
            };

            List<Panel> panelsB = new List<Panel>
            {
                AnalyticalCreate.Panel(wall, PanelType.SlabOnGrade, F(5, 0, 0, 10, 0, 0, 10, 4, 0, 5, 4, 0)),
                AnalyticalCreate.Panel(wall, PanelType.Roof, F(5, 0, 3, 10, 0, 3, 10, 4, 3, 5, 4, 3)),
                AnalyticalCreate.Panel(wall, PanelType.WallExternal, F(5, 0, 0, 10, 0, 0, 10, 0, 3, 5, 0, 3)),
                AnalyticalCreate.Panel(wall, PanelType.WallExternal, F(10, 0, 0, 10, 4, 0, 10, 4, 3, 10, 0, 3)),
                AnalyticalCreate.Panel(wall, PanelType.WallExternal, F(5, 4, 0, 10, 4, 0, 10, 4, 3, 5, 4, 3)),
            };

            adjacencyCluster.AddObject(sharedWall);
            adjacencyCluster.AddRelation(spaceA, sharedWall);
            adjacencyCluster.AddRelation(spaceB, sharedWall);
            foreach (Panel panel in panelsA)
            {
                adjacencyCluster.AddObject(panel);
                adjacencyCluster.AddRelation(spaceA, panel);
            }

            foreach (Panel panel in panelsB)
            {
                adjacencyCluster.AddObject(panel);
                adjacencyCluster.AddRelation(spaceB, panel);
            }

            return new AnalyticalModel("Optics Two Boxes", "revision 8 fixture", null, null, adjacencyCluster, materialLibrary, AnalyticalModelFixtures.CreateProfileLibrary());
        }

        private static List<global::OpenStudio.StandardOpaqueMaterial> Layers(global::OpenStudio.Surface surface)
        {
            global::OpenStudio.OptionalConstructionBase optionalConstructionBase = surface.construction();
            Assert.That(optionalConstructionBase != null && !optionalConstructionBase.isNull(), surface.nameString() + " has no construction");
            global::OpenStudio.OptionalConstruction optionalConstruction = optionalConstructionBase.get().to_Construction();
            return optionalConstruction.get().layers().Select(x => x.to_StandardOpaqueMaterial().get()).ToList();
        }

        private static bool HasConstruction(global::OpenStudio.Surface surface)
        {
            global::OpenStudio.OptionalConstructionBase optionalConstructionBase = surface.construction();
            return optionalConstructionBase != null && !optionalConstructionBase.isNull();
        }

        /// <summary>ε, ρs, ρv of a written opaque material.</summary>
        private static double[] Optics(global::OpenStudio.StandardOpaqueMaterial material)
        {
            return new[] { material.thermalAbsorptance(), 1 - material.solarAbsorptance(), 1 - material.visibleAbsorptance() };
        }

        private static readonly double[] plasterboardExternal = { 0.9, 0.5, 0.5 };
        private static readonly double[] plasterboardInternal = { 0.8, 0.7, 0.75 };
        private static readonly double[] brickExternal = { 0.9, 0.3, 0.3 };

        private static string Feature(global::OpenStudio.ModelObject modelObject, string key)
        {
            return Core.OpenStudio.Query.TryGetSAMFeature(modelObject, key, out string value) ? value : null;
        }

        private static global::OpenStudio.Surface Single(global::OpenStudio.Model model, Func<global::OpenStudio.Surface, bool> predicate)
        {
            return model.getSurfaces().Single(predicate);
        }

        private static bool OfSpace(global::OpenStudio.Surface surface, string spaceName)
        {
            return surface.space().get().nameString().Contains(spaceName.Replace(" ", "_")) || surface.space().get().nameString().Contains(spaceName);
        }

        // ---- writer ----------------------------------------------------------------------------------------------

        [Test]
        public void TheZoneFace_CarriesTheInternalVariant_AndTheOutdoorFaceTheExternalValues()
        {
            using (OpenStudioConversionResult result = Box(Library(), AnalyticalModelFixtures.WallConstruction).ToOpenStudio())
            {
                Assert.That(result.IsValid, Is.True, string.Join(" | ", result.Diagnostics));
                foreach (global::OpenStudio.Surface surface in result.Model.getSurfaces())
                {
                    List<global::OpenStudio.StandardOpaqueMaterial> layers = Layers(surface);
                    Assert.That(layers.Last().nameString(), Does.Contain("Plasterboard_12.5mm_Internal"), surface.nameString() + ": SAM's first layer is the zone face");
                    Assert.That(Optics(layers.Last()), Is.EqualTo(plasterboardInternal).Within(1e-12), surface.nameString());
                    Assert.That(layers.First().nameString(), Does.Not.Contain("_Internal"));
                    Assert.That(Optics(layers.First()), Is.EqualTo(brickExternal).Within(1e-12), surface.nameString() + ": the outdoor face keeps the external values");
                }

                Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OS-MAT-002" && d.Message.Contains("Internal-side")), Is.False, "the zone face is represented, not approximated");
            }
        }

        [Test]
        public void InterzoneConstructions_AreExactMirrors_AndEachZoneFaceIsCorrect()
        {
            using (OpenStudioConversionResult result = TwoBoxes(Library(), AnalyticalModelFixtures.WallConstruction).ToOpenStudio())
            {
                Assert.That(result.IsValid, Is.True, string.Join(" | ", result.Diagnostics));
                global::OpenStudio.Surface sideA = Single(result.Model, x => x.outsideBoundaryCondition() == "Surface" && OfSpace(x, "Space A"));
                global::OpenStudio.Surface sideB = Single(result.Model, x => x.outsideBoundaryCondition() == "Surface" && OfSpace(x, "Space B"));
                List<string> namesA = Layers(sideA).Select(x => x.nameString()).ToList();
                List<string> namesB = Layers(sideB).Select(x => x.nameString()).ToList();
                Assert.That(namesB, Is.EqualTo(Enumerable.Reverse(namesA).ToList()), "the reverse construction lists the same objects in mirror order");
                Assert.That(Optics(Layers(sideA).Last()), Is.EqualTo(plasterboardInternal).Within(1e-12), "space A (first) sees SAM's first layer's internal face");
                Assert.That(Optics(Layers(sideB).Last()), Is.EqualTo(brickExternal).Within(1e-12), "space B sees SAM's last layer's external face");
            }
        }

        [Test]
        public void TheForwardSide_FollowsTheFirstSpaceInGetSpacesOrder()
        {
            using (OpenStudioConversionResult result = TwoBoxes(Library(), AnalyticalModelFixtures.WallConstruction, bFirst: true).ToOpenStudio())
            {
                Assert.That(result.IsValid, Is.True, string.Join(" | ", result.Diagnostics));
                global::OpenStudio.Surface sideA = Single(result.Model, x => x.outsideBoundaryCondition() == "Surface" && OfSpace(x, "Space A"));
                global::OpenStudio.Surface sideB = Single(result.Model, x => x.outsideBoundaryCondition() == "Surface" && OfSpace(x, "Space B"));
                Assert.That(sideB.construction().get().nameString(), Does.Contain("_Forward"), "space B is first in the cluster");
                Assert.That(sideA.construction().get().nameString(), Does.Contain("_Reverse"));
                Assert.That(Optics(Layers(sideB).Last()), Is.EqualTo(plasterboardInternal).Within(1e-12));
                Assert.That(Optics(Layers(sideA).Last()), Is.EqualTo(brickExternal).Within(1e-12));
            }
        }

        /// <summary>
        /// The forward construction goes to the first space's side only. When that side produced no surface the
        /// remaining side is refused, never given the forward construction (unit level: no realistic SAM model was
        /// found that drops only the first space's side of a related panel; see the record).
        /// </summary>
        [Test]
        public void AMissingFirstSpaceSide_IsANamedError_NeverASwappedSide()
        {
            Space spaceA = new Space(new Guid("aaaaaaaa-0000-0000-0000-0000000000ef"), "Space A", null);
            Guid spaceB = new Guid("bbbbbbbb-0000-0000-0000-0000000000ef");
            Assert.That(Convert.ForwardSideProblem(spaceA, new[] { spaceA.Guid, spaceB }), Is.Null, "both sides present");
            Assert.That(Convert.ForwardSideProblem(spaceA, new[] { spaceA.Guid }), Is.Null, "the first side alone keeps its orientation");
            Assert.That(Convert.ForwardSideProblem(null, new[] { spaceB }), Is.Null, "no ordered space");
            Assert.That(Convert.ForwardSideProblem(spaceA, new[] { spaceB }), Does.Contain("first space ('Space A')").And.Contain("never swapped"));
        }

        [Test]
        public void AMaterialOnTheZoneFaceAndInsideAnotherConstruction_HasTwoThermallyIdenticalObjects()
        {
            using (OpenStudioConversionResult result = Box(Library(), AnalyticalModelFixtures.WallConstruction, roof: sandwich).ToOpenStudio())
            {
                Assert.That(result.IsValid, Is.True, string.Join(" | ", result.Diagnostics));
                List<global::OpenStudio.StandardOpaqueMaterial> plasterboards = result.Model.getStandardOpaqueMaterials().Where(x => x.nameString().Contains("Plasterboard")).OrderBy(x => x.nameString()).ToList();
                Assert.That(plasterboards.Select(x => x.nameString()), Has.Exactly(1).Contains("_Internal"));
                Assert.That(plasterboards.Count, Is.EqualTo(2));
                Assert.That(plasterboards[0].thermalConductivity(), Is.EqualTo(plasterboards[1].thermalConductivity()));
                Assert.That(plasterboards[0].density(), Is.EqualTo(plasterboards[1].density()));
                Assert.That(plasterboards[0].specificHeat(), Is.EqualTo(plasterboards[1].specificHeat()));
                Assert.That(plasterboards[0].thickness(), Is.EqualTo(plasterboards[1].thickness()));
                Assert.That(Optics(plasterboards.Single(x => !x.nameString().Contains("_Internal"))), Is.EqualTo(plasterboardExternal).Within(1e-12));
            }
        }

        [Test]
        public void AgreeingFaces_CreateNoVariant()
        {
            using (OpenStudioConversionResult result = TwoBoxes(Library(false), AnalyticalModelFixtures.WallConstruction).ToOpenStudio())
            {
                Assert.That(result.IsValid, Is.True);
                Assert.That(result.Model.getStandardOpaqueMaterials().Select(x => x.nameString()), Has.None.Contains("_Internal"));
                Assert.That(result.Model.getStandardOpaqueMaterials().Count, Is.EqualTo(3));
            }
        }

        [Test]
        public void EveryOpaqueObject_CarriesItsSourceFaceAndTheAuthoredFaceOptics()
        {
            Guid plasterboard = Library().GetMaterial("Plasterboard").Guid;
            using (OpenStudioConversionResult result = Box(Library(), AnalyticalModelFixtures.WallConstruction, roof: sandwich).ToOpenStudio())
            {
                foreach (global::OpenStudio.StandardOpaqueMaterial material in result.Model.getStandardOpaqueMaterials().Where(x => x.nameString().Contains("Plasterboard")))
                {
                    Assert.That(Feature(material, Core.OpenStudio.OpenStudioIdentityKeys.MaterialGuid), Is.EqualTo(plasterboard.ToString("D")));
                    Assert.That(Feature(material, Core.OpenStudio.OpenStudioIdentityKeys.MaterialFace), Is.EqualTo(material.nameString().Contains("_Internal") ? "Internal" : "External"));
                    Assert.That(Feature(material, Core.OpenStudio.OpenStudioIdentityKeys.OpticPrefix + "InternalLightReflectance"), Is.EqualTo("0.75"));
                    Assert.That(Feature(material, Core.OpenStudio.OpenStudioIdentityKeys.OpticPrefix + "ExternalLightReflectance"), Is.EqualTo("0.5"));
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ASingleLayerGroundOrAdiabaticConstruction_UsesTheZoneFace(bool adiabatic)
        {
            AnalyticalModel model = adiabatic
                ? Box(Library(), AnalyticalModelFixtures.WallConstruction, roof: single, adiabaticRoof: true)
                : Box(Library(), AnalyticalModelFixtures.WallConstruction, floor: single);
            using (OpenStudioConversionResult result = model.ToOpenStudio())
            {
                Assert.That(result.IsValid, Is.True, string.Join(" | ", result.Diagnostics));
                global::OpenStudio.Surface surface = Single(result.Model, x => x.outsideBoundaryCondition() == (adiabatic ? "Adiabatic" : "Ground"));
                List<global::OpenStudio.StandardOpaqueMaterial> layers = Layers(surface);
                Assert.That(layers.Count, Is.EqualTo(1));
                Assert.That(Optics(layers[0]), Is.EqualTo(plasterboardInternal).Within(1e-12));
            }
        }

        [Test]
        public void ASingleLayerOutdoorConstructionWithDifferentFaces_IsANamedErrorAndAnUnsuccessfulConversion()
        {
            using (OpenStudioConversionResult result = Box(Library(), single, floor: AnalyticalModelFixtures.WallConstruction, roof: AnalyticalModelFixtures.WallConstruction).ToOpenStudio())
            {
                List<Core.OpenStudio.OpenStudioDiagnostic> errors = result.Diagnostics.Where(d => d.Code == MAT_FACE).ToList();
                Assert.That(errors.Count, Is.EqualTo(4), "one per outdoor wall");
                Assert.That(errors.All(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error && d.Message.Contains("'Single Plasterboard'") && d.Message.Contains("Outdoors") && d.Message.Contains("Emissivity")), Is.True, string.Join(" | ", errors));
                Assert.That(result.IsValid, Is.False);
                Assert.That(result.Model.getSurfaces().Where(x => x.outsideBoundaryCondition() == "Outdoors" && x.surfaceType() == "Wall").All(x => !HasConstruction(x)), Is.True, "no external-valued substitute is written");
            }
        }

        [Test]
        public void ASingleLayerInterzoneConstructionWithDifferentFaces_IsANamedError()
        {
            using (OpenStudioConversionResult result = TwoBoxes(Library(), single).ToOpenStudio())
            {
                Assert.That(result.Diagnostics.Count(d => d.Code == MAT_FACE && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error && d.Message.Contains("Surface")), Is.EqualTo(1), string.Join(" | ", result.Diagnostics));
                Assert.That(result.IsValid, Is.False);
                Assert.That(result.Model.getSurfaces().Where(x => x.outsideBoundaryCondition() == "Surface").All(x => !HasConstruction(x)), Is.True);
            }
        }

        /// <summary>The same single-layer construction on the ground (supported) and on an outdoor roof (refused): each use is judged on its own, whichever is converted first.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void TheSingleLayerRule_IsCheckedPerUse_BeforeTheCachedConstructionIsReused(bool roofFirst)
        {
            using (OpenStudioConversionResult result = Box(Library(), AnalyticalModelFixtures.WallConstruction, floor: single, roof: single, roofFirst: roofFirst).ToOpenStudio())
            {
                Assert.That(result.Diagnostics.Count(d => d.Code == MAT_FACE), Is.EqualTo(1), string.Join(" | ", result.Diagnostics));
                Assert.That(HasConstruction(Single(result.Model, x => x.outsideBoundaryCondition() == "Ground")), Is.True, "the ground use is supported");
                Assert.That(HasConstruction(Single(result.Model, x => x.surfaceType() == "RoofCeiling")), Is.False, "the outdoor use is refused");
                Assert.That(result.IsValid, Is.False);
            }
        }

        [Test]
        public void ASingleLayerConstructionWithAgreeingFaces_IsWrittenNormally()
        {
            using (OpenStudioConversionResult result = Box(Library(), singleAgreeing).ToOpenStudio())
            {
                Assert.That(result.IsValid, Is.True, string.Join(" | ", result.Diagnostics));
                Assert.That(result.Diagnostics.Any(d => d.Code == MAT_FACE), Is.False);
                Assert.That(result.Model.getSurfaces().All(HasConstruction), Is.True);
            }
        }

        // ---- importer --------------------------------------------------------------------------------------------

        private static OpaqueMaterial Imported(OpenStudioImportResult result, string fragment)
        {
            return result.AnalyticalModel.MaterialLibrary.GetMaterials().OfType<OpaqueMaterial>().Single(x => x.Name.Contains(fragment));
        }

        private static double Value(OpaqueMaterial material, OpaqueMaterialParameter parameter)
        {
            Assert.That(material.TryGetValue(parameter, out double value), Is.True, material.Name + " " + parameter);
            return value;
        }

        private static void AssertPlasterboardFaces(OpaqueMaterial material)
        {
            Assert.That(Value(material, OpaqueMaterialParameter.ExternalEmissivity), Is.EqualTo(0.9).Within(1e-12));
            Assert.That(Value(material, OpaqueMaterialParameter.ExternalSolarReflectance), Is.EqualTo(0.5).Within(1e-12));
            Assert.That(Value(material, OpaqueMaterialParameter.ExternalLightReflectance), Is.EqualTo(0.5).Within(1e-12));
            Assert.That(Value(material, OpaqueMaterialParameter.InternalEmissivity), Is.EqualTo(0.8).Within(1e-12));
            Assert.That(Value(material, OpaqueMaterialParameter.InternalSolarReflectance), Is.EqualTo(0.7).Within(1e-12));
            Assert.That(Value(material, OpaqueMaterialParameter.InternalLightReflectance), Is.EqualTo(0.75).Within(1e-12));
        }

        private static readonly Construction sandwich15 = new Construction(new Guid("55555555-0000-0000-0000-000000000004"), "Sandwich 15", new List<ConstructionLayer>
        {
            new ConstructionLayer("Insulation", 0.1),
            new ConstructionLayer("Plasterboard", 0.015),
            new ConstructionLayer("Brick", 0.1),
        });

        /// <summary>Import warnings about face metadata, edits and merges (SAM-OSI-ID-001 / SAM-OSI-MAT-001).</summary>
        private static List<Core.OpenStudio.OpenStudioDiagnostic> FaceWarnings(OpenStudioImportResult result)
        {
            return result.Diagnostics.Where(d => (d.Code == "SAM-OSI-ID-001" || d.Code == "SAM-OSI-MAT-001") && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning).ToList();
        }

        private static List<OpaqueMaterial> ImportedAll(OpenStudioImportResult result, string fragment)
        {
            return result.AnalyticalModel.MaterialLibrary.GetMaterials().OfType<OpaqueMaterial>().Where(x => x.Name.Contains(fragment)).ToList();
        }

        /// <summary>The written opaque objects of source material <paramref name="source"/> on face <paramref name="face"/>.</summary>
        private static List<global::OpenStudio.StandardOpaqueMaterial> FaceObjects(global::OpenStudio.Model model, Guid source, string face)
        {
            return model.getStandardOpaqueMaterials().Where(x => Feature(x, Core.OpenStudio.OpenStudioIdentityKeys.MaterialGuid) == source.ToString("D") && Feature(x, Core.OpenStudio.OpenStudioIdentityKeys.MaterialFace) == face).ToList();
        }

        private static MaterialLibrary LibraryWithoutPlasterboardLight()
        {
            MaterialLibrary materialLibrary = Library();
            OpaqueMaterial plasterboard = materialLibrary.GetMaterial("Plasterboard") as OpaqueMaterial;
            plasterboard.RemoveValue(OpaqueMaterialParameter.ExternalLightReflectance);
            plasterboard.RemoveValue(OpaqueMaterialParameter.InternalLightReflectance);
            materialLibrary.Add(plasterboard);
            return materialLibrary;
        }

        [Test]
        public void TheImport_RecoversBothFaces_FromTheInternalVariantAlone()
        {
            Guid plasterboard = Library().GetMaterial("Plasterboard").Guid;
            using (OpenStudioConversionResult conversion = Box(Library(), AnalyticalModelFixtures.WallConstruction).ToOpenStudio())
            {
                Assert.That(conversion.Model.getStandardOpaqueMaterials().Where(x => x.nameString().Contains("Plasterboard")).Select(x => x.nameString()), Has.All.Contains("_Internal"), "only the internal variant is referenced");
                OpenStudioImportResult result = conversion.Model.ToSAM();
                OpaqueMaterial material = Imported(result, "Plasterboard");
                AssertPlasterboardFaces(material);
                Assert.That(material.Guid, Is.EqualTo(plasterboard), "the source Guid is restored from the explicit identity");
                Assert.That(FaceWarnings(result), Is.Empty);
            }
        }

        /// <summary>
        /// Review of 901e592a: face and thickness objects of one SAM material are merged by their explicit source Guid into
        /// one SAM material with both faces, the source Guid, and each construction layer's own thickness.
        /// </summary>
        [Test]
        public void FaceAndThicknessObjectsOfOneSource_MergeIntoOneSamMaterial_KeepingLayerThicknesses()
        {
            Guid plasterboard = Library().GetMaterial("Plasterboard").Guid;
            using (OpenStudioConversionResult conversion = Box(Library(), AnalyticalModelFixtures.WallConstruction, roof: sandwich15).ToOpenStudio())
            {
                Assert.That(conversion.Model.getStandardOpaqueMaterials().Count(x => x.nameString().Contains("Plasterboard")), Is.EqualTo(2), "an internal 12.5 mm object and an external 15 mm object");
                OpenStudioImportResult result = conversion.Model.ToSAM();
                List<OpaqueMaterial> plasterboards = ImportedAll(result, "Plasterboard");
                Assert.That(plasterboards.Count, Is.EqualTo(1));
                AssertPlasterboardFaces(plasterboards[0]);
                Assert.That(plasterboards[0].Guid, Is.EqualTo(plasterboard));
                Assert.That(FaceWarnings(result), Is.Empty, string.Join(" | ", result.Diagnostics));

                List<ConstructionLayer> layers = result.AnalyticalModel.AdjacencyCluster.GetConstructions().SelectMany(x => x.ConstructionLayers).Where(x => x.Name == plasterboards[0].Name).ToList();
                Assert.That(layers.Select(x => Math.Round(x.Thickness, 6)).Distinct().OrderBy(x => x), Is.EqualTo(new[] { 0.0125, 0.015 }), "every construction references the merged material at its own thickness");
            }
        }

        [Test]
        public void FaceObjectsWithDifferentThermalProperties_AreNotMerged_AndDiagnosed()
        {
            using (OpenStudioConversionResult conversion = Box(Library(), AnalyticalModelFixtures.WallConstruction, roof: sandwich15).ToOpenStudio())
            {
                conversion.Model.getStandardOpaqueMaterials().Single(x => x.nameString().Contains("Plasterboard_15mm")).setDensity(950);
                OpenStudioImportResult result = conversion.Model.ToSAM();
                Assert.That(FaceWarnings(result).Count(d => d.Code == "SAM-OSI-ID-001" && d.Message.Contains("are not merged: their thermal properties differ")), Is.EqualTo(1), string.Join(" | ", result.Diagnostics));
                List<OpaqueMaterial> plasterboards = ImportedAll(result, "Plasterboard");
                Assert.That(plasterboards.Count, Is.EqualTo(2));
                plasterboards.ForEach(AssertPlasterboardFaces);
            }
        }

        [Test]
        public void FaceObjectsWithDifferentAuthoredOptics_AreNotMerged_AndDiagnosed()
        {
            using (OpenStudioConversionResult conversion = Box(Library(), AnalyticalModelFixtures.WallConstruction, roof: sandwich).ToOpenStudio())
            {
                conversion.Model.getStandardOpaqueMaterials().Single(x => x.nameString().Contains("Plasterboard") && !x.nameString().Contains("_Internal"))
                    .additionalProperties().setFeature(Core.OpenStudio.OpenStudioIdentityKeys.OpticPrefix + "InternalEmissivity", "0.6");
                OpenStudioImportResult result = conversion.Model.ToSAM();
                Assert.That(FaceWarnings(result).Count(d => d.Code == "SAM-OSI-ID-001" && d.Message.Contains("their authored face optics differ")), Is.EqualTo(1), string.Join(" | ", result.Diagnostics));
                Assert.That(ImportedAll(result, "Plasterboard").Count, Is.EqualTo(2));
            }
        }

        /// <summary>Two external-face objects (12.5 and 15 mm) where only one was edited: one SAM material cannot hold both, so they are not merged.</summary>
        [Test]
        public void FaceObjectsWithConflictingEditsOnOneFace_AreNotMerged_AndDiagnosed()
        {
            using (OpenStudioConversionResult conversion = Box(Library(), AnalyticalModelFixtures.WallConstruction, roof: sandwich, floor: sandwich15).ToOpenStudio())
            {
                Assert.That(conversion.Model.getStandardOpaqueMaterials().Count(x => x.nameString().Contains("Plasterboard")), Is.EqualTo(3));
                conversion.Model.getStandardOpaqueMaterials().Single(x => x.nameString().Contains("Plasterboard_15mm")).setSolarAbsorptance(0.4);
                OpenStudioImportResult result = conversion.Model.ToSAM();
                Assert.That(FaceWarnings(result).Count(d => d.Code == "SAM-OSI-ID-001" && d.Message.Contains("ExternalSolarReflectance differs between the external-face objects")), Is.EqualTo(1), string.Join(" | ", result.Diagnostics));
                Assert.That(ImportedAll(result, "Plasterboard").Count, Is.EqualTo(3));
                Assert.That(Value(ImportedAll(result, "Plasterboard_15mm").Single(), OpaqueMaterialParameter.ExternalSolarReflectance), Is.EqualTo(0.6).Within(1e-12), "the edit is kept on its own material");
            }
        }

        [Test]
        public void AnObjectWithoutSourceIdentity_IsNeverMergedByName()
        {
            using (OpenStudioConversionResult conversion = Box(Library(), AnalyticalModelFixtures.WallConstruction, roof: sandwich15).ToOpenStudio())
            {
                global::OpenStudio.AdditionalProperties additionalProperties = conversion.Model.getStandardOpaqueMaterials().Single(x => x.nameString().Contains("Plasterboard_15mm")).additionalProperties();
                additionalProperties.resetFeature(Core.OpenStudio.OpenStudioIdentityKeys.MaterialFace);
                additionalProperties.resetFeature(Core.OpenStudio.OpenStudioIdentityKeys.MaterialGuid);
                OpenStudioImportResult result = conversion.Model.ToSAM();
                Assert.That(ImportedAll(result, "Plasterboard").Count, Is.EqualTo(2), "the same name stem is not an identity");
                Assert.That(Value(ImportedAll(result, "Plasterboard_15mm").Single(), OpaqueMaterialParameter.InternalEmissivity), Is.EqualTo(0.9).Within(1e-12), "a third-party object keeps its single value on both faces");
            }
        }

        [Test]
        public void TheImport_DoesNotInventAFaceValueSamDidNotState()
        {
            using (OpenStudioConversionResult conversion = Box(LibraryWithoutPlasterboardLight(), AnalyticalModelFixtures.WallConstruction).ToOpenStudio())
            {
                OpenStudioImportResult result = conversion.Model.ToSAM();
                OpaqueMaterial material = Imported(result, "Plasterboard");
                Assert.That(material.TryGetValue(OpaqueMaterialParameter.ExternalLightReflectance, out double _), Is.False, "the OpenStudio default is not imported as SAM data");
                Assert.That(material.TryGetValue(OpaqueMaterialParameter.InternalLightReflectance, out double _), Is.False);
                Assert.That(Value(material, OpaqueMaterialParameter.InternalEmissivity), Is.EqualTo(0.8).Within(1e-12));
                Assert.That(FaceWarnings(result), Is.Empty);
            }
        }

        /// <summary>
        /// Review of 901e592a (reproduction): Plasterboard light reflectance unstated on both faces, exported (OpenStudio
        /// default visible absorptance 0.7), then edited natively to 0.42. The reviewed head re-exported 0.7. The edit is
        /// now kept on the face the object carries, reported, and survives a re-export.
        /// The re-export is read on the single-layer ground floor, whose one layer is the zone face in either layer order:
        /// the importer keeps OpenStudio's outside-first layer order, so a multi-layer construction comes back reversed
        /// (a separate, pre-existing round-trip defect recorded in the PR record).
        /// </summary>
        [Test]
        public void ANativeEditOfAnUnstatedValue_IsKeptAndReported_ThroughImportAndReexport()
        {
            Guid plasterboard = Library().GetMaterial("Plasterboard").Guid;
            using (OpenStudioConversionResult conversion = Box(LibraryWithoutPlasterboardLight(), AnalyticalModelFixtures.WallConstruction, floor: single).ToOpenStudio())
            {
                global::OpenStudio.StandardOpaqueMaterial variant = FaceObjects(conversion.Model, plasterboard, "Internal").Single();
                Assert.That(variant.visibleAbsorptance(), Is.EqualTo(0.7).Within(1e-12), "the OpenStudio default was written");
                variant.setVisibleAbsorptance(0.42);

                OpenStudioImportResult result = conversion.Model.ToSAM();
                OpaqueMaterial material = Imported(result, "Plasterboard");
                Assert.That(Value(material, OpaqueMaterialParameter.InternalLightReflectance), Is.EqualTo(0.58).Within(1e-12), "the edit is kept on the zone (internal) face");
                Assert.That(material.TryGetValue(OpaqueMaterialParameter.ExternalLightReflectance, out double _), Is.False, "the other face stays unstated");
                Assert.That(FaceWarnings(result).Count(d => d.Code == "SAM-OSI-MAT-001" && d.Message.Contains("changed after it was written (VisibleAbsorptance written 0.7, now 0.42)") && d.Message.Contains("InternalLightReflectance")), Is.EqualTo(1), string.Join(" | ", result.Diagnostics));

                using (OpenStudioConversionResult reexport = result.AnalyticalModel.ToOpenStudio())
                {
                    Assert.That(FaceObjects(reexport.Model, plasterboard, "Internal").Single().visibleAbsorptance(), Is.EqualTo(0.42).Within(1e-12), "the edited value is written again, not the default");
                }
            }
        }

        /// <summary>The unchanged control: an unedited default stays unstated and re-exports as the same default, with no edit reported.</summary>
        [Test]
        public void AnUnchangedDefault_StaysUnstated_ThroughImportAndReexport()
        {
            Guid plasterboard = Library().GetMaterial("Plasterboard").Guid;
            using (OpenStudioConversionResult conversion = Box(LibraryWithoutPlasterboardLight(), AnalyticalModelFixtures.WallConstruction, floor: single).ToOpenStudio())
            {
                OpenStudioImportResult result = conversion.Model.ToSAM();
                Assert.That(FaceWarnings(result), Is.Empty);
                Assert.That(Imported(result, "Plasterboard").TryGetValue(OpaqueMaterialParameter.InternalLightReflectance, out double _), Is.False);
                using (OpenStudioConversionResult reexport = result.AnalyticalModel.ToOpenStudio())
                {
                    Assert.That(FaceObjects(reexport.Model, plasterboard, "Internal").Single().visibleAbsorptance(), Is.EqualTo(0.7).Within(1e-12));
                }
            }
        }

        /// <summary>An edit of a stated value is kept on the face the object carries; the other face keeps its authored value.</summary>
        [Test]
        public void AnEditOfAStatedValue_IsKeptOnItsFace_AndReported()
        {
            using (OpenStudioConversionResult conversion = Box(Library(), AnalyticalModelFixtures.WallConstruction).ToOpenStudio())
            {
                conversion.Model.getStandardOpaqueMaterials().Single(x => x.nameString().Contains("_Internal")).setSolarAbsorptance(0.45);
                OpenStudioImportResult result = conversion.Model.ToSAM();
                Assert.That(FaceWarnings(result).Count(d => d.Code == "SAM-OSI-MAT-001" && d.Message.Contains("changed after it was written (SolarAbsorptance written 0.3") && d.Message.Contains(", now 0.45)")), Is.EqualTo(1), string.Join(" | ", result.Diagnostics));
                OpaqueMaterial material = Imported(result, "Plasterboard");
                Assert.That(Value(material, OpaqueMaterialParameter.InternalSolarReflectance), Is.EqualTo(0.55).Within(1e-12));
                Assert.That(Value(material, OpaqueMaterialParameter.ExternalSolarReflectance), Is.EqualTo(0.5).Within(1e-12), "the external face keeps its authored value");
            }
        }

        [Test]
        public void AFaceObjectWhoseIdentityDisagrees_IsDiagnosed()
        {
            using (OpenStudioConversionResult conversion = Box(Library(), AnalyticalModelFixtures.WallConstruction).ToOpenStudio())
            {
                global::OpenStudio.StandardOpaqueMaterial variant = conversion.Model.getStandardOpaqueMaterials().Single(x => x.nameString().Contains("_Internal"));
                Assert.That(Core.OpenStudio.Query.TryGetSAMGuid(variant, out Guid _), Is.True, "the variant is the material's registered object here");
                variant.additionalProperties().setFeature(Core.OpenStudio.OpenStudioIdentityKeys.MaterialGuid, Guid.NewGuid().ToString("D"));
                OpenStudioImportResult result = conversion.Model.ToSAM();
                Assert.That(FaceWarnings(result).Any(d => d.Code == "SAM-OSI-ID-001" && d.Message.Contains("disagree")), Is.True, string.Join(" | ", result.Diagnostics));
            }
        }

        [Test]
        public void AnObjectWithoutFaceMetadata_KeepsItsSingleValueOnBothFaces()
        {
            using (OpenStudioConversionResult conversion = Box(Library(), AnalyticalModelFixtures.WallConstruction).ToOpenStudio())
            {
                conversion.Model.getStandardOpaqueMaterials().Single(x => x.nameString().Contains("_Internal")).additionalProperties().resetFeature(Core.OpenStudio.OpenStudioIdentityKeys.MaterialFace);
                OpenStudioImportResult result = conversion.Model.ToSAM();
                OpaqueMaterial material = Imported(result, "Plasterboard");
                Assert.That(Value(material, OpaqueMaterialParameter.ExternalEmissivity), Is.EqualTo(0.8).Within(1e-12));
                Assert.That(Value(material, OpaqueMaterialParameter.InternalEmissivity), Is.EqualTo(0.8).Within(1e-12));
                Assert.That(FaceWarnings(result), Is.Empty, "absent metadata (a third-party object) is not a diagnostic");
            }
        }

        // ---- inherited zone-face values (review of 901e592a) --------------------------------------------------------

        /// <summary>
        /// An Internal* value SAM does not state, absent or NaN, on a zone face takes the external value: reported once at
        /// Summary (SAM-OS-MAT-004), never stamped as authored, and imported back as unstated.
        /// </summary>
        [TestCase("absent")]
        [TestCase("nan")]
        public void AnUnstatedInternalValue_IsReportedAtSummary_AndKeepsItsUnspecifiedProvenance(string what)
        {
            MaterialLibrary materialLibrary = Library();
            OpaqueMaterial plasterboard = materialLibrary.GetMaterial("Plasterboard") as OpaqueMaterial;
            if (what == "nan")
            {
                plasterboard.SetValue(OpaqueMaterialParameter.InternalLightReflectance, double.NaN);
            }
            else
            {
                plasterboard.RemoveValue(OpaqueMaterialParameter.InternalLightReflectance);
            }

            materialLibrary.Add(plasterboard);
            using (OpenStudioConversionResult conversion = Box(materialLibrary, AnalyticalModelFixtures.WallConstruction).ToOpenStudio())
            {
                List<Core.OpenStudio.OpenStudioDiagnostic> notes = conversion.Diagnostics.Where(d => d.Code == "SAM-OS-MAT-004").ToList();
                Assert.That(notes.Count, Is.EqualTo(1), string.Join(" | ", conversion.Diagnostics));
                Assert.That(notes[0].Severity, Is.EqualTo(Core.OpenStudio.OpenStudioDiagnosticSeverity.Information));
                Assert.That(Core.OpenStudio.Query.GetDiagnosticDetail(notes[0]), Is.EqualTo(DiagnosticDetail.Summary));
                Assert.That(notes[0].Message, Does.StartWith("Zone face: InternalLightReflectance not stated"));
                Assert.That(notes[0].SamGuid, Is.EqualTo(plasterboard.Guid));

                global::OpenStudio.StandardOpaqueMaterial variant = FaceObjects(conversion.Model, plasterboard.Guid, "Internal").Single();
                Assert.That(variant.visibleAbsorptance(), Is.EqualTo(0.5).Within(1e-12), "the external light reflectance 0.5 is written on the zone face");
                Assert.That(Feature(variant, Core.OpenStudio.OpenStudioIdentityKeys.OpticPrefix + "InternalLightReflectance"), Is.Null, "an inherited value is not stamped as authored");

                OpenStudioImportResult result = conversion.Model.ToSAM();
                OpaqueMaterial material = Imported(result, "Plasterboard");
                Assert.That(material.TryGetValue(OpaqueMaterialParameter.InternalLightReflectance, out double imported) && !double.IsNaN(imported), Is.False, "the internal value comes back unstated");
                Assert.That(Value(material, OpaqueMaterialParameter.ExternalLightReflectance), Is.EqualTo(0.5).Within(1e-12));
                Assert.That(FaceWarnings(result), Is.Empty);
            }
        }

        /// <summary>The unchanged control: every zone-face internal value stated, so nothing is inherited or reported.</summary>
        [Test]
        public void StatedInternalValues_RaiseNoInheritanceNote()
        {
            using (OpenStudioConversionResult conversion = Box(Library(), AnalyticalModelFixtures.WallConstruction).ToOpenStudio())
            {
                Assert.That(conversion.Diagnostics.Any(d => d.Code == "SAM-OS-MAT-004"), Is.False, string.Join(" | ", conversion.Diagnostics.Where(d => d.Code == "SAM-OS-MAT-004")));
            }
        }

        [Test]
        public void EveryOpaqueObject_RecordsWhatWasWritten()
        {
            using (OpenStudioConversionResult conversion = Box(LibraryWithoutPlasterboardLight(), AnalyticalModelFixtures.WallConstruction, roof: sandwich15).ToOpenStudio())
            {
                foreach (global::OpenStudio.StandardOpaqueMaterial material in conversion.Model.getStandardOpaqueMaterials())
                {
                    Assert.That(double.Parse(Feature(material, Core.OpenStudio.OpenStudioIdentityKeys.WrittenPrefix + "ThermalAbsorptance"), System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(material.thermalAbsorptance()), material.nameString());
                    Assert.That(double.Parse(Feature(material, Core.OpenStudio.OpenStudioIdentityKeys.WrittenPrefix + "SolarAbsorptance"), System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(material.solarAbsorptance()), material.nameString());
                    Assert.That(double.Parse(Feature(material, Core.OpenStudio.OpenStudioIdentityKeys.WrittenPrefix + "VisibleAbsorptance"), System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(material.visibleAbsorptance()), material.nameString());
                }
            }
        }

        // ---- group membership (review of 354e5322, finding 1) -------------------------------------------------------

        private const string thirdName = "ReviewerMalformedThirdFace";

        /// <summary>
        /// Adds a third StandardOpaqueMaterial carrying the source Guid of <paramref name="template"/> and a copy of its face
        /// metadata and absorptances, but its own thermal properties (9 W/mK, 1000 kg/m³, 1200 J/kgK, 20 mm), then applies
        /// <paramref name="defect"/>: "face" (malformed face), "identity" (SAM.Guid names another material), "optic" (a
        /// stamped optic is not a fraction), "written" (a written record disagrees with the authored face) or "thermal"
        /// (well-formed metadata, only the thermal properties differ).
        /// </summary>
        private static global::OpenStudio.StandardOpaqueMaterial AddThird(global::OpenStudio.Model model, global::OpenStudio.StandardOpaqueMaterial template, string defect)
        {
            global::OpenStudio.StandardOpaqueMaterial result = new global::OpenStudio.StandardOpaqueMaterial(model);
            result.setName(thirdName);
            result.setThermalConductivity(9.0);
            result.setDensity(1000);
            result.setSpecificHeat(1200);
            result.setThickness(0.02);
            result.setThermalAbsorptance(template.thermalAbsorptance());
            result.setSolarAbsorptance(template.solarAbsorptance());
            result.setVisibleAbsorptance(template.visibleAbsorptance());

            List<string> keys = new List<string> { Core.OpenStudio.OpenStudioIdentityKeys.MaterialGuid, Core.OpenStudio.OpenStudioIdentityKeys.MaterialFace };
            keys.AddRange(new[] { "ExternalEmissivity", "InternalEmissivity", "ExternalSolarReflectance", "InternalSolarReflectance", "ExternalLightReflectance", "InternalLightReflectance" }.Select(x => Core.OpenStudio.OpenStudioIdentityKeys.OpticPrefix + x));
            keys.AddRange(new[] { "ThermalAbsorptance", "SolarAbsorptance", "VisibleAbsorptance" }.Select(x => Core.OpenStudio.OpenStudioIdentityKeys.WrittenPrefix + x));
            foreach (string key in keys)
            {
                string value = Feature(template, key);
                if (value != null)
                {
                    result.additionalProperties().setFeature(key, value);
                }
            }

            switch (defect)
            {
                case "face":
                    result.additionalProperties().setFeature(Core.OpenStudio.OpenStudioIdentityKeys.MaterialFace, "BROKEN");
                    break;
                case "identity":
                    result.additionalProperties().setFeature(Core.OpenStudio.OpenStudioIdentityKeys.Guid, Guid.NewGuid().ToString("D"));
                    break;
                case "optic":
                    result.additionalProperties().setFeature(Core.OpenStudio.OpenStudioIdentityKeys.OpticPrefix + "ExternalEmissivity", "1.7");
                    break;
                case "written":
                    result.additionalProperties().setFeature(Core.OpenStudio.OpenStudioIdentityKeys.WrittenPrefix + "ThermalAbsorptance", "0.123");
                    break;
            }

            return result;
        }

        private static void AssertOwnThermalProperties(OpaqueMaterial material)
        {
            Assert.That(material.Name, Is.EqualTo(thirdName));
            Assert.That(material.ThermalConductivity, Is.EqualTo(9.0).Within(1e-12), "the object's own conductivity, not the group's 0.25");
            Assert.That(material.Density, Is.EqualTo(1000).Within(1e-9));
            Assert.That(material.SpecificHeatCapacity, Is.EqualTo(1200).Within(1e-9));
        }

        /// <summary>
        /// Review of 354e5322 (reproduction): a third object carrying the source Guid of a valid two-object Plasterboard group
        /// but untrustworthy face metadata was aliased to the group (conductivity 0.25 instead of 9, no diagnostic). Each
        /// object is now validated before any group result is returned: the malformed one is imported as its own material,
        /// keeping its own physical properties, and named in a diagnostic; the valid pair still merges. Both import orders.
        /// </summary>
        [TestCase("face", false)]
        [TestCase("face", true)]
        [TestCase("identity", false)]
        [TestCase("identity", true)]
        [TestCase("optic", false)]
        [TestCase("optic", true)]
        [TestCase("written", false)]
        [TestCase("written", true)]
        public void AMalformedObjectOfAMergedSource_IsImportedOnItsOwn_NeverAliasedToTheGroup(string defect, bool malformedFirst)
        {
            using (OpenStudioConversionResult conversion = Box(Library(), AnalyticalModelFixtures.WallConstruction, roof: sandwich15).ToOpenStudio())
            {
                List<global::OpenStudio.StandardOpaqueMaterial> valid = conversion.Model.getStandardOpaqueMaterials().Where(x => x.nameString().Contains("Plasterboard")).ToList();
                Assert.That(valid.Count, Is.EqualTo(2));
                global::OpenStudio.StandardOpaqueMaterial third = AddThird(conversion.Model, valid.Single(x => !x.nameString().Contains("_Internal")), defect);

                OpenStudioImportContext context = new OpenStudioImportContext(conversion.Model);
                List<global::OpenStudio.StandardOpaqueMaterial> order = malformedFirst ? new[] { third }.Concat(valid).ToList() : valid.Concat(new[] { third }).ToList();
                Dictionary<string, IMaterial> imported = order.ToDictionary(x => x.nameString(), x => ((global::OpenStudio.Material)x).ToSAM(context));

                AssertOwnThermalProperties((OpaqueMaterial)imported[thirdName]);
                Assert.That(context.Diagnostics.Count(d => d.Code == "SAM-OSI-ID-001" || d.Code == "SAM-OSI-MAT-001"), Is.EqualTo(1), string.Join(" | ", context.Diagnostics));
                Core.OpenStudio.OpenStudioDiagnostic diagnostic = context.Diagnostics.Single(d => d.Code == "SAM-OSI-ID-001" || d.Code == "SAM-OSI-MAT-001");
                Assert.That(diagnostic.OpenStudioObjectName, Does.StartWith(thirdName + " ["), "the diagnostic names the malformed object");
                Assert.That(diagnostic.Message, Does.Contain("imported as its own material, never merged"));

                IMaterial merged = imported[valid[0].nameString()];
                Assert.That(imported[valid[1].nameString()], Is.SameAs(merged), "the valid pair still merges");
                Assert.That(merged, Is.Not.SameAs(imported[thirdName]));
                Assert.That(((OpaqueMaterial)merged).ThermalConductivity, Is.EqualTo(0.25).Within(1e-12));
                AssertPlasterboardFaces((OpaqueMaterial)merged);
            }
        }

        /// <summary>
        /// The whole-model import of the same reproduction, with the malformed object used as the floor's single layer: the
        /// library holds the merged Plasterboard and the malformed object as its own material.
        /// </summary>
        [TestCase("face")]
        [TestCase("identity")]
        public void AMalformedObjectOfAMergedSource_KeepsItsOwnProperties_InAWholeModelImport(string defect)
        {
            using (OpenStudioConversionResult conversion = Box(Library(), AnalyticalModelFixtures.WallConstruction, roof: sandwich15).ToOpenStudio())
            {
                global::OpenStudio.StandardOpaqueMaterial third = AddThird(conversion.Model, conversion.Model.getStandardOpaqueMaterials().Single(x => x.nameString().Contains("Plasterboard_15mm")), defect);
                global::OpenStudio.Construction construction = new global::OpenStudio.Construction(conversion.Model);
                construction.setName("Reviewer Third Floor");
                global::OpenStudio.MaterialVector materialVector = new global::OpenStudio.MaterialVector();
                materialVector.Add(third);
                Assert.That(construction.setLayers(materialVector), Is.True);
                Assert.That(conversion.Model.getSurfaces().Single(x => x.surfaceType() == "Floor").setConstruction(construction), Is.True);

                OpenStudioImportResult result = conversion.Model.ToSAM();
                AssertOwnThermalProperties(ImportedAll(result, thirdName).Single());
                Assert.That(ImportedAll(result, "Plasterboard").Count, Is.EqualTo(1), "the valid pair still merges");
                AssertPlasterboardFaces(ImportedAll(result, "Plasterboard").Single());
                Assert.That(FaceWarnings(result).Count(d => d.OpenStudioObjectName != null && d.OpenStudioObjectName.StartsWith(thirdName + " [", StringComparison.Ordinal)), Is.EqualTo(1), string.Join(" | ", result.Diagnostics));
            }
        }

        /// <summary>
        /// The inconsistent-member control: a third object with well-formed metadata whose thermal properties differ is a
        /// conflict of the whole group, so no object is merged, each keeps its own properties, and the conflict is reported once.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void AConsistentlyStampedObjectWithOtherThermalProperties_StopsTheMerge(bool thirdFirst)
        {
            using (OpenStudioConversionResult conversion = Box(Library(), AnalyticalModelFixtures.WallConstruction, roof: sandwich15).ToOpenStudio())
            {
                List<global::OpenStudio.StandardOpaqueMaterial> valid = conversion.Model.getStandardOpaqueMaterials().Where(x => x.nameString().Contains("Plasterboard")).ToList();
                global::OpenStudio.StandardOpaqueMaterial third = AddThird(conversion.Model, valid.Single(x => !x.nameString().Contains("_Internal")), "thermal");

                OpenStudioImportContext context = new OpenStudioImportContext(conversion.Model);
                List<global::OpenStudio.StandardOpaqueMaterial> order = thirdFirst ? new[] { third }.Concat(valid).ToList() : valid.Concat(new[] { third }).ToList();
                List<IMaterial> imported = order.Select(x => ((global::OpenStudio.Material)x).ToSAM(context)).ToList();

                Assert.That(imported.Distinct().Count(), Is.EqualTo(3), "nothing is merged");
                AssertOwnThermalProperties((OpaqueMaterial)imported[order.IndexOf(third)]);
                Assert.That(context.Diagnostics.Count(d => d.Code == "SAM-OSI-ID-001" && d.Message.Contains("their thermal properties differ") && d.Message.Contains(thirdName)), Is.EqualTo(1), string.Join(" | ", context.Diagnostics));
            }
        }

        /// <summary>The unchanged control: the valid two-object group, imported object by object in either order, merges with no diagnostic.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void TheValidPair_MergesInEitherImportOrder(bool reversed)
        {
            using (OpenStudioConversionResult conversion = Box(Library(), AnalyticalModelFixtures.WallConstruction, roof: sandwich15).ToOpenStudio())
            {
                List<global::OpenStudio.StandardOpaqueMaterial> valid = conversion.Model.getStandardOpaqueMaterials().Where(x => x.nameString().Contains("Plasterboard")).ToList();
                if (reversed)
                {
                    valid.Reverse();
                }

                OpenStudioImportContext context = new OpenStudioImportContext(conversion.Model);
                List<IMaterial> imported = valid.Select(x => ((global::OpenStudio.Material)x).ToSAM(context)).ToList();
                Assert.That(imported[1], Is.SameAs(imported[0]));
                AssertPlasterboardFaces((OpaqueMaterial)imported[0]);
                Assert.That(context.Diagnostics.Where(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning), Is.Empty, string.Join(" | ", context.Diagnostics));
            }
        }

        // ---- shared equal-face objects (review of 354e5322, finding 2) ----------------------------------------------

        /// <summary>A library whose <paramref name="name"/> states equal internal and external optics: ε 0.8, ρs 0.4, ρv 0.4 (absorptances 0.8 / 0.6 / 0.6).</summary>
        private static MaterialLibrary LibraryWithEqualFaces(string name)
        {
            MaterialLibrary materialLibrary = Library();
            OpaqueMaterial material = materialLibrary.GetMaterial(name) as OpaqueMaterial;
            foreach (OpaqueMaterialParameter opaqueMaterialParameter in new[] { OpaqueMaterialParameter.InternalEmissivity, OpaqueMaterialParameter.ExternalEmissivity })
            {
                material.SetValue(opaqueMaterialParameter, 0.8);
            }

            foreach (OpaqueMaterialParameter opaqueMaterialParameter in new[] { OpaqueMaterialParameter.InternalSolarReflectance, OpaqueMaterialParameter.ExternalSolarReflectance, OpaqueMaterialParameter.InternalLightReflectance, OpaqueMaterialParameter.ExternalLightReflectance })
            {
                material.SetValue(opaqueMaterialParameter, 0.4);
            }

            materialLibrary.Add(material);
            return materialLibrary;
        }

        /// <summary>
        /// Review of 354e5322 (reproduction): a single-layer construction of an equal-face material shares one external-face
        /// object for both faces. Its native edit (visible absorptance 0.6 → 0.42) was kept on the external face only, which
        /// invented differing faces and made the re-export a SAM-OS-MAT-003 error on five outdoor surfaces. The edit is now
        /// kept on both faces the object supplies, so the faces still agree and the re-export is valid with the edited value.
        /// </summary>
        [Test]
        public void AnEditOfASharedEqualFaceSingleLayerObject_IsKeptOnBothFaces_AndReexportsValid()
        {
            using (OpenStudioConversionResult conversion = Box(LibraryWithEqualFaces("Insulation"), singleAgreeing).ToOpenStudio())
            {
                Assert.That(conversion.IsValid, Is.True, string.Join(" | ", conversion.Diagnostics));
                global::OpenStudio.StandardOpaqueMaterial native = conversion.Model.getStandardOpaqueMaterials().Single();
                Assert.That(native.visibleAbsorptance(), Is.EqualTo(0.6).Within(1e-12));
                native.setVisibleAbsorptance(0.42);

                OpenStudioImportResult result = conversion.Model.ToSAM();
                OpaqueMaterial material = Imported(result, "Insulation");
                Assert.That(Value(material, OpaqueMaterialParameter.InternalLightReflectance), Is.EqualTo(0.58).Within(1e-12));
                Assert.That(Value(material, OpaqueMaterialParameter.ExternalLightReflectance), Is.EqualTo(0.58).Within(1e-12));
                Assert.That(Value(material, OpaqueMaterialParameter.InternalSolarReflectance), Is.EqualTo(0.4).Within(1e-12), "unedited values stay authored");
                Assert.That(Value(material, OpaqueMaterialParameter.ExternalSolarReflectance), Is.EqualTo(0.4).Within(1e-12));
                List<Core.OpenStudio.OpenStudioDiagnostic> notes = FaceWarnings(result).Where(d => d.Message.Contains("changed after it was written (VisibleAbsorptance written 0.6, now 0.42; its material's faces agree and this shared object supplies the zone face and the outer face)")).ToList();
                Assert.That(notes.Select(d => d.Message.Contains("InternalLightReflectance") ? "Internal" : "External").OrderBy(x => x), Is.EqualTo(new[] { "External", "Internal" }), string.Join(" | ", result.Diagnostics));

                using (OpenStudioConversionResult reexport = result.AnalyticalModel.ToOpenStudio())
                {
                    Assert.That(reexport.IsValid, Is.True, string.Join(" | ", reexport.Diagnostics.Where(d => d.Code == MAT_FACE)));
                    Assert.That(reexport.Diagnostics.Any(d => d.Code == MAT_FACE), Is.False);
                    Assert.That(reexport.Model.getStandardOpaqueMaterials().Single().visibleAbsorptance(), Is.EqualTo(0.42).Within(1e-12), "the edited value is written again on the one shared object");
                }
            }
        }

        /// <summary>The unchanged control of the single-layer reproduction: no edit, no warning, equal faces and a valid re-export with the authored value.</summary>
        [Test]
        public void AnUnchangedSharedEqualFaceSingleLayerObject_KeepsItsAuthoredFaces_AndReexportsValid()
        {
            using (OpenStudioConversionResult conversion = Box(LibraryWithEqualFaces("Insulation"), singleAgreeing).ToOpenStudio())
            {
                OpenStudioImportResult result = conversion.Model.ToSAM();
                OpaqueMaterial material = Imported(result, "Insulation");
                Assert.That(FaceWarnings(result), Is.Empty, string.Join(" | ", result.Diagnostics));
                Assert.That(Value(material, OpaqueMaterialParameter.InternalLightReflectance), Is.EqualTo(0.4).Within(1e-12));
                Assert.That(Value(material, OpaqueMaterialParameter.ExternalLightReflectance), Is.EqualTo(0.4).Within(1e-12));
                using (OpenStudioConversionResult reexport = result.AnalyticalModel.ToOpenStudio())
                {
                    Assert.That(reexport.IsValid, Is.True, string.Join(" | ", reexport.Diagnostics));
                    Assert.That(reexport.Model.getStandardOpaqueMaterials().Single().visibleAbsorptance(), Is.EqualTo(0.6).Within(1e-12));
                }
            }
        }

        /// <summary>
        /// An interzone single-layer construction of an equal-face material: the forward and reverse constructions share the
        /// one object, which supplies both zone faces; the edit is kept on both SAM faces and the re-export stays valid.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void AnEditOfASharedEqualFaceInterzoneSingleLayerObject_IsKeptOnBothFaces(bool bFirst)
        {
            using (OpenStudioConversionResult conversion = TwoBoxes(LibraryWithEqualFaces("Insulation"), singleAgreeing, bFirst).ToOpenStudio())
            {
                Assert.That(conversion.IsValid, Is.True, string.Join(" | ", conversion.Diagnostics));
                global::OpenStudio.StandardOpaqueMaterial native = conversion.Model.getStandardOpaqueMaterials().Single(x => x.nameString().Contains("Insulation"));
                native.setSolarAbsorptance(0.45);

                OpenStudioImportResult result = conversion.Model.ToSAM();
                OpaqueMaterial material = Imported(result, "Insulation");
                Assert.That(Value(material, OpaqueMaterialParameter.InternalSolarReflectance), Is.EqualTo(0.55).Within(1e-12));
                Assert.That(Value(material, OpaqueMaterialParameter.ExternalSolarReflectance), Is.EqualTo(0.55).Within(1e-12));
                using (OpenStudioConversionResult reexport = result.AnalyticalModel.ToOpenStudio())
                {
                    Assert.That(reexport.Diagnostics.Any(d => d.Code == MAT_FACE), Is.False, string.Join(" | ", reexport.Diagnostics.Where(d => d.Code == MAT_FACE)));
                }
            }
        }

        /// <summary>
        /// Multi-layer controls (import only: the re-export of a multi-layer construction is affected by the separate,
        /// pre-existing layer-order defect). An equal-face object supplies only the faces its layer position exposes: the
        /// zone-face layer's edit is kept on its internal face, the outer layer's on its external face, and the other face
        /// keeps its authored value. A middle layer supplies no exposed face, so its edit is kept on both faces.
        /// </summary>
        [TestCase("Plasterboard", true, false)]
        [TestCase("Brick", false, true)]
        [TestCase("Insulation", true, true)]
        public void AnEditOfASharedEqualFaceMultiLayerObject_IsKeptOnTheFacesItsPositionExposes(string name, bool internalEdited, bool externalEdited)
        {
            MaterialLibrary materialLibrary = LibraryWithEqualFaces(name);
            Guid source = materialLibrary.GetMaterial(name).Guid;
            using (OpenStudioConversionResult conversion = Box(materialLibrary, AnalyticalModelFixtures.WallConstruction).ToOpenStudio())
            {
                FaceObjects(conversion.Model, source, "External").Single().setVisibleAbsorptance(0.42);
                OpenStudioImportResult result = conversion.Model.ToSAM();
                OpaqueMaterial material = Imported(result, name);
                Assert.That(Value(material, OpaqueMaterialParameter.InternalLightReflectance), Is.EqualTo(internalEdited ? 0.58 : 0.4).Within(1e-12), "internal face");
                Assert.That(Value(material, OpaqueMaterialParameter.ExternalLightReflectance), Is.EqualTo(externalEdited ? 0.58 : 0.4).Within(1e-12), "external face");
                Assert.That(FaceWarnings(result).Count(d => d.Message.Contains("VisibleAbsorptance written 0.6, now 0.42")), Is.EqualTo((internalEdited ? 1 : 0) + (externalEdited ? 1 : 0)), string.Join(" | ", result.Diagnostics));
            }
        }

        /// <summary>An interzone multi-layer construction: the reverse construction mirrors the forward one, so the zone-face layer of the first space still supplies only its internal face.</summary>
        [Test]
        public void AnEditOfASharedEqualFaceInterzoneMultiLayerObject_IsKeptOnItsFirstSpaceFace()
        {
            MaterialLibrary materialLibrary = LibraryWithEqualFaces("Insulation");
            Guid source = materialLibrary.GetMaterial("Insulation").Guid;
            using (OpenStudioConversionResult conversion = TwoBoxes(materialLibrary, sandwich).ToOpenStudio())
            {
                List<global::OpenStudio.StandardOpaqueMaterial> objects = FaceObjects(conversion.Model, source, "External").Where(x => x.nameString().Contains("100mm")).ToList();
                Assert.That(objects.Count, Is.EqualTo(1), "one shared object for both directions and the fixture walls");
                objects[0].setVisibleAbsorptance(0.42);
                OpenStudioImportResult result = conversion.Model.ToSAM();
                OpaqueMaterial material = Imported(result, "Insulation");
                Assert.That(Value(material, OpaqueMaterialParameter.InternalLightReflectance), Is.EqualTo(0.58).Within(1e-12));
                Assert.That(Value(material, OpaqueMaterialParameter.ExternalLightReflectance), Is.EqualTo(0.4).Within(1e-12), "Insulation's other face is inside the fixture walls and the sandwich");
            }
        }

        /// <summary>
        /// The asymmetry control: a material whose authored faces differ keeps distinct variants, and an edit of its
        /// external-face object stays on the external face only (the internal variant supplies the zone face).
        /// </summary>
        [Test]
        public void AnEditOfADivergentMaterialsExternalObject_StaysOnTheExternalFace()
        {
            Guid brick = Library().GetMaterial("Brick").Guid;
            using (OpenStudioConversionResult conversion = Box(Library(), AnalyticalModelFixtures.WallConstruction).ToOpenStudio())
            {
                FaceObjects(conversion.Model, brick, "External").Single().setVisibleAbsorptance(0.42);
                OpenStudioImportResult result = conversion.Model.ToSAM();
                OpaqueMaterial material = Imported(result, "Brick");
                Assert.That(Value(material, OpaqueMaterialParameter.ExternalLightReflectance), Is.EqualTo(0.58).Within(1e-12));
                Assert.That(Value(material, OpaqueMaterialParameter.InternalLightReflectance), Is.EqualTo(0.6).Within(1e-12), "the authored internal face is kept");
                Assert.That(FaceWarnings(result).Count(d => d.Message.Contains("VisibleAbsorptance written 0.7, now 0.42)")), Is.EqualTo(1), string.Join(" | ", result.Diagnostics));
            }
        }
    }
}
