// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using SAM.Geometry.Spatial;

namespace SAM.Geometry.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// True when the polygon winds clockwise about the given reference normal: the polygon's
        /// Newell normal (right-hand rule) points against the reference direction. OpenStudio
        /// surfaces require counterclockwise vertices viewed from the side the normal points to,
        /// so a clockwise polygon must be reversed by the caller.
        /// Returns false for degenerate input (no orientation can be determined).
        /// </summary>
        /// <param name="point3Ds">Ordered polygon vertices.</param>
        /// <param name="viewNormal">Reference direction (for example the intended outward normal).</param>
        /// <returns>True when winding is clockwise about the reference normal.</returns>
        public static bool IsClockwise(IEnumerable<Point3D> point3Ds, Vector3D viewNormal)
        {
            if (viewNormal == null)
            {
                return false;
            }

            Vector3D normal = Normal(point3Ds);
            if (normal == null)
            {
                return false;
            }

            double dot = normal.X * viewNormal.X + normal.Y * viewNormal.Y + normal.Z * viewNormal.Z;
            return dot < 0;
        }
    }
}
