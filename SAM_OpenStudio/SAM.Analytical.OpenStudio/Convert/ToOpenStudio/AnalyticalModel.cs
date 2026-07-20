// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

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
            OpenStudioConversionContext context = ToOpenStudio_Context(analyticalModel, openStudioConversionOptions);
            return context == null ? null : new OpenStudioConversionResult(context);
        }

        /// <summary>
        /// Full MVP pipeline (plan §3): converts the model, assigns the EPW weather and MVP
        /// simulation settings, saves the OSM, generates the OSW, runs the OpenStudio CLI and
        /// extracts annual Ideal Loads energy. The returned result carries paths, run outcome,
        /// loads and every diagnostic raised along the way.
        /// </summary>
        /// <param name="analyticalModel">Source SAM analytical model.</param>
        /// <param name="epwPath">EPW weather file (explicit input; SAM weather is not consulted).</param>
        /// <param name="outputDirectory">Directory for the OSM/OSW and the isolated run folder.</param>
        /// <param name="openStudioConversionOptions">Conversion options; defaults when null.</param>
        /// <param name="openStudioRunOptions">Run options (CLI path, timeout); defaults when null.</param>
        /// <param name="run">False converts and saves OSM/OSW without executing the CLI.</param>
        /// <returns>Conversion result including RunResult and Loads; null when input is null.</returns>
        public static OpenStudioConversionResult ToOpenStudio(this AnalyticalModel analyticalModel, string epwPath, string outputDirectory, Core.OpenStudio.OpenStudioConversionOptions openStudioConversionOptions = null, Core.OpenStudio.OpenStudioRunOptions openStudioRunOptions = null, bool run = true)
        {
            OpenStudioConversionContext context = ToOpenStudio_Context(analyticalModel, openStudioConversionOptions, FirstDayOfWeekOffset(epwPath, openStudioConversionOptions));
            if (context == null)
            {
                return null;
            }

            context.ToOpenStudio_Weather(epwPath);

            // Design days: an explicit run-option DDY takes precedence over the conversion
            // option; sizing periods are enabled by the settings step when days were imported.
            string ddyPath = !string.IsNullOrWhiteSpace(openStudioRunOptions?.DdyPath) ? openStudioRunOptions.DdyPath : openStudioConversionOptions?.DdyPath;
            context.ToOpenStudio_DesignDays(ddyPath);

            context.ToOpenStudio_SimulationSettings();
            return OpenStudioSimulationRunner.Run(context, epwPath, outputDirectory, openStudioRunOptions, run);
        }

        /// <summary>
        /// Resolves the 1-Jan day-of-week offset (Monday = 0 … Sunday = 6) used to align weekly
        /// profiles: the explicit <see cref="Core.OpenStudio.OpenStudioConversionOptions.FirstDayOfWeek"/>
        /// when set, otherwise the EPW weather file's declared start day of week; 0 (Monday) when
        /// neither is available.
        /// </summary>
        private static int FirstDayOfWeekOffset(string epwPath, Core.OpenStudio.OpenStudioConversionOptions openStudioConversionOptions)
        {
            if (openStudioConversionOptions?.FirstDayOfWeek != null)
            {
                return ((int)openStudioConversionOptions.FirstDayOfWeek.Value + 6) % 7;
            }

            if (!string.IsNullOrWhiteSpace(epwPath) && System.IO.File.Exists(epwPath))
            {
                global::OpenStudio.OptionalEpwFile optionalEpwFile = global::OpenStudio.EpwFile.load(global::OpenStudio.OpenStudioUtilitiesCore.toPath(epwPath));
                if (optionalEpwFile != null && !optionalEpwFile.isNull())
                {
                    return (optionalEpwFile.get().startDayOfWeek().value() + 6) % 7;
                }
            }

            return 0;
        }

        private static OpenStudioConversionContext ToOpenStudio_Context(AnalyticalModel analyticalModel, Core.OpenStudio.OpenStudioConversionOptions openStudioConversionOptions, int? firstDayOfWeekOffset = null)
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
            if (firstDayOfWeekOffset.HasValue)
            {
                context.FirstDayOfWeekOffset = firstDayOfWeekOffset.Value;
            }
            else if (options.FirstDayOfWeek != null)
            {
                context.FirstDayOfWeekOffset = ((int)options.FirstDayOfWeek.Value + 6) % 7;
            }

            AdjacencyCluster adjacencyCluster = analyticalModel_Temp.AdjacencyCluster;
            if (adjacencyCluster == null)
            {
                context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.AdjacencyMissingSurface, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "AnalyticalModel has no AdjacencyCluster; nothing to convert", analyticalModel);
                return context;
            }

            SortedList<double, global::OpenStudio.BuildingStory> buildingStories = context.ToOpenStudio_BuildingStories();

            List<Panel> sourcePanels = adjacencyCluster.GetPanels();
            int sourceApertureCount = 0;
            if (sourcePanels != null)
            {
                foreach (Panel sourcePanel in sourcePanels)
                {
                    List<Aperture> sourceApertures = sourcePanel?.Apertures;
                    if (sourceApertures != null)
                    {
                        sourceApertureCount += sourceApertures.Count;
                    }
                }
            }

            Dictionary<Guid, List<global::OpenStudio.Surface>> surfacesByPanel = new Dictionary<Guid, List<global::OpenStudio.Surface>>();
            Dictionary<Guid, Panel> panelByGuid = new Dictionary<Guid, Panel>();
            Dictionary<Guid, List<global::OpenStudio.SubSurface>> subSurfacesByAperture = new Dictionary<Guid, List<global::OpenStudio.SubSurface>>();
            HashSet<Guid> spacesWithSurfaces = new HashSet<Guid>();

            List<Space> spaces = adjacencyCluster.GetSpaces();
            context.Statistics.SourceObjects = (spaces?.Count ?? 0) + (sourcePanels?.Count ?? 0) + sourceApertureCount;
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
                        double elevation;
                        try
                        {
                            elevation = space.MinElevation(adjacencyCluster);
                        }
                        catch (System.Exception)
                        {
                            elevation = double.NaN;
                        }

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

                    InternalCondition internalCondition = space.InternalCondition;
                    if (internalCondition != null)
                    {
                        global::OpenStudio.SpaceType spaceType = internalCondition.ToOpenStudio(space, context);
                        if (spaceType != null)
                        {
                            openStudioSpace.setSpaceType(spaceType);
                        }
                    }

                    if (space.TryGetValue(SpaceParameter.OutsideSupplyAirFlow, out double outsideSupplyAirFlow) && !double.IsNaN(outsideSupplyAirFlow) && outsideSupplyAirFlow > 0)
                    {
                        global::OpenStudio.DesignSpecificationOutdoorAir designSpecificationOutdoorAir = new global::OpenStudio.DesignSpecificationOutdoorAir(context.Target);
                        designSpecificationOutdoorAir.setName(Core.OpenStudio.Query.OpenStudioName("DesignSpecificationOutdoorAir", space.Name, space.Guid));
                        designSpecificationOutdoorAir.setOutdoorAirFlowRate(outsideSupplyAirFlow);
                        openStudioSpace.setDesignSpecificationOutdoorAir(designSpecificationOutdoorAir);
                    }

                    int index = adjacencyCluster.GetIndex(space);
                    List<IPanel> panels;
                    try
                    {
                        panels = adjacencyCluster.UpdateNormals(space, false, true, false, Core.Tolerance.MacroDistance, options.DistanceTolerance);
                    }
                    catch (System.Exception exception)
                    {
                        context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryInvalidBoundary, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("The space shell could not be computed ({0}); no surfaces were created for the space", exception.GetType().Name), space);
                        context.RegisterSkip();
                        continue;
                    }

                    if (panels == null || panels.Count == 0)
                    {
                        context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.AdjacencyMissingSurface, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "Space has no related panels; no surfaces were created", space);
                        context.RegisterSkip();
                        continue;
                    }

                    foreach (IPanel iPanel in panels)
                    {
                        Panel panel = iPanel as Panel;
                        if (panel == null)
                        {
                            context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryInvalidBoundary, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Unsupported panel kind {0}; skipped", iPanel?.GetType()?.Name), space);
                            context.RegisterSkip();
                            continue;
                        }

                        if (panel.PanelType == PanelType.Shade)
                        {
                            continue;
                        }

                        global::OpenStudio.Surface surface;
                        try
                        {
                            surface = panel.ToOpenStudio(openStudioSpace, index, context, subSurfacesByAperture);
                        }
                        catch (System.Exception exception)
                        {
                            context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryInvalidBoundary, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Panel geometry could not be converted ({0}); the panel was skipped, never repaired", exception.GetType().Name), panel);
                            context.RegisterSkip();
                            continue;
                        }

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
                        spacesWithSurfaces.Add(space.Guid);
                    }
                }
            }

            // No silent drops: any panel related to a space that never became a surface
            // (excluded from the computed space shell — degenerate or disconnected geometry,
            // or rejected by validation) is reported explicitly.
            if (spaces != null)
            {
                HashSet<Guid> reportedPanels = new HashSet<Guid>();
                foreach (Space space in spaces)
                {
                    if (space == null)
                    {
                        continue;
                    }

                    List<Panel> relatedPanels = adjacencyCluster.GetPanels(space);
                    if (relatedPanels == null)
                    {
                        continue;
                    }

                    foreach (Panel relatedPanel in relatedPanels)
                    {
                        if (relatedPanel == null || relatedPanel.PanelType == PanelType.Shade)
                        {
                            continue;
                        }

                        if (!surfacesByPanel.ContainsKey(relatedPanel.Guid) && reportedPanels.Add(relatedPanel.Guid))
                        {
                            context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryInvalidBoundary, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "Panel related to a space produced no surface (excluded from the space shell — degenerate or disconnected geometry); it was skipped, never repaired", relatedPanel);
                            context.RegisterSkip();
                        }
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

            // Pass D: constructions — forward (outside-first, EnergyPlus order) for every
            // single-sided surface and the primary side of internal pairs; reverse for the
            // paired secondary side; a shared air-boundary construction for Air panels.
            global::OpenStudio.ConstructionAirBoundary constructionAirBoundary = null;
            foreach (KeyValuePair<Guid, List<global::OpenStudio.Surface>> keyValuePair in surfacesByPanel)
            {
                Panel panel = panelByGuid[keyValuePair.Key];
                List<global::OpenStudio.Surface> surfaces = keyValuePair.Value;

                if (panel.PanelType == PanelType.Air)
                {
                    if (constructionAirBoundary == null)
                    {
                        constructionAirBoundary = new global::OpenStudio.ConstructionAirBoundary(context.Target);
                        constructionAirBoundary.setName("SAM_Construction_AirBoundary");
                    }

                    foreach (global::OpenStudio.Surface surface in surfaces)
                    {
                        surface.setConstruction(constructionAirBoundary);
                    }

                    continue;
                }

                Construction construction = panel.Construction;
                if (construction == null)
                {
                    context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionMissingLayer, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "Panel has no construction", panel);
                    continue;
                }

                global::OpenStudio.Construction forwardConstruction = construction.ToOpenStudio(true, context);
                if (forwardConstruction == null)
                {
                    continue;
                }

                surfaces[0].setConstruction(forwardConstruction);

                if (surfaces.Count >= 2)
                {
                    global::OpenStudio.Construction reverseConstruction = construction.ToOpenStudio(false, context);
                    if (reverseConstruction != null)
                    {
                        surfaces[1].setConstruction(reverseConstruction);
                    }
                }
            }

            Dictionary<Guid, Aperture> apertureByGuid = new Dictionary<Guid, Aperture>();
            foreach (Panel panel in panelByGuid.Values)
            {
                List<Aperture> apertures = panel.Apertures;
                if (apertures == null)
                {
                    continue;
                }

                foreach (Aperture aperture in apertures)
                {
                    if (aperture != null && !apertureByGuid.ContainsKey(aperture.Guid))
                    {
                        apertureByGuid[aperture.Guid] = aperture;
                    }
                }
            }

            foreach (KeyValuePair<Guid, List<global::OpenStudio.SubSurface>> keyValuePair in subSurfacesByAperture)
            {
                Aperture aperture;
                if (!apertureByGuid.TryGetValue(keyValuePair.Key, out aperture))
                {
                    continue;
                }

                ApertureConstruction apertureConstruction = aperture.ApertureConstruction;
                if (apertureConstruction == null)
                {
                    continue;
                }

                List<global::OpenStudio.SubSurface> subSurfaces = keyValuePair.Value;
                global::OpenStudio.Construction forwardConstruction = apertureConstruction.ToOpenStudio(true, context);
                if (forwardConstruction == null)
                {
                    continue;
                }

                subSurfaces[0].setConstruction(forwardConstruction);

                if (subSurfaces.Count >= 2)
                {
                    global::OpenStudio.Construction reverseConstruction = apertureConstruction.ToOpenStudio(false, context);
                    if (reverseConstruction != null)
                    {
                        subSurfaces[1].setConstruction(reverseConstruction);
                    }
                }
            }

            if (options.IncludeShading)
            {
                context.ToOpenStudio_ShadingSurfaces();
            }

            // Pass E: thermostats and Ideal Loads for conditioned zones only (central
            // Query.IsConditioned decision). Unconditioned/external spaces keep geometry
            // and internal gains but receive no thermostat and no Ideal Loads system.
            if (spaces != null)
            {
                foreach (Space space in spaces)
                {
                    if (space == null || !space.IsConditioned())
                    {
                        continue;
                    }

                    if (!spacesWithSurfaces.Contains(space.Guid))
                    {
                        // A conditioned zone without surfaces cannot be simulated (EnergyPlus
                        // fatals on a conditioned zone with no envelope) — reject it explicitly
                        // instead of attaching a thermostat and Ideal Loads to an empty zone.
                        context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.HvacMissingSetpoints, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "Conditioned space has no valid surfaces; thermostat and Ideal Loads were not assigned", space);
                        context.RegisterSkip();
                        continue;
                    }

                    if (!context.TryGetModelObject(space.Guid, out global::OpenStudio.Space openStudioSpace))
                    {
                        continue;
                    }

                    global::OpenStudio.OptionalThermalZone optionalThermalZone = openStudioSpace.thermalZone();
                    if (optionalThermalZone == null || optionalThermalZone.isNull())
                    {
                        context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.HvacMissingSetpoints, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "Conditioned space has no thermal zone", space);
                        continue;
                    }

                    global::OpenStudio.ThermalZone thermalZone = optionalThermalZone.get();

                    global::OpenStudio.ThermostatSetpointDualSetpoint thermostat = space.ToOpenStudio_Thermostat(thermalZone, context);
                    space.ToOpenStudio_Humidistat(thermalZone, context);
                    if (thermostat != null && options.AssignIdealLoads)
                    {
                        thermalZone.ToOpenStudio_IdealLoads(space, context);
                    }
                }
            }

            return context;
        }
    }
}
