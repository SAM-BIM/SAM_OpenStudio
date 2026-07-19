// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using SAM.Geometry.Spatial;

namespace SAM.Geometry.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Newell-method normal of a closed polygon. The returned vector is NOT normalized: its
        /// magnitude equals twice the polygon area, and its direction follows the right-hand rule
        /// for the vertex winding order. Returns null for null input or fewer than three vertices.
        /// </summary>
        /// <param name="point3Ds">Ordered polygon vertices.</param>
        /// <returns>Unnormalized normal vector, or null.</returns>
        public static Vector3D Normal(IEnumerable<Point3D> point3Ds)
        {
            if (point3Ds == null)
            {
                return null;
            }

            List<Point3D> points = point3Ds is List<Point3D> list ? list : new List<Point3D>(point3Ds);
            if (points.Count < 3)
            {
                return null;
            }

            double x = 0;
            double y = 0;
            double z = 0;
            for (int i = 0; i < points.Count; i++)
            {
                Point3D current = points[i];
                Point3D next = points[(i + 1) % points.Count];
                if (current == null || next == null)
                {
                    return null;
                }

                x += (current.Y - next.Y) * (current.Z + next.Z);
                y += (current.Z - next.Z) * (current.X + next.X);
                z += (current.X - next.X) * (current.Y + next.Y);
            }

            return new Vector3D(x, y, z);
        }
    }
}
