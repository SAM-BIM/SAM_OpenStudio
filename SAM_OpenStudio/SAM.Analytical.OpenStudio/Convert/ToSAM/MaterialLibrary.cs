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
        /// Nominal thickness [m] assigned to a massless layer so it can become a SAM material,
        /// which is always thickness-and-conductivity based. 25 mm is a typical insulation-board
        /// thickness and keeps the derived conductivity in a physically plausible range; the
        /// *resistance* — the only quantity EnergyPlus actually used — is preserved exactly,
        /// because conductivity is derived as thickness / R.
        /// </summary>
        private const double MasslessNominalThickness = 0.025;

        /// <summary>
        /// Density [kg/m³] and specific heat [J/kgK] assigned to a massless layer. Their product
        /// gives the layer's thermal mass, which a massless EnergyPlus layer does not have; they
        /// are chosen as low as SAM tolerates so the imported layer stays as close to massless as
        /// the SAM material model allows. The substitution is always reported.
        /// </summary>
        private const double MasslessNominalDensity = 1.0;

        /// <summary>Specific heat [J/kgK] paired with <see cref="MasslessNominalDensity"/>.</summary>
        private const double MasslessNominalSpecificHeat = 1000.0;

        /// <summary>
        /// Converts every material referenced by the model's constructions into a deduplicated
        /// SAM <see cref="MaterialLibrary"/>.
        /// <para>
        /// Deduplication is by OpenStudio material name, which is unique per model — one SAM
        /// material per source material, however many constructions use it.
        /// </para>
        /// <para>
        /// Nothing unknown is replaced by something known: an unsupported material family
        /// (blinds, shades, screens, thermochromic or refraction/extinction glazing) produces a
        /// name-only placeholder and a SAM-OSI-MAT-001 diagnostic naming the source object, never
        /// a plausible-looking default that would silently change the model's physics.
        /// </para>
        /// </summary>
        /// <param name="openStudioImportContext">Import context.</param>
        /// <returns>The material library; never null.</returns>
        public static MaterialLibrary ToSAM_MaterialLibrary(this OpenStudioImportContext openStudioImportContext)
        {
            MaterialLibrary result = new MaterialLibrary("OpenStudio");

            if (openStudioImportContext == null || !openStudioImportContext.Options.IncludeConstructions)
            {
                return result;
            }

            global::OpenStudio.MaterialVector materialVector;
            try
            {
                materialVector = openStudioImportContext.Source.getMaterials();
            }
            catch (Exception)
            {
                return result;
            }

            if (materialVector == null)
            {
                return result;
            }

            openStudioImportContext.Statistics.SourceObjects += materialVector.Count;

            foreach (global::OpenStudio.Material material in materialVector)
            {
                IMaterial iMaterial = material.ToSAM(openStudioImportContext);
                if (iMaterial != null)
                {
                    result.Add(iMaterial);
                }
            }

            return result;
        }

        /// <summary>
        /// Converts one OpenStudio material into a SAM material, caching it by OpenStudio name.
        /// The inverse of Convert/ToOpenStudio/Material.cs; see
        /// docs/SAM_OPENSTUDIO_MATERIAL_MAPPING.md for the forward mapping this must undo.
        /// </summary>
        /// <param name="material">OpenStudio material; null returns null.</param>
        /// <param name="openStudioImportContext">Import context.</param>
        /// <returns>The SAM material, or null when it could not be represented at all.</returns>
        public static IMaterial ToSAM(this global::OpenStudio.Material material, OpenStudioImportContext openStudioImportContext)
        {
            if (material == null || openStudioImportContext == null)
            {
                return null;
            }

            string name = material.nameString();

            IMaterial cached;
            if (openStudioImportContext.MaterialMap.TryGetValue(name, out cached))
            {
                return cached;
            }

            string label = OpenStudioImportContext.OpenStudioObjectLabel(material);
            Guid guid = openStudioImportContext.ResolveGuid(material);

            IMaterial result = null;

            global::OpenStudio.OptionalStandardOpaqueMaterial optionalStandardOpaqueMaterial = global::OpenStudio.OpenStudioModelResources.toStandardOpaqueMaterial(material);
            if (optionalStandardOpaqueMaterial != null && !optionalStandardOpaqueMaterial.isNull())
            {
                result = ToSAM_OpaqueMaterial(optionalStandardOpaqueMaterial.get(), guid, name, openStudioImportContext, label);
            }

            if (result == null)
            {
                global::OpenStudio.OptionalMasslessOpaqueMaterial optionalMasslessOpaqueMaterial = global::OpenStudio.OpenStudioModelResources.toMasslessOpaqueMaterial(material);
                if (optionalMasslessOpaqueMaterial != null && !optionalMasslessOpaqueMaterial.isNull())
                {
                    result = ToSAM_MasslessOpaqueMaterial(optionalMasslessOpaqueMaterial.get(), guid, name, openStudioImportContext, label);
                }
            }

            if (result == null)
            {
                global::OpenStudio.OptionalAirGap optionalAirGap = global::OpenStudio.OpenStudioModelResources.toAirGap(material);
                if (optionalAirGap != null && !optionalAirGap.isNull())
                {
                    result = ToSAM_AirGap(optionalAirGap.get(), guid, name, openStudioImportContext, label);
                }
            }

            if (result == null)
            {
                global::OpenStudio.OptionalStandardGlazing optionalStandardGlazing = global::OpenStudio.OpenStudioModelResources.toStandardGlazing(material);
                if (optionalStandardGlazing != null && !optionalStandardGlazing.isNull())
                {
                    result = ToSAM_StandardGlazing(optionalStandardGlazing.get(), guid, name, openStudioImportContext, label);
                }
            }

            if (result == null)
            {
                global::OpenStudio.OptionalSimpleGlazing optionalSimpleGlazing = global::OpenStudio.OpenStudioModelResources.toSimpleGlazing(material);
                if (optionalSimpleGlazing != null && !optionalSimpleGlazing.isNull())
                {
                    result = ToSAM_SimpleGlazing(optionalSimpleGlazing.get(), guid, name, openStudioImportContext, label);
                }
            }

            if (result == null)
            {
                global::OpenStudio.OptionalGas optionalGas = global::OpenStudio.OpenStudioModelResources.toGas(material);
                if (optionalGas != null && !optionalGas.isNull())
                {
                    result = ToSAM_Gas(optionalGas.get(), guid, name, openStudioImportContext, label);
                }
            }

            if (result == null)
            {
                global::OpenStudio.OptionalGasMixture optionalGasMixture = global::OpenStudio.OpenStudioModelResources.toGasMixture(material);
                if (optionalGasMixture != null && !optionalGasMixture.isNull())
                {
                    result = ToSAM_GasMixture(optionalGasMixture.get(), guid, name, openStudioImportContext, label);
                }
            }

            if (result == null)
            {
                // Placeholder, never a substitute: an opaque material with no properties is
                // visibly incomplete, whereas a "reasonable default" would look like real data.
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.MaterialUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Material type '{0}' has no SAM equivalent; a name-only placeholder was created with no physical properties - nothing was substituted", IddTypeName(material)), label);
                openStudioImportContext.RegisterSkip();
                result = new OpaqueMaterial(guid, name);
            }

            openStudioImportContext.MaterialMap[name] = result;
            openStudioImportContext.RegisterCreated();
            return result;
        }

        /// <summary>
        /// StandardOpaqueMaterial → SAM OpaqueMaterial. Thickness is not a SAM material property
        /// — it belongs to the construction layer — so it is carried on
        /// <see cref="MaterialParameter.DefaultThickness"/> and read back when the layer is built.
        /// </summary>
        private static IMaterial ToSAM_OpaqueMaterial(global::OpenStudio.StandardOpaqueMaterial standardOpaqueMaterial, Guid guid, string name, OpenStudioImportContext openStudioImportContext, string label)
        {
            OpaqueMaterial result = new OpaqueMaterial(guid, name, name, string.Empty, standardOpaqueMaterial.thermalConductivity(), standardOpaqueMaterial.density(), standardOpaqueMaterial.specificHeat());

            result.SetValue(Core.MaterialParameter.DefaultThickness, standardOpaqueMaterial.thickness());

            // EnergyPlus opaque materials are single-sided: one absorptance set governs both
            // faces. Both SAM sides therefore receive the same value - recorded here so the
            // asymmetry the forward converter warns about is not silently invented on the way
            // back.
            double thermalAbsorptance = standardOpaqueMaterial.thermalAbsorptance();
            result.SetValue(OpaqueMaterialParameter.ExternalEmissivity, thermalAbsorptance);
            result.SetValue(OpaqueMaterialParameter.InternalEmissivity, thermalAbsorptance);

            double solarReflectance = 1.0 - standardOpaqueMaterial.solarAbsorptance();
            result.SetValue(OpaqueMaterialParameter.ExternalSolarReflectance, solarReflectance);
            result.SetValue(OpaqueMaterialParameter.InternalSolarReflectance, solarReflectance);

            double visibleReflectance = 1.0 - standardOpaqueMaterial.visibleAbsorptance();
            result.SetValue(OpaqueMaterialParameter.ExternalLightReflectance, visibleReflectance);
            result.SetValue(OpaqueMaterialParameter.InternalLightReflectance, visibleReflectance);

            return result;
        }

        /// <summary>
        /// MasslessOpaqueMaterial → SAM OpaqueMaterial. SAM has no massless material, so the
        /// layer is reconstructed at a nominal thickness with conductivity = thickness / R: the
        /// thermal resistance EnergyPlus used is preserved exactly, and only the (absent) thermal
        /// mass is invented — as near zero as SAM allows. Always reported as an approximation.
        /// </summary>
        private static IMaterial ToSAM_MasslessOpaqueMaterial(global::OpenStudio.MasslessOpaqueMaterial masslessOpaqueMaterial, Guid guid, string name, OpenStudioImportContext openStudioImportContext, string label)
        {
            double thermalResistance = masslessOpaqueMaterial.thermalResistance();
            if (double.IsNaN(thermalResistance) || thermalResistance <= 0)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.MaterialUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format(CultureInfo.InvariantCulture, "Massless material has a non-usable thermal resistance ({0}); a name-only placeholder was created", thermalResistance), label);
                openStudioImportContext.RegisterSkip();
                return new OpaqueMaterial(guid, name);
            }

            double conductivity = MasslessNominalThickness / thermalResistance;

            OpaqueMaterial result = new OpaqueMaterial(guid, name, name, string.Empty, conductivity, MasslessNominalDensity, MasslessNominalSpecificHeat);
            result.SetValue(Core.MaterialParameter.DefaultThickness, MasslessNominalThickness);

            ApplyOptionalFraction(result, OpaqueMaterialParameter.ExternalEmissivity, masslessOpaqueMaterial.thermalAbsorptance());
            ApplyOptionalFraction(result, OpaqueMaterialParameter.InternalEmissivity, masslessOpaqueMaterial.thermalAbsorptance());
            ApplyOptionalComplement(result, OpaqueMaterialParameter.ExternalSolarReflectance, masslessOpaqueMaterial.solarAbsorptance());
            ApplyOptionalComplement(result, OpaqueMaterialParameter.InternalSolarReflectance, masslessOpaqueMaterial.solarAbsorptance());
            ApplyOptionalComplement(result, OpaqueMaterialParameter.ExternalLightReflectance, masslessOpaqueMaterial.visibleAbsorptance());
            ApplyOptionalComplement(result, OpaqueMaterialParameter.InternalLightReflectance, masslessOpaqueMaterial.visibleAbsorptance());

            openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format(CultureInfo.InvariantCulture, "Massless material (R = {0:G4} m²K/W) imported as a SAM material {1:G4} m thick with conductivity {2:G4} W/mK: the resistance is preserved exactly, but SAM has no massless material so a near-zero thermal mass ({3:G4} kg/m³ x {4:G4} J/kgK) was assigned", thermalResistance, MasslessNominalThickness, conductivity, MasslessNominalDensity, MasslessNominalSpecificHeat), label);

            return result;
        }

        /// <summary>
        /// AirGap → SAM GasMaterial. The forward direction writes R = 1/h from
        /// <see cref="GasMaterialParameter.HeatTransferCoefficient"/>; this inverts it exactly
        /// (h = 1/R), so an air cavity survives a round trip unchanged.
        /// </summary>
        private static IMaterial ToSAM_AirGap(global::OpenStudio.AirGap airGap, Guid guid, string name, OpenStudioImportContext openStudioImportContext, string label)
        {
            double thermalResistance = airGap.thermalResistance();
            if (double.IsNaN(thermalResistance) || thermalResistance <= 0)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.MaterialUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format(CultureInfo.InvariantCulture, "Air gap has a non-usable thermal resistance ({0}); a name-only placeholder was created", thermalResistance), label);
                openStudioImportContext.RegisterSkip();
                return new GasMaterial(guid, name);
            }

            GasMaterial result = new GasMaterial(guid, name);
            result.SetValue(GasMaterialParameter.HeatTransferCoefficient, 1.0 / thermalResistance);
            return result;
        }

        /// <summary>
        /// StandardGlazing → SAM TransparentMaterial. Front/Back map back to External/Internal
        /// exactly as Convert/ToOpenStudio/Material.cs writes them (EnergyPlus Front = the side
        /// away from the zone = SAM External), so the round trip does not mirror the glazing.
        /// </summary>
        private static IMaterial ToSAM_StandardGlazing(global::OpenStudio.StandardGlazing standardGlazing, Guid guid, string name, OpenStudioImportContext openStudioImportContext, string label)
        {
            double conductivity = standardGlazing.thermalConductivity();

            TransparentMaterial result = new TransparentMaterial(guid, name, name, string.Empty, conductivity, double.NaN, double.NaN);
            result.SetValue(Core.MaterialParameter.DefaultThickness, standardGlazing.thickness());

            ApplyOptionalFraction(result, TransparentMaterialParameter.SolarTransmittance, standardGlazing.solarTransmittanceatNormalIncidence());
            ApplyOptionalFraction(result, TransparentMaterialParameter.ExternalSolarReflectance, standardGlazing.frontSideSolarReflectanceatNormalIncidence());
            ApplyOptionalFraction(result, TransparentMaterialParameter.InternalSolarReflectance, standardGlazing.backSideSolarReflectanceatNormalIncidence());
            ApplyOptionalFraction(result, TransparentMaterialParameter.LightTransmittance, standardGlazing.visibleTransmittanceatNormalIncidence());
            ApplyOptionalFraction(result, TransparentMaterialParameter.ExternalLightReflectance, standardGlazing.frontSideVisibleReflectanceatNormalIncidence());
            ApplyOptionalFraction(result, TransparentMaterialParameter.InternalLightReflectance, standardGlazing.backSideVisibleReflectanceatNormalIncidence());

            result.SetValue(TransparentMaterialParameter.ExternalEmissivity, standardGlazing.frontSideInfraredHemisphericalEmissivity());
            result.SetValue(TransparentMaterialParameter.InternalEmissivity, standardGlazing.backSideInfraredHemisphericalEmissivity());

            return result;
        }

        /// <summary>
        /// SimpleGlazing → SAM TransparentMaterial.
        /// <para>
        /// SimpleGlazing is a *performance* specification (U, SHGC, VT) with no thickness,
        /// conductivity or spectral data, while SAM glazing is a physical layer. The reconstruction
        /// keeps the assembly U-value by deriving a conductivity from a nominal thickness, and
        /// takes solar transmittance from the SHGC — which overstates transmittance slightly,
        /// because SHGC includes the inward-flowing fraction of absorbed radiation. Both are
        /// reported: this is the least invented mapping available, not an exact one.
        /// </para>
        /// </summary>
        private static IMaterial ToSAM_SimpleGlazing(global::OpenStudio.SimpleGlazing simpleGlazing, Guid guid, string name, OpenStudioImportContext openStudioImportContext, string label)
        {
            double uFactor = simpleGlazing.uFactor();
            double solarHeatGainCoefficient = simpleGlazing.solarHeatGainCoefficient();

            double thickness = simpleGlazing.thickness();
            if (double.IsNaN(thickness) || thickness <= 0)
            {
                thickness = 0.006;
            }

            double conductivity = double.NaN;
            if (!double.IsNaN(uFactor) && uFactor > 0)
            {
                conductivity = thickness * uFactor;
            }

            TransparentMaterial result = new TransparentMaterial(guid, name, name, string.Empty, conductivity, double.NaN, double.NaN);
            result.SetValue(Core.MaterialParameter.DefaultThickness, thickness);

            if (!double.IsNaN(solarHeatGainCoefficient))
            {
                result.SetValue(TransparentMaterialParameter.SolarTransmittance, solarHeatGainCoefficient);
            }

            ApplyOptionalFraction(result, TransparentMaterialParameter.LightTransmittance, simpleGlazing.visibleTransmittance());

            openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format(CultureInfo.InvariantCulture, "SimpleGlazing (U = {0:G4} W/m²K, SHGC = {1:G4}) is a performance specification with no physical build-up; it was reconstructed as a single {2:G4} m SAM glazing layer with conductivity {3:G4} W/mK (preserving U) and solar transmittance taken from the SHGC (which overstates transmittance, since SHGC includes inward-flowing absorbed radiation)", uFactor, solarHeatGainCoefficient, thickness, conductivity), label);

            return result;
        }

        /// <summary>Gas (window cavity) → SAM GasMaterial, with h derived from the 20 °C conductance.</summary>
        private static IMaterial ToSAM_Gas(global::OpenStudio.Gas gas, Guid guid, string name, OpenStudioImportContext openStudioImportContext, string label)
        {
            GasMaterial result = new GasMaterial(guid, name);
            result.SetValue(Core.MaterialParameter.DefaultThickness, gas.thickness());
            result.SetValue(GasMaterialParameter.DefaultGasType, gas.gasType());

            ApplyGasConductance(result, () => gas.getThermalConductance(293.15), openStudioImportContext, label, name);

            return result;
        }

        /// <summary>
        /// GasMixture → SAM GasMaterial. SAM carries one gas type, so the mixture's composition
        /// is lost; its thermal conductance is preserved, which is what the fenestration heat
        /// balance uses. Reported as an approximation naming the constituents.
        /// </summary>
        private static IMaterial ToSAM_GasMixture(global::OpenStudio.GasMixture gasMixture, Guid guid, string name, OpenStudioImportContext openStudioImportContext, string label)
        {
            GasMaterial result = new GasMaterial(guid, name);
            result.SetValue(Core.MaterialParameter.DefaultThickness, gasMixture.thickness());

            ApplyGasConductance(result, () => gasMixture.getThermalConductance(293.15), openStudioImportContext, label, name);

            List<string> constituents = new List<string>();
            try
            {
                for (uint i = 0; i < gasMixture.numGases(); i++)
                {
                    constituents.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1:P0}", gasMixture.getGasType(i), gasMixture.getGasFraction(i)));
                }
            }
            catch (Exception)
            {
                // the composition is descriptive only; its absence must not fail the import
            }

            openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.MaterialUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Gas mixture ({0}) has no SAM equivalent - SAM carries a single gas type; the mixture's thermal conductance was preserved but its composition was not", constituents.Count == 0 ? "composition unavailable" : string.Join(", ", constituents)), label);

            return result;
        }

        /// <summary>
        /// Applies a gas cavity's conductance as SAM's authoritative
        /// <see cref="GasMaterialParameter.HeatTransferCoefficient"/>. A conductance that cannot
        /// be evaluated leaves the material without one, reported rather than guessed.
        /// </summary>
        private static void ApplyGasConductance(GasMaterial gasMaterial, Func<double> conductanceAccessor, OpenStudioImportContext openStudioImportContext, string label, string name)
        {
            double conductance;
            try
            {
                conductance = conductanceAccessor();
            }
            catch (Exception)
            {
                conductance = double.NaN;
            }

            if (double.IsNaN(conductance) || double.IsInfinity(conductance) || conductance <= 0)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.MaterialUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("The thermal conductance of gas layer '{0}' could not be evaluated; the SAM gas material carries no heat transfer coefficient (no value was substituted)", name), label);
                return;
            }

            gasMaterial.SetValue(GasMaterialParameter.HeatTransferCoefficient, conductance);
        }

        private static void ApplyOptionalFraction(ParameterizedSAMObject parameterizedSAMObject, Enum parameter, global::OpenStudio.OptionalDouble optionalDouble)
        {
            if (optionalDouble == null || optionalDouble.isNull())
            {
                return;
            }

            parameterizedSAMObject.SetValue(parameter, optionalDouble.get());
        }

        private static void ApplyOptionalFraction(ParameterizedSAMObject parameterizedSAMObject, Enum parameter, double value)
        {
            if (double.IsNaN(value))
            {
                return;
            }

            parameterizedSAMObject.SetValue(parameter, value);
        }

        /// <summary>Applies 1 − absorptance as a reflectance, skipping absent values.</summary>
        private static void ApplyOptionalComplement(ParameterizedSAMObject parameterizedSAMObject, Enum parameter, global::OpenStudio.OptionalDouble optionalDouble)
        {
            if (optionalDouble == null || optionalDouble.isNull())
            {
                return;
            }

            parameterizedSAMObject.SetValue(parameter, 1.0 - optionalDouble.get());
        }

        /// <summary>IDD type name of an OpenStudio object, for diagnostics; never throws.</summary>
        internal static string IddTypeName(global::OpenStudio.ModelObject modelObject)
        {
            try
            {
                return modelObject.iddObjectType().valueName();
            }
            catch (Exception)
            {
                return modelObject?.GetType().Name ?? "unknown";
            }
        }
    }
}
