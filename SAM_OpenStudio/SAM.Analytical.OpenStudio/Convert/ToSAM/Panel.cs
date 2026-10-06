// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Converts one OpenStudio Surface into a SAM Panel, including its subsurfaces as SAM
        /// apertures.
        /// <para>
        /// Geometry is read in the owning space's coordinate system and transformed with
        /// <paramref name="transformation"/>; the winding OpenStudio stores (outward normal) is
        /// preserved, and the SAM panel type is derived from surface type *and* boundary
        /// condition together (<see cref="Query.SAMPanelType"/>).
        /// </para>
        /// <para>
        /// One call produces one panel per Surface. The two surfaces of an interzone partition
        /// are merged into a single shared panel later, by the topology pass, using the
        /// OpenStudio adjacency handles — see Convert/ToSAM/AnalyticalModel.cs.
        /// </para>
        /// </summary>
        /// <param name="surface">OpenStudio surface; null returns null.</param>
        /// <param name="openStudioImportContext">Import context.</param>
        /// <param name="transformation">Space → model transformation applied to the vertices.</param>
        /// <returns>The created SAM panel, or null when the surface could not be converted.</returns>
        public static Panel ToSAM(this global::OpenStudio.Surface surface, OpenStudioImportContext openStudioImportContext, global::OpenStudio.Transformation transformation = null)
        {
            if (surface == null || openStudioImportContext == null)
            {
                return null;
            }

            string label = OpenStudioImportContext.OpenStudioObjectLabel(surface);

            string surfaceType = surface.surfaceType();
            string outsideBoundaryCondition = surface.outsideBoundaryCondition();

            bool adiabatic;
            bool supported;
            PanelType panelType = Query.SAMPanelType(surfaceType, outsideBoundaryCondition, out adiabatic, out supported);

            if (!supported)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.BoundaryConditionUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Surface type '{0}' with outside boundary condition '{1}' has no exact SAM equivalent; imported as {2}{3}", surfaceType, outsideBoundaryCondition, panelType, adiabatic ? " (adiabatic)" : string.Empty), label);
            }

            // An air boundary is a SAM PanelType.Air regardless of its surface type: the
            // construction, not the orientation, is what makes it an opening between zones.
            if (IsAirBoundary(surface))
            {
                panelType = PanelType.Air;
                adiabatic = false;
            }

            List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics;
            Geometry.Spatial.Face3D face3D = Geometry.OpenStudio.Convert.ToSAM(surface, openStudioImportContext.Options.DistanceTolerance, openStudioImportContext.Options.AngleTolerance, openStudioImportContext.Options.MinimumArea, out diagnostics, transformation);

            foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in diagnostics)
            {
                openStudioImportContext.AddDiagnostic(diagnostic.Code, diagnostic.Severity, diagnostic.Message, label);
            }

            if (face3D == null)
            {
                openStudioImportContext.RegisterSkip();
                return null;
            }

            Construction construction = ResolveConstruction(surface, panelType, openStudioImportContext);

            Panel result;
            try
            {
                result = Analytical.Create.Panel(construction, panelType, face3D);
            }
            catch (Exception exception)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.GeometryInvalid, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("The SAM panel could not be created ({0}: {1})", exception.GetType().Name, exception.Message), label);
                openStudioImportContext.RegisterSkip();
                return null;
            }

            if (result == null)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.GeometryInvalid, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "The SAM panel could not be created from the surface boundary", label);
                openStudioImportContext.RegisterSkip();
                return null;
            }

            Guid guid = openStudioImportContext.ResolveGuid(surface, typeof(Panel).Name);
            result = Analytical.Create.Panel(guid, result);

            // A SAM Panel's Name is structural — it comes from its Construction — so the
            // OpenStudio name cannot be written to it. Provenance goes into the parameter set
            // instead, together with the handle, which is the only identity that is unique.
            Modify.SetOpenStudioSource(result, surface);

            if (adiabatic)
            {
                result.SetValue(PanelParameter.Adiabatic, true);
            }

            ToSAM_Apertures(surface, result, panelType, openStudioImportContext, transformation);

            openStudioImportContext.RegisterCreated();
            return result;
        }

        /// <summary>
        /// True when the surface behaves as an air boundary. Both routes are checked: OpenStudio's
        /// own <c>isAirWall()</c>, and an explicitly assigned <c>ConstructionAirBoundary</c> —
        /// they do not always agree on models produced by older versions or by measures.
        /// </summary>
        private static bool IsAirBoundary(global::OpenStudio.Surface surface)
        {
            try
            {
                if (surface.isAirWall())
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // fall through to the construction check
            }

            global::OpenStudio.OptionalConstructionBase optionalConstructionBase = surface.construction();
            if (optionalConstructionBase == null || optionalConstructionBase.isNull())
            {
                return false;
            }

            global::OpenStudio.OptionalConstructionAirBoundary optionalConstructionAirBoundary = global::OpenStudio.OpenStudioModelResources.toConstructionAirBoundary(optionalConstructionBase.get());
            return optionalConstructionAirBoundary != null && !optionalConstructionAirBoundary.isNull();
        }

        /// <summary>
        /// Resolves the SAM construction for a surface from the constructions already imported
        /// into <see cref="OpenStudioImportContext.ConstructionMap"/>.
        /// <para>
        /// When the construction is absent — the surface has none, its kind is unsupported, or
        /// construction import is switched off — a name-only placeholder is created and cached so
        /// every surface sharing that OpenStudio construction still shares one SAM construction.
        /// A placeholder is never populated with invented layers: the panel keeps its identity
        /// and the missing build-up is visible instead of being quietly replaced by a default.
        /// </para>
        /// </summary>
        private static Construction ResolveConstruction(global::OpenStudio.Surface surface, PanelType panelType, OpenStudioImportContext openStudioImportContext)
        {
            string constructionName = null;
            global::OpenStudio.OptionalConstructionBase optionalConstructionBase = surface.construction();
            if (optionalConstructionBase != null && !optionalConstructionBase.isNull())
            {
                constructionName = optionalConstructionBase.get().nameString();
            }

            if (string.IsNullOrWhiteSpace(constructionName))
            {
                // No construction at all: name the placeholder after the panel type so several
                // construction-less surfaces of the same type share one SAM construction rather
                // than producing one per surface.
                constructionName = string.Format("Unassigned_{0}", panelType);
                if (openStudioImportContext.RegisterOnce("SAM-OSI-CON-001:Unassigned:" + constructionName))
                {
                    openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ConstructionUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Surface has no construction; a name-only SAM construction '{0}' was created with no layers (nothing was substituted)", constructionName), OpenStudioImportContext.OpenStudioObjectLabel(surface));
                }
            }

            Construction result;
            if (openStudioImportContext.ConstructionMap.TryGetValue(constructionName, out result))
            {
                return result;
            }

            result = new Construction(constructionName);
            openStudioImportContext.ConstructionMap[constructionName] = result;
            return result;
        }
    }
}
