// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Core;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Converts a SAM Construction to an OpenStudio Construction in the requested direction.
        /// SAM stores layers inside → outside (verified, see docs/SAM_OPENSTUDIO_MATERIAL_MAPPING.md);
        /// forward = reversed SAM order (EnergyPlus "outside first"), used for all external
        /// surfaces and the primary side of internal pairs; reverse = SAM order as stored, used
        /// for the secondary side. Results are cached per (Guid, direction). Missing layers or
        /// unresolvable materials raise SAM-OS-CON-001 errors and return null.
        /// </summary>
        /// <param name="construction">SAM construction.</param>
        /// <param name="forward">True for the outside-first (EnergyPlus order) variant.</param>
        /// <param name="openStudioConversionContext">Conversion context.</param>
        /// <returns>OpenStudio construction, or null (diagnostic raised).</returns>
        public static global::OpenStudio.Construction ToOpenStudio(this Construction construction, bool forward, OpenStudioConversionContext openStudioConversionContext)
        {
            if (construction == null || openStudioConversionContext == null)
            {
                return null;
            }

            return ToOpenStudio_Construction(construction, construction.ConstructionLayers, forward, null, OpenStudioMaterialUsage.OpaqueConstruction, openStudioConversionContext);
        }

        /// <summary>
        /// Converts a SAM ApertureConstruction (pane layers only — frame layers are a documented
        /// MVP gap) to an OpenStudio Construction in the requested direction. Same ordering and
        /// caching rules as opaque constructions, cache keys suffixed ":Pane".
        /// </summary>
        /// <param name="apertureConstruction">SAM aperture construction.</param>
        /// <param name="forward">True for the outside-first (EnergyPlus order) variant.</param>
        /// <param name="openStudioConversionContext">Conversion context.</param>
        /// <returns>OpenStudio construction, or null (diagnostic raised).</returns>
        public static global::OpenStudio.Construction ToOpenStudio(this ApertureConstruction apertureConstruction, bool forward, OpenStudioConversionContext openStudioConversionContext)
        {
            if (apertureConstruction == null || openStudioConversionContext == null)
            {
                return null;
            }

            return ToOpenStudio_Construction(apertureConstruction, apertureConstruction.PaneConstructionLayers, forward, "Pane", OpenStudioMaterialUsage.FenestrationConstruction, openStudioConversionContext);
        }

        private static global::OpenStudio.Construction ToOpenStudio_Construction(SAMObject sAMObject, List<ConstructionLayer> constructionLayers, bool forward, string variant, OpenStudioMaterialUsage openStudioMaterialUsage, OpenStudioConversionContext openStudioConversionContext)
        {
            string direction = forward ? "Forward" : "Reverse";
            string cacheKey = string.Format("{0:N}:{1}{2}", sAMObject.Guid, variant == null ? string.Empty : variant + ":", direction);

            global::OpenStudio.Construction cached;
            if (openStudioConversionContext.ConstructionMap.TryGetValue(cacheKey, out cached))
            {
                return cached;
            }

            string name = Core.OpenStudio.Query.OpenStudioName("Construction", string.Format("{0}{1}_{2}", sAMObject.Name, variant == null ? string.Empty : "_" + variant, direction), sAMObject.Guid);

            if (constructionLayers == null || constructionLayers.Count == 0)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionMissingLayer, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "Construction has no layers", sAMObject, name);
                return null;
            }

            MaterialLibrary materialLibrary = openStudioConversionContext.Source?.MaterialLibrary;
            if (materialLibrary == null)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionMissingLayer, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "AnalyticalModel has no MaterialLibrary; construction layers cannot be resolved", sAMObject, name);
                return null;
            }

            List<ConstructionLayer> orderedLayers = new List<ConstructionLayer>(constructionLayers);
            if (forward)
            {
                orderedLayers.Reverse();
            }

            global::OpenStudio.MaterialVector materialVector = new global::OpenStudio.MaterialVector();
            foreach (ConstructionLayer constructionLayer in orderedLayers)
            {
                if (constructionLayer == null)
                {
                    continue;
                }

                IMaterial material = materialLibrary.GetMaterial(constructionLayer.Name);
                if (material == null)
                {
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionMissingLayer, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Layer material '{0}' was not found in the MaterialLibrary", constructionLayer.Name), sAMObject, name);
                    return null;
                }

                double thickness = constructionLayer.Thickness;
                if (double.IsNaN(thickness) || thickness <= 0)
                {
                    SAMObject materialSAMObject = material as SAMObject;
                    double defaultThickness = double.NaN;
                    if (materialSAMObject != null && materialSAMObject.TryGetValue(Core.MaterialParameter.DefaultThickness, out defaultThickness) && !double.IsNaN(defaultThickness) && defaultThickness > 0)
                    {
                        openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionMissingLayer, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Layer '{0}' has no thickness; material DefaultThickness {1} m used", constructionLayer.Name, defaultThickness), sAMObject, name);
                        thickness = defaultThickness;
                    }
                    else
                    {
                        openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionMissingLayer, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Layer '{0}' has no usable thickness", constructionLayer.Name), sAMObject, name);
                        return null;
                    }
                }

                global::OpenStudio.Material openStudioMaterial = material.ToOpenStudio(thickness, openStudioConversionContext, openStudioMaterialUsage);
                if (openStudioMaterial == null)
                {
                    return null;
                }

                if (!IsValidMaterialFamily(openStudioMaterial, openStudioMaterialUsage))
                {
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionMissingLayer, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Material '{0}' ({1}) is not valid in a {2} layer set — mixed opaque/fenestration material families are rejected", constructionLayer.Name, openStudioMaterial.iddObjectType().valueName(), openStudioMaterialUsage), sAMObject, name);
                    return null;
                }

                materialVector.Add(openStudioMaterial);
            }

            global::OpenStudio.Construction result = new global::OpenStudio.Construction(openStudioConversionContext.Target);
            result.setName(name);
            if (!result.setLayers(materialVector))
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionMissingLayer, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "OpenStudio rejected the layer set (incompatible material kinds in one construction)", sAMObject, name);
                result.remove();
                return null;
            }

            openStudioConversionContext.ConstructionMap[cacheKey] = result;
            if (forward && !openStudioConversionContext.References.Contains(sAMObject.Guid))
            {
                openStudioConversionContext.RegisterModelObject(sAMObject, result);
            }

            return result;
        }

        /// <summary>
        /// Material-family guard (review P1-06): opaque constructions accept only mass-full or
        /// massless opaque materials and air cavities (AirGap); fenestration pane constructions
        /// accept glazing, window gas and opaque (door) materials. An OpenStudio Gas is never
        /// valid in an opaque layer set, and an OpenStudio AirGap is never valid in a pane set.
        /// </summary>
        private static bool IsValidMaterialFamily(global::OpenStudio.Material material, OpenStudioMaterialUsage openStudioMaterialUsage)
        {
            if (material == null)
            {
                return false;
            }

            if (openStudioMaterialUsage == OpenStudioMaterialUsage.FenestrationConstruction)
            {
                return !material.to_StandardGlazing().isNull()
                    || !material.to_Gas().isNull()
                    || !material.to_StandardOpaqueMaterial().isNull();
            }

            return !material.to_StandardOpaqueMaterial().isNull()
                || !material.to_AirGap().isNull()
                || !material.to_MasslessOpaqueMaterial().isNull();
        }
    }
}
