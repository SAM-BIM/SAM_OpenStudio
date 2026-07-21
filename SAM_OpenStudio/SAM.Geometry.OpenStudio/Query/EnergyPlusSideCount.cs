// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using SAM.Geometry.Spatial;

namespace SAM.Geometry.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// The side count a polygon keeps under the EnergyPlus vertex-proximity rule
        /// (SurfaceGeometry GetVertices: consecutive vertices closer than 0.01 m are dropped,
        /// and a surface dropping below three sides is reported as a severe "degenerate
        /// surface" error). EnergyPlus applies this on top of the converter's own cleaning,
        /// which works to far finer tolerances — a sliver panel/aperture (e.g. a 2 mm residual
        /// door) survives our cleaning yet collapses in EnergyPlus. Fewer than three surviving
        /// sides means EnergyPlus would call the surface degenerate.
        /// </summary>
        /// <param name="point3Ds">Ordered polygon vertices (already cleaned by the caller).</param>
        /// <param name="proximity">Proximity threshold [m]; EnergyPlus uses 0.01.</param>
        /// <returns>Surviving side count; 0 for null/empty input.</returns>
        public static int EnergyPlusSideCount(IEnumerable<Point3D> point3Ds, double proximity = 0.01)
        {
            if (point3Ds == null)
            {
                return 0;
            }

            List<Point3D> kept = new List<Point3D>();
            foreach (Point3D point3D in point3Ds)
            {
                if (point3D == null)
                {
                    continue;
                }

                if (kept.Count == 0 || kept[kept.Count - 1].Distance(point3D) >= proximity)
                {
                    kept.Add(point3D);
                }
            }

            // Closing pair: the last vertex is checked against the first, as EnergyPlus checks
            // the wrap-around edge.
            while (kept.Count > 1 && kept[kept.Count - 1].Distance(kept[0]) < proximity)
            {
                kept.RemoveAt(kept.Count - 1);
            }

            return kept.Count;
        }
    }
}
