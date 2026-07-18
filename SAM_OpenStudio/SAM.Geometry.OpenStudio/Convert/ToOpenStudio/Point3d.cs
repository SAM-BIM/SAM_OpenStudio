// SPDX-License-Identifier: LGPL-3.0-only

namespace SAM.Geometry.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Converts a SAM Point3D to an OpenStudio Point3d. Coordinates are copied unchanged:
        /// SAM global coordinates in metres are retained (no local transformations, plan §8).
        /// </summary>
        /// <param name="point3D">SAM point; null returns null.</param>
        /// <returns>OpenStudio point, or null.</returns>
        public static global::OpenStudio.Point3d ToOpenStudio(this Spatial.Point3D point3D)
        {
            if (point3D == null)
            {
                return null;
            }

            return new global::OpenStudio.Point3d(point3D.X, point3D.Y, point3D.Z);
        }
    }
}
