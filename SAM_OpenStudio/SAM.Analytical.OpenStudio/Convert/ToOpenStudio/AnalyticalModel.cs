// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Converts a SAM AnalyticalModel to an OpenStudio model (geometry scope: stories, spaces,
        /// thermal zones, surfaces, subsurfaces, shading; constructions/loads/HVAC follow in later
        /// milestones). The source model is never mutated: a copy receives the established SAM
        /// preprocessing (OffsetAperturesOnEdge, ReplaceTransparentPanels — as SAM_LadybugTools).
        /// Conversion runs in passes: A) stories/spaces/zones; B) space-facing panels from
        /// AdjacencyCluster.UpdateNormals → surfaces and subsurfaces; C) boundary conditions and
        /// explicit adjacency pairing from SAM topology (never geometric matching). All
        /// deviations are reported through diagnostics on the returned result.
        /// </summary>
        /// <param name="analyticalModel">Source SAM analytical model.</param>
        /// <param name="openStudioConversionOptions">Options; defaults are used when null.</param>
        /// <returns>Conversion result with model, diagnostics and object map; null when input is null.</returns>
        public static OpenStudioConversionResult ToOpenStudio(this AnalyticalModel analyticalModel, Core.OpenStudio.OpenStudioConversionOptions openStudioConversionOptions = null)
        {
            if (analyticalModel == null)
            {
                return null;
            }

            Core.OpenStudio.OpenStudioConversionOptions options = openStudioConversionOptions ?? new Core.OpenStudio.OpenStudioConversionOptions();

            AnalyticalModel analyticalModel_Temp = new AnalyticalModel(analyticalModel);
            analyticalModel_Temp.OffsetAperturesOnEdge(0.1, options.DistanceTolerance);
            analyticalModel_Temp.ReplaceTransparentPanels(0.1);

            OpenStudioConversionContext context = new OpenStudioConversionContext(analyticalModel_Temp, new global::OpenStudio.Model(), options);

            AdjacencyCluster adjacencyCluster = analyticalModel_Temp.AdjacencyCluster;
            if (adjacencyCluster == null)
            {
                context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.AdjacencyMissingSurface, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "AnalyticalModel has no AdjacencyCluster; nothing to convert", analyticalModel);
                return new OpenStudioConversionResult(context);
            }

            SortedList<double, global::OpenStudio.BuildingStory> buildingStories = context.ToOpenStudio_BuildingStories();

            Dictionary<Guid, List<global::OpenStudio.Surface>> surfacesByPanel = new Dictionary<Guid, List<global::OpenStudio.Surface>>();
            Dictionary<Guid, Panel> panelByGuid = new Dictionary<Guid, Panel>();
            Dictionary<Guid, List<global::OpenStudio.SubSurface>> subSurfacesByAperture = new Dictionary<Guid, List<global::OpenStudio.SubSurface>>();

            List<Space> spaces = adjacencyCluster.GetSpaces();
            if (spaces == null || spaces.Count == 0)
            {
                context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.AdjacencyMissingSurface, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "AnalyticalModel contains no spaces", analyticalModel);
            }
            else
            {
                foreach (Space space in spaces)
                {
                    if (space == null)
                    {
                        continue;
                    }

                    global::OpenStudio.Space openStudioSpace = space.ToOpenStudio(context);
                    if (openStudioSpace == null)
                    {
                        continue;
                    }

                    if (buildingStories.Count > 0)
                    {
                        double elevation = space.MinElevation(adjacencyCluster);
                        if (!double.IsNaN(elevation))
                        {
                            double difference_Min = double.MaxValue;
                            global::OpenStudio.BuildingStory buildingStory_Min = null;
                            foreach (KeyValuePair<double, global::OpenStudio.BuildingStory> keyValuePair in buildingStories)
                            {
                                double difference = Math.Abs(keyValuePair.Key - elevation);
                                if (difference < difference_Min)
                                {
                                    difference_Min = difference;
                                    buildingStory_Min = keyValuePair.Value;
                                }
                            }

                            if (buildingStory_Min != null)
                            {
                                openStudioSpace.setBuildingStory(buildingStory_Min);
                            }
                        }
                    }

                    int index = adjacencyCluster.GetIndex(space);
                    List<IPanel> panels = adjacencyCluster.UpdateNormals(space, false, true, false, Core.Tolerance.MacroDistance, options.DistanceTolerance);
                    if (panels == null || panels.Count == 0)
                    {
                        context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.AdjacencyMissingSurface, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "Space has no related panels; no surfaces were created", space);
                        continue;
                    }

                    foreach (IPanel iPanel in panels)
                    {
                        Panel panel = iPanel as Panel;
                        if (panel == null)
                        {
                            context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryInvalidBoundary, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Unsupported panel kind {0}; skipped", iPanel?.GetType()?.Name), space);
                            continue;
                        }

                        if (panel.PanelType == PanelType.Shade)
                        {
                            continue;
                        }

                        global::OpenStudio.Surface surface = panel.ToOpenStudio(openStudioSpace, index, context, subSurfacesByAperture);
                        if (surface == null)
                        {
                            continue;
                        }

                        List<global::OpenStudio.Surface> surfaces;
                        if (!surfacesByPanel.TryGetValue(panel.Guid, out surfaces))
                        {
                            surfaces = new List<global::OpenStudio.Surface>();
                            surfacesByPanel[panel.Guid] = surfaces;
                        }

                        surfaces.Add(surface);
                        panelByGuid[panel.Guid] = panel;
                    }
                }
            }

            foreach (KeyValuePair<Guid, List<global::OpenStudio.Surface>> keyValuePair in surfacesByPanel)
            {
                Panel panel = panelByGuid[keyValuePair.Key];
                List<global::OpenStudio.Surface> surfaces = keyValuePair.Value;
                string boundaryCondition = panel.PanelType.OutsideBoundaryCondition();

                if (boundaryCondition == "Surface")
                {
                    if (surfaces.Count >= 2)
                    {
                        if (surfaces.Count > 2)
                        {
                            context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.AdjacencyMissingSurface, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Internal panel produced {0} surfaces; only the first two were paired", surfaces.Count), panel);
                        }

                        if (!surfaces[0].setAdjacentSurface(surfaces[1]))
                        {
                            context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.AdjacencyMissingSurface, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "OpenStudio rejected the adjacent-surface pairing; Adiabatic applied to both sides", panel);
                            foreach (global::OpenStudio.Surface surface in surfaces)
                            {
                                surface.setOutsideBoundaryCondition("Adiabatic");
                            }
                        }
                    }
                    else
                    {
                        context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.AdjacencyMissingSurface, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "Internal panel bounds a single space; Adiabatic applied", panel);
                        surfaces[0].setOutsideBoundaryCondition("Adiabatic");
                    }
                }
                else if (boundaryCondition != null)
                {
                    foreach (global::OpenStudio.Surface surface in surfaces)
                    {
                        surface.setOutsideBoundaryCondition(boundaryCondition);
                    }
                }
                else
                {
                    context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.AdjacencyMissingSurface, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("PanelType {0} has no boundary-condition mapping; Adiabatic applied", panel.PanelType), panel);
                    foreach (global::OpenStudio.Surface surface in surfaces)
                    {
                        surface.setOutsideBoundaryCondition("Adiabatic");
                    }
                }
            }

            foreach (KeyValuePair<Guid, List<global::OpenStudio.SubSurface>> keyValuePair in subSurfacesByAperture)
            {
                List<global::OpenStudio.SubSurface> subSurfaces = keyValuePair.Value;
                if (subSurfaces.Count >= 2)
                {
                    if (!subSurfaces[0].setAdjacentSubSurface(subSurfaces[1]))
                    {
                        context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.AdjacencyMissingSurface, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "OpenStudio rejected the adjacent-subsurface pairing for an internal aperture", null, subSurfaces[0].nameString());
                    }
                }
            }

            if (options.IncludeShading)
            {
                context.ToOpenStudio_ShadingSurfaces();
            }

            return new OpenStudioConversionResult(context);
        }
    }
}
