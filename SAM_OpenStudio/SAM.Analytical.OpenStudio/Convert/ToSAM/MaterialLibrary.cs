// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
            global::OpenStudio.OptionalStandardOpaqueMaterial optionalStandardOpaqueMaterial = global::OpenStudio.OpenStudioModelResources.toStandardOpaqueMaterial(material);

            // SAM-written face and thickness objects of one source material become one SAM material, matched by the
            // explicit source Guid only (never by name). Null when there is nothing to merge or they conflict.
            if (optionalStandardOpaqueMaterial != null && !optionalStandardOpaqueMaterial.isNull() && TryGetSourceMaterialGuid(optionalStandardOpaqueMaterial.get(), out Guid sourceGuid))
            {
                if (!openStudioImportContext.OpaqueMaterialGroups.TryGetValue(sourceGuid, out IMaterial merged))
                {
                    merged = MergedOpaqueMaterial(sourceGuid, openStudioImportContext);
                    openStudioImportContext.OpaqueMaterialGroups[sourceGuid] = merged;
                }

                if (merged != null)
                {
                    openStudioImportContext.MaterialMap[name] = merged;
                    return merged;
                }
            }

            Guid guid = openStudioImportContext.ResolveGuid(material);

            IMaterial result = null;

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

            double thermalAbsorptance = standardOpaqueMaterial.thermalAbsorptance();
            double solarReflectance = 1.0 - standardOpaqueMaterial.solarAbsorptance();
            double visibleReflectance = 1.0 - standardOpaqueMaterial.visibleAbsorptance();

            // A SAM-written object carries both authored face sets explicitly (the forward converter
            // writes an internal-face variant for a zone face). When that metadata is usable, exactly the
            // authored values are restored - nothing unstated is filled in - and an edit made to the object
            // after it was written is kept on the face it carries, reported.
            FaceObject faceObject = ReadFaceObject(standardOpaqueMaterial, openStudioImportContext, label, true);
            if (faceObject != null)
            {
                foreach (KeyValuePair<OpaqueMaterialParameter, double> keyValuePair in faceObject.Authored)
                {
                    result.SetValue(keyValuePair.Key, keyValuePair.Value);
                }

                ApplyEdits(result, new[] { faceObject }, faceObject.Edits, openStudioImportContext);
                return result;
            }

            // EnergyPlus opaque materials are single-sided: one absorptance set governs both
            // faces. Without usable SAM face metadata both SAM sides therefore receive the same
            // value - recorded here so no asymmetry is invented on the way back.
            result.SetValue(OpaqueMaterialParameter.ExternalEmissivity, thermalAbsorptance);
            result.SetValue(OpaqueMaterialParameter.InternalEmissivity, thermalAbsorptance);
            result.SetValue(OpaqueMaterialParameter.ExternalSolarReflectance, solarReflectance);
            result.SetValue(OpaqueMaterialParameter.InternalSolarReflectance, solarReflectance);
            result.SetValue(OpaqueMaterialParameter.ExternalLightReflectance, visibleReflectance);
            result.SetValue(OpaqueMaterialParameter.InternalLightReflectance, visibleReflectance);

            return result;
        }

        private static readonly OpaqueMaterialParameter[] stampedOptics =
        {
            OpaqueMaterialParameter.ExternalEmissivity, OpaqueMaterialParameter.InternalEmissivity,
            OpaqueMaterialParameter.ExternalSolarReflectance, OpaqueMaterialParameter.InternalSolarReflectance,
            OpaqueMaterialParameter.ExternalLightReflectance, OpaqueMaterialParameter.InternalLightReflectance,
        };

        /// <summary>One SAM-written opaque face object, read from its explicit metadata.</summary>
        private sealed class FaceObject
        {
            public global::OpenStudio.StandardOpaqueMaterial Material { get; set; }
            public string Name { get; set; }
            public string Label { get; set; }
            public Guid MaterialGuid { get; set; }
            public bool Internal { get; set; }
            public bool CarriesIdentity { get; set; }

            /// <summary>The authored SAM face optics the writer stamped (unstated ones absent).</summary>
            public Dictionary<OpaqueMaterialParameter, double> Authored { get; } = new Dictionary<OpaqueMaterialParameter, double>();

            /// <summary>Values changed since the object was written, as SAM values on the face the object carries.</summary>
            public Dictionary<OpaqueMaterialParameter, double> Edits { get; } = new Dictionary<OpaqueMaterialParameter, double>();

            /// <summary>Per edited parameter: what was written and what is there now.</summary>
            public Dictionary<OpaqueMaterialParameter, string> EditNotes { get; } = new Dictionary<OpaqueMaterialParameter, string>();

            public string Signature => string.Join(";", Authored.OrderBy(x => x.Key.ToString(), StringComparer.Ordinal).Select(x => x.Key + "=" + x.Value.ToString("R", CultureInfo.InvariantCulture)));
        }

        private const string singleValueOnBothFaces = "; the material's single EnergyPlus value is imported on both faces";

        /// <summary>The source SAM material Guid stamped on a SAM-written opaque object, if well formed.</summary>
        private static bool TryGetSourceMaterialGuid(global::OpenStudio.StandardOpaqueMaterial standardOpaqueMaterial, out Guid materialGuid)
        {
            materialGuid = Guid.Empty;
            return Core.OpenStudio.Query.TryGetSAMFeature(standardOpaqueMaterial, Core.OpenStudio.OpenStudioIdentityKeys.MaterialFace, out string _)
                && Core.OpenStudio.Query.TryGetSAMFeature(standardOpaqueMaterial, Core.OpenStudio.OpenStudioIdentityKeys.MaterialGuid, out string raw)
                && Guid.TryParse(raw, out materialGuid) && materialGuid != Guid.Empty;
        }

        /// <summary>
        /// Reads the SAM face metadata of an opaque object (see <see cref="Core.OpenStudio.OpenStudioIdentityKeys.MaterialFace"/>).
        /// Null when the object has none (a third-party or older OSM: silent) or it cannot be trusted (reported when
        /// <paramref name="report"/>): the source Guid and face must be well formed, any <c>SAM.Guid</c> must name the
        /// same material, every stamped value must be a fraction, and the written values must agree with the authored
        /// face they came from.
        /// <para>
        /// Each absorptance is compared with the value the writer put there (<c>SAM.Written.*</c>, or the authored face it
        /// came from): an unchanged object keeps exactly the authored values, so an OpenStudio default written for an
        /// unstated value is never imported as SAM data, while a changed value is an edit, kept on the face the object
        /// carries. Without either record the value's provenance is unknown and it is kept as an edit too.
        /// </para>
        /// </summary>
        private static FaceObject ReadFaceObject(global::OpenStudio.StandardOpaqueMaterial standardOpaqueMaterial, OpenStudioImportContext openStudioImportContext, string label, bool report)
        {
            if (!Core.OpenStudio.Query.TryGetSAMFeature(standardOpaqueMaterial, Core.OpenStudio.OpenStudioIdentityKeys.MaterialFace, out string face))
            {
                return null;
            }

            if (!Core.OpenStudio.Query.TryGetSAMFeature(standardOpaqueMaterial, Core.OpenStudio.OpenStudioIdentityKeys.MaterialGuid, out string rawGuid) || !Guid.TryParse(rawGuid, out Guid materialGuid) || materialGuid == Guid.Empty
                || (face != Core.OpenStudio.OpenStudioIdentityKeys.MaterialFaceExternal && face != Core.OpenStudio.OpenStudioIdentityKeys.MaterialFaceInternal))
            {
                Report(report, openStudioImportContext, Core.OpenStudio.OpenStudioImportDiagnosticCodes.IdentityCollision, string.Format("SAM face metadata is malformed (source material '{0}', face '{1}')" + singleValueOnBothFaces, rawGuid, face), label);
                return null;
            }

            bool carriesIdentity = Core.OpenStudio.Query.TryGetSAMGuid(standardOpaqueMaterial, out Guid ownGuid);
            if (carriesIdentity && ownGuid != materialGuid)
            {
                Report(report, openStudioImportContext, Core.OpenStudio.OpenStudioImportDiagnosticCodes.IdentityCollision, string.Format("SAM identity {0} and source material {1} of this face object disagree" + singleValueOnBothFaces, ownGuid, materialGuid), label);
                return null;
            }

            FaceObject result = new FaceObject { Material = standardOpaqueMaterial, Name = standardOpaqueMaterial.nameString(), Label = label, MaterialGuid = materialGuid, Internal = face == Core.OpenStudio.OpenStudioIdentityKeys.MaterialFaceInternal, CarriesIdentity = carriesIdentity };
            foreach (OpaqueMaterialParameter opaqueMaterialParameter in stampedOptics)
            {
                if (!Core.OpenStudio.Query.TryGetSAMFeature(standardOpaqueMaterial, Core.OpenStudio.OpenStudioIdentityKeys.OpticPrefix + opaqueMaterialParameter, out string raw))
                {
                    continue;
                }

                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || double.IsNaN(value) || value < 0 || value > 1)
                {
                    Report(report, openStudioImportContext, Core.OpenStudio.OpenStudioImportDiagnosticCodes.MaterialUnsupported, string.Format("Stamped SAM {0} '{1}' is not a fraction" + singleValueOnBothFaces, opaqueMaterialParameter, raw), label);
                    return null;
                }

                result.Authored[opaqueMaterialParameter] = value;
            }

            // (internal, external, written key, native absorptance, absorptance is the SAM value itself)
            (OpaqueMaterialParameter Internal, OpaqueMaterialParameter External, string Written, double Native, bool Direct)[] properties =
            {
                (OpaqueMaterialParameter.InternalEmissivity, OpaqueMaterialParameter.ExternalEmissivity, "ThermalAbsorptance", standardOpaqueMaterial.thermalAbsorptance(), true),
                (OpaqueMaterialParameter.InternalSolarReflectance, OpaqueMaterialParameter.ExternalSolarReflectance, "SolarAbsorptance", standardOpaqueMaterial.solarAbsorptance(), false),
                (OpaqueMaterialParameter.InternalLightReflectance, OpaqueMaterialParameter.ExternalLightReflectance, "VisibleAbsorptance", standardOpaqueMaterial.visibleAbsorptance(), false),
            };

            foreach ((OpaqueMaterialParameter internalParameter, OpaqueMaterialParameter externalParameter, string writtenKey, double native, bool direct) in properties)
            {
                double? written = null;
                if (Core.OpenStudio.Query.TryGetSAMFeature(standardOpaqueMaterial, Core.OpenStudio.OpenStudioIdentityKeys.WrittenPrefix + writtenKey, out string rawWritten))
                {
                    if (!double.TryParse(rawWritten, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) || double.IsNaN(parsed))
                    {
                        Report(report, openStudioImportContext, Core.OpenStudio.OpenStudioImportDiagnosticCodes.MaterialUnsupported, string.Format("Stamped written {0} '{1}' is not a number" + singleValueOnBothFaces, writtenKey, rawWritten), label);
                        return null;
                    }

                    written = parsed;
                }

                // The authored face the writer used: the internal value on an internal-face object when stated, else the external one.
                OpaqueMaterialParameter source = result.Internal && result.Authored.ContainsKey(internalParameter) ? internalParameter : externalParameter;
                if (result.Authored.TryGetValue(source, out double authored))
                {
                    double expected = direct ? authored : 1.0 - authored;
                    if (written.HasValue && Math.Abs(written.Value - expected) > 1e-9)
                    {
                        Report(report, openStudioImportContext, Core.OpenStudio.OpenStudioImportDiagnosticCodes.MaterialUnsupported, string.Format(CultureInfo.InvariantCulture, "Stamped written {0} {1:R} disagrees with the stamped SAM {2} {3:R}" + singleValueOnBothFaces, writtenKey, written.Value, source, authored), label);
                        return null;
                    }

                    written = written ?? expected;
                }

                if (written.HasValue && Math.Abs(written.Value - native) <= 1e-9)
                {
                    continue;
                }

                OpaqueMaterialParameter onFace = result.Internal ? internalParameter : externalParameter;
                result.Edits[onFace] = direct ? native : 1.0 - native;
                result.EditNotes[onFace] = written.HasValue
                    ? string.Format(CultureInfo.InvariantCulture, "{0} written {1:R}, now {2:R}", writtenKey, written.Value, native)
                    : string.Format(CultureInfo.InvariantCulture, "{0} {1:R} with no record of what was written", writtenKey, native);
            }

            return result;
        }

        private static void Report(bool report, OpenStudioImportContext openStudioImportContext, string code, string message, string label)
        {
            if (report)
            {
                openStudioImportContext.AddDiagnostic(code, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, message, label);
            }
        }

        /// <summary>Sets each edited value on its face and reports it: an edit made after export is kept, never silently replaced by an authored value or a default.</summary>
        private static void ApplyEdits(OpaqueMaterial opaqueMaterial, IEnumerable<FaceObject> faceObjects, Dictionary<OpaqueMaterialParameter, double> edits, OpenStudioImportContext openStudioImportContext)
        {
            foreach (KeyValuePair<OpaqueMaterialParameter, double> keyValuePair in edits)
            {
                opaqueMaterial.SetValue(keyValuePair.Key, keyValuePair.Value);
            }

            foreach (FaceObject faceObject in faceObjects)
            {
                foreach (KeyValuePair<OpaqueMaterialParameter, string> note in faceObject.EditNotes)
                {
                    openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.MaterialUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format(CultureInfo.InvariantCulture, "The {0}-face object was changed after it was written ({1}); the changed value is imported as SAM {2} = {3:R}", faceObject.Internal ? "internal" : "external", note.Value, note.Key, faceObject.Edits[note.Key]), faceObject.Label);
                }
            }
        }

        /// <summary>
        /// One SAM material for all SAM-written face and thickness objects of source material <paramref name="materialGuid"/>,
        /// or null when fewer than two usable objects exist or they conflict (each is then imported on its own).
        /// <para>
        /// Matching is by the explicit <c>SAM.MaterialGuid</c> only, never by name. The objects merge when their thermal
        /// properties and authored face optics agree and, per face, every object resolves each optic to the same value
        /// (its edit, else the authored value). Layer thicknesses stay on the construction layers. The SAM Guid is
        /// restored from the object that carries it.
        /// </para>
        /// </summary>
        private static IMaterial MergedOpaqueMaterial(Guid materialGuid, OpenStudioImportContext openStudioImportContext)
        {
            List<FaceObject> faceObjects = new List<FaceObject>();
            foreach (global::OpenStudio.StandardOpaqueMaterial standardOpaqueMaterial in openStudioImportContext.Source.getStandardOpaqueMaterials())
            {
                if (TryGetSourceMaterialGuid(standardOpaqueMaterial, out Guid guid) && guid == materialGuid)
                {
                    FaceObject faceObject = ReadFaceObject(standardOpaqueMaterial, openStudioImportContext, OpenStudioImportContext.OpenStudioObjectLabel(standardOpaqueMaterial), false);
                    if (faceObject != null)
                    {
                        faceObjects.Add(faceObject);
                    }
                }
            }

            if (faceObjects.Count < 2)
            {
                return null;
            }

            faceObjects = faceObjects.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
            FaceObject first = faceObjects[0];
            string conflict = null;
            if (faceObjects.Any(x => !Same(x.Material.thermalConductivity(), first.Material.thermalConductivity()) || !Same(x.Material.density(), first.Material.density()) || !Same(x.Material.specificHeat(), first.Material.specificHeat())))
            {
                conflict = "their thermal properties differ";
            }
            else if (faceObjects.Any(x => x.Signature != first.Signature))
            {
                conflict = "their authored face optics differ";
            }

            Dictionary<OpaqueMaterialParameter, double> edits = new Dictionary<OpaqueMaterialParameter, double>();
            foreach (OpaqueMaterialParameter opaqueMaterialParameter in stampedOptics)
            {
                if (conflict != null)
                {
                    break;
                }

                bool internalParameter = opaqueMaterialParameter.ToString().StartsWith("Internal", StringComparison.Ordinal);
                List<FaceObject> onFace = faceObjects.Where(x => x.Internal == internalParameter).ToList();
                List<double?> values = onFace.Select(x => x.Edits.TryGetValue(opaqueMaterialParameter, out double edit) ? edit : x.Authored.TryGetValue(opaqueMaterialParameter, out double authored) ? authored : (double?)null).ToList();
                if (values.Any(x => x.HasValue != values[0].HasValue || (x.HasValue && !Same(x.Value, values[0].Value))))
                {
                    conflict = opaqueMaterialParameter + " differs between the " + (internalParameter ? "internal" : "external") + "-face objects";
                }
                else if (onFace.Any(x => x.Edits.ContainsKey(opaqueMaterialParameter)))
                {
                    edits[opaqueMaterialParameter] = values[0].Value;
                }
            }

            if (conflict != null)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.IdentityCollision, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("The face and thickness objects of source material {0} ({1}) are not merged: {2}; each is imported as its own material", materialGuid, string.Join(", ", faceObjects.Select(x => x.Name)), conflict), first.Label);
                return null;
            }

            // The identity carrier, else an external-face object, else the first by name; ties by thickness then name.
            FaceObject primary = faceObjects.OrderByDescending(x => x.CarriesIdentity).ThenBy(x => x.Internal).ThenBy(x => x.Material.thickness()).ThenBy(x => x.Name, StringComparer.Ordinal).First();
            OpaqueMaterial result = new OpaqueMaterial(openStudioImportContext.ResolveGuid(primary.Material), primary.Name, primary.Name, string.Empty, primary.Material.thermalConductivity(), primary.Material.density(), primary.Material.specificHeat());
            result.SetValue(Core.MaterialParameter.DefaultThickness, primary.Material.thickness());
            foreach (KeyValuePair<OpaqueMaterialParameter, double> keyValuePair in first.Authored)
            {
                result.SetValue(keyValuePair.Key, keyValuePair.Value);
            }

            ApplyEdits(result, faceObjects, edits, openStudioImportContext);
            openStudioImportContext.RegisterCreated();
            return result;
        }

        private static bool Same(double a, double b)
        {
            return Math.Abs(a - b) <= 1e-9 * Math.Max(1.0, Math.Max(Math.Abs(a), Math.Abs(b)));
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
