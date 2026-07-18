// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Geometry.OpenStudio;
using SAM.Geometry.Spatial;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Converts one space-facing side of a SAM Panel to an OpenStudio Surface. The panel is
        /// expected to come from AdjacencyCluster.UpdateNormals for the owning space, so its
        /// Face3D winding already gives an outward (out-of-space) normal — vertices are converted
        /// in that order. Geometry is cleaned and validated (never repaired beyond cleaning);
        /// failures are recorded as diagnostics and the surface is skipped. The first created side
        /// of a panel is registered as its primary surface; boundary conditions and adjacent
        /// surfaces are resolved by the orchestrator in a later pass.
        /// </summary>
        /// <param name="panel">Space-oriented SAM panel (from UpdateNormals).</param>
        /// <param name="openStudioSpace">OpenStudio space this side belongs to.</param>
        /// <param name="spaceIndex">AdjacencyCluster index of the owning space (name disambiguation).</param>
        /// <param name="openStudioConversionContext">Conversion context.</param>
        /// <param name="subSurfacesByAperture">Registry collecting created SubSurfaces per aperture Guid for later pairing; may be null.</param>
        /// <returns>The created Surface, or null when skipped.</returns>
        public static global::OpenStudio.Surface ToOpenStudio(this Panel panel, global::OpenStudio.Space openStudioSpace, int spaceIndex, OpenStudioConversionContext openStudioConversionContext, IDictionary<Guid, List<global::OpenStudio.SubSurface>> subSurfacesByAperture = null)
        {
            if (panel == null || openStudioSpace == null || openStudioConversionContext == null)
            {
                return null;
            }

            if (panel.PanelType == PanelType.Shade)
            {
                return null;
            }

            string name = Core.OpenStudio.Query.OpenStudioName("Surface", string.Format("{0}_{1}", panel.Name, spaceIndex), panel.Guid);

            Face3D face3D = panel.GetFace3D();
            if (face3D == null)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryInvalidBoundary, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "Panel has no geometry", panel, name);
                return null;
            }

            ISegmentable3D segmentable3D = face3D.GetExternalEdge3D() as ISegmentable3D;
            List<Point3D> point3Ds = segmentable3D?.GetPoints();

            List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = Geometry.OpenStudio.Query.ValidatePolygon(point3Ds, openStudioConversionContext.Options.DistanceTolerance, openStudioConversionContext.Options.AngleTolerance, openStudioConversionContext.Options.MinimumArea);
            bool valid = true;
            foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in diagnostics)
            {
                openStudioConversionContext.AddDiagnostic(diagnostic.Code, diagnostic.Severity, diagnostic.Message, panel, name);
                if (diagnostic.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)
                {
                    valid = false;
                }
            }

            if (!valid)
            {
                return null;
            }

            List<IClosedPlanar3D> internalEdges = face3D.GetInternalEdge3Ds();
            if (internalEdges != null && internalEdges.Count > 0)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryInvalidBoundary, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Panel face has {0} internal edge(s) (holes); holes are not converted in the MVP — only the external boundary is used", internalEdges.Count), panel, name);
            }

            global::OpenStudio.Point3dVector point3dVector = face3D.ToOpenStudio(openStudioConversionContext.Options.DistanceTolerance, openStudioConversionContext.Options.AngleTolerance);
            if (point3dVector == null || point3dVector.Count < 3)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryInvalidBoundary, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "Panel boundary could not be converted to an OpenStudio polygon", panel, name);
                return null;
            }

            global::OpenStudio.Surface result = new global::OpenStudio.Surface(point3dVector, openStudioConversionContext.Target);
            result.setName(name);
            result.setSpace(openStudioSpace);

            string surfaceType = Query.SurfaceType(panel);
            if (!string.IsNullOrWhiteSpace(surfaceType))
            {
                result.setSurfaceType(surfaceType);
            }

            if (!openStudioConversionContext.PrimarySurfaceMap.ContainsKey(panel.Guid))
            {
                openStudioConversionContext.PrimarySurfaceMap[panel.Guid] = result;
                openStudioConversionContext.RegisterModelObject(panel, result);
            }

            List<Aperture> apertures = panel.Apertures;
            if (apertures != null)
            {
                foreach (Aperture aperture in apertures)
                {
                    global::OpenStudio.SubSurface subSurface = aperture.ToOpenStudio(result, panel, spaceIndex, openStudioConversionContext);
                    if (subSurface == null)
                    {
                        continue;
                    }

                    if (subSurfacesByAperture != null)
                    {
                        List<global::OpenStudio.SubSurface> subSurfaces;
                        if (!subSurfacesByAperture.TryGetValue(aperture.Guid, out subSurfaces))
                        {
                            subSurfaces = new List<global::OpenStudio.SubSurface>();
                            subSurfacesByAperture[aperture.Guid] = subSurfaces;
                        }

                        subSurfaces.Add(subSurface);
                    }
                }
            }

            return result;
        }
    }
}
