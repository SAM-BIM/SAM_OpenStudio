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
        /// Converts every construction in the model into SAM
        /// <see cref="Analytical.Construction"/> (opaque) or
        /// <see cref="Analytical.ApertureConstruction"/> (fenestration) objects and caches them
        /// on the context, keyed by OpenStudio construction name, so panels and apertures created
        /// later resolve straight out of the cache.
        /// <para>
        /// Which of the two a construction becomes is decided by its layers, not by the surfaces
        /// that reference it: a construction whose layers are glazing/gas materials is a
        /// fenestration construction even if nothing uses it yet. That keeps the classification
        /// stable regardless of import order.
        /// </para>
        /// <para>
        /// Layer order is preserved exactly as OpenStudio stores it — outside first — which is
        /// the order the forward converter writes, so a construction survives a round trip
        /// without being reversed.
        /// </para>
        /// </summary>
        /// <param name="openStudioImportContext">Import context.</param>
        /// <param name="materialLibrary">Material library the layers resolve against.</param>
        public static void ToSAM_Constructions(this OpenStudioImportContext openStudioImportContext, MaterialLibrary materialLibrary)
        {
            if (openStudioImportContext == null || !openStudioImportContext.Options.IncludeConstructions)
            {
                return;
            }

            global::OpenStudio.ConstructionBaseVector constructionBaseVector;
            try
            {
                constructionBaseVector = openStudioImportContext.Source.getConstructionBases();
            }
            catch (Exception)
            {
                return;
            }

            if (constructionBaseVector == null)
            {
                return;
            }

            openStudioImportContext.Statistics.SourceObjects += constructionBaseVector.Count;

            foreach (global::OpenStudio.ConstructionBase constructionBase in constructionBaseVector)
            {
                if (constructionBase == null)
                {
                    continue;
                }

                ToSAM_Construction(constructionBase, openStudioImportContext, materialLibrary);
            }
        }

        /// <summary>
        /// Converts one OpenStudio construction, caching the result on the context. Returns the
        /// SAM object created (a <see cref="Analytical.Construction"/>, an
        /// <see cref="Analytical.ApertureConstruction"/>, or null when the kind is unsupported).
        /// </summary>
        private static object ToSAM_Construction(global::OpenStudio.ConstructionBase constructionBase, OpenStudioImportContext openStudioImportContext, MaterialLibrary materialLibrary)
        {
            string name = constructionBase.nameString();
            string label = OpenStudioImportContext.OpenStudioObjectLabel(constructionBase);

            Construction cachedConstruction;
            if (openStudioImportContext.ConstructionMap.TryGetValue(name, out cachedConstruction))
            {
                return cachedConstruction;
            }

            ApertureConstruction cachedApertureConstruction;
            if (openStudioImportContext.ApertureConstructionMap.TryGetValue(name, out cachedApertureConstruction))
            {
                return cachedApertureConstruction;
            }

            // An air boundary carries no layers at all — it becomes PanelType.Air on the panel,
            // and only a named, layer-free SAM construction here.
            global::OpenStudio.OptionalConstructionAirBoundary optionalConstructionAirBoundary = global::OpenStudio.OpenStudioModelResources.toConstructionAirBoundary(constructionBase);
            if (optionalConstructionAirBoundary != null && !optionalConstructionAirBoundary.isNull())
            {
                Construction airConstruction = new Construction(openStudioImportContext.ResolveGuid(constructionBase, typeof(Construction).Name), name);
                Modify.SetOpenStudioSource(airConstruction, constructionBase);
                openStudioImportContext.ConstructionMap[name] = airConstruction;
                openStudioImportContext.RegisterCreated();
                return airConstruction;
            }

            global::OpenStudio.OptionalConstruction optionalConstruction = global::OpenStudio.OpenStudioModelResources.toConstruction(constructionBase);
            if (optionalConstruction == null || optionalConstruction.isNull())
            {
                // ConstructionWithInternalSource, CFactorUndergroundWall, FFactorGroundFloor,
                // ConstructionAirBoundary variants and window data files all land here: they
                // describe heat transfer in ways SAM's layered model cannot express.
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ConstructionUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Construction type '{0}' is not a layered construction and has no SAM equivalent; a name-only SAM construction was created with no layers - no build-up was invented", IddTypeName(constructionBase)), label);
                openStudioImportContext.RegisterSkip();

                Construction placeholder = new Construction(openStudioImportContext.ResolveGuid(constructionBase, typeof(Construction).Name), name);
                Modify.SetOpenStudioSource(placeholder, constructionBase);
                openStudioImportContext.ConstructionMap[name] = placeholder;
                return placeholder;
            }

            global::OpenStudio.Construction construction = optionalConstruction.get();

            List<ConstructionLayer> constructionLayers = new List<ConstructionLayer>();

            // The stamped SAM type is authoritative when present. An aperture construction with
            // opaque panes — a solid door — exports as an OS:Construction with opaque layers,
            // indistinguishable from a wall construction by its materials alone. Only the
            // SAM.Type feature says it is fenestration, and trusting it both restores the GUID
            // (the type-guard would otherwise reject it) and files it in the aperture-construction
            // map, so the door keeps its real construction instead of a placeholder.
            string stampedType;
            bool fenestration = Core.OpenStudio.Query.TryGetSAMType(constructionBase, out stampedType)
                && string.Equals(stampedType, typeof(ApertureConstruction).Name, StringComparison.Ordinal);
            bool anyLayer = false;

            global::OpenStudio.MaterialVector materialVector = construction.layers();
            if (materialVector != null)
            {
                foreach (global::OpenStudio.Material material in materialVector)
                {
                    if (material == null)
                    {
                        openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ConstructionUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "Construction contains a null layer; the layer was skipped", label);
                        openStudioImportContext.RegisterSkip();
                        continue;
                    }

                    anyLayer = true;

                    if (IsFenestrationMaterial(material))
                    {
                        fenestration = true;
                    }

                    IMaterial iMaterial = material.ToSAM(openStudioImportContext);
                    if (iMaterial == null)
                    {
                        continue;
                    }

                    if (materialLibrary != null && materialLibrary.GetMaterial(iMaterial.Name) == null)
                    {
                        materialLibrary.Add(iMaterial);
                    }

                    constructionLayers.Add(new ConstructionLayer(iMaterial.Name, LayerThickness(material, iMaterial)));
                }
            }

            if (!anyLayer)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ConstructionUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "Construction has no layers; a name-only SAM construction was created", label);
            }

            if (fenestration)
            {
                // Pane layers only: OpenStudio has no frame layer inside the construction (frame
                // and divider are a separate WindowPropertyFrameAndDivider object), so inventing
                // SAM frame layers here would fabricate build-up that does not exist.
                ApertureConstruction apertureConstruction = new ApertureConstruction(openStudioImportContext.ResolveGuid(constructionBase, typeof(ApertureConstruction).Name), name, ApertureType.Window, constructionLayers, null);
                Modify.SetOpenStudioSource(apertureConstruction, constructionBase);
                openStudioImportContext.ApertureConstructionMap[name] = apertureConstruction;
                openStudioImportContext.RegisterCreated();
                return apertureConstruction;
            }

            Construction result = new Construction(openStudioImportContext.ResolveGuid(constructionBase, typeof(Construction).Name), name, constructionLayers);
            Modify.SetOpenStudioSource(result, constructionBase);
            openStudioImportContext.ConstructionMap[name] = result;
            openStudioImportContext.RegisterCreated();
            return result;
        }

        /// <summary>
        /// Thickness [m] of a construction layer. OpenStudio reports 0 for layers defined by
        /// resistance rather than geometry (massless, air gap, simple glazing); the nominal
        /// thickness the material converter recorded is used instead, so the SAM layer is never
        /// zero-thickness.
        /// </summary>
        private static double LayerThickness(global::OpenStudio.Material material, IMaterial iMaterial)
        {
            double thickness = double.NaN;
            try
            {
                thickness = material.thickness();
            }
            catch (Exception)
            {
                // resistance-defined layers report no thickness
            }

            if (!double.IsNaN(thickness) && thickness > 0)
            {
                return thickness;
            }

            ParameterizedSAMObject parameterizedSAMObject = iMaterial as ParameterizedSAMObject;
            double defaultThickness;
            if (parameterizedSAMObject != null && parameterizedSAMObject.TryGetValue(Core.MaterialParameter.DefaultThickness, out defaultThickness) && !double.IsNaN(defaultThickness) && defaultThickness > 0)
            {
                return defaultThickness;
            }

            return 0;
        }

        /// <summary>
        /// True when a layer belongs to the fenestration material family (glazing or gas), which
        /// is what makes its construction an aperture construction.
        /// </summary>
        private static bool IsFenestrationMaterial(global::OpenStudio.Material material)
        {
            global::OpenStudio.OptionalFenestrationMaterial optionalFenestrationMaterial = global::OpenStudio.OpenStudioModelResources.toFenestrationMaterial(material);
            return optionalFenestrationMaterial != null && !optionalFenestrationMaterial.isNull();
        }
    }
}
