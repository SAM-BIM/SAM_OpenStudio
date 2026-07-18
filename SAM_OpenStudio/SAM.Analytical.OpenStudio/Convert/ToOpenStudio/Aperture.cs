// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using SAM.Geometry.OpenStudio;
using SAM.Geometry.Spatial;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Converts a SAM Aperture to an OpenStudio SubSurface hosted by the given surface.
        /// The aperture polygon is cleaned and validated, its winding is aligned with the host
        /// panel's space-oriented outward normal, and its planarity against the host plane is
        /// verified (coplanarity within the elevation tolerance) — an aperture failing validation
        /// is skipped with an error diagnostic, never silently repaired. An aperture without an
        /// ApertureConstruction is skipped with a warning (mirrors SAM_LadybugTools).
        /// </summary>
        /// <param name="aperture">SAM aperture.</param>
        /// <param name="surface">Host OpenStudio surface (already created for the panel side).</param>
        /// <param name="panel">Space-oriented host panel (normal points out of the space).</param>
        /// <param name="spaceIndex">AdjacencyCluster index of the owning space (name disambiguation).</param>
        /// <param name="openStudioConversionContext">Conversion context.</param>
        /// <returns>The created SubSurface, or null when skipped.</returns>
        public static global::OpenStudio.SubSurface ToOpenStudio(this Aperture aperture, global::OpenStudio.Surface surface, Panel panel, int spaceIndex, OpenStudioConversionContext openStudioConversionContext)
        {
            if (aperture == null || surface == null || panel == null || openStudioConversionContext == null)
            {
                return null;
            }

            string name = Core.OpenStudio.Query.OpenStudioName("SubSurface", string.Format("{0}_{1}", aperture.Name, spaceIndex), aperture.Guid);

            if (aperture.ApertureConstruction == null)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionMissingLayer, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "Aperture has no ApertureConstruction and was skipped", aperture, name);
                return null;
            }

            Face3D face3D = aperture.Face3D;
            if (face3D == null)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryInvalidBoundary, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "Aperture has no geometry", aperture, name);
                return null;
            }

            ISegmentable3D segmentable3D = face3D.GetExternalEdge3D() as ISegmentable3D;
            List<Point3D> point3Ds = segmentable3D?.GetPoints();

            List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = Geometry.OpenStudio.Query.ValidatePolygon(point3Ds, openStudioConversionContext.Options.DistanceTolerance, openStudioConversionContext.Options.AngleTolerance, openStudioConversionContext.Options.MinimumArea);
            bool valid = true;
            foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in diagnostics)
            {
                openStudioConversionContext.AddDiagnostic(diagnostic.Code, diagnostic.Severity, diagnostic.Message, aperture, name);
                if (diagnostic.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)
                {
                    valid = false;
                }
            }

            if (!valid)
            {
                return null;
            }

            point3Ds = Geometry.OpenStudio.Query.CleanVertices(point3Ds, openStudioConversionContext.Options.DistanceTolerance, openStudioConversionContext.Options.AngleTolerance);

            Plane plane = panel.GetFace3D()?.GetPlane();
            if (plane != null)
            {
                foreach (Point3D point3D in point3Ds)
                {
                    double distance = plane.Distance(point3D);
                    if (double.IsNaN(distance) || distance > openStudioConversionContext.Options.ElevationTolerance)
                    {
                        openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryInvalidBoundary, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Aperture vertex lies {0:G4} m off the host panel plane", distance), aperture, name);
                        return null;
                    }
                }
            }

            if (Geometry.OpenStudio.Query.IsClockwise(point3Ds, panel.Normal))
            {
                point3Ds.Reverse();
            }

            global::OpenStudio.SubSurface result = new global::OpenStudio.SubSurface(point3Ds.ToOpenStudio(), openStudioConversionContext.Target);
            result.setName(name);
            result.setSurface(surface);

            string subSurfaceType = Query.SubSurfaceType(aperture, openStudioConversionContext.Source?.MaterialLibrary);
            if (!string.IsNullOrWhiteSpace(subSurfaceType))
            {
                result.setSubSurfaceType(subSurfaceType);
            }

            if (!openStudioConversionContext.References.Contains(aperture.Guid))
            {
                openStudioConversionContext.RegisterModelObject(aperture, result);
            }

            return result;
        }
    }
}
