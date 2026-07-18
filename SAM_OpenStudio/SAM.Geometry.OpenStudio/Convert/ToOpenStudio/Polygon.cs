// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using SAM.Geometry.Spatial;

namespace SAM.Geometry.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Converts the external edge of a SAM Face3D to an OpenStudio polygon (Point3dVector)
        /// after vertex cleaning (duplicate closing vertex, consecutive duplicates, collinear
        /// vertices). Internal edges (holes) are NOT converted here — apertures are converted as
        /// SubSurfaces; a Face3D with other internal edges must be handled by the caller.
        /// Returns null when fewer than three vertices remain after cleaning.
        /// </summary>
        /// <param name="face3D">SAM face; null returns null.</param>
        /// <param name="distanceTolerance">Distance tolerance [m] for duplicate-vertex removal.</param>
        /// <param name="angleTolerance">Angle tolerance [rad] for collinear-vertex removal.</param>
        /// <returns>Cleaned OpenStudio point vector, or null.</returns>
        public static global::OpenStudio.Point3dVector ToOpenStudio(this Face3D face3D, double distanceTolerance, double angleTolerance)
        {
            if (face3D == null)
            {
                return null;
            }

            ISegmentable3D segmentable3D = face3D.GetExternalEdge3D() as ISegmentable3D;
            if (segmentable3D == null)
            {
                return null;
            }

            List<Point3D> point3Ds = Query.CleanVertices(segmentable3D.GetPoints(), distanceTolerance, angleTolerance);
            if (point3Ds == null || point3Ds.Count < 3)
            {
                return null;
            }

            return point3Ds.ToOpenStudio();
        }
    }
}
