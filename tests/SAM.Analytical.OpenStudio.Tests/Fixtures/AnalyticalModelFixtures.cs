// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using AnalyticalCreate = SAM.Analytical.Create;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// Deterministic SAM fixture models for the MVP test suite (plan §12). Geometry is metric,
    /// axis-aligned and hand-checkable: box A spans x 0–5, box B x 5–10, both y 0–4, z 0–3,
    /// sharing the internal wall at x = 5. Box A's south wall carries one 2×1.4 m window.
    /// Materials/constructions are physically plausible; wall layers are stored inside → outside
    /// (SAM convention): Plasterboard, Insulation, Brick.
    /// </summary>
    public static class AnalyticalModelFixtures
    {
        /// <summary>Opaque wall construction, layers inside → outside: Plasterboard 12.5 mm, Insulation 100 mm, Brick 100 mm.</summary>
        public static readonly Construction WallConstruction = new Construction(new Guid("11111111-1111-1111-1111-111111111111"), "Fixture Construction", new List<ConstructionLayer>
        {
            new ConstructionLayer("Plasterboard", 0.0125),
            new ConstructionLayer("Insulation", 0.1),
            new ConstructionLayer("Brick", 0.1),
        });

        /// <summary>Double-glazing aperture construction, pane layers inside → outside: Glass 6 mm, Air 12 mm, Glass 6 mm.</summary>
        public static readonly ApertureConstruction WindowConstruction = new ApertureConstruction(new Guid("22222222-2222-2222-2222-222222222222"), "Fixture Window", ApertureType.Window, new List<ConstructionLayer>
        {
            new ConstructionLayer("Glass", 0.006),
            new ConstructionLayer("Air", 0.012),
            new ConstructionLayer("Glass", 0.006),
        });

        private static Point3D P(double x, double y, double z)
        {
            return new Point3D(x, y, z);
        }

        private static Face3D F(params Point3D[] point3Ds)
        {
            return new Face3D(new Polygon3D(point3Ds));
        }

        /// <summary>
        /// Material library backing the fixture constructions: Brick, Insulation, Plasterboard
        /// (opaque), Glass (transparent), Air (gas). Values are round, physically plausible and
        /// asserted by the M4 tests.
        /// </summary>
        public static MaterialLibrary CreateMaterialLibrary()
        {
            OpaqueMaterial brick = new OpaqueMaterial(new Guid("33333333-0000-0000-0000-000000000001"), "Brick", "Brick", "Fixture brick", 0.84, 1700, 800);
            brick.SetValue(OpaqueMaterialParameter.ExternalEmissivity, 0.9);
            brick.SetValue(OpaqueMaterialParameter.ExternalSolarReflectance, 0.3);
            brick.SetValue(OpaqueMaterialParameter.ExternalLightReflectance, 0.3);

            OpaqueMaterial insulation = new OpaqueMaterial(new Guid("33333333-0000-0000-0000-000000000002"), "Insulation", "Insulation", "Fixture insulation", 0.035, 25, 1400);
            insulation.SetValue(OpaqueMaterialParameter.ExternalEmissivity, 0.9);
            insulation.SetValue(OpaqueMaterialParameter.ExternalSolarReflectance, 0.3);
            insulation.SetValue(OpaqueMaterialParameter.ExternalLightReflectance, 0.3);

            OpaqueMaterial plasterboard = new OpaqueMaterial(new Guid("33333333-0000-0000-0000-000000000003"), "Plasterboard", "Plasterboard", "Fixture plasterboard", 0.25, 900, 1000);
            plasterboard.SetValue(OpaqueMaterialParameter.ExternalEmissivity, 0.9);
            plasterboard.SetValue(OpaqueMaterialParameter.ExternalSolarReflectance, 0.5);
            plasterboard.SetValue(OpaqueMaterialParameter.ExternalLightReflectance, 0.5);

            TransparentMaterial glass = new TransparentMaterial(new Guid("33333333-0000-0000-0000-000000000004"), "Glass", "Glass", "Fixture glass", 1.0, 2500, 840);
            glass.SetValue(TransparentMaterialParameter.SolarTransmittance, 0.7);
            glass.SetValue(TransparentMaterialParameter.InternalSolarReflectance, 0.07);
            glass.SetValue(TransparentMaterialParameter.ExternalSolarReflectance, 0.07);
            glass.SetValue(TransparentMaterialParameter.LightTransmittance, 0.8);
            glass.SetValue(TransparentMaterialParameter.InternalLightReflectance, 0.07);
            glass.SetValue(TransparentMaterialParameter.ExternalLightReflectance, 0.07);
            glass.SetValue(TransparentMaterialParameter.InternalEmissivity, 0.84);
            glass.SetValue(TransparentMaterialParameter.ExternalEmissivity, 0.84);

            GasMaterial air = new GasMaterial(new Guid("33333333-0000-0000-0000-000000000005"), "Air");

            MaterialLibrary result = new MaterialLibrary("Fixture Material Library");
            result.Add(brick);
            result.Add(insulation);
            result.Add(plasterboard);
            result.Add(glass);
            result.Add(air);
            return result;
        }

        /// <summary>
        /// Two adjacent boxes sharing one internal wall; box A has a window in its south wall.
        /// 2 spaces, 11 panels (12 OpenStudio surfaces once the shared wall is duplicated per
        /// side), 1 aperture, full material library.
        /// </summary>
        public static AnalyticalModel TwoAdjacentBoxes()
        {
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster();

            Space spaceA = new Space(new Guid("aaaaaaaa-0000-0000-0000-000000000001"), "Space A", P(2.5, 2, 1.5));
            Space spaceB = new Space(new Guid("bbbbbbbb-0000-0000-0000-000000000002"), "Space B", P(7.5, 2, 1.5));
            adjacencyCluster.AddObject(spaceA);
            adjacencyCluster.AddObject(spaceB);

            Panel floorA = AnalyticalCreate.Panel(WallConstruction, PanelType.SlabOnGrade, F(P(0, 0, 0), P(5, 0, 0), P(5, 4, 0), P(0, 4, 0)));
            Panel roofA = AnalyticalCreate.Panel(WallConstruction, PanelType.Roof, F(P(0, 0, 3), P(5, 0, 3), P(5, 4, 3), P(0, 4, 3)));
            Panel wallSouthA = AnalyticalCreate.Panel(WallConstruction, PanelType.Wall, F(P(0, 0, 0), P(5, 0, 0), P(5, 0, 3), P(0, 0, 3)));
            Panel wallWestA = AnalyticalCreate.Panel(WallConstruction, PanelType.Wall, F(P(0, 0, 0), P(0, 4, 0), P(0, 4, 3), P(0, 0, 3)));
            Panel wallNorthA = AnalyticalCreate.Panel(WallConstruction, PanelType.Wall, F(P(0, 4, 0), P(5, 4, 0), P(5, 4, 3), P(0, 4, 3)));

            Panel wallShared = AnalyticalCreate.Panel(WallConstruction, PanelType.WallInternal, F(P(5, 0, 0), P(5, 4, 0), P(5, 4, 3), P(5, 0, 3)));

            Panel floorB = AnalyticalCreate.Panel(WallConstruction, PanelType.SlabOnGrade, F(P(5, 0, 0), P(10, 0, 0), P(10, 4, 0), P(5, 4, 0)));
            Panel roofB = AnalyticalCreate.Panel(WallConstruction, PanelType.Roof, F(P(5, 0, 3), P(10, 0, 3), P(10, 4, 3), P(5, 4, 3)));
            Panel wallSouthB = AnalyticalCreate.Panel(WallConstruction, PanelType.Wall, F(P(5, 0, 0), P(10, 0, 0), P(10, 0, 3), P(5, 0, 3)));
            Panel wallEastB = AnalyticalCreate.Panel(WallConstruction, PanelType.Wall, F(P(10, 0, 0), P(10, 4, 0), P(10, 4, 3), P(10, 0, 3)));
            Panel wallNorthB = AnalyticalCreate.Panel(WallConstruction, PanelType.Wall, F(P(5, 4, 0), P(10, 4, 0), P(10, 4, 3), P(5, 4, 3)));

            Aperture window = AnalyticalCreate.Aperture(WindowConstruction, F(P(1, 0, 0.8), P(3, 0, 0.8), P(3, 0, 2.2), P(1, 0, 2.2)));
            wallSouthA.AddAperture(window);

            List<Panel> panelsA = new List<Panel> { floorA, roofA, wallSouthA, wallWestA, wallNorthA, wallShared };
            List<Panel> panelsB = new List<Panel> { floorB, roofB, wallSouthB, wallEastB, wallNorthB, wallShared };

            foreach (Panel panel in panelsA)
            {
                adjacencyCluster.AddObject(panel);
                adjacencyCluster.AddRelation(spaceA, panel);
            }

            foreach (Panel panel in panelsB)
            {
                if (panel != wallShared)
                {
                    adjacencyCluster.AddObject(panel);
                }

                adjacencyCluster.AddRelation(spaceB, panel);
            }

            return new AnalyticalModel("Two Box Model", "MVP two adjacent boxes fixture", null, null, adjacencyCluster, CreateMaterialLibrary(), null);
        }

        /// <summary>
        /// One-zone box (x 0–5, y 0–4, z 0–3) with an optional south window; all panels use the
        /// given construction (fixture default when null). Used by simulation and failure-policy
        /// tests.
        /// </summary>
        public static AnalyticalModel SingleBox(Construction wallConstruction = null, bool includeWindow = true)
        {
            Construction construction = wallConstruction ?? WallConstruction;

            AdjacencyCluster adjacencyCluster = new AdjacencyCluster();

            Space space = new Space(new Guid("cccccccc-0000-0000-0000-000000000001"), "Space Single", P(2.5, 2, 1.5));
            adjacencyCluster.AddObject(space);

            List<Panel> panels = new List<Panel>
            {
                AnalyticalCreate.Panel(construction, PanelType.SlabOnGrade, F(P(0, 0, 0), P(5, 0, 0), P(5, 4, 0), P(0, 4, 0))),
                AnalyticalCreate.Panel(construction, PanelType.Roof, F(P(0, 0, 3), P(5, 0, 3), P(5, 4, 3), P(0, 4, 3))),
                AnalyticalCreate.Panel(construction, PanelType.Wall, F(P(0, 0, 0), P(5, 0, 0), P(5, 0, 3), P(0, 0, 3))),
                AnalyticalCreate.Panel(construction, PanelType.Wall, F(P(0, 0, 0), P(0, 4, 0), P(0, 4, 3), P(0, 0, 3))),
                AnalyticalCreate.Panel(construction, PanelType.Wall, F(P(0, 4, 0), P(5, 4, 0), P(5, 4, 3), P(0, 4, 3))),
                AnalyticalCreate.Panel(construction, PanelType.Wall, F(P(5, 0, 0), P(5, 4, 0), P(5, 4, 3), P(5, 0, 3))),
            };

            if (includeWindow)
            {
                panels[2].AddAperture(AnalyticalCreate.Aperture(WindowConstruction, F(P(1, 0, 0.8), P(3, 0, 0.8), P(3, 0, 2.2), P(1, 0, 2.2))));
            }

            foreach (Panel panel in panels)
            {
                adjacencyCluster.AddObject(panel);
                adjacencyCluster.AddRelation(space, panel);
            }

            return new AnalyticalModel("Single Box Model", "MVP one-zone box fixture", null, null, adjacencyCluster, CreateMaterialLibrary(), null);
        }
    }
}
