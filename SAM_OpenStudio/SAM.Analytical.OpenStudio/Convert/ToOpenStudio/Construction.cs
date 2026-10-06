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

            // TAS internal-shadow flag (coverage manifest ConstructionParameter.IsInternalShadow,
            // Unsupported SAM-OS-CON-002 info; review P1-01): no deterministic EnergyPlus
            // mapping. Reported once per construction, however many panels or directions use it.
            if (construction.TryGetValue(ConstructionParameter.IsInternalShadow, out bool isInternalShadow) && isInternalShadow
                && openStudioConversionContext.RegisterOnce("SAM-OS-CON-002:IsInternalShadow:" + construction.Guid.ToString("N")))
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "Internal-shadow flag (TAS) on the construction is not converted — no deterministic EnergyPlus mapping", construction);
                openStudioConversionContext.RegisterSkip();
            }

            return ToOpenStudio_Construction(construction, construction.ConstructionLayers, forward, null, OpenStudioMaterialUsage.OpaqueConstruction, openStudioConversionContext);
        }

        /// <summary>
        /// Converts a SAM ApertureConstruction to an OpenStudio Construction in the requested
        /// direction. Pane layers produce a layered construction (same ordering and caching
        /// rules as opaque constructions, cache keys suffixed ":Pane"). When the construction
        /// has NO pane layers but carries optical/thermal performance parameters
        /// (ThermalTransmittance / TotalSolarEnergyTransmittance / LightTransmittance —
        /// aperture-level values taking precedence), a documented SimpleGlazingSystem fallback
        /// is built instead (coverage manifest, Approximated); with neither layers nor complete
        /// performance parameters the construction fails with SAM-OS-CON-001.
        /// </summary>
        /// <param name="apertureConstruction">SAM aperture construction.</param>
        /// <param name="forward">True for the outside-first (EnergyPlus order) variant.</param>
        /// <param name="openStudioConversionContext">Conversion context.</param>
        /// <param name="aperture">The aperture being converted (aperture-level performance parameters take precedence); may be null.</param>
        /// <returns>OpenStudio construction, or null (diagnostic raised).</returns>
        public static global::OpenStudio.Construction ToOpenStudio(this ApertureConstruction apertureConstruction, bool forward, OpenStudioConversionContext openStudioConversionContext, Aperture aperture = null)
        {
            if (apertureConstruction == null || openStudioConversionContext == null)
            {
                return null;
            }

            List<ConstructionLayer> paneLayers = apertureConstruction.PaneConstructionLayers;
            if (paneLayers == null || paneLayers.Count == 0)
            {
                return ToOpenStudio_SimpleGlazingFallback(apertureConstruction, aperture, forward, openStudioConversionContext);
            }

            return ToOpenStudio_Construction(apertureConstruction, paneLayers, forward, "Pane", OpenStudioMaterialUsage.FenestrationConstruction, openStudioConversionContext);
        }

        /// <summary>
        /// Documented SimpleGlazingSystem fallback (coverage manifest, Approximated): an
        /// aperture construction without pane layers but with U / SHGC / visible transmittance
        /// becomes a WindowMaterial:SimpleGlazingSystem. Aperture-level parameters take
        /// precedence over construction-level ones, so the RESOLVED performance — not the
        /// construction Guid alone — is the identity of the glazing: two apertures sharing one
        /// construction but overriding it differently each get their own SimpleGlazing and their
        /// own deterministic name, while two carrying identical values still share one.
        /// Incomplete parameters are an explicit error — no hidden defaults.
        /// </summary>
        private static global::OpenStudio.Construction ToOpenStudio_SimpleGlazingFallback(ApertureConstruction apertureConstruction, Aperture aperture, bool forward, OpenStudioConversionContext openStudioConversionContext)
        {
            string direction = forward ? "Forward" : "Reverse";

            double uFactor = PerformanceParameter(aperture, apertureConstruction, ApertureParameter.ThermalTransmittance, ApertureConstructionParameter.ThermalTransmittance);
            double solarHeatGainCoefficient = PerformanceParameter(aperture, apertureConstruction, ApertureParameter.TotalSolarEnergyTransmittance, ApertureConstructionParameter.TotalSolarEnergyTransmittance);
            double visibleTransmittance = PerformanceParameter(aperture, apertureConstruction, ApertureParameter.LightTransmittance, ApertureConstructionParameter.LightTransmittance);

            string cacheKey = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:N}:SimpleGlazing:{1}:{2:R}:{3:R}:{4:R}", apertureConstruction.Guid, direction, uFactor, solarHeatGainCoefficient, visibleTransmittance);

            global::OpenStudio.Construction cached;
            if (openStudioConversionContext.ConstructionMap.TryGetValue(cacheKey, out cached))
            {
                return cached;
            }

            // Aperture-level overrides carry the performance into the name too, so the extra
            // constructions stay distinguishable in the OSM instead of being auto-renamed by
            // OpenStudio (which would break the trailing-Guid resolution convention).
            string variant = IsApertureSpecificPerformance(apertureConstruction, uFactor, solarHeatGainCoefficient, visibleTransmittance)
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}_SimpleGlazing_U{1:R}_G{2:R}_T{3:R}_{4}", apertureConstruction.Name, uFactor, solarHeatGainCoefficient, visibleTransmittance, direction)
                : string.Format("{0}_SimpleGlazing_{1}", apertureConstruction.Name, direction);

            string name = Core.OpenStudio.Query.OpenStudioName("Construction", variant, apertureConstruction.Guid);

            if (double.IsNaN(uFactor) || uFactor <= 0 || double.IsNaN(solarHeatGainCoefficient) || solarHeatGainCoefficient < 0 || solarHeatGainCoefficient > 1 || double.IsNaN(visibleTransmittance) || visibleTransmittance < 0 || visibleTransmittance > 1)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionMissingLayer, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "Aperture construction has no pane layers and no complete performance parameters (U, SHGC, visible transmittance) for the SimpleGlazingSystem fallback", apertureConstruction, name);
                return null;
            }

            global::OpenStudio.SimpleGlazing simpleGlazingSystem = new global::OpenStudio.SimpleGlazing(openStudioConversionContext.Target);
            simpleGlazingSystem.setName(name + "_SimpleGlazing");
            simpleGlazingSystem.setUFactor(uFactor);
            simpleGlazingSystem.setSolarHeatGainCoefficient(solarHeatGainCoefficient);
            simpleGlazingSystem.setVisibleTransmittance(visibleTransmittance);

            global::OpenStudio.MaterialVector materialVector = new global::OpenStudio.MaterialVector();
            materialVector.Add(simpleGlazingSystem);

            global::OpenStudio.Construction result = new global::OpenStudio.Construction(openStudioConversionContext.Target);
            result.setName(name);
            if (!result.setLayers(materialVector))
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionMissingLayer, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "OpenStudio rejected the SimpleGlazingSystem layer set", apertureConstruction, name);
                result.remove();
                return null;
            }

            openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionMissingLayer, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "Aperture construction has no pane layers; SimpleGlazingSystem fallback built from the performance parameters (approximation)", apertureConstruction, name);

            openStudioConversionContext.ConstructionMap[cacheKey] = result;
            if (forward && !openStudioConversionContext.References.Contains(apertureConstruction.Guid))
            {
                openStudioConversionContext.RegisterModelObject(apertureConstruction, result);
            }

            return result;
        }

        /// <summary>
        /// True when the resolved performance differs from what the shared ApertureConstruction
        /// alone would give — that is, when an aperture-level override actually changed a value.
        /// Compared with Equals so that NaN (an incomplete set, reported as an error further
        /// down) does not read as a difference.
        /// </summary>
        private static bool IsApertureSpecificPerformance(ApertureConstruction apertureConstruction, double uFactor, double solarHeatGainCoefficient, double visibleTransmittance)
        {
            return !uFactor.Equals(PerformanceParameter(null, apertureConstruction, ApertureParameter.ThermalTransmittance, ApertureConstructionParameter.ThermalTransmittance))
                || !solarHeatGainCoefficient.Equals(PerformanceParameter(null, apertureConstruction, ApertureParameter.TotalSolarEnergyTransmittance, ApertureConstructionParameter.TotalSolarEnergyTransmittance))
                || !visibleTransmittance.Equals(PerformanceParameter(null, apertureConstruction, ApertureParameter.LightTransmittance, ApertureConstructionParameter.LightTransmittance));
        }

        private static double PerformanceParameter(Aperture aperture, ApertureConstruction apertureConstruction, ApertureParameter apertureParameter, ApertureConstructionParameter apertureConstructionParameter)
        {
            double value;
            if (aperture != null && aperture.TryGetValue(apertureParameter, out value) && !double.IsNaN(value))
            {
                return value;
            }

            if (apertureConstruction.TryGetValue(apertureConstructionParameter, out value) && !double.IsNaN(value))
            {
                return value;
            }

            return double.NaN;
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
