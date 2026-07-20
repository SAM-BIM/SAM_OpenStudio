// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using SAM.Core;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Converts a SAM material to an OpenStudio material in the given construction-usage
        /// context, following docs/SAM_OPENSTUDIO_MATERIAL_MAPPING.md. Opaque and transparent
        /// materials produce the same object in both contexts; a SAM GasMaterial becomes an
        /// OpenStudio AirGap (OS:Material:AirGap, R = 1/h from its Heat Transfer Coefficient
        /// [W/m²K]) in an opaque construction and an OpenStudio Gas (OS:WindowMaterial:Gas) in a
        /// fenestration pane construction — the cache key includes the usage, so the two objects
        /// are never shared. Invalid physical values raise SAM-OS-MAT-001 errors (no hidden
        /// defaults), missing optical values raise warnings and keep the documented OpenStudio
        /// defaults.
        /// </summary>
        /// <param name="material">SAM material (OpaqueMaterial, TransparentMaterial or GasMaterial).</param>
        /// <param name="thickness">Layer thickness [m] from the construction layer.</param>
        /// <param name="openStudioConversionContext">Conversion context.</param>
        /// <param name="openStudioMaterialUsage">Construction-usage context (opaque or fenestration).</param>
        /// <returns>OpenStudio material, or null when unsupported/invalid (diagnostic raised).</returns>
        public static global::OpenStudio.Material ToOpenStudio(this IMaterial material, double thickness, OpenStudioConversionContext openStudioConversionContext, OpenStudioMaterialUsage openStudioMaterialUsage = OpenStudioMaterialUsage.OpaqueConstruction)
        {
            if (material == null || openStudioConversionContext == null)
            {
                return null;
            }

            SAMObject sAMObject = material as SAMObject;
            if (sAMObject == null)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.MaterialUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Unsupported material kind {0}", material.GetType().Name));
                return null;
            }

            // Vapour diffusion factor (coverage manifest AnalyticalMaterialParameter
            // .VapourDiffusionFactor, Unsupported SAM-OS-MAT-002 info; review P1-01): the
            // EnergyPlus CTF heat balance carries no moisture transport, and HAMT/EMPD need
            // data SAM does not hold. Reported once per material, any kind, thickness or usage.
            if (sAMObject.TryGetValue(MaterialParameter.VapourDiffusionFactor, out double vapourDiffusionFactor) && !double.IsNaN(vapourDiffusionFactor) && vapourDiffusionFactor > 0
                && openStudioConversionContext.RegisterOnce("SAM-OS-MAT-002:VapourDiffusionFactor:" + sAMObject.Guid.ToString("N")))
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.MaterialUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format(CultureInfo.InvariantCulture, "Vapour diffusion factor ({0:G4}) is not converted — EnergyPlus moisture modelling (HAMT/EMPD) needs data SAM does not carry", vapourDiffusionFactor), sAMObject);
            }

            if (material is GasMaterial gasMaterial)
            {
                return openStudioMaterialUsage == OpenStudioMaterialUsage.FenestrationConstruction
                    ? ToOpenStudio_WindowGas(gasMaterial, sAMObject, thickness, openStudioConversionContext)
                    : ToOpenStudio_AirGap(gasMaterial, sAMObject, openStudioConversionContext);
            }

            string cacheKey = string.Format(CultureInfo.InvariantCulture, "{0:N}:{1:R}", sAMObject.Guid, thickness);
            global::OpenStudio.Material cached;
            if (openStudioConversionContext.MaterialMap.TryGetValue(cacheKey, out cached))
            {
                return cached;
            }

            if (double.IsNaN(thickness) || thickness <= 0)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.MaterialUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Invalid layer thickness {0} m", thickness), sAMObject);
                return null;
            }

            string name = Core.OpenStudio.Query.OpenStudioName(sAMObject.GetType().Name, string.Format(CultureInfo.InvariantCulture, "{0}_{1:0.###}mm", sAMObject.Name, thickness * 1000), sAMObject.Guid);

            global::OpenStudio.Material result = null;

            if (material is OpaqueMaterial opaqueMaterial)
            {
                if (!IsValidPositive(opaqueMaterial.ThermalConductivity) || !IsValidPositive(opaqueMaterial.Density) || !IsValidPositive(opaqueMaterial.SpecificHeatCapacity))
                {
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.MaterialUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Invalid physical properties (conductivity {0}, density {1}, specific heat {2})", opaqueMaterial.ThermalConductivity, opaqueMaterial.Density, opaqueMaterial.SpecificHeatCapacity), sAMObject, name);
                    return null;
                }

                global::OpenStudio.StandardOpaqueMaterial standardOpaqueMaterial = new global::OpenStudio.StandardOpaqueMaterial(openStudioConversionContext.Target);
                standardOpaqueMaterial.setRoughness("MediumSmooth");
                standardOpaqueMaterial.setThickness(thickness);
                standardOpaqueMaterial.setThermalConductivity(opaqueMaterial.ThermalConductivity);
                standardOpaqueMaterial.setDensity(opaqueMaterial.Density);
                standardOpaqueMaterial.setSpecificHeat(opaqueMaterial.SpecificHeatCapacity);

                if (TryGetFraction(openStudioConversionContext, sAMObject, OpaqueMaterialParameter.ExternalEmissivity, name, out double externalEmissivity))
                {
                    standardOpaqueMaterial.setThermalAbsorptance(externalEmissivity);
                }

                if (TryGetFraction(openStudioConversionContext, sAMObject, OpaqueMaterialParameter.ExternalSolarReflectance, name, out double externalSolarReflectance))
                {
                    standardOpaqueMaterial.setSolarAbsorptance(1 - externalSolarReflectance);
                }

                if (TryGetFraction(openStudioConversionContext, sAMObject, OpaqueMaterialParameter.ExternalLightReflectance, name, out double externalLightReflectance))
                {
                    standardOpaqueMaterial.setVisibleAbsorptance(1 - externalLightReflectance);
                }

                // Single-sided approximation (coverage manifest OpaqueMaterialParameter
                // .Internal*, Approximated SAM-OS-MAT-002 info when Internal differs from
                // External; review P1-01): EnergyPlus opaque materials carry one absorptance
                // set — the external-side values govern both sides. Reported once per material.
                List<string> divergentInternalOptics = DivergentInternalOptics(sAMObject);
                if (divergentInternalOptics.Count > 0
                    && openStudioConversionContext.RegisterOnce("SAM-OS-MAT-002:InternalOptics:" + sAMObject.Guid.ToString("N")))
                {
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.MaterialUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format("Internal-side optical properties ({0}) differ from the external-side values — EnergyPlus opaque materials are single-sided; the external values govern both sides", string.Join(", ", divergentInternalOptics)), sAMObject, name);
                }

                result = standardOpaqueMaterial;
            }
            else if (material is TransparentMaterial transparentMaterial)
            {
                if (openStudioMaterialUsage != OpenStudioMaterialUsage.FenestrationConstruction)
                {
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.MaterialUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "TransparentMaterial (glazing) cannot be used as a layer of an opaque construction — mixed opaque/fenestration material families are rejected", sAMObject, name);
                    return null;
                }

                if (!IsValidPositive(transparentMaterial.ThermalConductivity))
                {
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.MaterialUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Invalid thermal conductivity {0}", transparentMaterial.ThermalConductivity), sAMObject, name);
                    return null;
                }

                if (sAMObject.TryGetValue(TransparentMaterialParameter.IsBlind, out bool isBlind) && isBlind)
                {
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.MaterialUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "Material is flagged as a blind but SAM carries no slat geometry — converted as plain glazing (no WindowMaterial:Blind fabricated)", sAMObject, name);
                }

                global::OpenStudio.StandardGlazing standardGlazing = new global::OpenStudio.StandardGlazing(openStudioConversionContext.Target);
                standardGlazing.setThickness(thickness);
                standardGlazing.setThermalConductivity(transparentMaterial.ThermalConductivity);
                standardGlazing.setInfraredTransmittance(0);
                standardGlazing.setDirtCorrectionFactorforSolarandVisibleTransmittance(1);
                standardGlazing.setSolarDiffusing(false);

                // EnergyPlus defines the glazing Front side as "the side of the layer opposite
                // the zone" (exterior-facing for exterior windows) and Back as the side closest
                // to the zone (I/O Reference, Materials for Glass Windows and Doors). SAM
                // External* is the building-exterior-facing side and Internal* the room-facing
                // side, therefore External → Front and Internal → Back. (SAM_LadybugTools
                // EnergyWindowMaterialGlazing.cs maps these the other way round — a deliberate
                // deviation recorded in docs/SAM_OPENSTUDIO_MATERIAL_MAPPING.md, review P1-04.)
                if (TryGetFraction(openStudioConversionContext, sAMObject, TransparentMaterialParameter.SolarTransmittance, name, out double solarTransmittance))
                {
                    standardGlazing.setSolarTransmittanceatNormalIncidence(solarTransmittance);
                }

                if (TryGetFraction(openStudioConversionContext, sAMObject, TransparentMaterialParameter.ExternalSolarReflectance, name, out double externalSolarReflectance))
                {
                    standardGlazing.setFrontSideSolarReflectanceatNormalIncidence(externalSolarReflectance);
                }

                if (TryGetFraction(openStudioConversionContext, sAMObject, TransparentMaterialParameter.InternalSolarReflectance, name, out double internalSolarReflectance))
                {
                    standardGlazing.setBackSideSolarReflectanceatNormalIncidence(internalSolarReflectance);
                }

                if (TryGetFraction(openStudioConversionContext, sAMObject, TransparentMaterialParameter.LightTransmittance, name, out double lightTransmittance))
                {
                    standardGlazing.setVisibleTransmittanceatNormalIncidence(lightTransmittance);
                }

                if (TryGetFraction(openStudioConversionContext, sAMObject, TransparentMaterialParameter.ExternalLightReflectance, name, out double externalLightReflectance))
                {
                    standardGlazing.setFrontSideVisibleReflectanceatNormalIncidence(externalLightReflectance);
                }

                if (TryGetFraction(openStudioConversionContext, sAMObject, TransparentMaterialParameter.InternalLightReflectance, name, out double internalLightReflectance))
                {
                    standardGlazing.setBackSideVisibleReflectanceatNormalIncidence(internalLightReflectance);
                }

                if (TryGetFraction(openStudioConversionContext, sAMObject, TransparentMaterialParameter.ExternalEmissivity, name, out double externalEmissivity))
                {
                    standardGlazing.setFrontSideInfraredHemisphericalEmissivity(externalEmissivity);
                }

                if (TryGetFraction(openStudioConversionContext, sAMObject, TransparentMaterialParameter.InternalEmissivity, name, out double internalEmissivity))
                {
                    standardGlazing.setBackSideInfraredHemisphericalEmissivity(internalEmissivity);
                }

                result = standardGlazing;
            }
            else
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.MaterialUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Unsupported material kind {0}", material.GetType().Name), sAMObject, name);
                return null;
            }

            result.setName(name);

            openStudioConversionContext.MaterialMap[cacheKey] = result;
            if (!openStudioConversionContext.References.Contains(sAMObject.Guid))
            {
                openStudioConversionContext.RegisterModelObject(sAMObject, result);
            }

            return result;
        }

        /// <summary>
        /// Opaque-context conversion of a SAM GasMaterial: an air/gas cavity layer of an opaque
        /// construction becomes an OpenStudio AirGap (OS:Material:AirGap). The thermal resistance
        /// is derived from SAM's authoritative cavity conductance — GasMaterialParameter
        /// .HeatTransferCoefficient h [W/m²K] (written by SAM's UpdateHeatTransferCoefficients
        /// from gas type, thickness and tilt) — as R = 1/h [m²K/W]. Missing, non-finite,
        /// non-positive, or below-EnergyPlus-minimum (0.001 m²K/W) values raise SAM-OS-MAT-001
        /// errors before any CLI execution — never a silent zero-resistance layer.
        /// </summary>
        private static global::OpenStudio.Material ToOpenStudio_AirGap(GasMaterial gasMaterial, SAMObject sAMObject, OpenStudioConversionContext openStudioConversionContext)
        {
            const double energyPlusMinimumResistance = 0.001; // InitConductionTransferFunctions per-layer minimum [m²K/W]

            string name = Core.OpenStudio.Query.OpenStudioName(sAMObject.GetType().Name, sAMObject.Name, sAMObject.Guid);

            double heatTransferCoefficient = double.NaN;
            if (!sAMObject.TryGetValue(GasMaterialParameter.HeatTransferCoefficient, out heatTransferCoefficient) || double.IsNaN(heatTransferCoefficient) || double.IsInfinity(heatTransferCoefficient) || heatTransferCoefficient <= 0)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.MaterialUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Opaque gas cavity has no usable Heat Transfer Coefficient [W/m²K] (value {0}); the thermal resistance cannot be derived — no layer was substituted", double.IsNaN(heatTransferCoefficient) ? "missing" : heatTransferCoefficient.ToString(CultureInfo.InvariantCulture)), sAMObject, name);
                return null;
            }

            double thermalResistance = 1.0 / heatTransferCoefficient;
            if (double.IsNaN(thermalResistance) || double.IsInfinity(thermalResistance) || thermalResistance < energyPlusMinimumResistance)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.MaterialUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format(CultureInfo.InvariantCulture, "Opaque gas cavity resistance {0:G4} m²K/W (from conductance {1} W/m²K) is below the EnergyPlus per-layer minimum {2:G4} m²K/W", thermalResistance, heatTransferCoefficient, energyPlusMinimumResistance), sAMObject, name);
                return null;
            }

            string cacheKey = string.Format(CultureInfo.InvariantCulture, "{0:N}:OpaqueAirGap:{1:R}", sAMObject.Guid, thermalResistance);
            global::OpenStudio.Material cached;
            if (openStudioConversionContext.MaterialMap.TryGetValue(cacheKey, out cached))
            {
                return cached;
            }

            global::OpenStudio.AirGap result = new global::OpenStudio.AirGap(openStudioConversionContext.Target);
            result.setName(name);
            result.setThermalResistance(thermalResistance);

            openStudioConversionContext.MaterialMap[cacheKey] = result;
            if (!openStudioConversionContext.References.Contains(sAMObject.Guid))
            {
                openStudioConversionContext.RegisterModelObject(sAMObject, result);
            }

            return result;
        }

        /// <summary>
        /// Fenestration-context conversion of a SAM GasMaterial: a gas layer between glazing
        /// panes becomes an OpenStudio Gas (OS:WindowMaterial:Gas), preserving gas type and
        /// physical thickness with the existing supported-gas validation (Air/Argon/Krypton/Xenon).
        /// </summary>
        private static global::OpenStudio.Material ToOpenStudio_WindowGas(GasMaterial gasMaterial, SAMObject sAMObject, double thickness, OpenStudioConversionContext openStudioConversionContext)
        {
            string name = Core.OpenStudio.Query.OpenStudioName(sAMObject.GetType().Name, string.Format(CultureInfo.InvariantCulture, "{0}_{1:0.###}mm", sAMObject.Name, thickness * 1000), sAMObject.Guid);

            if (double.IsNaN(thickness) || thickness <= 0)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.MaterialUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Invalid layer thickness {0} m", thickness), sAMObject, name);
                return null;
            }

            DefaultGasType defaultGasType = gasMaterial.DefaultGasType();
            if (defaultGasType != DefaultGasType.Air && defaultGasType != DefaultGasType.Argon && defaultGasType != DefaultGasType.Krypton && defaultGasType != DefaultGasType.Xenon)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.MaterialUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Gas type {0} is not supported by OpenStudio (Air/Argon/Krypton/Xenon)", defaultGasType), sAMObject, name);
                return null;
            }

            string cacheKey = string.Format(CultureInfo.InvariantCulture, "{0:N}:WindowGas:{1:R}", sAMObject.Guid, thickness);
            global::OpenStudio.Material cached;
            if (openStudioConversionContext.MaterialMap.TryGetValue(cacheKey, out cached))
            {
                return cached;
            }

            global::OpenStudio.Gas result = new global::OpenStudio.Gas(openStudioConversionContext.Target);
            result.setName(name);
            result.setGasType(defaultGasType.ToString());
            result.setThickness(thickness);

            openStudioConversionContext.MaterialMap[cacheKey] = result;
            if (!openStudioConversionContext.References.Contains(sAMObject.Guid))
            {
                openStudioConversionContext.RegisterModelObject(sAMObject, result);
            }

            return result;
        }

        /// <summary>
        /// Names of the opaque Internal* optical parameters whose value is present and diverges
        /// from its External* counterpart (or has no external counterpart to govern it) — the
        /// cases where the single-sided EnergyPlus approximation loses information.
        /// </summary>
        private static List<string> DivergentInternalOptics(SAMObject sAMObject)
        {
            List<string> result = new List<string>();
            AddDivergentInternalOptic(sAMObject, OpaqueMaterialParameter.InternalEmissivity, OpaqueMaterialParameter.ExternalEmissivity, result);
            AddDivergentInternalOptic(sAMObject, OpaqueMaterialParameter.InternalSolarReflectance, OpaqueMaterialParameter.ExternalSolarReflectance, result);
            AddDivergentInternalOptic(sAMObject, OpaqueMaterialParameter.InternalLightReflectance, OpaqueMaterialParameter.ExternalLightReflectance, result);
            return result;
        }

        private static void AddDivergentInternalOptic(SAMObject sAMObject, OpaqueMaterialParameter internalParameter, OpaqueMaterialParameter externalParameter, List<string> divergent)
        {
            if (!sAMObject.TryGetValue(internalParameter, out double internalValue) || double.IsNaN(internalValue))
            {
                return;
            }

            if (!sAMObject.TryGetValue(externalParameter, out double externalValue) || double.IsNaN(externalValue) || Math.Abs(internalValue - externalValue) > 1e-9)
            {
                divergent.Add(internalParameter.ToString());
            }
        }

        private static bool IsValidPositive(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
        }

        private static bool TryGetFraction(OpenStudioConversionContext openStudioConversionContext, SAMObject sAMObject, Enum parameter, string openStudioObjectName, out double value)
        {
            if (!sAMObject.TryGetValue(parameter, out value) || double.IsNaN(value))
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.MaterialUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Parameter {0} missing; OpenStudio default retained", parameter), sAMObject, openStudioObjectName);
                return false;
            }

            if (value < 0 || value > 1)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.MaterialUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Parameter {0} value {1} outside [0,1]", parameter, value), sAMObject, openStudioObjectName);
                return false;
            }

            return true;
        }
    }
}
