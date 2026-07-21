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
        /// <param name="progress">Optional stage progress sink.</param>
        /// <param name="cancellationToken">Cancellation; kills the CLI/EnergyPlus process tree when triggered.</param>
        /// <returns>Conversion result including RunResult and Loads; null when input is null.</returns>
        public static OpenStudioConversionResult ToOpenStudio(this AnalyticalModel analyticalModel, string epwPath, string outputDirectory, Core.OpenStudio.OpenStudioConversionOptions openStudioConversionOptions = null, Core.OpenStudio.OpenStudioRunOptions openStudioRunOptions = null, bool run = true, System.IProgress<Core.OpenStudio.OpenStudioSimulationProgress> progress = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
        {
            OpenStudioConversionContext context = ToOpenStudio_ContextWithSite(analyticalModel, epwPath, openStudioConversionOptions, openStudioRunOptions, run);
            if (context == null)
            {
                return null;
            }

            return OpenStudioSimulationRunner.Run(context, context.EpwPath ?? epwPath, outputDirectory, openStudioRunOptions, run, progress, cancellationToken);
        }

        /// <summary>
        /// Asynchronous full pipeline (C6): conversion executes inline (fast CPU work), the
        /// save → OSW → CLI → parse pipeline on a worker thread; cancellation terminates the
        /// CLI/EnergyPlus process tree. Progress stages are reported through
        /// <paramref name="progress"/>.
        /// </summary>
        public static System.Threading.Tasks.Task<OpenStudioConversionResult> ToOpenStudioAsync(this AnalyticalModel analyticalModel, string epwPath, string outputDirectory, Core.OpenStudio.OpenStudioConversionOptions openStudioConversionOptions = null, Core.OpenStudio.OpenStudioRunOptions openStudioRunOptions = null, bool run = true, System.IProgress<Core.OpenStudio.OpenStudioSimulationProgress> progress = null, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
        {
            OpenStudioConversionContext context = ToOpenStudio_ContextWithSite(analyticalModel, epwPath, openStudioConversionOptions, openStudioRunOptions, run);
            if (context == null)
            {
                return System.Threading.Tasks.Task.FromResult<OpenStudioConversionResult>(null);
            }

            return OpenStudioSimulationRunner.RunAsync(context, context.EpwPath ?? epwPath, outputDirectory, openStudioRunOptions, run, progress, cancellationToken);
        }

        private static OpenStudioConversionContext ToOpenStudio_ContextWithSite(AnalyticalModel analyticalModel, string epwPath, Core.OpenStudio.OpenStudioConversionOptions openStudioConversionOptions, Core.OpenStudio.OpenStudioRunOptions openStudioRunOptions, bool run)
        {
            // Annual weather source precedence (documented contract):
            //   1. the explicit EPW path, when supplied and valid;
            //   2. WeatherData embedded in the AnalyticalModel, exported through the existing
            //      SAM.Weather ToEPW API when it carries hourly weather years;
            //   3. otherwise a blocking diagnostic when an annual run was requested (a
            //      conversion-only run stays valid with a warning).
            // The source is resolved before the context is built: the run-calendar offset must
            // be known before any profile is converted.
            string epwPath_Effective = !string.IsNullOrWhiteSpace(epwPath) && System.IO.File.Exists(epwPath) ? epwPath : null;
            bool embeddedWeatherSource = false;
            bool invalidExplicitEpwPath = false;
            if (epwPath_Effective == null)
            {
                invalidExplicitEpwPath = !string.IsNullOrWhiteSpace(epwPath);
                epwPath_Effective = analyticalModel.EmbeddedAnnualWeatherPath();
                embeddedWeatherSource = epwPath_Effective != null;
            }

            OpenStudioConversionContext context = ToOpenStudio_Context(analyticalModel, openStudioConversionOptions, FirstDayOfWeekOffset(epwPath_Effective, openStudioConversionOptions));
            if (context == null)
            {
                return null;
            }

            if (invalidExplicitEpwPath)
            {
                context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("The explicit EPW path is not usable: {0}; falling back to the AnalyticalModel WeatherData", epwPath));
            }

            context.EpwPath = epwPath_Effective;

            context.ToOpenStudio_Weather(epwPath_Effective, run, embeddedWeatherSource);

            // Design-day source precedence (documented contract):
            //   1. an explicit DDY path (run option takes precedence over the conversion option);
            //   2. HeatingDesignDays/CoolingDesignDays embedded in the AnalyticalModel;
            //   3. no design days. Explicit DDY and embedded design days are never merged.
            string ddyPath = !string.IsNullOrWhiteSpace(openStudioRunOptions?.DdyPath) ? openStudioRunOptions.DdyPath : openStudioConversionOptions?.DdyPath;
            if (!string.IsNullOrWhiteSpace(ddyPath) && System.IO.File.Exists(ddyPath))
            {
                context.ToOpenStudio_DesignDays(ddyPath);
                context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format("Design-day source: explicit DDY ({0}); embedded AnalyticalModel design days are not merged", System.IO.Path.GetFileName(ddyPath)));
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(ddyPath))
                {
                    context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("The explicit DDY path is not usable: {0}; falling back to the AnalyticalModel design days", ddyPath));
                }

                Core.SAMCollection<DesignDay> heatingDesignDays = null;
                context.Source?.TryGetValue(AnalyticalModelParameter.HeatingDesignDays, out heatingDesignDays);

                Core.SAMCollection<DesignDay> coolingDesignDays = null;
                context.Source?.TryGetValue(AnalyticalModelParameter.CoolingDesignDays, out coolingDesignDays);

                int embeddedCount = context.ToOpenStudio_DesignDays(heatingDesignDays, coolingDesignDays);
                if (embeddedCount > 0)
                {
                    context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format("Design-day source: AnalyticalModel heating/cooling design days ({0} imported)", embeddedCount));
                }
                else
                {
                    context.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "No design-day source supplied; sizing-period runs stay disabled unless OpenStudioConversionOptions.RunSizingPeriods overrides");
                }
            }

            context.ToOpenStudio_SimulationSettings();
            return context;
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
                string boundaryCondition = panel.OutsideBoundaryCondition();

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

                    if (boundaryCondition == "Outdoors")
                    {
                        EmitNonConvexCastingDiagnostic(panel, surfaces[0].nameString(), context);
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
                        ApplyAirBoundaryAirExchange(constructionAirBoundary, context);
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
                global::OpenStudio.Construction forwardConstruction = apertureConstruction.ToOpenStudio(true, context, aperture);
                if (forwardConstruction == null)
                {
                    continue;
                }

                subSurfaces[0].setConstruction(forwardConstruction);

                if (subSurfaces.Count >= 2)
                {
                    global::OpenStudio.Construction reverseConstruction = apertureConstruction.ToOpenStudio(false, context, aperture);
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

        /// <summary>
        /// Advisory diagnostic for a non-convex shadow-casting surface. With PolygonClipping
        /// resolved it is a Warning: EnergyPlus raises a severe DetermineShadowingCombinations
        /// error and shadowing may be inaccurate. With PixelCounting resolved it is an
        /// Information: the method itself has no concavity limitation, so wherever
        /// PixelCounting actually runs the surface is fine — the advisory is a precaution,
        /// not GPU detection (the converter never queries for a GPU). Only on a machine
        /// without a GPU (or GPU emulation) does EnergyPlus warn in eplusout.err, revert to
        /// PolygonClipping, and flag the surface anyway. Best-effort: a geometry-kernel
        /// failure skips the advisory, never the surface.
        /// </summary>
        private static void EmitNonConvexCastingDiagnostic(Panel panel, string openStudioObjectName, OpenStudioConversionContext openStudioConversionContext)
        {
            Geometry.Spatial.Face3D face3D = panel?.GetFace3D();
            Geometry.Spatial.ISegmentable3D segmentable3D = face3D?.GetExternalEdge3D() as Geometry.Spatial.ISegmentable3D;
            List<Geometry.Spatial.Point3D> point3Ds = segmentable3D?.GetPoints();
            if (point3Ds == null)
            {
                return;
            }

            // Check the polygon EnergyPlus actually sees: the converter's cleaned boundary,
            // with near-collinear vertices ignored by the convexity test — a raw CAD shell
            // vertex stream would false-positive on every collinear mid-edge point.
            point3Ds = Geometry.OpenStudio.Query.CleanVertices(point3Ds, openStudioConversionContext.Options.DistanceTolerance, openStudioConversionContext.Options.AngleTolerance);
            if (point3Ds == null || point3Ds.Count < 3)
            {
                return;
            }

            bool convex;
            try
            {
                convex = Geometry.OpenStudio.Query.IsConvex(point3Ds, openStudioConversionContext.Options.AngleTolerance);
            }
            catch (System.Exception)
            {
                return;
            }

            if (convex)
            {
                return;
            }

            string method = openStudioConversionContext.Options?.ShadingCalculationMethod;
            if (string.Equals(method, "PixelCounting", System.StringComparison.OrdinalIgnoreCase))
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryNonConvexCasting, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "Non-convex surface casts shadows: PixelCounting (the resolved shading calculation method) has no concavity limitation, so wherever it runs this surface is fine — this advisory is a precaution, not a report that a GPU is missing (the converter does not detect GPUs). The caveat applies only on a machine with no GPU (or GPU emulation): there EnergyPlus warns in eplusout.err, reverts to PolygonClipping, and then reports this surface as a severe DetermineShadowingCombinations error. To be safe on such machines, split the panel into convex parts (e.g. an L-shape into rectangles)", panel, openStudioObjectName);
            }
            else
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryNonConvexCasting, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "Non-convex surface casts shadows: with Shading Calculation Method 'PolygonClipping' EnergyPlus reports a severe DetermineShadowingCombinations error and shadowing may be inaccurate — split the panel into convex parts (e.g. an L-shape into rectangles) or use PixelCounting (needs a GPU or GPU emulation; without one EnergyPlus warns and reverts to PolygonClipping)", panel, openStudioObjectName);
            }
        }

        /// <summary>
        /// Applies the opt-in inter-zone air exchange to the shared air-boundary construction
        /// (coverage manifest: PanelType.Air — Approximated when enabled).
        /// <para>
        /// An air boundary always groups its two zones for solar, daylighting and radiant
        /// exchange; what the EnergyPlus <c>Air Exchange Method</c> field controls is whether AIR
        /// moves between them. The default <c>None</c> leaves the zones convectively uncoupled,
        /// which under-models a real opening — but SAM carries no per-panel airflow data, so a
        /// rate can only come from the caller
        /// (<see cref="Core.OpenStudio.OpenStudioConversionOptions.AirBoundaryAirChangesPerHour"/>)
        /// and is never inferred. Whichever branch applies is named in a diagnostic: an assumed
        /// mixing rate and a deliberately uncoupled boundary are both modelling decisions the
        /// reader must see.
        /// </para>
        /// </summary>
        private static void ApplyAirBoundaryAirExchange(global::OpenStudio.ConstructionAirBoundary constructionAirBoundary, OpenStudioConversionContext openStudioConversionContext)
        {
            double airChangesPerHour = openStudioConversionContext.Options.AirBoundaryAirChangesPerHour;

            if (double.IsNaN(airChangesPerHour))
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.InternalConditionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "Air panel converted with Air Exchange Method 'None': the zones share one radiant/solar enclosure but exchange no air. SAM carries no airflow data for the opening — set OpenStudioConversionOptions.AirBoundaryAirChangesPerHour to model mixing");
                return;
            }

            if (double.IsInfinity(airChangesPerHour) || airChangesPerHour < 0)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.InternalConditionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format(System.Globalization.CultureInfo.InvariantCulture, "AirBoundaryAirChangesPerHour {0} is not a valid rate (finite, ≥ 0); Air Exchange Method 'None' kept", airChangesPerHour));
                return;
            }

            if (!constructionAirBoundary.setAirExchangeMethod("SimpleMixing"))
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.InternalConditionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "OpenStudio rejected the SimpleMixing air exchange method; Air Exchange Method 'None' kept");
                return;
            }

            constructionAirBoundary.setSimpleMixingAirChangesPerHour(airChangesPerHour);
            openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.InternalConditionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format(System.Globalization.CultureInfo.InvariantCulture, "Air panel converted with Air Exchange Method 'SimpleMixing' at {0} ACH (caller-supplied assumption — SAM carries no airflow data for the opening; EnergyPlus applies the rate to the smaller zone's volume, on an always-on schedule)", airChangesPerHour));
        }
    }
}
