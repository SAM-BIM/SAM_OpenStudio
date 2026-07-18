// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;

namespace SAM.Geometry.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Converts an ordered SAM point collection to an OpenStudio Point3dVector, preserving
        /// order. No cleaning is performed here — see Query.CleanVertices and the Face3D overload.
        /// </summary>
        /// <param name="point3Ds">Ordered SAM points; null returns null; null entries are skipped.</param>
        /// <returns>OpenStudio point vector, or null.</returns>
        public static global::OpenStudio.Point3dVector ToOpenStudio(this IEnumerable<Spatial.Point3D> point3Ds)
        {
            if (point3Ds == null)
            {
                return null;
            }

            global::OpenStudio.Point3dVector result = new global::OpenStudio.Point3dVector();
            foreach (Spatial.Point3D point3D in point3Ds)
            {
                global::OpenStudio.Point3d point3d = point3D.ToOpenStudio();
                if (point3d == null)
                {
                    continue;
                }

                result.Add(point3d);
            }

            return result;
        }
    }
}
