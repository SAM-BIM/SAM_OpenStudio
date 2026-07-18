// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using SAM.Geometry.Spatial;

namespace SAM.Geometry.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Cleans an ordered closed-polygon vertex list for OpenStudio surface creation:
        /// removes null entries, consecutive duplicates within the distance tolerance (including
        /// the duplicate closing vertex), and collinear or spike vertices within the angle
        /// tolerance. The polygon is treated as closed (first–last wraparound). The input is not
        /// modified. The result may contain fewer than three vertices — callers must validate.
        /// </summary>
        /// <param name="point3Ds">Ordered polygon vertices; null returns null.</param>
        /// <param name="distanceTolerance">Distance tolerance [m] for duplicate removal.</param>
        /// <param name="angleTolerance">Angle tolerance [rad] for collinearity removal.</param>
        /// <returns>New cleaned vertex list, or null.</returns>
        public static List<Point3D> CleanVertices(IEnumerable<Point3D> point3Ds, double distanceTolerance, double angleTolerance)
        {
            if (point3Ds == null)
            {
                return null;
            }

            List<Point3D> result = new List<Point3D>();
            foreach (Point3D point3D in point3Ds)
            {
                if (point3D == null)
                {
                    continue;
                }

                if (result.Count > 0 && Distance(result[result.Count - 1], point3D) <= distanceTolerance)
                {
                    continue;
                }

                result.Add(point3D);
            }

            while (result.Count > 1 && Distance(result[0], result[result.Count - 1]) <= distanceTolerance)
            {
                result.RemoveAt(result.Count - 1);
            }

            if (result.Count < 3)
            {
                return result;
            }

            double sinAngleTolerance = Math.Sin(angleTolerance);
            bool removed = true;
            int iterations = 0;
            while (removed && result.Count >= 3 && iterations <= result.Count * result.Count)
            {
                removed = false;
                for (int i = 0; i < result.Count; i++)
                {
                    iterations++;

                    Point3D previous = result[(i + result.Count - 1) % result.Count];
                    Point3D current = result[i];
                    Point3D next = result[(i + 1) % result.Count];

                    double aX = current.X - previous.X;
                    double aY = current.Y - previous.Y;
                    double aZ = current.Z - previous.Z;
                    double bX = next.X - current.X;
                    double bY = next.Y - current.Y;
                    double bZ = next.Z - current.Z;

                    double aLength = Math.Sqrt(aX * aX + aY * aY + aZ * aZ);
                    double bLength = Math.Sqrt(bX * bX + bY * bY + bZ * bZ);
                    if (aLength <= distanceTolerance || bLength <= distanceTolerance)
                    {
                        result.RemoveAt(i);
                        removed = true;
                        break;
                    }

                    double crossX = aY * bZ - aZ * bY;
                    double crossY = aZ * bX - aX * bZ;
                    double crossZ = aX * bY - aY * bX;
                    double crossLength = Math.Sqrt(crossX * crossX + crossY * crossY + crossZ * crossZ);

                    if (crossLength <= aLength * bLength * sinAngleTolerance)
                    {
                        result.RemoveAt(i);
                        removed = true;
                        break;
                    }
                }
            }

            return result;
        }

        private static double Distance(Point3D point3D_1, Point3D point3D_2)
        {
            double dX = point3D_1.X - point3D_2.X;
            double dY = point3D_1.Y - point3D_2.Y;
            double dZ = point3D_1.Z - point3D_2.Z;
            return Math.Sqrt(dX * dX + dY * dY + dZ * dZ);
        }
    }
}
