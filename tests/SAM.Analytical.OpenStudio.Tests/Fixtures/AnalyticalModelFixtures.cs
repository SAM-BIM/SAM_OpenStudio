// SPDX-License-Identifier: LGPL-3.0-only

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
    /// </summary>
    public static class AnalyticalModelFixtures
    {
        /// <summary>Construction used for all opaque fixture panels.</summary>
        public static readonly Construction WallConstruction = new Construction(new Guid("11111111-1111-1111-1111-111111111111"), "Fixture Construction");

        /// <summary>Aperture construction used for the fixture window.</summary>
        public static readonly ApertureConstruction WindowConstruction = new ApertureConstruction(new Guid("22222222-2222-2222-2222-222222222222"), "Fixture Window", ApertureType.Window);

        private static Point3D P(double x, double y, double z)
        {
            return new Point3D(x, y, z);
        }

        private static Face3D F(params Point3D[] point3Ds)
        {
            return new Face3D(new Polygon3D(point3Ds));
        }

        /// <summary>
        /// Two adjacent conditioned-geometry boxes sharing one internal wall; box A has a window
        /// in its south wall. 2 spaces, 11 panels (12 OpenStudio surfaces once the shared wall is
        /// duplicated per side), 1 aperture.
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

            return new AnalyticalModel("Two Box Model", "MVP two adjacent boxes fixture", null, null, adjacencyCluster);
        }
    }
}
