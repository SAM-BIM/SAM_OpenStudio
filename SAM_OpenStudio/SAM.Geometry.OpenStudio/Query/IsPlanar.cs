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
        /// True when every vertex lies within the distance tolerance of the best-fit plane
        /// (Newell normal through the vertex centroid). Three valid vertices are always planar.
        /// Returns false for null input, fewer than three vertices, or a degenerate normal.
        /// </summary>
        /// <param name="point3Ds">Ordered polygon vertices.</param>
        /// <param name="distanceTolerance">Maximum out-of-plane distance [m].</param>
        /// <returns>True when planar within tolerance.</returns>
        public static bool IsPlanar(IEnumerable<Point3D> point3Ds, double distanceTolerance)
        {
            if (point3Ds == null)
            {
                return false;
            }

            List<Point3D> points = point3Ds is List<Point3D> list ? list : new List<Point3D>(point3Ds);
            if (points.Count < 3)
            {
                return false;
            }

            Vector3D normal = Normal(points);
            if (normal == null)
            {
                return false;
            }

            double length = Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z);
            if (length == 0 || double.IsNaN(length))
            {
                return false;
            }

            double unitX = normal.X / length;
            double unitY = normal.Y / length;
            double unitZ = normal.Z / length;

            double centroidX = 0;
            double centroidY = 0;
            double centroidZ = 0;
            foreach (Point3D point3D in points)
            {
                if (point3D == null)
                {
                    return false;
                }

                centroidX += point3D.X;
                centroidY += point3D.Y;
                centroidZ += point3D.Z;
            }

            centroidX /= points.Count;
            centroidY /= points.Count;
            centroidZ /= points.Count;

            foreach (Point3D point3D in points)
            {
                double distance = Math.Abs((point3D.X - centroidX) * unitX + (point3D.Y - centroidY) * unitY + (point3D.Z - centroidZ) * unitZ);
                if (distance > distanceTolerance)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
