// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Geometry.Spatial;

namespace SAM.Geometry.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// True when any two non-adjacent edges of the (assumed planar) closed polygon properly
        /// cross. Adjacent edge pairs sharing a vertex are ignored; near-touching within the
        /// distance tolerance counts as intersecting. Projection uses the dominant axis of the
        /// Newell normal, so the check is robust for vertical and sloping polygons. O(n²);
        /// returns false for null input or fewer than four vertices.
        /// </summary>
        /// <param name="point3Ds">Ordered polygon vertices.</param>
        /// <param name="distanceTolerance">Distance tolerance [m] for touch detection.</param>
        /// <returns>True when the polygon self-intersects.</returns>
        public static bool IsSelfIntersecting(IEnumerable<Point3D> point3Ds, double distanceTolerance)
        {
            if (point3Ds == null)
            {
                return false;
            }

            List<Point3D> points = point3Ds is List<Point3D> list ? list : new List<Point3D>(point3Ds);
            int count = points.Count;
            if (count < 4)
            {
                return false;
            }

            Vector3D normal = Normal(points);
            if (normal == null)
            {
                return false;
            }

            // Project onto the plane most facing the polygon: drop the dominant normal axis.
            double absX = Math.Abs(normal.X);
            double absY = Math.Abs(normal.Y);
            double absZ = Math.Abs(normal.Z);

            double[] u = new double[count];
            double[] v = new double[count];
            for (int i = 0; i < count; i++)
            {
                if (absZ >= absX && absZ >= absY)
                {
                    u[i] = points[i].X;
                    v[i] = points[i].Y;
                }
                else if (absY >= absX)
                {
                    u[i] = points[i].X;
                    v[i] = points[i].Z;
                }
                else
                {
                    u[i] = points[i].Y;
                    v[i] = points[i].Z;
                }
            }

            for (int i = 0; i < count; i++)
            {
                int i2 = (i + 1) % count;
                for (int j = i + 1; j < count; j++)
                {
                    int j2 = (j + 1) % count;

                    // Skip adjacent edges (shared endpoint, including the wraparound pair).
                    if (i == j || i2 == j || j2 == i)
                    {
                        continue;
                    }

                    if (SegmentsIntersect(u[i], v[i], u[i2], v[i2], u[j], v[j], u[j2], v[j2], distanceTolerance))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool SegmentsIntersect(double p1x, double p1y, double p2x, double p2y, double p3x, double p3y, double p4x, double p4y, double tolerance)
        {
            double d1 = Cross(p3x, p3y, p4x, p4y, p1x, p1y);
            double d2 = Cross(p3x, p3y, p4x, p4y, p2x, p2y);
            double d3 = Cross(p1x, p1y, p2x, p2y, p3x, p3y);
            double d4 = Cross(p1x, p1y, p2x, p2y, p4x, p4y);

            double norm = Math.Max(1.0, Math.Abs(p2x - p1x) + Math.Abs(p2y - p1y)) * Math.Max(1.0, Math.Abs(p4x - p3x) + Math.Abs(p4y - p3y));
            double epsilon = tolerance * Math.Sqrt(norm);

            if (((d1 > epsilon && d2 < -epsilon) || (d1 < -epsilon && d2 > epsilon)) && ((d3 > epsilon && d4 < -epsilon) || (d3 < -epsilon && d4 > epsilon)))
            {
                return true;
            }

            // Touching (an endpoint on the other segment within tolerance) counts as intersecting.
            if (Math.Abs(d1) <= epsilon && OnSegment(p3x, p3y, p4x, p4y, p1x, p1y, tolerance))
            {
                return true;
            }

            if (Math.Abs(d2) <= epsilon && OnSegment(p3x, p3y, p4x, p4y, p2x, p2y, tolerance))
            {
                return true;
            }

            if (Math.Abs(d3) <= epsilon && OnSegment(p1x, p1y, p2x, p2y, p3x, p3y, tolerance))
            {
                return true;
            }

            if (Math.Abs(d4) <= epsilon && OnSegment(p1x, p1y, p2x, p2y, p4x, p4y, tolerance))
            {
                return true;
            }

            return false;
        }

        private static double Cross(double ax, double ay, double bx, double by, double px, double py)
        {
            return (bx - ax) * (py - ay) - (by - ay) * (px - ax);
        }

        private static bool OnSegment(double ax, double ay, double bx, double by, double px, double py, double tolerance)
        {
            return px >= Math.Min(ax, bx) - tolerance && px <= Math.Max(ax, bx) + tolerance
                && py >= Math.Min(ay, by) - tolerance && py <= Math.Max(ay, by) + tolerance;
        }
    }
}
