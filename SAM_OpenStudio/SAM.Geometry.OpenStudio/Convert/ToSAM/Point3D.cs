// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using SAM.Geometry.Spatial;

namespace SAM.Geometry.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Converts an OpenStudio Point3d to a SAM Point3D. Coordinates are copied unchanged
        /// (both are metres); any coordinate-system change is the caller's business — see
        /// <see cref="ToSAM(global::OpenStudio.Point3dVector, global::OpenStudio.Transformation)"/>.
        /// </summary>
        /// <param name="point3d">OpenStudio point; null returns null.</param>
        /// <returns>SAM point, or null.</returns>
        public static Point3D ToSAM(this global::OpenStudio.Point3d point3d)
        {
            if (point3d == null)
            {
                return null;
            }

            return new Point3D(point3d.x(), point3d.y(), point3d.z());
        }

        /// <summary>
        /// Converts an OpenStudio Point3dVector to SAM points, preserving order.
        /// <para>
        /// <paramref name="transformation"/> is applied first when supplied. This matters:
        /// OpenStudio surface vertices are stored in the coordinate system of their
        /// <c>PlanarSurfaceGroup</c> (the owning Space or ShadingSurfaceGroup), NOT in world
        /// coordinates. A space with a non-zero origin or a relative-north rotation therefore
        /// produces vertices that are meaningless until the group transformation is applied.
        /// SAM-authored OSM files happen to place every space at the origin, so the
        /// transformation is the identity there and round trips are unaffected — but a
        /// third-party OSM would import as overlapping geometry without this.
        /// </para>
        /// No cleaning is performed here; see <see cref="Query.CleanVertices"/>.
        /// </summary>
        /// <param name="point3dVector">OpenStudio points; null returns null; null entries are skipped.</param>
        /// <param name="transformation">Group → target-space transformation; null means no transformation.</param>
        /// <returns>Ordered SAM points, or null.</returns>
        public static List<Point3D> ToSAM(this global::OpenStudio.Point3dVector point3dVector, global::OpenStudio.Transformation transformation = null)
        {
            if (point3dVector == null)
            {
                return null;
            }

            global::OpenStudio.Point3dVector transformed = point3dVector;
            if (transformation != null)
            {
                // A transformation the native layer rejects must not silently yield untransformed
                // (i.e. wrong) geometry — the caller validates the result and reports it.
                transformed = transformation.Multiply(point3dVector);
                if (transformed == null)
                {
                    return null;
                }
            }

            List<Point3D> result = new List<Point3D>();
            foreach (global::OpenStudio.Point3d point3d in transformed)
            {
                Point3D point3D = point3d.ToSAM();
                if (point3D == null)
                {
                    continue;
                }

                result.Add(point3D);
            }

            return result;
        }
    }
}
