// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OpenStudio.InputReadBack
{
    /// <summary>
    /// Plain vector geometry on native vertex lists (no SAM or OpenStudio geometry kernel): polygon area, shell volume and
    /// the frame outline EnergyPlus grows around a pane. Used for the native side only; SAM's expected geometry comes from
    /// ReadBackExpected and the evaluation core.
    /// </summary>
    public static class OsGeometry
    {
        /// <summary>The vector area (Newell) of a planar polygon: its length is the area, its direction the normal by winding.</summary>
        public static double[] VectorArea(IReadOnlyList<double[]> points)
        {
            double x = 0, y = 0, z = 0;
            for (int i = 0; i < points.Count; i++)
            {
                double[] a = points[i], b = points[(i + 1) % points.Count];
                x += (a[1] - b[1]) * (a[2] + b[2]);
                y += (a[2] - b[2]) * (a[0] + b[0]);
                z += (a[0] - b[0]) * (a[1] + b[1]);
            }

            return new[] { x / 2, y / 2, z / 2 };
        }

        public static double Area(IReadOnlyList<double[]> points)
        {
            if (points == null || points.Count < 3)
            {
                return double.NaN;
            }

            double[] v = VectorArea(points);
            return Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
        }

        /// <summary>
        /// The volume enclosed by a closed set of polygons (divergence theorem, sum of signed pyramid volumes). EnergyPlus
        /// requires outward-facing (counter-clockwise from outside) surfaces, so a consistent shell gives a positive sum; the
        /// magnitude is returned and <paramref name="signed"/> reports the sign for the record.
        /// </summary>
        public static double ShellVolume(IEnumerable<IReadOnlyList<double[]>> polygons, out double signed)
        {
            signed = 0;
            foreach (IReadOnlyList<double[]> polygon in polygons)
            {
                if (polygon == null || polygon.Count < 3)
                {
                    signed = double.NaN;
                    return double.NaN;
                }

                double[] v = VectorArea(polygon);
                double[] p = polygon[0];
                signed += (p[0] * v[0] + p[1] * v[1] + p[2] * v[2]) / 3.0;
            }

            return Math.Abs(signed);
        }

        /// <summary>
        /// A convex planar polygon moved outwards in its own plane by <paramref name="width"/>: the outline of an EnergyPlus
        /// frame of that width around the pane. Null when the polygon is not convex or degenerate (never approximated).
        /// </summary>
        public static List<double[]> OffsetOutwards(IReadOnlyList<double[]> points, double width)
        {
            if (points == null || points.Count < 3 || double.IsNaN(width))
            {
                return null;
            }

            double[] normal = Unit(VectorArea(points));
            double[] u = Unit(Sub(points[1], points[0]));
            if (normal == null || u == null)
            {
                return null;
            }

            double[] v = Cross(normal, u);
            List<double[]> p2 = points.Select(p => new[] { Dot(Sub(p, points[0]), u), Dot(Sub(p, points[0]), v) }).ToList();
            int n = p2.Count;

            // In (u, v) with v = normal x u the polygon winds counter-clockwise; the outward normal of edge (dx, dy) is (dy, -dx).
            for (int i = 0; i < n; i++)
            {
                double[] a = p2[i], b = p2[(i + 1) % n], c = p2[(i + 2) % n];
                if ((b[0] - a[0]) * (c[1] - b[1]) - (b[1] - a[1]) * (c[0] - b[0]) < -1e-12)
                {
                    return null;
                }
            }

            List<double[]> lines = new List<double[]>();
            for (int i = 0; i < n; i++)
            {
                double[] a = p2[i], b = p2[(i + 1) % n];
                double dx = b[0] - a[0], dy = b[1] - a[1], length = Math.Sqrt(dx * dx + dy * dy);
                if (length < 1e-12)
                {
                    return null;
                }

                lines.Add(new[] { a[0] + dy / length * width, a[1] - dx / length * width, dx, dy });
            }

            List<double[]> result = new List<double[]>();
            for (int i = 0; i < n; i++)
            {
                double[] l1 = lines[(i + n - 1) % n], l2 = lines[i];
                double det = l1[2] * l2[3] - l1[3] * l2[2];
                if (Math.Abs(det) < 1e-12)
                {
                    return null;
                }

                double t = ((l2[0] - l1[0]) * l2[3] - (l2[1] - l1[1]) * l2[2]) / det;
                double x = l1[0] + t * l1[2], y = l1[1] + t * l1[3];
                result.Add(new[] { points[0][0] + x * u[0] + y * v[0], points[0][1] + x * u[1] + y * v[1], points[0][2] + x * u[2] + y * v[2] });
            }

            return result;
        }

        public static Geometry.Spatial.Polygon3D ToPolygon3D(IReadOnlyList<double[]> points)
        {
            return points == null || points.Count < 3 ? null : new Geometry.Spatial.Polygon3D(points.Select(p => new Geometry.Spatial.Point3D(p[0], p[1], p[2])).ToList());
        }

        private static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };

        private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

        private static double[] Cross(double[] a, double[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };

        private static double[] Unit(double[] a)
        {
            double length = Math.Sqrt(Dot(a, a));
            return length < 1e-12 ? null : new[] { a[0] / length, a[1] / length, a[2] / length };
        }
    }
}
