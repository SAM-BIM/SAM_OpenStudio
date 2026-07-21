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

            // EnergyPlus applies its own 0.01 m vertex-proximity rule on top of our cleaning:
            // a sliver aperture (e.g. a residual millimetre-wide door) collapses below three
            // sides and is reported as a severe "degenerate surface". Reject it here instead —
            // EnergyPlus never sees it.
            int energyPlusSides = Geometry.OpenStudio.Query.EnergyPlusSideCount(point3Ds);
            if (energyPlusSides < 3)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryInvalidBoundary, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Aperture would be degenerate in EnergyPlus: its vertices collapse to {0} side(s) under the 0.01 m proximity rule (sliver geometry); skipped — repair the source aperture", energyPlusSides), aperture, name);
                openStudioConversionContext.RegisterSkip();
                return null;
            }

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

            ApertureConstruction apertureConstruction = aperture.ApertureConstruction;
            EmitUnsupportedDiagnostics(aperture, apertureConstruction, name, openStudioConversionContext);

            string subSurfaceType = Query.SubSurfaceType(aperture, openStudioConversionContext.Source?.MaterialLibrary);

            // Frame (C3): when the aperture construction carries frame data, the SubSurface
            // polygon becomes the PANE polygon (EnergyPlus grows the frame outward from the
            // glass — coverage manifest: Aperture.PaneFrameGeometry) and a
            // WindowPropertyFrameAndDivider carries width/conductance/absorptances. Any invalid
            // or incomplete frame data falls back to the full-polygon frameless conversion with
            // a structured warning — never wrong geometry.
            List<Point3D> subSurfacePoint3Ds = point3Ds;
            global::OpenStudio.WindowPropertyFrameAndDivider frameAndDivider = TryCreateFrameAndDivider(aperture, apertureConstruction, subSurfaceType, face3D, point3Ds, name, openStudioConversionContext, out List<Point3D> panePoint3Ds);
            if (frameAndDivider != null && panePoint3Ds != null)
            {
                subSurfacePoint3Ds = panePoint3Ds;
            }

            if (Geometry.OpenStudio.Query.IsClockwise(subSurfacePoint3Ds, panel.Normal))
            {
                subSurfacePoint3Ds.Reverse();
            }

            global::OpenStudio.SubSurface result = new global::OpenStudio.SubSurface(subSurfacePoint3Ds.ToOpenStudio(), openStudioConversionContext.Target);
            result.setName(name);
            result.setSurface(surface);

            if (!string.IsNullOrWhiteSpace(subSurfaceType))
            {
                result.setSubSurfaceType(subSurfaceType);
            }

            if (frameAndDivider != null)
            {
                result.setWindowPropertyFrameAndDivider(frameAndDivider);
            }

            // Identity on every side: an aperture in an internal panel becomes two subsurfaces,
            // and the reverse import must restore the same SAM Guid whichever it meets first.
            Core.OpenStudio.Modify.SetSAMIdentity(result, aperture);

            if (!openStudioConversionContext.References.Contains(aperture.Guid))
            {
                openStudioConversionContext.RegisterModelObject(aperture, result);
            }

            return result;
        }

        /// <summary>
        /// Builds a WindowPropertyFrameAndDivider from SAM frame data (coverage manifest:
        /// ApertureConstruction.FrameConstructionLayers — Approximated): width from
        /// DefaultFrameWidth else the frame thickness; conductance U = 1/Σ(thickness/conductivity)
        /// over the frame layers (no film coefficients, documented approximation); solar/visible
        /// absorptance = 1 − External*Reflectance of the outermost frame layer. Returns null
        /// (frameless fallback) when there is no frame data, the aperture is an opaque door, or
        /// any derivation is invalid — a diagnostic explains every non-silent case.
        /// </summary>
        private static global::OpenStudio.WindowPropertyFrameAndDivider TryCreateFrameAndDivider(Aperture aperture, ApertureConstruction apertureConstruction, string subSurfaceType, Face3D apertureFace3D, List<Point3D> aperturePoint3Ds, string openStudioObjectName, OpenStudioConversionContext openStudioConversionContext, out List<Point3D> panePoint3Ds)
        {
            panePoint3Ds = null;

            double frameWidth = double.NaN;
            if (!apertureConstruction.TryGetValue(ApertureConstructionParameter.DefaultFrameWidth, out frameWidth) || double.IsNaN(frameWidth) || frameWidth <= 0)
            {
                frameWidth = apertureConstruction.GetFrameThickness();
            }

            List<ConstructionLayer> frameLayers = apertureConstruction.FrameConstructionLayers;
            bool hasFrameData = (!double.IsNaN(frameWidth) && frameWidth > 0) || (frameLayers != null && frameLayers.Count > 0);
            if (!hasFrameData)
            {
                return null;
            }

            if (subSurfaceType == "Door")
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "Frame data on an opaque door is not converted (frames apply to glazed subsurfaces); frameless conversion", aperture, openStudioObjectName);
                return null;
            }

            if (double.IsNaN(frameWidth) || frameWidth <= 0 || frameLayers == null || frameLayers.Count == 0)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "Incomplete frame data (width or frame layers missing); frameless conversion", aperture, openStudioObjectName);
                return null;
            }

            Core.MaterialLibrary materialLibrary = openStudioConversionContext.Source?.MaterialLibrary;
            if (materialLibrary == null)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "No MaterialLibrary; frame conductance cannot be derived — frameless conversion", aperture, openStudioObjectName);
                return null;
            }

            double resistance = 0;
            foreach (ConstructionLayer frameLayer in frameLayers)
            {
                if (frameLayer == null)
                {
                    continue;
                }

                Core.IMaterial material = materialLibrary.GetMaterial(frameLayer.Name);
                Core.OpaqueMaterial opaqueMaterial = material as Core.OpaqueMaterial;
                if (opaqueMaterial == null)
                {
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Frame layer material '{0}' is missing or not opaque; frame conductance cannot be derived — frameless conversion", frameLayer.Name), aperture, openStudioObjectName);
                    return null;
                }

                double layerThickness = frameLayer.Thickness;
                if (double.IsNaN(layerThickness) || layerThickness <= 0)
                {
                    if (!opaqueMaterial.TryGetValue(Core.MaterialParameter.DefaultThickness, out layerThickness) || double.IsNaN(layerThickness) || layerThickness <= 0)
                    {
                        openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Frame layer '{0}' has no usable thickness; frameless conversion", frameLayer.Name), aperture, openStudioObjectName);
                        return null;
                    }
                }

                if (double.IsNaN(opaqueMaterial.ThermalConductivity) || opaqueMaterial.ThermalConductivity <= 0)
                {
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Frame material '{0}' has an invalid conductivity; frameless conversion", frameLayer.Name), aperture, openStudioObjectName);
                    return null;
                }

                resistance += layerThickness / opaqueMaterial.ThermalConductivity;
            }

            if (resistance <= 0)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "Frame layers give zero resistance; frameless conversion", aperture, openStudioObjectName);
                return null;
            }

            // Pane geometry from SAM itself (Aperture.GetFace3Ds(AperturePart.Pane) insets by the
            // frame width), validated against the full aperture polygon.
            List<Face3D> paneFace3Ds;
            try
            {
                paneFace3Ds = aperture.GetPaneFace3Ds();
            }
            catch (System.Exception exception)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Pane geometry could not be computed ({0}); frameless conversion", exception.GetType().Name), aperture, openStudioObjectName);
                return null;
            }

            Face3D paneFace3D = paneFace3Ds?.Find(x => x != null);
            ISegmentable3D paneSegmentable3D = paneFace3D?.GetExternalEdge3D() as ISegmentable3D;
            List<Point3D> candidatePoint3Ds = paneSegmentable3D?.GetPoints();
            if (candidatePoint3Ds == null || candidatePoint3Ds.Count < 3)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "Pane geometry is degenerate; frameless conversion", aperture, openStudioObjectName);
                return null;
            }

            double apertureArea = apertureFace3D.GetArea();
            double paneArea = paneFace3D.GetArea();
            if (double.IsNaN(apertureArea) || double.IsNaN(paneArea) || paneArea < openStudioConversionContext.Options.MinimumArea || paneArea >= apertureArea)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Area conservation failed (aperture {0:G4} m², pane {1:G4} m² — the pane must be strictly smaller); frameless conversion", apertureArea, paneArea), aperture, openStudioObjectName);
                return null;
            }

            global::OpenStudio.WindowPropertyFrameAndDivider result = new global::OpenStudio.WindowPropertyFrameAndDivider(openStudioConversionContext.Target);
            result.setName(openStudioObjectName + "_FrameAndDivider");
            result.setFrameWidth(frameWidth);
            result.setFrameConductance(1.0 / resistance);

            ConstructionLayer outermostLayer = frameLayers[frameLayers.Count - 1];
            Core.IMaterial outermostMaterial = outermostLayer == null ? null : materialLibrary.GetMaterial(outermostLayer.Name);
            Core.SAMObject outermostSAMObject = outermostMaterial as Core.SAMObject;
            if (outermostSAMObject != null)
            {
                if (outermostSAMObject.TryGetValue(OpaqueMaterialParameter.ExternalSolarReflectance, out double externalSolarReflectance) && !double.IsNaN(externalSolarReflectance) && externalSolarReflectance >= 0 && externalSolarReflectance <= 1)
                {
                    result.setFrameSolarAbsorptance(1 - externalSolarReflectance);
                }

                if (outermostSAMObject.TryGetValue(OpaqueMaterialParameter.ExternalLightReflectance, out double externalLightReflectance) && !double.IsNaN(externalLightReflectance) && externalLightReflectance >= 0 && externalLightReflectance <= 1)
                {
                    result.setFrameVisibleAbsorptance(1 - externalLightReflectance);
                }
            }

            panePoint3Ds = candidatePoint3Ds;
            return result;
        }

        /// <summary>
        /// Structured diagnostics for aperture data with no safe Ideal-Loads representation
        /// (coverage manifest): opening properties (AirflowNetwork territory), feature shades,
        /// blind flags and TAS additional-heat-transfer percentages. Never silently dropped.
        /// </summary>
        private static void EmitUnsupportedDiagnostics(Aperture aperture, ApertureConstruction apertureConstruction, string openStudioObjectName, OpenStudioConversionContext openStudioConversionContext)
        {
            if (aperture.GetValue<IOpeningProperties>(ApertureParameter.OpeningProperties) != null)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "Opening properties (openable fraction, discharge coefficient) are not converted — natural ventilation needs AirflowNetwork/ZoneVentilation (deferred to the HVAC programme)", aperture, openStudioObjectName);
                openStudioConversionContext.RegisterSkip();
            }

            if (aperture.GetValue<FeatureShade>(ApertureParameter.FeatureShade) != null)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "Aperture feature shade is not converted — SAM carries no shade/slat geometry to build WindowShadingControl", aperture, openStudioObjectName);
                openStudioConversionContext.RegisterSkip();
            }

            if (apertureConstruction != null)
            {
                if (apertureConstruction.TryGetValue(ApertureConstructionParameter.PaneAdditionalHeatTransfer, out double paneAdditionalHeatTransfer) && !double.IsNaN(paneAdditionalHeatTransfer) && paneAdditionalHeatTransfer != 0)
                {
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "Pane additional heat transfer (TAS % U-adjustment) is not converted", aperture, openStudioObjectName);
                    openStudioConversionContext.RegisterSkip();
                }

                if (apertureConstruction.TryGetValue(ApertureConstructionParameter.FrameAdditionalHeatTransfer, out double frameAdditionalHeatTransfer) && !double.IsNaN(frameAdditionalHeatTransfer) && frameAdditionalHeatTransfer != 0)
                {
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "Frame additional heat transfer (TAS % U-adjustment) is not converted", aperture, openStudioObjectName);
                    openStudioConversionContext.RegisterSkip();
                }

                if (apertureConstruction.TryGetValue(ApertureConstructionParameter.IsInternalShadow, out bool isInternalShadow) && isInternalShadow)
                {
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "Internal-shadow flag (TAS) on the aperture construction is not converted — no deterministic EnergyPlus mapping", aperture, openStudioObjectName);
                    openStudioConversionContext.RegisterSkip();
                }
            }
        }
    }
}
