// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using SAM.Geometry.Spatial;

namespace SAM.Geometry.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Validates a polygon for OpenStudio surface creation, applying the vertex-cleaning and
        /// validation rules of the implementation plan §8: cleaning is reported as a warning
        /// (SAM-OS-GEO-002); fewer than three vertices, non-planarity or area below the minimum
        /// are errors (SAM-OS-GEO-001). No repair beyond cleaning is attempted — non-planar
        /// polygons are rejected, never repaired. An empty result list means the polygon is valid
        /// and unchanged.
        /// </summary>
        /// <param name="point3Ds">Ordered polygon vertices.</param>
        /// <param name="distanceTolerance">Distance tolerance [m] (duplicates, planarity).</param>
        /// <param name="angleTolerance">Angle tolerance [rad] (collinearity).</param>
        /// <param name="minimumArea">Minimum polygon area [m²].</param>
        /// <returns>Diagnostics describing cleaning actions and validation failures.</returns>
        public static List<Core.OpenStudio.OpenStudioDiagnostic> ValidatePolygon(IEnumerable<Point3D> point3Ds, double distanceTolerance, double angleTolerance, double minimumArea)
        {
            List<Core.OpenStudio.OpenStudioDiagnostic> result = new List<Core.OpenStudio.OpenStudioDiagnostic>();

            if (point3Ds == null)
            {
                result.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryInvalidBoundary, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "Polygon has no vertices"));
                return result;
            }

            List<Point3D> points = new List<Point3D>(point3Ds);
            List<Point3D> cleaned = CleanVertices(points, distanceTolerance, angleTolerance);

            if (cleaned.Count != points.Count)
            {
                result.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryVerticesCleaned, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Removed {0} duplicate/collinear vertices ({1} → {2})", points.Count - cleaned.Count, points.Count, cleaned.Count)));
            }

            if (cleaned.Count < 3)
            {
                result.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryInvalidBoundary, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Polygon has fewer than three valid vertices after cleaning ({0})", cleaned.Count)));
                return result;
            }

            if (!IsPlanar(cleaned, distanceTolerance))
            {
                result.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryInvalidBoundary, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "Polygon is not planar within tolerance; automatic repair is not performed"));
                return result;
            }

            if (IsSelfIntersecting(cleaned, distanceTolerance))
            {
                result.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryInvalidBoundary, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "Polygon is self-intersecting; automatic repair is not performed"));
                return result;
            }

            double area = Area(cleaned);
            if (double.IsNaN(area) || area < minimumArea)
            {
                result.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryInvalidBoundary, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Polygon area {0:G6} m² is below the minimum {1:G6} m²", area, minimumArea)));
            }

            return result;
        }
    }
}
