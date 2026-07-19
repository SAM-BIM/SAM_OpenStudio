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
        /// Area of a closed (assumed planar) polygon, computed as half the magnitude of the
        /// Newell normal. Winding-independent (always non-negative); use
        /// <see cref="IsClockwise(IEnumerable{Point3D}, Vector3D)"/> for orientation.
        /// Returns double.NaN for null input or fewer than three vertices.
        /// </summary>
        /// <param name="point3Ds">Ordered polygon vertices.</param>
        /// <returns>Polygon area [m²], or NaN.</returns>
        public static double Area(IEnumerable<Point3D> point3Ds)
        {
            Vector3D normal = Normal(point3Ds);
            if (normal == null)
            {
                return double.NaN;
            }

            return 0.5 * Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z);
        }
    }
}
