// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>M3: spaces, stories, zones, surfaces, adjacency and aperture conversion on the two-box fixture.</summary>
    [TestFixture]
    public class SpacesSurfacesTests
    {
        private OpenStudioConversionResult result;
        private global::OpenStudio.Model model;

        [OneTimeSetUp]
        public void Convert()
        {
            result = AnalyticalModelFixtures.TwoAdjacentBoxes().ToOpenStudio();
            Assert.That(result, Is.Not.Null);
            model = result.Model;
            Assert.That(model, Is.Not.Null);
        }

        private List<global::OpenStudio.Surface> Surfaces()
        {
            List<global::OpenStudio.Surface> surfaces = new List<global::OpenStudio.Surface>();
            foreach (global::OpenStudio.Surface surface in model.getSurfaces())
            {
                surfaces.Add(surface);
            }

            return surfaces;
        }

        [Test]
        public void Converts_SpacesZonesStoriesSurfacesSubSurfaces()
        {
            foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in result.Diagnostics)
            {
                TestContext.Out.WriteLine(diagnostic.ToString());
            }

            Assert.That(result.IsValid, Is.True, "Conversion must be valid (warnings allowed)");
            Assert.That(model.getSpaces().Count, Is.EqualTo(2));
            Assert.That(model.getThermalZones().Count, Is.EqualTo(2));
            Assert.That(model.getBuildingStorys().Count, Is.EqualTo(1), "Both boxes share one elevation");
            Assert.That(Surfaces().Count, Is.EqualTo(12), "6 surfaces per box, shared wall duplicated per side");
            Assert.That(model.getSubSurfaces().Count, Is.EqualTo(1));
        }

        [Test]
        public void BoundaryConditions_FollowPanelTypes()
        {
            List<global::OpenStudio.Surface> surfaces = Surfaces();

            Assert.That(surfaces.Count(s => s.outsideBoundaryCondition() == "Ground"), Is.EqualTo(2), "Two SlabOnGrade floors");
            Assert.That(surfaces.Count(s => s.outsideBoundaryCondition() == "Outdoors"), Is.EqualTo(8), "Two roofs and six external walls");
            Assert.That(surfaces.Count(s => s.outsideBoundaryCondition() == "Surface"), Is.EqualTo(2), "Both sides of the shared internal wall");
        }

        [Test]
        public void InternalWall_IsExplicitlyPaired_WithReversedNormals()
        {
            List<global::OpenStudio.Surface> internalSurfaces = Surfaces().FindAll(s => s.outsideBoundaryCondition() == "Surface");
            Assert.That(internalSurfaces.Count, Is.EqualTo(2));

            global::OpenStudio.OptionalSurface adjacent0 = internalSurfaces[0].adjacentSurface();
            global::OpenStudio.OptionalSurface adjacent1 = internalSurfaces[1].adjacentSurface();
            Assert.That(adjacent0 != null && !adjacent0.isNull(), "First internal surface must reference its pair");
            Assert.That(adjacent1 != null && !adjacent1.isNull(), "Second internal surface must reference its pair");
            Assert.That(adjacent0.get().nameString(), Is.EqualTo(internalSurfaces[1].nameString()));
            Assert.That(adjacent1.get().nameString(), Is.EqualTo(internalSurfaces[0].nameString()));

            global::OpenStudio.Vector3d normal0 = internalSurfaces[0].outwardNormal();
            global::OpenStudio.Vector3d normal1 = internalSurfaces[1].outwardNormal();
            double dot = normal0.x() * normal1.x() + normal0.y() * normal1.y() + normal0.z() * normal1.z();
            Assert.That(dot, Is.EqualTo(-1).Within(1e-6), "Paired surfaces must have opposite outward normals");
        }

        [Test]
        public void SurfaceNormals_PointOutOfTheirSpaces()
        {
            foreach (global::OpenStudio.Space space in model.getSpaces())
            {
                bool isSpaceA = space.nameString().Contains("Space_A");
                double insideX = isSpaceA ? 2.5 : 7.5;

                foreach (global::OpenStudio.Surface surface in space.surfaces)
                {
                    global::OpenStudio.Vector3d normal = surface.outwardNormal();
                    global::OpenStudio.Point3dVector vertices = surface.vertices();

                    double centroidX = 0, centroidY = 0, centroidZ = 0;
                    foreach (global::OpenStudio.Point3d vertex in vertices)
                    {
                        centroidX += vertex.x();
                        centroidY += vertex.y();
                        centroidZ += vertex.z();
                    }

                    centroidX /= vertices.Count;
                    centroidY /= vertices.Count;
                    centroidZ /= vertices.Count;

                    double toInsideX = insideX - centroidX;
                    double toInsideY = 2.0 - centroidY;
                    double toInsideZ = 1.5 - centroidZ;

                    double dot = normal.x() * toInsideX + normal.y() * toInsideY + normal.z() * toInsideZ;
                    Assert.That(dot, Is.LessThan(0), string.Format("Surface {0} normal must point out of its space", surface.nameString()));
                }
            }
        }

        [Test]
        public void FloorAreas_AndVolumes_MatchSam()
        {
            foreach (global::OpenStudio.Space space in model.getSpaces())
            {
                Assert.That(space.floorArea, Is.EqualTo(20.0).Within(1e-6), space.nameString());
                Assert.That(space.volume, Is.EqualTo(60.0).Within(1e-6), space.nameString());
            }
        }

        [Test]
        public void Window_BecomesFixedWindow_OnOutdoorsWall()
        {
            global::OpenStudio.SubSurfaceVector subSurfaces = model.getSubSurfaces();
            Assert.That(subSurfaces.Count, Is.EqualTo(1));

            global::OpenStudio.SubSurface subSurface = subSurfaces[0];
            Assert.That(subSurface.subSurfaceType(), Is.EqualTo("FixedWindow"));
            Assert.That(subSurface.grossArea(), Is.EqualTo(2.8).Within(1e-6), "2 m × 1.4 m window");

            global::OpenStudio.OptionalSurface host = subSurface.surface();
            Assert.That(host != null && !host.isNull(), "SubSurface must have a host surface");
            Assert.That(host.get().outsideBoundaryCondition(), Is.EqualTo("Outdoors"));
        }

        [Test]
        public void ObjectMap_TracksSpacesAndPanels()
        {
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.TwoAdjacentBoxes();
            OpenStudioConversionResult localResult = analyticalModel.ToOpenStudio();

            List<Space> spaces = analyticalModel.AdjacencyCluster.GetSpaces();
            foreach (Space space in spaces)
            {
                Assert.That(localResult.ObjectMap.ContainsKey(space.Guid), Is.True, "Every space must be traceable");
                Assert.That(localResult.ObjectMap[space.Guid], Does.StartWith("SAM_Space_"));
            }

            int panelCount = analyticalModel.AdjacencyCluster.GetPanels().Count;
            Assert.That(localResult.ObjectMap.Count, Is.GreaterThanOrEqualTo(panelCount + spaces.Count), "Panels and spaces must be registered");
        }
    }
}
