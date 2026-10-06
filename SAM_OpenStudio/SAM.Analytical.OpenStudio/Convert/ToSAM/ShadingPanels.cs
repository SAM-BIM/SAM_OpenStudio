// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Converts every shading surface in the model into a SAM shade panel
        /// (<see cref="PanelType.Shade"/>).
        /// <para>
        /// All three OpenStudio group types are handled — Site, Building and Space — and the
        /// transformation chain differs by type: a Space group is nested inside its space, so its
        /// vertices need the group transformation *and* the space's building transformation,
        /// while Site and Building groups need only their own. Getting this wrong puts every
        /// space-attached shade at the model origin, which is why the chain is composed
        /// explicitly rather than assumed.
        /// </para>
        /// <para>
        /// The group type and name are recorded on each panel so the grouping survives the import
        /// even though SAM has no shading-group concept.
        /// </para>
        /// </summary>
        /// <param name="openStudioImportContext">Import context.</param>
        /// <returns>The created shade panels; never null.</returns>
        public static List<Panel> ToSAM_ShadingPanels(this OpenStudioImportContext openStudioImportContext)
        {
            List<Panel> result = new List<Panel>();

            if (openStudioImportContext == null)
            {
                return result;
            }

            global::OpenStudio.ShadingSurfaceGroupVector shadingSurfaceGroupVector;
            try
            {
                shadingSurfaceGroupVector = openStudioImportContext.Source.getShadingSurfaceGroups();
            }
            catch (Exception)
            {
                return result;
            }

            if (shadingSurfaceGroupVector == null)
            {
                return result;
            }

            foreach (global::OpenStudio.ShadingSurfaceGroup shadingSurfaceGroup in shadingSurfaceGroupVector)
            {
                if (shadingSurfaceGroup == null)
                {
                    continue;
                }

                string groupType = shadingSurfaceGroup.shadingSurfaceType();
                string groupName = shadingSurfaceGroup.nameString();

                global::OpenStudio.Transformation transformation = ShadingTransformation(shadingSurfaceGroup);

                if (shadingSurfaceGroup.shadedSurface() != null && !shadingSurfaceGroup.shadedSurface().isNull()
                    && openStudioImportContext.RegisterOnce("SAM-OSI-APX-001:ShadedSurface:" + groupName))
                {
                    openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format("Shading group '{0}' declares a shaded surface; SAM shade panels are free-standing, so the association is kept as metadata only and has no effect on SAM shading calculations", groupName), OpenStudioImportContext.OpenStudioObjectLabel(shadingSurfaceGroup));
                }

                global::OpenStudio.ShadingSurfaceVector shadingSurfaceVector = shadingSurfaceGroup.shadingSurfaces();
                if (shadingSurfaceVector == null)
                {
                    continue;
                }

                foreach (global::OpenStudio.ShadingSurface shadingSurface in shadingSurfaceVector)
                {
                    Panel panel = shadingSurface.ToSAM(groupType, groupName, transformation, openStudioImportContext);
                    if (panel != null)
                    {
                        result.Add(panel);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Composes the group → model transformation for a shading surface group. A Space group's
        /// vertices are relative to the group, which is itself relative to the space, so the
        /// space's building transformation must be applied on top; Site and Building groups are
        /// already relative to the building.
        /// </summary>
        private static global::OpenStudio.Transformation ShadingTransformation(global::OpenStudio.ShadingSurfaceGroup shadingSurfaceGroup)
        {
            global::OpenStudio.Transformation transformation = shadingSurfaceGroup.transformation();

            global::OpenStudio.OptionalSpace optionalSpace = shadingSurfaceGroup.space();
            if (optionalSpace != null && !optionalSpace.isNull())
            {
                global::OpenStudio.Transformation spaceTransformation = optionalSpace.get().buildingTransformation();
                if (spaceTransformation != null && transformation != null)
                {
                    return spaceTransformation.Multiply(transformation);
                }
            }

            return transformation;
        }

        /// <summary>
        /// Converts one OpenStudio ShadingSurface into a SAM shade panel.
        /// </summary>
        /// <param name="shadingSurface">OpenStudio shading surface; null returns null.</param>
        /// <param name="groupType">Shading group type ("Site", "Building" or "Space").</param>
        /// <param name="groupName">Shading group name.</param>
        /// <param name="transformation">Composed group → model transformation.</param>
        /// <param name="openStudioImportContext">Import context.</param>
        /// <returns>The created shade panel, or null.</returns>
        public static Panel ToSAM(this global::OpenStudio.ShadingSurface shadingSurface, string groupType, string groupName, global::OpenStudio.Transformation transformation, OpenStudioImportContext openStudioImportContext)
        {
            if (shadingSurface == null || openStudioImportContext == null)
            {
                return null;
            }

            string label = OpenStudioImportContext.OpenStudioObjectLabel(shadingSurface);

            List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics;
            Geometry.Spatial.Face3D face3D = Geometry.OpenStudio.Convert.ToSAM(shadingSurface, openStudioImportContext.Options.DistanceTolerance, openStudioImportContext.Options.AngleTolerance, openStudioImportContext.Options.MinimumArea, out diagnostics, transformation);

            foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in diagnostics)
            {
                openStudioImportContext.AddDiagnostic(diagnostic.Code, diagnostic.Severity, diagnostic.Message, label);
            }

            if (face3D == null)
            {
                openStudioImportContext.RegisterSkip();
                return null;
            }

            string constructionName = null;
            global::OpenStudio.OptionalConstructionBase optionalConstructionBase = shadingSurface.construction();
            if (optionalConstructionBase != null && !optionalConstructionBase.isNull())
            {
                constructionName = optionalConstructionBase.get().nameString();
            }

            if (string.IsNullOrWhiteSpace(constructionName))
            {
                constructionName = "Unassigned_Shade";
            }

            Construction construction;
            if (!openStudioImportContext.ConstructionMap.TryGetValue(constructionName, out construction))
            {
                construction = new Construction(constructionName);
                openStudioImportContext.ConstructionMap[constructionName] = construction;
            }

            Panel result;
            try
            {
                result = Analytical.Create.Panel(construction, PanelType.Shade, face3D);
            }
            catch (Exception exception)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.GeometryInvalid, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("The SAM shade panel could not be created ({0}: {1})", exception.GetType().Name, exception.Message), label);
                openStudioImportContext.RegisterSkip();
                return null;
            }

            if (result == null)
            {
                return null;
            }

            Guid guid = openStudioImportContext.ResolveGuid(shadingSurface, typeof(Panel).Name);
            result = Analytical.Create.Panel(guid, result);

            Modify.SetOpenStudioSource(result, shadingSurface);

            if (!string.IsNullOrWhiteSpace(groupType))
            {
                result.SetValue(OpenStudioSourceParameter.ShadingGroupType, groupType);
            }

            if (!string.IsNullOrWhiteSpace(groupName))
            {
                result.SetValue(OpenStudioSourceParameter.ShadingGroupName, groupName);
            }

            openStudioImportContext.RegisterCreated();
            return result;
        }
    }
}
