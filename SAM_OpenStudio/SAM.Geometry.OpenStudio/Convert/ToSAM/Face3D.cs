// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using SAM.Geometry.Spatial;

namespace SAM.Geometry.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Converts an OpenStudio PlanarSurface (Surface, SubSurface or ShadingSurface) into a
        /// SAM Face3D, applying <paramref name="transformation"/> to reach the target coordinate
        /// system and running the same validation the forward direction applies
        /// (<see cref="Query.ValidatePolygon"/>) before anything is built.
        /// <para>
        /// Nothing is repaired: a boundary that fails validation returns null with the reasons in
        /// <paramref name="diagnostics"/>, so the caller can name the offending OpenStudio object
        /// and handle rather than emit silently-wrong geometry.
        /// </para>
        /// <para>
        /// Winding is preserved exactly as OpenStudio stores it (counter-clockwise viewed from
        /// outside, giving an outward normal). The importer relies on that normal to classify
        /// floors versus ceilings, so re-winding here would destroy information.
        /// </para>
        /// </summary>
        /// <param name="planarSurface">OpenStudio planar surface; null returns null.</param>
        /// <param name="distanceTolerance">Distance tolerance [m].</param>
        /// <param name="angleTolerance">Angle tolerance [rad].</param>
        /// <param name="minimumArea">Minimum polygon area [m²].</param>
        /// <param name="diagnostics">Receives cleaning and validation diagnostics; never null on return.</param>
        /// <param name="transformation">Group → target-space transformation; null means none.</param>
        /// <returns>The converted face, or null when the boundary is unusable.</returns>
        public static Face3D ToSAM(this global::OpenStudio.PlanarSurface planarSurface, double distanceTolerance, double angleTolerance, double minimumArea, out List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics, global::OpenStudio.Transformation transformation = null)
        {
            diagnostics = new List<Core.OpenStudio.OpenStudioDiagnostic>();

            if (planarSurface == null)
            {
                return null;
            }

            global::OpenStudio.Point3dVector point3dVector;
            try
            {
                point3dVector = planarSurface.vertices();
            }
            catch (System.Exception exception)
            {
                diagnostics.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.GeometryInvalid, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("The vertices could not be read ({0}: {1})", exception.GetType().Name, exception.Message)));
                return null;
            }

            List<Point3D> point3Ds = point3dVector.ToSAM(transformation);
            if (point3Ds == null)
            {
                diagnostics.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.GeometryInvalid, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "The vertices could not be transformed into the target coordinate system"));
                return null;
            }

            return ToSAM(point3Ds, distanceTolerance, angleTolerance, minimumArea, diagnostics);
        }

        /// <summary>
        /// Builds a validated SAM Face3D from ordered points already in the target coordinate
        /// system. Shared by surfaces, subsurfaces and shading surfaces so all three obey one
        /// validation policy.
        /// </summary>
        /// <param name="point3Ds">Ordered boundary points.</param>
        /// <param name="distanceTolerance">Distance tolerance [m].</param>
        /// <param name="angleTolerance">Angle tolerance [rad].</param>
        /// <param name="minimumArea">Minimum polygon area [m²].</param>
        /// <param name="diagnostics">Receives cleaning and validation diagnostics; required.</param>
        /// <returns>The converted face, or null when the boundary is unusable.</returns>
        public static Face3D ToSAM(IEnumerable<Point3D> point3Ds, double distanceTolerance, double angleTolerance, double minimumArea, IList<Core.OpenStudio.OpenStudioDiagnostic> diagnostics)
        {
            if (point3Ds == null || diagnostics == null)
            {
                return null;
            }

            // Reuse the forward validator so both directions reject the same geometry. Its codes
            // are forward codes, so they are re-badged with the import equivalents: a consumer
            // filtering on SAM-OSI-* must see every import geometry event.
            //
            // Vertex cleaning keeps its own code and drops to Information: removing a collinear
            // vertex leaves the polygon's shape, area and normal identical, so reporting it at the
            // same code and severity as genuinely invalid geometry buries the real failures among
            // routine normalisation (a real model produces one per surface).
            bool valid = true;
            foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in Query.ValidatePolygon(point3Ds, distanceTolerance, angleTolerance, minimumArea))
            {
                bool verticesCleaned = diagnostic.Code == Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryVerticesCleaned;

                diagnostics.Add(new Core.OpenStudio.OpenStudioDiagnostic(
                    verticesCleaned ? Core.OpenStudio.OpenStudioImportDiagnosticCodes.GeometryVerticesCleaned : Core.OpenStudio.OpenStudioImportDiagnosticCodes.GeometryInvalid,
                    verticesCleaned ? Core.OpenStudio.OpenStudioDiagnosticSeverity.Information : diagnostic.Severity,
                    diagnostic.Message));

                if (diagnostic.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)
                {
                    valid = false;
                }
            }

            if (!valid)
            {
                return null;
            }

            List<Point3D> cleaned = Query.CleanVertices(point3Ds, distanceTolerance, angleTolerance);
            if (cleaned == null || cleaned.Count < 3)
            {
                diagnostics.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.GeometryInvalid, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "The boundary collapses to fewer than three vertices after cleaning"));
                return null;
            }

            try
            {
                Polygon3D polygon3D = new Polygon3D(cleaned, distanceTolerance);
                if (polygon3D == null)
                {
                    diagnostics.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.GeometryInvalid, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "The boundary could not be built into a SAM polygon"));
                    return null;
                }

                return new Face3D(polygon3D);
            }
            catch (System.Exception exception)
            {
                diagnostics.Add(new Core.OpenStudio.OpenStudioDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.GeometryInvalid, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("The boundary could not be built into a SAM face ({0}: {1})", exception.GetType().Name, exception.Message)));
                return null;
            }
        }

        /// <summary>
        /// Outward normal of an OpenStudio planar surface expressed in the target coordinate
        /// system. Derived from the transformed vertices with the Newell method rather than from
        /// <c>PlanarSurface.outwardNormal()</c>, because the native normal is in group
        /// coordinates and would be wrong for any rotated or non-origin group.
        /// </summary>
        /// <param name="planarSurface">OpenStudio planar surface; null returns null.</param>
        /// <param name="transformation">Group → target-space transformation; null means none.</param>
        /// <returns>Unnormalized outward normal, or null.</returns>
        public static Vector3D ToSAM_Normal(this global::OpenStudio.PlanarSurface planarSurface, global::OpenStudio.Transformation transformation = null)
        {
            if (planarSurface == null)
            {
                return null;
            }

            List<Point3D> point3Ds;
            try
            {
                point3Ds = planarSurface.vertices().ToSAM(transformation);
            }
            catch (System.Exception)
            {
                return null;
            }

            return Query.Normal(point3Ds);
        }
    }
}
