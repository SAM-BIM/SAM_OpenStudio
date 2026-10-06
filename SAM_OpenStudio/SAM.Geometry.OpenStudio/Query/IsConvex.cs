// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using SAM.Geometry.Spatial;

namespace SAM.Geometry.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Convexity test mirroring what EnergyPlus (CheckConvexity) sees: the turn sign at
        /// every vertex must agree, and near-collinear vertices are ignored rather than
        /// treated as a sign flip — a polygon straight out of a CAD/BIM shell carries
        /// collinear mid-edge points whose zero turn would otherwise read as concave. A
        /// genuine reflex vertex (turn beyond the angle tolerance) makes the polygon
        /// non-convex.
        /// </summary>
        /// <param name="point3Ds">Ordered polygon vertices (planar within tolerance).</param>
        /// <param name="angleTolerance">Turn angle [rad] below which a vertex counts as collinear and is skipped.</param>
        /// <returns>True when every non-collinear turn has the same sign; false for null, fewer than three vertices or a reflex vertex.</returns>
        public static bool IsConvex(IEnumerable<Point3D> point3Ds, double angleTolerance)
        {
            if (point3Ds == null)
            {
                return false;
            }

            List<Point3D> points = new List<Point3D>(point3Ds);
            if (points.Count < 3)
            {
                return false;
            }

            Vector3D normal = Normal(points);
            if (normal == null)
            {
                return false;
            }

            double sinTolerance = System.Math.Sin(angleTolerance);

            int sign = 0;
            for (int i = 0; i < points.Count; i++)
            {
                Point3D previous = points[(i + points.Count - 1) % points.Count];
                Point3D current = points[i];
                Point3D next = points[(i + 1) % points.Count];

                Vector3D edge1 = new Vector3D(previous, current);
                Vector3D edge2 = new Vector3D(current, next);

                double length = edge1.Length * edge2.Length;
                if (double.IsNaN(length) || length < 1e-12)
                {
                    continue;
                }

                double sin = edge1.CrossProduct(edge2).DotProduct(normal) / length;
                if (double.IsNaN(sin) || System.Math.Abs(sin) < sinTolerance)
                {
                    continue;
                }

                int sign_Temp = sin > 0 ? 1 : -1;
                if (sign == 0)
                {
                    sign = sign_Temp;
                    continue;
                }

                if (sign != sign_Temp)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
