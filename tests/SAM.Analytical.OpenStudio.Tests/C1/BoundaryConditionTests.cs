// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using AnalyticalCreate = SAM.Analytical.Create;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// C1: boundary-condition regression tests — the Adiabatic panel flag must override the
    /// PanelType mapping (LadybugTools New/BoundaryCondition.cs parity; coverage manifest
    /// PanelParameter.Adiabatic / BoundaryType.Adiabatic) and an Air panel must convert to a
    /// shared OS:Construction:AirBoundary on both paired sides (coverage manifest PanelType.Air).
    /// </summary>
    [TestFixture]
    public class BoundaryConditionTests
    {
        private static List<global::OpenStudio.Surface> Surfaces(global::OpenStudio.Model model)
        {
            List<global::OpenStudio.Surface> surfaces = new List<global::OpenStudio.Surface>();
            foreach (global::OpenStudio.Surface surface in model.getSurfaces())
            {
                surfaces.Add(surface);
            }

            return surfaces;
        }

        private static Geometry.Spatial.Face3D F(params Geometry.Spatial.Point3D[] point3Ds)
        {
            return new Geometry.Spatial.Face3D(new Geometry.Spatial.Polygon3D(point3Ds));
        }

        [Test]
        public void AdiabaticFlag_OverridesPanelTypeMapping()
        {
            Panel floor = AnalyticalCreate.Panel(AnalyticalModelFixtures.WallConstruction, PanelType.SlabOnGrade, F(new Geometry.Spatial.Point3D(0, 0, 0), new Geometry.Spatial.Point3D(5, 0, 0), new Geometry.Spatial.Point3D(5, 4, 0), new Geometry.Spatial.Point3D(0, 4, 0)));

            Assert.That(floor.PanelType.OutsideBoundaryCondition(), Is.EqualTo("Ground"), "The PanelType mapping is unchanged: SlabOnGrade stays Ground");
            Assert.That(floor.OutsideBoundaryCondition(), Is.EqualTo("Ground"), "Without the flag the panel follows its PanelType");

            floor.SetValue(PanelParameter.Adiabatic, true);
            Assert.That(floor.OutsideBoundaryCondition(), Is.EqualTo("Adiabatic"), "The Adiabatic flag must win over the SlabOnGrade Ground mapping");

            floor.SetValue(PanelParameter.Adiabatic, false);
            Assert.That(floor.OutsideBoundaryCondition(), Is.EqualTo("Ground"), "An explicit false flag falls back to the PanelType mapping");
        }

        [Test]
        public void AirPanel_IsNeverAdiabatic()
        {
            Panel airPanel = AnalyticalCreate.Panel(null, PanelType.Air, F(new Geometry.Spatial.Point3D(5, 0, 0), new Geometry.Spatial.Point3D(5, 4, 0), new Geometry.Spatial.Point3D(5, 4, 3), new Geometry.Spatial.Point3D(5, 0, 3)));

            Assert.That(airPanel.Adiabatic(), Is.False, "Air panels are never adiabatic, even without a construction");
            Assert.That(airPanel.OutsideBoundaryCondition(), Is.EqualTo("Surface"), "An Air panel stays an internal (paired) boundary");
        }

        [Test]
        public void AdiabaticFloors_ConvertToAdiabaticSurfaces()
        {
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.TwoAdjacentBoxes();
            AdjacencyCluster adjacencyCluster = analyticalModel.AdjacencyCluster;
            foreach (Panel panel in adjacencyCluster.GetPanels())
            {
                if (panel.PanelType != PanelType.SlabOnGrade)
                {
                    continue;
                }

                panel.SetValue(PanelParameter.Adiabatic, true);
                adjacencyCluster.AddObject(panel);
            }

            OpenStudioConversionResult result = new AnalyticalModel(analyticalModel, adjacencyCluster).ToOpenStudio();
            Assert.That(result.IsValid, Is.True, "Conversion must be valid (warnings allowed)");

            List<global::OpenStudio.Surface> surfaces = Surfaces(result.Model);
            Assert.That(surfaces.Count(s => s.outsideBoundaryCondition() == "Ground"), Is.EqualTo(0), "No surface may stay ground-coupled once its panel is flagged adiabatic");

            List<global::OpenStudio.Surface> adiabaticSurfaces = surfaces.FindAll(s => s.outsideBoundaryCondition() == "Adiabatic");
            Assert.That(adiabaticSurfaces.Count, Is.EqualTo(2), "Both flagged floors convert to Adiabatic surfaces");
            foreach (global::OpenStudio.Surface surface in adiabaticSurfaces)
            {
                Assert.That(surface.surfaceType(), Is.EqualTo("Floor"), "The surface type is unaffected by the boundary condition");
                Assert.That(surface.sunExposure(), Is.EqualTo("NoSun"));
                Assert.That(surface.windExposure(), Is.EqualTo("NoWind"));
            }

            Assert.That(surfaces.Count(s => s.outsideBoundaryCondition() == "Outdoors"), Is.EqualTo(8), "Roofs and external walls are untouched");
            Assert.That(surfaces.Count(s => s.outsideBoundaryCondition() == "Surface"), Is.EqualTo(2), "The shared internal wall is untouched");
        }

        [Test]
        public void AdiabaticInternalPanel_DropsThePairing_BothSidesAdiabatic()
        {
            // The flag changes more than the boundary string on an internal panel: "Adiabatic"
            // no longer takes the "Surface" branch, so the two engine surfaces are deliberately
            // NOT paired with setAdjacentSurface. That is the correct EnergyPlus model of an
            // adiabatic partition — no heat crosses it — and it must stay a clean conversion.
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.TwoAdjacentBoxes();
            AdjacencyCluster adjacencyCluster = analyticalModel.AdjacencyCluster;
            Panel sharedWall = adjacencyCluster.GetPanels().Single(x => x.PanelType == PanelType.WallInternal);
            sharedWall.SetValue(PanelParameter.Adiabatic, true);
            adjacencyCluster.AddObject(sharedWall);

            OpenStudioConversionResult result = new AnalyticalModel(analyticalModel, adjacencyCluster).ToOpenStudio();
            Assert.That(result.IsValid, Is.True, "Conversion must be valid (warnings allowed)");

            List<global::OpenStudio.Surface> surfaces = Surfaces(result.Model);
            List<global::OpenStudio.Surface> adiabaticSurfaces = surfaces.FindAll(s => s.outsideBoundaryCondition() == "Adiabatic");
            Assert.That(adiabaticSurfaces.Count, Is.EqualTo(2), "Both sides of the flagged partition are adiabatic");
            Assert.That(surfaces.Count(s => s.outsideBoundaryCondition() == "Surface"), Is.EqualTo(0), "No surface stays paired once the partition is adiabatic");

            foreach (global::OpenStudio.Surface surface in adiabaticSurfaces)
            {
                global::OpenStudio.OptionalSurface adjacent = surface.adjacentSurface();
                Assert.That(adjacent == null || adjacent.isNull(), Is.True, string.Format("{0} must not keep an adjacent surface", surface.nameString()));
            }

            // Skipping the pairing is intentional, not a failure to pair — the "single space" and
            // "OpenStudio rejected the pairing" warnings belong to the Surface branch only.
            Assert.That(result.Diagnostics.Any(d => d.Message.Contains("bounds a single space") || d.Message.Contains("rejected the adjacent-surface pairing")), Is.False, "An adiabatic partition is not a pairing failure: " + string.Join(" | ", result.Diagnostics.Select(d => d.Message)));
        }

        [Test]
        public void AirPanel_ConvertsToSharedAirBoundaryConstruction()
        {
            OpenStudioConversionResult result = AnalyticalModelFixtures.TwoAdjacentBoxes(sharedWallIsAir: true).ToOpenStudio();

            foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in result.Diagnostics)
            {
                TestContext.Out.WriteLine(diagnostic.ToString());
            }

            Assert.That(result.IsValid, Is.True, "Conversion must be valid (warnings allowed)");

            List<global::OpenStudio.Surface> internalSurfaces = Surfaces(result.Model).FindAll(s => s.outsideBoundaryCondition() == "Surface");
            Assert.That(internalSurfaces.Count, Is.EqualTo(2), "Both sides of the Air panel remain an explicitly paired internal boundary");

            foreach (global::OpenStudio.Surface surface in internalSurfaces)
            {
                global::OpenStudio.OptionalConstructionBase constructionBase = surface.construction();
                Assert.That(constructionBase != null && !constructionBase.isNull(), string.Format("Surface {0} must carry the air-boundary construction", surface.nameString()));
                Assert.That(constructionBase.get().nameString(), Is.EqualTo("SAM_Construction_AirBoundary"));
            }

            global::OpenStudio.OptionalSurface adjacent = internalSurfaces[0].adjacentSurface();
            Assert.That(adjacent != null && !adjacent.isNull(), "The Air panel sides must stay paired");
            Assert.That(adjacent.get().nameString(), Is.EqualTo(internalSurfaces[1].nameString()));
        }
    }
}
