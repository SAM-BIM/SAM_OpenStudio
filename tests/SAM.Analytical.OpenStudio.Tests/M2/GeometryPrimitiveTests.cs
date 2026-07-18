// SPDX-License-Identifier: LGPL-3.0-only

using NUnit.Framework;
using SAM.Geometry.OpenStudio;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>M2: SAM → OpenStudio geometry primitive conversion and validation tests (plan §8).</summary>
    [TestFixture]
    public class GeometryPrimitiveTests
    {
        private const double DistanceTolerance = 1e-6;
        private const double AngleTolerance = 0.0349066;
        private const double MinimumArea = 0.0001;

        private static List<Point3D> HorizontalFloor()
        {
            return new List<Point3D>
            {
                new Point3D(0, 0, 0),
                new Point3D(10, 0, 0),
                new Point3D(10, 10, 0),
                new Point3D(0, 10, 0),
            };
        }

        [Test]
        public void Point3d_Conversion_PreservesCoordinates()
        {
            global::OpenStudio.Point3d point3d = new Point3D(1.5, -2.25, 3.125).ToOpenStudio();

            Assert.That(point3d.x(), Is.EqualTo(1.5));
            Assert.That(point3d.y(), Is.EqualTo(-2.25));
            Assert.That(point3d.z(), Is.EqualTo(3.125));

            global::OpenStudio.Point3dVector vector = HorizontalFloor().ToOpenStudio();
            Assert.That(vector.Count, Is.EqualTo(4));
            Assert.That(vector[2].x(), Is.EqualTo(10));
            Assert.That(vector[2].y(), Is.EqualTo(10));
        }

        [Test]
        public void HorizontalFloor_IsValid_NormalUp()
        {
            List<Point3D> points = HorizontalFloor();

            Assert.That(Geometry.OpenStudio.Query.IsPlanar(points, DistanceTolerance), Is.True);
            Assert.That(Geometry.OpenStudio.Query.Area(points), Is.EqualTo(100).Within(1e-9));
            Assert.That(Geometry.OpenStudio.Query.Normal(points).Z, Is.GreaterThan(0), "Counterclockwise-from-above floor must have +Z Newell normal");
            Assert.That(Geometry.OpenStudio.Query.ValidatePolygon(points, DistanceTolerance, AngleTolerance, MinimumArea), Is.Empty);
        }

        [Test]
        public void VerticalWall_IsValid()
        {
            List<Point3D> points = new List<Point3D>
            {
                new Point3D(0, 0, 0),
                new Point3D(10, 0, 0),
                new Point3D(10, 0, 3),
                new Point3D(0, 0, 3),
            };

            Assert.That(Geometry.OpenStudio.Query.IsPlanar(points, DistanceTolerance), Is.True);
            Assert.That(Geometry.OpenStudio.Query.Area(points), Is.EqualTo(30).Within(1e-9));
            Assert.That(Geometry.OpenStudio.Query.Normal(points).Y, Is.LessThan(0));
            Assert.That(Geometry.OpenStudio.Query.ValidatePolygon(points, DistanceTolerance, AngleTolerance, MinimumArea), Is.Empty);
        }

        [Test]
        public void SlopingRoof_IsValid()
        {
            List<Point3D> points = new List<Point3D>
            {
                new Point3D(0, 0, 0),
                new Point3D(10, 0, 0),
                new Point3D(10, 10, 5),
                new Point3D(0, 10, 5),
            };

            Assert.That(Geometry.OpenStudio.Query.IsPlanar(points, DistanceTolerance), Is.True);
            Assert.That(Geometry.OpenStudio.Query.Area(points), Is.EqualTo(10 * Math.Sqrt(125)).Within(1e-9));
            Assert.That(Geometry.OpenStudio.Query.ValidatePolygon(points, DistanceTolerance, AngleTolerance, MinimumArea), Is.Empty);
        }

        [Test]
        public void ReversedPolygon_IsDetectedByIsClockwise()
        {
            List<Point3D> counterclockwise = HorizontalFloor();
            List<Point3D> clockwise = new List<Point3D>(counterclockwise);
            clockwise.Reverse();

            Vector3D up = new Vector3D(0, 0, 1);

            Assert.That(Geometry.OpenStudio.Query.IsClockwise(counterclockwise, up), Is.False);
            Assert.That(Geometry.OpenStudio.Query.IsClockwise(clockwise, up), Is.True);
            Assert.That(Geometry.OpenStudio.Query.Normal(clockwise).Z, Is.LessThan(0));
        }

        [Test]
        public void DuplicateVertices_AreRemovedWithWarning()
        {
            List<Point3D> points = new List<Point3D>
            {
                new Point3D(0, 0, 0),
                new Point3D(10, 0, 0),
                new Point3D(10, 0, 0),
                new Point3D(10, 10, 0),
                new Point3D(0, 10, 0),
                new Point3D(0, 0, 0),
            };

            List<Point3D> cleaned = Geometry.OpenStudio.Query.CleanVertices(points, DistanceTolerance, AngleTolerance);
            Assert.That(cleaned.Count, Is.EqualTo(4), "Consecutive duplicate and closing vertex must be removed");

            List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = Geometry.OpenStudio.Query.ValidatePolygon(points, DistanceTolerance, AngleTolerance, MinimumArea);
            Assert.That(diagnostics.Count, Is.EqualTo(1));
            Assert.That(diagnostics[0].Code, Is.EqualTo("SAM-OS-GEO-002"));
            Assert.That(diagnostics[0].Severity, Is.EqualTo(Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning));
        }

        [Test]
        public void CollinearVertices_AreRemovedWithWarning()
        {
            List<Point3D> points = new List<Point3D>
            {
                new Point3D(0, 0, 0),
                new Point3D(5, 0, 0),
                new Point3D(10, 0, 0),
                new Point3D(10, 10, 0),
                new Point3D(0, 10, 0),
            };

            List<Point3D> cleaned = Geometry.OpenStudio.Query.CleanVertices(points, DistanceTolerance, AngleTolerance);
            Assert.That(cleaned.Count, Is.EqualTo(4), "Collinear mid-edge vertex must be removed");
            Assert.That(Geometry.OpenStudio.Query.Area(cleaned), Is.EqualTo(100).Within(1e-9), "Cleaning must not change the polygon shape");

            List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = Geometry.OpenStudio.Query.ValidatePolygon(points, DistanceTolerance, AngleTolerance, MinimumArea);
            Assert.That(diagnostics.Count, Is.EqualTo(1));
            Assert.That(diagnostics[0].Code, Is.EqualTo("SAM-OS-GEO-002"));
        }

        [Test]
        public void TooFewVertices_IsAnError()
        {
            List<Point3D> points = new List<Point3D>
            {
                new Point3D(0, 0, 0),
                new Point3D(10, 0, 0),
            };

            List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = Geometry.OpenStudio.Query.ValidatePolygon(points, DistanceTolerance, AngleTolerance, MinimumArea);

            Assert.That(diagnostics.Any(d => d.Code == "SAM-OS-GEO-001" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error), Is.True);
            Assert.That(diagnostics.Any(d => d.Message.Contains("fewer than three")), Is.True);
        }

        [Test]
        public void NonPlanarBoundary_IsRejectedNotRepaired()
        {
            List<Point3D> points = new List<Point3D>
            {
                new Point3D(0, 0, 0),
                new Point3D(10, 0, 0),
                new Point3D(10, 10, 2),
                new Point3D(0, 10, 0),
            };

            Assert.That(Geometry.OpenStudio.Query.IsPlanar(points, 0.001), Is.False);

            List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = Geometry.OpenStudio.Query.ValidatePolygon(points, DistanceTolerance, AngleTolerance, MinimumArea);
            Assert.That(diagnostics.Any(d => d.Code == "SAM-OS-GEO-001" && d.Message.Contains("planar")), Is.True);
        }

        [Test]
        public void NearZeroArea_IsAnError()
        {
            List<Point3D> points = new List<Point3D>
            {
                new Point3D(0, 0, 0),
                new Point3D(0.001, 0, 0),
                new Point3D(0.001, 0.001, 0),
                new Point3D(0, 0.001, 0),
            };

            List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = Geometry.OpenStudio.Query.ValidatePolygon(points, DistanceTolerance, AngleTolerance, MinimumArea);

            Assert.That(diagnostics.Any(d => d.Code == "SAM-OS-GEO-001" && d.Message.Contains("below the minimum")), Is.True);
        }
    }
}
