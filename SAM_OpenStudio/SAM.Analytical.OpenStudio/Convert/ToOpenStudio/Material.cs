// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Globalization;
using SAM.Core;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Converts a SAM material to an OpenStudio material at the given layer thickness,
        /// following docs/SAM_OPENSTUDIO_MATERIAL_MAPPING.md. One OpenStudio material is created
        /// per (SAM material Guid, thickness) pair and cached; invalid physical values raise
        /// SAM-OS-MAT-001 errors (no hidden defaults), missing optical values raise warnings and
        /// keep the documented OpenStudio defaults.
        /// </summary>
        /// <param name="material">SAM material (OpaqueMaterial, TransparentMaterial or GasMaterial).</param>
        /// <param name="thickness">Layer thickness [m] from the construction layer.</param>
        /// <param name="openStudioConversionContext">Conversion context.</param>
        /// <returns>OpenStudio material, or null when unsupported/invalid (diagnostic raised).</returns>
        public static global::OpenStudio.Material ToOpenStudio(this IMaterial material, double thickness, OpenStudioConversionContext openStudioConversionContext)
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

                result = standardOpaqueMaterial;
            }
            else if (material is TransparentMaterial transparentMaterial)
            {
                if (!IsValidPositive(transparentMaterial.ThermalConductivity))
                {
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.MaterialUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Invalid thermal conductivity {0}", transparentMaterial.ThermalConductivity), sAMObject, name);
                    return null;
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
            else if (material is GasMaterial gasMaterial)
            {
                DefaultGasType defaultGasType = gasMaterial.DefaultGasType();
                if (defaultGasType != DefaultGasType.Air && defaultGasType != DefaultGasType.Argon && defaultGasType != DefaultGasType.Krypton && defaultGasType != DefaultGasType.Xenon)
                {
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.MaterialUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Gas type {0} is not supported by OpenStudio (Air/Argon/Krypton/Xenon)", defaultGasType), sAMObject, name);
                    return null;
                }

                global::OpenStudio.Gas gas = new global::OpenStudio.Gas(openStudioConversionContext.Target);
                gas.setGasType(defaultGasType.ToString());
                gas.setThickness(thickness);

                result = gas;
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
