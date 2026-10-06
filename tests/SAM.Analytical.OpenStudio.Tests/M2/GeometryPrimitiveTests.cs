// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

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

        [Test]
        public void SelfIntersectingPolygon_IsAnError()
        {
            // Planar star (self-intersecting) with non-zero signed area — area and planarity
            // checks alone accept it; the self-intersection check must reject it (review P2-01).
            List<Point3D> star = new List<Point3D>
            {
                new Point3D(0, 0, 0),
                new Point3D(10, 0, 0),
                new Point3D(3, 8, 0),
                new Point3D(5, -4, 0),
                new Point3D(7, 8, 0),
            };

            Assert.That(Geometry.OpenStudio.Query.IsSelfIntersecting(star, DistanceTolerance), Is.True);

            List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = Geometry.OpenStudio.Query.ValidatePolygon(star, DistanceTolerance, AngleTolerance, MinimumArea);
            Assert.That(diagnostics.Any(d => d.Code == "SAM-OS-GEO-001" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error && d.Message.Contains("self-intersect")), Is.True, "A self-intersecting polygon must be rejected, never converted");
        }

        [Test]
        public void BowtiePolygon_IsAnError()
        {
            List<Point3D> bowtie = new List<Point3D>
            {
                new Point3D(0, 0, 0),
                new Point3D(10, 10, 0),
                new Point3D(10, 0, 0),
                new Point3D(0, 10, 0),
            };

            Assert.That(Geometry.OpenStudio.Query.IsSelfIntersecting(bowtie, DistanceTolerance), Is.True);
        }

        [Test]
        public void ConcavePolygon_IsAccepted()
        {
            // L-shaped (concave but non-intersecting) polygon must remain valid.
            List<Point3D> lShape = new List<Point3D>
            {
                new Point3D(0, 0, 0),
                new Point3D(10, 0, 0),
                new Point3D(10, 5, 0),
                new Point3D(5, 5, 0),
                new Point3D(5, 10, 0),
                new Point3D(0, 10, 0),
            };

            Assert.That(Geometry.OpenStudio.Query.IsSelfIntersecting(lShape, DistanceTolerance), Is.False);
            Assert.That(Geometry.OpenStudio.Query.ValidatePolygon(lShape, DistanceTolerance, AngleTolerance, MinimumArea), Is.Empty);
        }

        [Test]
        public void EnergyPlusSideCount_SliverCollapses_BelowThreeSides()
        {
            // The HungaryHouse door: a 1.6 mm wide, 2.125 m tall sliver. It survives the
            // converter's own cleaning (1e-6 m) but EnergyPlus drops consecutive vertices
            // closer than 0.01 m — 4 sides collapse to 2 → degenerate (severe).
            List<Point3D> sliverDoor = new List<Point3D>
            {
                new Point3D(6.036156, 0.424724, 5.125),
                new Point3D(6.036156, 0.424724, 3),
                new Point3D(6.037758, 0.424724, 3),
                new Point3D(6.037758, 0.424724, 5.125),
            };

            Assert.That(Geometry.OpenStudio.Query.EnergyPlusSideCount(sliverDoor), Is.EqualTo(2), "Both 1.6 mm vertex pairs collapse under the EnergyPlus 0.01 m rule");
            Assert.That(Geometry.OpenStudio.Query.EnergyPlusSideCount(HorizontalFloor()), Is.EqualTo(4), "An ordinary rectangle keeps its sides");
            Assert.That(Geometry.OpenStudio.Query.EnergyPlusSideCount(new List<Point3D> { new Point3D(0, 0, 0), new Point3D(1, 0, 0), new Point3D(0, 1, 0) }), Is.EqualTo(3), "A triangle is the last valid polygon");
            Assert.That(Geometry.OpenStudio.Query.EnergyPlusSideCount(null), Is.EqualTo(0));
        }

        [Test]
        public void EnergyPlusSideCount_ClosingPairCollapse()
        {
            // First and last vertices within 0.01 m (the wrap-around edge) also collapse.
            List<Point3D> closingSliver = new List<Point3D>
            {
                new Point3D(0, 0, 0),
                new Point3D(2, 0, 0),
                new Point3D(2, 0.005, 0),
                new Point3D(0, 0.002, 0),
            };

            Assert.That(Geometry.OpenStudio.Query.EnergyPlusSideCount(closingSliver), Is.LessThan(3));
        }
    }
}
