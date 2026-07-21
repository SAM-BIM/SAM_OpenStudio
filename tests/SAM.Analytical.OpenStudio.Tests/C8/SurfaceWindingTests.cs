// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Geometry.Spatial;
using System;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// C8: surface winding invariants. Every OpenStudio Surface must be wound so its outward
    /// normal points OUT of the space, and every SubSurface must agree with its host.
    ///
    /// Background — a real model produced 12 severe "checkSubSurfAzTiltNorm: Outward facing
    /// angle of subsurface differs more than 90.0 degrees from base surface" errors and
    /// EnergyPlus terminated before simulating. Panel conversion took the Face3D external-edge
    /// vertex order as-is while aperture conversion re-wound to the panel's outward normal, so
    /// where the two disagreed the host and its window ended up exactly 180 degrees apart. The
    /// same inversion silently produced "Floor is upside down", "Roof/Ceiling is upside down"
    /// and "Indicated Zone Volume &lt;= 0.0" on models with no windows, which corrupts
    /// volume-derived quantities instead of failing loudly.
    ///
    /// SCOPE — read before trusting these as a regression guard. They assert the invariant, but
    /// they do NOT reproduce the divergence that broke the real model. Panel.Normal is the
    /// PLANE normal, and AdjacencyCluster.UpdateNormals (called by the converter) flips that
    /// plane without rewinding the stored boundary; a fixture cannot reproduce that by simply
    /// reversing vertices, because Polygon3D re-derives its plane from the points it is given,
    /// so the plane normal flips with them and the two never disagree. Reproducing it needs a
    /// PlanarBoundary3D whose plane and boundary order are independently set. Until such a
    /// fixture exists these tests would still have passed against the broken conversion, and
    /// the real cover for that model remains manual verification in Rhino.
    /// </summary>
    [TestFixture]
    public class SurfaceWindingTests
    {
        private static Vector3D OutwardNormal(global::OpenStudio.PlanarSurface planarSurface)
        {
            global::OpenStudio.Vector3d vector3d = planarSurface.outwardNormal();
            return new Vector3D(vector3d.x(), vector3d.y(), vector3d.z());
        }

        private static Point3D Centroid(global::OpenStudio.PlanarSurface planarSurface)
        {
            double x = 0, y = 0, z = 0;
            int count = 0;
            foreach (global::OpenStudio.Point3d point3d in planarSurface.vertices())
            {
                x += point3d.x();
                y += point3d.y();
                z += point3d.z();
                count++;
            }

            return new Point3D(x / count, y / count, z / count);
        }

        [Test]
        public void Surfaces_FaceOutOfTheirSpace()
        {
            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox().ToOpenStudio();
            Assert.That(result?.Model, Is.Not.Null, "Conversion must produce a model");

            // The fixture box spans x 0-5, y 0-4, z 0-3 — centre (2.5, 2, 1.5).
            Point3D centre = new Point3D(2.5, 2, 1.5);
            int checkedSurfaces = 0;

            foreach (global::OpenStudio.Surface surface in result.Model.getSurfaces())
            {
                Vector3D normal = OutwardNormal(surface);
                Point3D centroid = Centroid(surface);
                Vector3D outward = new Vector3D(centroid.X - centre.X, centroid.Y - centre.Y, centroid.Z - centre.Z);

                double dot = (normal.X * outward.X) + (normal.Y * outward.Y) + (normal.Z * outward.Z);
                Assert.That(dot, Is.GreaterThan(0),
                    $"Surface '{surface.nameString()}' faces INTO the space — EnergyPlus reports the floor/roof " +
                    "upside down and computes a zone volume of 0");
                checkedSurfaces++;
            }

            Assert.That(checkedSurfaces, Is.EqualTo(6), "One surface per box face");
        }

        [Test]
        public void SubSurfaces_AreCoplanarAndCodirectionalWithTheirHost()
        {
            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox().ToOpenStudio();
            Assert.That(result?.Model, Is.Not.Null);

            int checkedSubSurfaces = 0;
            foreach (global::OpenStudio.SubSurface subSurface in result.Model.getSubSurfaces())
            {
                global::OpenStudio.OptionalSurface optionalSurface = subSurface.surface();
                Assert.That(optionalSurface.is_initialized(), Is.True, "Every SubSurface must have a host Surface");

                Vector3D subNormal = OutwardNormal(subSurface);
                Vector3D hostNormal = OutwardNormal(optionalSurface.get());

                double dot = (subNormal.X * hostNormal.X) + (subNormal.Y * hostNormal.Y) + (subNormal.Z * hostNormal.Z);
                double angle = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot))) * 180.0 / Math.PI;

                // EnergyPlus rejects anything beyond 90 degrees with a fatal
                // checkSubSurfAzTiltNorm severe; a correct conversion is coplanar (0).
                Assert.That(angle, Is.LessThan(1.0),
                    $"SubSurface '{subSurface.nameString()}' is {angle:F1} deg from its host — EnergyPlus raises " +
                    "checkSubSurfAzTiltNorm and terminates before simulating");
                checkedSubSurfaces++;
            }

            Assert.That(checkedSubSurfaces, Is.EqualTo(1), "The fixture has one window");
        }
    }
}
