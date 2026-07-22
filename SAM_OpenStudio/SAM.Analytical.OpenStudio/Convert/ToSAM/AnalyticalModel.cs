// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Geometry.Spatial;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Converts an OpenStudio model into a SAM <see cref="AnalyticalModel"/>.
        /// <para>
        /// The model is read, never mutated, and never disposed here — the caller owns it. Use
        /// the path overloads in Convert/ToSAM/Import.cs when the model should be loaded and
        /// released for you.
        /// </para>
        /// <para>
        /// Passes, mirroring the established SAM_LadybugTools reverse pipeline:
        /// A) shared libraries (materials, constructions, schedules, internal conditions);
        /// B) surfaces → panels, with the two sides of an interzone partition deduplicated into
        /// one shared panel; C) spaces, each with the shell of its own converted faces;
        /// D) the AdjacencyCluster and its space ↔ panel relations; E) shading; F) model
        /// metadata, site and design days; G) pruning of unreferenced library entries.
        /// </para>
        /// </summary>
        /// <param name="model">Source OpenStudio model; null returns null.</param>
        /// <param name="openStudioImportOptions">Import options; defaults when null.</param>
        /// <returns>Import result carrying the SAM model, diagnostics and statistics; null when the model is null.</returns>
        public static OpenStudioImportResult ToSAM(this global::OpenStudio.Model model, Core.OpenStudio.OpenStudioImportOptions openStudioImportOptions = null)
        {
            if (model == null)
            {
                return null;
            }

            OpenStudioImportContext context = new OpenStudioImportContext(model, openStudioImportOptions);
            AnalyticalModel analyticalModel = ToSAM_AnalyticalModel(context);
            return new OpenStudioImportResult(context, analyticalModel);
        }

        /// <summary>
        /// Runs the whole import against a prepared context. Separated from
        /// <see cref="ToSAM(global::OpenStudio.Model, Core.OpenStudio.OpenStudioImportOptions)"/>
        /// so the path/OSW entry points can reuse one context across loading and conversion and
        /// keep their diagnostics in a single stream.
        /// </summary>
        /// <param name="openStudioImportContext">Import context; null returns null.</param>
        /// <returns>The imported SAM model; never null once a context exists.</returns>
        internal static AnalyticalModel ToSAM_AnalyticalModel(OpenStudioImportContext openStudioImportContext)
        {
            if (openStudioImportContext == null)
            {
                return null;
            }

            global::OpenStudio.Model model = openStudioImportContext.Source;

            // Pass A: shared libraries first — panels and spaces resolve their constructions and
            // internal conditions out of these caches.
            Core.MaterialLibrary materialLibrary = openStudioImportContext.ToSAM_MaterialLibrary();
            ProfileLibrary profileLibrary = openStudioImportContext.ToSAM_ProfileLibrary();
            openStudioImportContext.ToSAM_Constructions(materialLibrary);
            openStudioImportContext.ToSAM_InternalConditions(profileLibrary);

            AdjacencyCluster adjacencyCluster = new AdjacencyCluster();

            global::OpenStudio.SpaceVector spaceVector = model.getSpaces();
            int sourceSpaceCount = spaceVector?.Count ?? 0;
            openStudioImportContext.Statistics.SourceObjects += sourceSpaceCount;

            if (sourceSpaceCount == 0)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ZoneIncomplete, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "The OpenStudio model contains no spaces; the imported SAM model has no geometry", (string)null);
            }

            // Pass B/C: surfaces → panels (deduplicated across an interzone pair) and spaces.
            // panelBySurfaceName is the deduplication ledger: both sides of a pair resolve to the
            // same SAM panel, which is then related to both SAM spaces in pass D.
            Dictionary<string, Panel> panelBySurfaceName = new Dictionary<string, Panel>();
            List<KeyValuePair<Space, List<Panel>>> spacePanels = new List<KeyValuePair<Space, List<Panel>>>();
            List<UnpairedInterzoneSurface> unpairedInterzoneSurfaces = new List<UnpairedInterzoneSurface>();

            if (spaceVector != null)
            {
                foreach (global::OpenStudio.Space openStudioSpace in spaceVector)
                {
                    if (openStudioSpace == null)
                    {
                        continue;
                    }

                    global::OpenStudio.Transformation transformation = SpaceTransformation(openStudioSpace);

                    List<Panel> panels = new List<Panel>();
                    List<Face3D> face3Ds = new List<Face3D>();

                    global::OpenStudio.SurfaceVector surfaceVector = openStudioSpace.surfaces;
                    if (surfaceVector != null)
                    {
                        openStudioImportContext.Statistics.SourceObjects += surfaceVector.Count;

                        foreach (global::OpenStudio.Surface surface in surfaceVector)
                        {
                            if (surface == null)
                            {
                                continue;
                            }

                            Panel panel = ResolvePanel(surface, transformation, panelBySurfaceName, unpairedInterzoneSurfaces, openStudioImportContext);
                            if (panel == null)
                            {
                                continue;
                            }

                            panels.Add(panel);

                            // The shell must be built from THIS space's own surface geometry, not
                            // from the shared panel's face: a shared internal panel carries the
                            // winding of whichever space converted it first, and reusing it here
                            // would give the second space an inconsistent shell.
                            List<Core.OpenStudio.OpenStudioDiagnostic> faceDiagnostics;
                            Face3D face3D = Geometry.OpenStudio.Convert.ToSAM(surface, openStudioImportContext.Options.DistanceTolerance, openStudioImportContext.Options.AngleTolerance, openStudioImportContext.Options.MinimumArea, out faceDiagnostics, transformation);
                            if (face3D != null)
                            {
                                face3Ds.Add(face3D);
                            }
                        }
                    }

                    Space space = openStudioSpace.ToSAM(face3Ds, openStudioImportContext);
                    if (space == null)
                    {
                        continue;
                    }

                    openStudioImportContext.SpaceMap[openStudioSpace.nameString()] = space;
                    spacePanels.Add(new KeyValuePair<Space, List<Panel>>(space, panels));
                }
            }

            // Interzone surfaces whose partner never materialised: try the geometric fallback
            // once, across the whole model, then demote what is still unpaired. A pairing
            // supersedes the second surface's panel with the first; those supersessions are
            // recorded so the captured spacePanels lists can be remapped before topology.
            Dictionary<Panel, Panel> supersededPanels = new Dictionary<Panel, Panel>();
            ResolveUnpairedInterzoneSurfaces(unpairedInterzoneSurfaces, panelBySurfaceName, supersededPanels, openStudioImportContext);

            // Pass D: topology. A shared internal panel is added once and related to BOTH spaces —
            // that relation, not a second coincident panel, is how SAM represents one physical
            // partition between two rooms.
            foreach (KeyValuePair<Space, List<Panel>> keyValuePair in spacePanels)
            {
                adjacencyCluster.AddObject(keyValuePair.Key);

                // A geometric-fallback pairing replaced the second panel with the first after
                // this list was captured, so resolve each panel through the supersession map
                // (and dedupe) or the cluster would keep two coincident panels for one partition.
                HashSet<Panel> addedPanels = new HashSet<Panel>();
                foreach (Panel panel in keyValuePair.Value)
                {
                    Panel resolvedPanel = ResolveSupersededPanel(panel, supersededPanels);
                    if (resolvedPanel == null || !addedPanels.Add(resolvedPanel))
                    {
                        continue;
                    }

                    adjacencyCluster.AddObject(resolvedPanel);
                    adjacencyCluster.AddRelation(keyValuePair.Key, resolvedPanel);
                }
            }

            // Pass E: shading. Shade panels are free-standing — added to the cluster with no
            // space relation, as the forward direction and SAM_LadybugTools both expect.
            if (openStudioImportContext.Options.IncludeShading)
            {
                foreach (Panel panel in openStudioImportContext.ToSAM_ShadingPanels())
                {
                    adjacencyCluster.AddObject(panel);
                }
            }

            // Pass F: assign internal conditions now that spaces exist and zone data can be read.
            if (openStudioImportContext.Options.IncludeInternalConditions)
            {
                openStudioImportContext.ToSAM_AssignInternalConditions(adjacencyCluster, profileLibrary);
            }

            // Pass G: prune library entries nothing references, then build the model.
            if (openStudioImportContext.Options.PruneUnreferencedLibraryEntries)
            {
                Modify.RemoveUnreferencedMaterials(materialLibrary, adjacencyCluster);
                Modify.RemoveUnreferencedProfiles(profileLibrary, adjacencyCluster);
            }

            return openStudioImportContext.ToSAM_AnalyticalModel(adjacencyCluster, materialLibrary, profileLibrary);
        }

        /// <summary>
        /// Space → building transformation. Surface vertices are stored relative to their space,
        /// so this is what puts them into the shared coordinate system SAM works in.
        /// </summary>
        private static global::OpenStudio.Transformation SpaceTransformation(global::OpenStudio.Space space)
        {
            try
            {
                return space.buildingTransformation();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Returns the SAM panel for an OpenStudio surface, creating it only once per physical
        /// element.
        /// <para>
        /// When the surface declares an <c>adjacentSurface</c> and that partner has already been
        /// converted, the existing panel is returned — this is the deduplication that turns two
        /// opposite OpenStudio surfaces into one SAM panel related to two spaces. OpenStudio's
        /// own adjacency handles are authoritative and are always tried first; geometry is only
        /// consulted later, for surfaces that claim a "Surface" boundary but carry no handle.
        /// </para>
        /// </summary>
        private static Panel ResolvePanel(global::OpenStudio.Surface surface, global::OpenStudio.Transformation transformation, Dictionary<string, Panel> panelBySurfaceName, List<UnpairedInterzoneSurface> unpairedInterzoneSurfaces, OpenStudioImportContext openStudioImportContext)
        {
            string surfaceName = surface.nameString();

            Panel existing;
            if (panelBySurfaceName.TryGetValue(surfaceName, out existing))
            {
                return existing;
            }

            global::OpenStudio.OptionalSurface optionalAdjacentSurface = null;
            try
            {
                optionalAdjacentSurface = surface.adjacentSurface();
            }
            catch (Exception)
            {
                // an unreadable adjacency is treated as absent and handled by the fallback
            }

            bool hasAdjacentHandle = optionalAdjacentSurface != null && !optionalAdjacentSurface.isNull();
            if (hasAdjacentHandle)
            {
                string adjacentName = optionalAdjacentSurface.get().nameString();
                Panel adjacentPanel;
                if (panelBySurfaceName.TryGetValue(adjacentName, out adjacentPanel))
                {
                    // Second side of an already-converted pair: reuse, do not duplicate.
                    panelBySurfaceName[surfaceName] = adjacentPanel;
                    return adjacentPanel;
                }
            }

            Panel panel = surface.ToSAM(openStudioImportContext, transformation);
            if (panel == null)
            {
                return null;
            }

            panelBySurfaceName[surfaceName] = panel;

            if (hasAdjacentHandle)
            {
                // Claim the partner now so it resolves to this panel whichever space reaches it.
                panelBySurfaceName[optionalAdjacentSurface.get().nameString()] = panel;
            }
            else if (string.Equals(surface.outsideBoundaryCondition(), "Surface", StringComparison.OrdinalIgnoreCase))
            {
                // Snapshot the managed identity NOW. The native SWIG wrapper must not be retained
                // and dereferenced after the model walk: doing so aborts the process with an
                // unmanaged AccessViolationException. The name is the key back into
                // panelBySurfaceName; the label is all the diagnostics need.
                unpairedInterzoneSurfaces.Add(new UnpairedInterzoneSurface(surfaceName, OpenStudioImportContext.OpenStudioObjectLabel(surface)));
            }

            return panel;
        }

        /// <summary>
        /// Handles surfaces that declare an interzone ("Surface") boundary but carry no
        /// OpenStudio adjacency handle.
        /// <para>
        /// A validated geometric fallback pairs them: coincident centroids within the distance
        /// tolerance, comparable areas, and opposed normals — all three, so two parallel surfaces
        /// of a thick construction or two stacked floor slabs are not mistaken for a pair. Every
        /// geometric pairing is reported (SAM-OSI-ADJ-001) because it is an inference, not data
        /// read from the model.
        /// </para>
        /// <para>
        /// What still cannot be paired is reported (SAM-OSI-ADJ-002) and marked adiabatic — the
        /// only defensible reading of "internal surface with nothing on the other side", and
        /// exactly what the forward direction does with the mirror-image case.
        /// </para>
        /// </summary>
        private static void ResolveUnpairedInterzoneSurfaces(List<UnpairedInterzoneSurface> surfaces, Dictionary<string, Panel> panelBySurfaceName, Dictionary<Panel, Panel> supersededPanels, OpenStudioImportContext openStudioImportContext)
        {
            if (surfaces == null || surfaces.Count == 0)
            {
                return;
            }

            double distanceTolerance = openStudioImportContext.Options.DistanceTolerance;
            HashSet<string> paired = new HashSet<string>();

            if (openStudioImportContext.Options.AllowGeometricAdjacencyFallback)
            {
                for (int i = 0; i < surfaces.Count; i++)
                {
                    string nameI = surfaces[i].Name;
                    if (paired.Contains(nameI))
                    {
                        continue;
                    }

                    Panel panelI;
                    if (!panelBySurfaceName.TryGetValue(nameI, out panelI) || panelI == null)
                    {
                        continue;
                    }

                    for (int j = i + 1; j < surfaces.Count; j++)
                    {
                        string nameJ = surfaces[j].Name;
                        if (paired.Contains(nameJ))
                        {
                            continue;
                        }

                        Panel panelJ;
                        if (!panelBySurfaceName.TryGetValue(nameJ, out panelJ) || panelJ == null || ReferenceEquals(panelI, panelJ))
                        {
                            continue;
                        }

                        if (!AreCoincidentOpposites(panelI, panelJ, distanceTolerance))
                        {
                            continue;
                        }

                        // Both surfaces now resolve to the first panel; the second panel is
                        // superseded. Record the supersession so the topology pass can rewrite
                        // the already-captured space→panel list, and repoint the name ledger too.
                        panelBySurfaceName[nameJ] = panelI;
                        supersededPanels[panelJ] = panelI;
                        paired.Add(nameI);
                        paired.Add(nameJ);

                        openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.AdjacencyGeometricFallback, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Surfaces '{0}' and '{1}' declare an interzone boundary but carry no OpenStudio adjacency handle; they were paired into one SAM panel by validated geometric matching (coincident centroids, comparable areas, opposed normals) - verify the source model's adjacency", nameI, nameJ), surfaces[i].Label, panelI);
                        break;
                    }
                }
            }

            foreach (UnpairedInterzoneSurface surface in surfaces)
            {
                string name = surface.Name;
                if (paired.Contains(name))
                {
                    continue;
                }

                Panel panel;
                if (!panelBySurfaceName.TryGetValue(name, out panel) || panel == null)
                {
                    continue;
                }

                panel.SetValue(PanelParameter.Adiabatic, true);
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.AdjacencyPairingFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Surface '{0}' declares an interzone boundary but no partner could be resolved{1}; the SAM panel was marked adiabatic", name, openStudioImportContext.Options.AllowGeometricAdjacencyFallback ? " (neither by handle nor by geometric matching)" : " (by handle; geometric matching is disabled)"), surface.Label, panel);
            }
        }

        /// <summary>
        /// Follows the supersession map so a captured panel reference resolves to the panel that
        /// replaced it during the geometric-adjacency fallback. The map is a flat old→new lookup,
        /// but the walk is bounded so a pathological cycle can never loop forever.
        /// </summary>
        private static Panel ResolveSupersededPanel(Panel panel, Dictionary<Panel, Panel> supersededPanels)
        {
            Panel current = panel;
            int guard = supersededPanels.Count + 1;
            Panel next;
            while (current != null && guard-- > 0 && supersededPanels.TryGetValue(current, out next))
            {
                current = next;
            }

            return current;
        }

        /// <summary>
        /// Managed snapshot of an interzone surface that carried no OpenStudio adjacency handle:
        /// its name — the key back into the panel-by-surface-name ledger — and its diagnostic
        /// label. Captured during the model walk so the native SWIG <c>Surface</c> wrapper is
        /// never retained and dereferenced afterwards, which can abort the process with an
        /// unmanaged access violation.
        /// </summary>
        private struct UnpairedInterzoneSurface
        {
            public UnpairedInterzoneSurface(string name, string label)
            {
                Name = name;
                Label = label;
            }

            /// <summary>OpenStudio surface name; the key into <c>panelBySurfaceName</c>.</summary>
            public string Name { get; }

            /// <summary>"&lt;name&gt; [&lt;handle&gt;]" diagnostic label captured at traversal time.</summary>
            public string Label { get; }
        }

        /// <summary>
        /// True when two panels look like the two faces of one physical partition: their internal
        /// points coincide within tolerance, their areas agree to within 1 %, and their normals
        /// oppose. All three conditions are required — centroid coincidence alone would also
        /// match two surfaces of a thick wall or a floor/ceiling stack.
        /// </summary>
        private static bool AreCoincidentOpposites(Panel panel_1, Panel panel_2, double distanceTolerance)
        {
            try
            {
                Face3D face3D_1 = panel_1.GetFace3D();
                Face3D face3D_2 = panel_2.GetFace3D();
                if (face3D_1 == null || face3D_2 == null)
                {
                    return false;
                }

                Point3D point3D_1 = face3D_1.GetInternalPoint3D(distanceTolerance);
                Point3D point3D_2 = face3D_2.GetInternalPoint3D(distanceTolerance);
                if (point3D_1 == null || point3D_2 == null)
                {
                    return false;
                }

                // MacroDistance, not the raw distance tolerance: two modelled faces of the same
                // partition are authored to the same plane but rarely to 1e-6 m.
                if (point3D_1.Distance(point3D_2) > Core.Tolerance.MacroDistance)
                {
                    return false;
                }

                double area_1 = face3D_1.GetArea();
                double area_2 = face3D_2.GetArea();
                if (double.IsNaN(area_1) || double.IsNaN(area_2) || area_1 <= 0 || area_2 <= 0)
                {
                    return false;
                }

                if (Math.Abs(area_1 - area_2) / Math.Max(area_1, area_2) > 0.01)
                {
                    return false;
                }

                Vector3D normal_1 = panel_1.Normal;
                Vector3D normal_2 = panel_2.Normal;
                if (normal_1 == null || normal_2 == null)
                {
                    return false;
                }

                return normal_1.Unit.DotProduct(normal_2.Unit) < -0.99;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
