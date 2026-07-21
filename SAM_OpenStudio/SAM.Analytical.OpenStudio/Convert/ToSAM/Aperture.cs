// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Converts the subsurfaces of an OpenStudio Surface into SAM apertures and attaches them
        /// to the host panel.
        /// <para>
        /// Attachment goes through <see cref="Panel.AddAperture(Aperture, double, double)"/> with
        /// the import tolerances, so SAM validates containment: a subsurface that OpenStudio
        /// accepted but that does not actually sit inside its host boundary (a stale window after
        /// a measure resized a wall, for example) is reported rather than silently attached to a
        /// panel it does not fit.
        /// </para>
        /// </summary>
        /// <param name="surface">OpenStudio host surface.</param>
        /// <param name="panel">SAM panel created for it.</param>
        /// <param name="panelType">SAM panel type of the host, used to type the aperture construction.</param>
        /// <param name="openStudioImportContext">Import context.</param>
        /// <param name="transformation">Space → model transformation applied to the vertices.</param>
        /// <returns>The apertures successfully attached.</returns>
        public static List<Aperture> ToSAM_Apertures(this global::OpenStudio.Surface surface, Panel panel, PanelType panelType, OpenStudioImportContext openStudioImportContext, global::OpenStudio.Transformation transformation = null)
        {
            List<Aperture> result = new List<Aperture>();

            if (surface == null || panel == null || openStudioImportContext == null)
            {
                return result;
            }

            global::OpenStudio.SubSurfaceVector subSurfaceVector;
            try
            {
                subSurfaceVector = surface.subSurfaces();
            }
            catch (Exception)
            {
                return result;
            }

            if (subSurfaceVector == null)
            {
                return result;
            }

            foreach (global::OpenStudio.SubSurface subSurface in subSurfaceVector)
            {
                Aperture aperture = subSurface.ToSAM(panelType, openStudioImportContext, transformation);
                if (aperture == null)
                {
                    continue;
                }

                if (!panel.AddAperture(aperture, openStudioImportContext.Options.AngleTolerance, openStudioImportContext.Options.DistanceTolerance))
                {
                    openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.GeometryInvalid, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "The subsurface is not contained by its host surface boundary within tolerance; it was not attached to the SAM panel", OpenStudioImportContext.OpenStudioObjectLabel(subSurface), panel);
                    openStudioImportContext.RegisterSkip();
                    continue;
                }

                result.Add(aperture);
                openStudioImportContext.RegisterCreated();
            }

            return result;
        }

        /// <summary>
        /// Converts one OpenStudio SubSurface into a SAM Aperture. Unsupported subsurface types
        /// are reported (SAM-OSI-SUB-001) and skipped — never silently dropped.
        /// </summary>
        /// <param name="subSurface">OpenStudio subsurface; null returns null.</param>
        /// <param name="panelType">SAM panel type of the host surface.</param>
        /// <param name="openStudioImportContext">Import context.</param>
        /// <param name="transformation">Space → model transformation applied to the vertices.</param>
        /// <returns>The created aperture, or null.</returns>
        public static Aperture ToSAM(this global::OpenStudio.SubSurface subSurface, PanelType panelType, OpenStudioImportContext openStudioImportContext, global::OpenStudio.Transformation transformation = null)
        {
            if (subSurface == null || openStudioImportContext == null)
            {
                return null;
            }

            string label = OpenStudioImportContext.OpenStudioObjectLabel(subSurface);
            string subSurfaceType = subSurface.subSurfaceType();

            bool approximated;
            ApertureType apertureType = Query.SAMApertureType(subSurfaceType, out approximated);
            if (apertureType == ApertureType.Undefined)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.SubSurfaceUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Subsurface type '{0}' has no SAM equivalent; the subsurface was not imported", subSurfaceType), label);
                openStudioImportContext.RegisterSkip();
                return null;
            }

            if (approximated && openStudioImportContext.RegisterOnce("SAM-OSI-APX-001:SubSurfaceType:" + subSurfaceType))
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format("Subsurface type '{0}' was imported as a SAM {1}; SAM has no distinct representation for it (openability, tubular light path and similar behaviour are not carried)", subSurfaceType, apertureType), label);
            }

            List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics;
            Geometry.Spatial.Face3D face3D = Geometry.OpenStudio.Convert.ToSAM(subSurface, openStudioImportContext.Options.DistanceTolerance, openStudioImportContext.Options.AngleTolerance, openStudioImportContext.Options.MinimumArea, out diagnostics, transformation);

            foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in diagnostics)
            {
                openStudioImportContext.AddDiagnostic(diagnostic.Code, diagnostic.Severity, diagnostic.Message, label);
            }

            if (face3D == null)
            {
                openStudioImportContext.RegisterSkip();
                return null;
            }

            double multiplier = 1;
            try
            {
                multiplier = subSurface.multiplier();
            }
            catch (Exception)
            {
                // an unreadable multiplier is treated as 1 and never reported as a fabricated value
            }

            if (multiplier > 1)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format(System.Globalization.CultureInfo.InvariantCulture, "Subsurface multiplier {0:G4} is not represented: SAM apertures have no multiplier, so the imported aperture carries the modelled geometry once and the total glazed area is lower than in the OpenStudio model", multiplier), label);
            }

            ApertureConstruction apertureConstruction = ResolveApertureConstruction(subSurface, apertureType, panelType, openStudioImportContext);

            Aperture result;
            try
            {
                result = new Aperture(apertureConstruction, face3D);
            }
            catch (Exception exception)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.GeometryInvalid, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("The SAM aperture could not be created ({0}: {1})", exception.GetType().Name, exception.Message), label);
                openStudioImportContext.RegisterSkip();
                return null;
            }

            Guid guid = openStudioImportContext.ResolveGuid(subSurface, typeof(Aperture).Name);
            result = new Aperture(guid, result);

            Modify.SetOpenStudioSource(result, subSurface);

            return result;
        }

        /// <summary>
        /// Resolves the SAM aperture construction for a subsurface from the constructions already
        /// imported into <see cref="OpenStudioImportContext.ApertureConstructionMap"/>, falling
        /// back to a name-only placeholder with no invented layers. See the equivalent policy in
        /// Convert/ToSAM/Panel.cs.
        /// </summary>
        private static ApertureConstruction ResolveApertureConstruction(global::OpenStudio.SubSurface subSurface, ApertureType apertureType, PanelType panelType, OpenStudioImportContext openStudioImportContext)
        {
            string constructionName = null;
            global::OpenStudio.OptionalConstructionBase optionalConstructionBase = subSurface.construction();
            if (optionalConstructionBase != null && !optionalConstructionBase.isNull())
            {
                constructionName = optionalConstructionBase.get().nameString();
            }

            if (string.IsNullOrWhiteSpace(constructionName))
            {
                constructionName = string.Format("Unassigned_{0}_{1}", panelType, apertureType);
                if (openStudioImportContext.RegisterOnce("SAM-OSI-CON-001:UnassignedAperture:" + constructionName))
                {
                    openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ConstructionUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Subsurface has no construction; a name-only SAM aperture construction '{0}' was created with no layers (nothing was substituted)", constructionName), OpenStudioImportContext.OpenStudioObjectLabel(subSurface));
                }
            }

            ApertureConstruction result;
            if (openStudioImportContext.ApertureConstructionMap.TryGetValue(constructionName, out result))
            {
                return result;
            }

            result = new ApertureConstruction(constructionName, apertureType);
            openStudioImportContext.ApertureConstructionMap[constructionName] = result;
            return result;
        }
    }
}
