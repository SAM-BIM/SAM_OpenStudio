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
        /// Converts SAM shading panels (PanelType.Shade from AdjacencyCluster.GetShadingPanels)
        /// to OpenStudio ShadingSurfaces in a single building-level ShadingSurfaceGroup —
        /// mirroring the SAM_LadybugTools orphan-shade handling. Invalid shade geometry is
        /// skipped with a diagnostic.
        /// </summary>
        /// <param name="openStudioConversionContext">Conversion context with a non-null source model.</param>
        /// <returns>Created shading surfaces (possibly empty, never null).</returns>
        public static List<global::OpenStudio.ShadingSurface> ToOpenStudio_ShadingSurfaces(this OpenStudioConversionContext openStudioConversionContext)
        {
            List<global::OpenStudio.ShadingSurface> result = new List<global::OpenStudio.ShadingSurface>();

            AdjacencyCluster adjacencyCluster = openStudioConversionContext?.Source?.AdjacencyCluster;
            if (adjacencyCluster == null)
            {
                return result;
            }

            List<Panel> panels = adjacencyCluster.GetShadingPanels();
            if (panels == null || panels.Count == 0)
            {
                return result;
            }

            global::OpenStudio.ShadingSurfaceGroup shadingSurfaceGroup = null;

            foreach (Panel panel in panels)
            {
                if (panel == null || panel.PanelType != PanelType.Shade)
                {
                    continue;
                }

                string name = Core.OpenStudio.Query.OpenStudioName("ShadingSurface", panel.Name, panel.Guid);

                Face3D face3D = panel.GetFace3D();
                ISegmentable3D segmentable3D = face3D?.GetExternalEdge3D() as ISegmentable3D;
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
                    continue;
                }

                global::OpenStudio.Point3dVector point3dVector = face3D.ToOpenStudio(openStudioConversionContext.Options.DistanceTolerance, openStudioConversionContext.Options.AngleTolerance);
                if (point3dVector == null || point3dVector.Count < 3)
                {
                    continue;
                }

                if (shadingSurfaceGroup == null)
                {
                    shadingSurfaceGroup = new global::OpenStudio.ShadingSurfaceGroup(openStudioConversionContext.Target);
                    shadingSurfaceGroup.setName("SAM_ShadingSurfaceGroup_Building");
                    shadingSurfaceGroup.setShadingSurfaceType("Building");
                }

                global::OpenStudio.ShadingSurface shadingSurface = new global::OpenStudio.ShadingSurface(point3dVector, openStudioConversionContext.Target);
                shadingSurface.setName(name);
                shadingSurface.setShadingSurfaceGroup(shadingSurfaceGroup);

                if (!openStudioConversionContext.References.Contains(panel.Guid))
                {
                    openStudioConversionContext.RegisterModelObject(panel, shadingSurface);
                }

                result.Add(shadingSurface);
            }

            return result;
        }
    }
}
