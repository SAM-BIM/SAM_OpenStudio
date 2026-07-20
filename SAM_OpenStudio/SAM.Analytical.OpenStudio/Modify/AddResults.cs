using System.Collections.Generic;
using System.Data.SQLite;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Modify
    {
        /// <summary>
        /// Reads the EnergyPlus SQLite output and attaches simulation results to the matching
        /// SAM spaces and panels of the given AdjacencyCluster. Two space-result families are
        /// read: the annual Ideal Loads family through the C5 engine-neutral reader
        /// (OpenStudioSimulationRunner -> OpenStudioSimulationResultSet -> per-LoadType
        /// SpaceSimulationResults with peak load [W], peak hour-of-year and unmet hours), and
        /// the design-day ZoneSizes family (design loads) when sizing periods ran. Surface
        /// results keep one SurfaceSimulationResult per engine surface (identity preserved -
        /// an internal panel represented by two engine surfaces receives two results related
        /// to the same panel; values are never summed across surfaces). EnergyPlus/OpenStudio
        /// names are resolved back to SAM objects by their deterministic Guid suffix
        /// (SAM_<type>_<name>_<guid8>), never by display name alone; a zone or
        /// surface that matches nothing is reported. A genuine zero value stays a valid
        /// result - only a zone absent from the SQL counts as missing and is diagnosed.
        /// Rerunning never duplicates: an identical result (type, name, reference, load type)
        /// already present from the same source is skipped, its relation ensured.
        /// </summary>
        /// <param name="adjacencyCluster">Cluster receiving the results (results and relations are added in place).</param>
        /// <param name="path">EnergyPlus SQLite output path.</param>
        /// <param name="diagnostics">Structured diagnostics: unmatched SQL names, spaces/panels without results.</param>
        /// <returns>All results added (or already present), or null on invalid input.</returns>
        public static List<Core.Result> AddResults(this AdjacencyCluster adjacencyCluster, string path, out List<string> diagnostics)
        {
            diagnostics = new List<string>();

            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path) || adjacencyCluster == null)
            {
                return null;
            }

            List<SpaceSimulationResult> spaceSimulationResults = new List<SpaceSimulationResult>();

            // Annual family (C5 engine-neutral reader - the one authoritative SQL extraction).
            OpenStudioLoadSummary openStudioLoadSummary = OpenStudioSimulationRunner.ExtractLoads(path, out string failureDetail);
            if (openStudioLoadSummary == null)
            {
                diagnostics.Add(string.Format("Annual Ideal Loads could not be read ({0}); only design-day results (when present) are attached", failureDetail));
            }
            else
            {
                OpenStudioSimulationResultSet openStudioSimulationResultSet = OpenStudioSimulationRunner.ExtractResultSet(path, openStudioLoadSummary, false, 0, 0, 0, 0);
                List<SpaceSimulationResult> annualResults = openStudioSimulationResultSet?.ToSAM_SpaceSimulationResults(adjacencyCluster.GetSpaces());
                if (annualResults != null)
                {
                    spaceSimulationResults.AddRange(annualResults);
                }

                // Never silently merged: a reported zone that matches no SAM space is named.
                List<Space> spaces_ForZoneKeys = adjacencyCluster.GetSpaces();
                HashSet<string> zoneKeys = new HashSet<string>(openStudioLoadSummary.ZoneHeating.Keys, System.StringComparer.OrdinalIgnoreCase);
                zoneKeys.UnionWith(openStudioLoadSummary.ZoneCooling.Keys);
                foreach (string zoneKey in zoneKeys)
                {
                    if (LookupSpace(zoneKey, null, spaces_ForZoneKeys) == null)
                    {
                        diagnostics.Add(string.Format("SQL zone '{0}' matched no SAM space; its annual results were not attached", zoneKey));
                    }
                }
            }

            // Design-day family (ZoneSizes design loads) + surface results.
            List<SurfaceSimulationResult> surfaceSimulationResults = null;
            using (SQLiteConnection sQLiteConnection = Core.SQLite.Create.SQLiteConnection(path))
            {
                List<SpaceSimulationResult> designDayResults = Create.SpaceSimulationResults(sQLiteConnection);
                if (designDayResults != null)
                {
                    designDayResults.RemoveAll(x => !x.TryGetValue(Analytical.SpaceSimulationResultParameter.LoadType, out string loadType) || string.IsNullOrWhiteSpace(loadType));
                    spaceSimulationResults.AddRange(designDayResults);
                }

                surfaceSimulationResults = Create.SurfaceSimulationResults(sQLiteConnection, designDayResults);
            }

            List<Core.Result> result = new List<Core.Result>();

            List<Space> spaces = adjacencyCluster.GetSpaces();
            List<Panel> panels = adjacencyCluster.GetPanels();

            string source = Query.Source();
            HashSet<string> existingIdentities = ExistingResultIdentities(adjacencyCluster, source);

            HashSet<Space> matchedSpaces = new HashSet<Space>();
            if (spaceSimulationResults != null)
            {
                foreach (SpaceSimulationResult spaceSimulationResult in spaceSimulationResults)
                {
                    if (spaceSimulationResult == null)
                    {
                        continue;
                    }

                    Space space = LookupSpace(spaceSimulationResult, spaces);
                    if (space == null)
                    {
                        diagnostics.Add(string.Format("SQL zone result '{0}' matched no SAM space; left unattached", spaceSimulationResult.Name));
                    }
                    else
                    {
                        matchedSpaces.Add(space);
                    }

                    AddResult(adjacencyCluster, spaceSimulationResult, space, existingIdentities);
                    result.Add(spaceSimulationResult);
                }
            }

            if (spaces != null)
            {
                foreach (Space space in spaces)
                {
                    if (space != null && !matchedSpaces.Contains(space))
                    {
                        diagnostics.Add(string.Format("No simulation result found for space '{0}' ({1}) - the zone is missing from the SQL output or not conditioned (a reported zero would have been kept as a result)", space.Name, Query.GuidSuffix(space)));
                    }
                }
            }

            HashSet<Panel> matchedPanels = new HashSet<Panel>();
            if (surfaceSimulationResults != null)
            {
                foreach (SurfaceSimulationResult surfaceSimulationResult in surfaceSimulationResults)
                {
                    if (surfaceSimulationResult == null)
                    {
                        continue;
                    }

                    Panel panel = LookupPanel(surfaceSimulationResult, panels);
                    if (panel == null)
                    {
                        diagnostics.Add(string.Format("SQL surface result '{0}' matched no SAM panel; left unattached", surfaceSimulationResult.Name));
                    }
                    else
                    {
                        matchedPanels.Add(panel);
                    }

                    AddResult(adjacencyCluster, surfaceSimulationResult, panel, existingIdentities);
                    result.Add(surfaceSimulationResult);

                    if (surfaceSimulationResult.TryGetValue(SurfaceSimulationResultParameter.ZoneName, out string zoneName))
                    {
                        Space space = LookupSpace(zoneName, null, spaces);
                        if (space != null)
                        {
                            adjacencyCluster.AddRelation(space, surfaceSimulationResult);
                            matchedSpaces.Add(space);
                        }
                    }
                }
            }

            if (panels != null)
            {
                foreach (Panel panel in panels)
                {
                    if (panel != null && panel.PanelType != PanelType.Shade && !matchedPanels.Contains(panel))
                    {
                        diagnostics.Add(string.Format("No surface result found for panel '{0}' ({1}) - the surface is missing from the SQL output (a reported zero would have been kept as a result)", panel.Name, Query.GuidSuffix(panel)));
                    }
                }
            }

            return result;
        }

        public static List<Core.Result> AddResults(this AdjacencyCluster adjacencyCluster, string path)
        {
            return AddResults(adjacencyCluster, path, out _);
        }

        /// <summary>Adds the result unless an identical one (type, name, reference, load type) already exists from the same source; the space/panel relation is ensured either way.</summary>
        private static void AddResult(AdjacencyCluster adjacencyCluster, Core.Result result, Core.IJSAMObject relatedObject, HashSet<string> existingIdentities)
        {
            if (existingIdentities.Add(ResultIdentity(result)))
            {
                adjacencyCluster.AddObject(result);
            }

            if (relatedObject != null)
            {
                adjacencyCluster.AddRelation(relatedObject, result);
            }
        }

        private static string ResultIdentity(Core.Result result)
        {
            string loadType = null;
            if (result is SpaceSimulationResult spaceSimulationResult)
            {
                spaceSimulationResult.TryGetValue(Analytical.SpaceSimulationResultParameter.LoadType, out loadType);
            }
            else if (result is SurfaceSimulationResult surfaceSimulationResult)
            {
                surfaceSimulationResult.TryGetValue(Analytical.SurfaceSimulationResultParameter.LoadType, out loadType);
            }

            return string.Format("{0}|{1}|{2}|{3}", result.GetType().Name, result.Name, result.Reference, loadType);
        }

        private static HashSet<string> ExistingResultIdentities(AdjacencyCluster adjacencyCluster, string source)
        {
            HashSet<string> result = new HashSet<string>();

            List<SpaceSimulationResult> spaceSimulationResults = adjacencyCluster.GetResults<SpaceSimulationResult>(source);
            if (spaceSimulationResults != null)
            {
                foreach (SpaceSimulationResult spaceSimulationResult in spaceSimulationResults)
                {
                    if (spaceSimulationResult != null)
                    {
                        result.Add(ResultIdentity(spaceSimulationResult));
                    }
                }
            }

            List<SurfaceSimulationResult> surfaceSimulationResults = adjacencyCluster.GetResults<SurfaceSimulationResult>(source);
            if (surfaceSimulationResults != null)
            {
                foreach (SurfaceSimulationResult surfaceSimulationResult in surfaceSimulationResults)
                {
                    if (surfaceSimulationResult != null)
                    {
                        result.Add(ResultIdentity(surfaceSimulationResult));
                    }
                }
            }

            return result;
        }

        private static Space LookupSpace(SpaceSimulationResult spaceSimulationResult, List<Space> spaces)
        {
            if (spaceSimulationResult == null)
            {
                return null;
            }

            // The C5 annual family references the space Guid directly.
            if (System.Guid.TryParse(spaceSimulationResult.Reference, out System.Guid guid) && guid != System.Guid.Empty)
            {
                Space space = spaces?.Find(x => x != null && x.Guid == guid);
                if (space != null)
                {
                    return space;
                }
            }

            return LookupSpace(spaceSimulationResult.Name, spaceSimulationResult.Reference, spaces);
        }

        /// <summary>Resolves an EnergyPlus zone name / SQL zone index to a SAM space: Guid suffix -> full-Guid reference -> sanitized name (never display-name-only when a Guid is available).</summary>
        private static Space LookupSpace(string zoneName, string zoneIndex, List<Space> spaces)
        {
            if (spaces == null || spaces.Count == 0)
            {
                return null;
            }

            if (Query.TryGetGuidSuffix(zoneName, out string suffix))
            {
                Space space = spaces.Find(x => x != null && Query.GuidSuffix(x) == suffix);
                if (space != null)
                {
                    return space;
                }
            }

            if (System.Guid.TryParse(zoneName, out System.Guid guid))
            {
                Space space = spaces.Find(x => x != null && x.Guid == guid);
                if (space != null)
                {
                    return space;
                }
            }

            if (!string.IsNullOrWhiteSpace(zoneName))
            {
                string sanitized = Core.OpenStudio.Query.SanitizeName(zoneName);
                Space space = spaces.Find(x => x != null && string.Equals(Core.OpenStudio.Query.SanitizeName(x.Name), sanitized, System.StringComparison.OrdinalIgnoreCase));
                if (space != null)
                {
                    return space;
                }
            }

            return null;
        }

        private static Panel LookupPanel(SurfaceSimulationResult surfaceSimulationResult, List<Panel> panels)
        {
            if (surfaceSimulationResult == null || panels == null || panels.Count == 0)
            {
                return null;
            }

            string surfaceName = surfaceSimulationResult.Name;

            if (Query.TryGetGuidSuffix(surfaceName, out string suffix))
            {
                Panel panel = panels.Find(x => x != null && Query.GuidSuffix(x) == suffix);
                if (panel != null)
                {
                    return panel;
                }
            }

            // Legacy naming: "<name>__<panel Guid>".
            if (!string.IsNullOrWhiteSpace(surfaceName))
            {
                string[] values = surfaceName.Split(new string[] { "__" }, System.StringSplitOptions.None);
                if (values.Length > 1 && System.Guid.TryParse(values[values.Length - 1], out System.Guid guid))
                {
                    Panel panel = panels.Find(x => x != null && x.Guid == guid);
                    if (panel != null)
                    {
                        return panel;
                    }
                }

                string sanitized = Core.OpenStudio.Query.SanitizeName(surfaceName);
                Panel panel_Name = panels.Find(x => x != null && string.Equals(Core.OpenStudio.Query.SanitizeName(x.Name), sanitized, System.StringComparison.OrdinalIgnoreCase));
                if (panel_Name != null)
                {
                    return panel_Name;
                }
            }

            return null;
        }

        public static List<Core.Result> AddResults(this BuildingModel buildingModel, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path) || buildingModel == null)
            {
                return null;
            }

            List<SpaceSimulationResult> spaceSimulationResults = Create.SpaceSimulationResults(path);
            if (spaceSimulationResults == null)
            {
                return null;
            }

            List<Space> spaces = buildingModel.GetSpaces();

            List<Core.Result> result = new List<Core.Result>();
            foreach (SpaceSimulationResult spaceSimulationResult in spaceSimulationResults)
            {
                Space space = LookupSpace(spaceSimulationResult, spaces);

                buildingModel.Add(spaceSimulationResult, space);

                result.Add(spaceSimulationResult);
            }

            return result;
        }
    }
}
