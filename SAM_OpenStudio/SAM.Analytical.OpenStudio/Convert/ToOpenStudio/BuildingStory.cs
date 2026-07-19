// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using System.Globalization;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Creates OpenStudio BuildingStories from the SAM panel minimum-elevation dictionary
        /// (the same level source SAM_LadybugTools uses). Stories are keyed and named by
        /// elevation, ordered ascending, with deterministic names. Spaces are assigned to the
        /// nearest story elevation by the caller.
        /// </summary>
        /// <param name="openStudioConversionContext">Conversion context with a non-null source model.</param>
        /// <returns>Elevation → BuildingStory, sorted ascending; empty when no panels exist.</returns>
        public static SortedList<double, global::OpenStudio.BuildingStory> ToOpenStudio_BuildingStories(this OpenStudioConversionContext openStudioConversionContext)
        {
            SortedList<double, global::OpenStudio.BuildingStory> result = new SortedList<double, global::OpenStudio.BuildingStory>();

            AdjacencyCluster adjacencyCluster = openStudioConversionContext?.Source?.AdjacencyCluster;
            if (adjacencyCluster == null)
            {
                return result;
            }

            List<Panel> panels = adjacencyCluster.GetPanels();
            if (panels == null || panels.Count == 0)
            {
                return result;
            }

            // Per-panel elevation with exception isolation: SAM's geometry kernel can throw on
            // pathological panels (e.g. degenerate slivers) — such panels are skipped here and
            // surfaced later by the no-silent-drop check, never allowed to crash the conversion.
            List<double> rawElevations = new List<double>();
            foreach (Panel panel in panels)
            {
                // Stories derive from floor-group panels only (matching SAM's
                // MinElevationDictionary filtering) — walls and roofs do not define levels.
                if (panel == null || panel.PanelType.PanelGroup() != PanelGroup.Floor)
                {
                    continue;
                }

                try
                {
                    double panelElevation = Analytical.Query.MinElevation(panel);
                    if (!double.IsNaN(panelElevation))
                    {
                        rawElevations.Add(panelElevation);
                    }
                }
                catch (System.Exception exception)
                {
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryInvalidBoundary, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Panel elevation could not be computed ({0}); panel ignored for story grouping", exception.GetType().Name), panel);
                }
            }

            if (rawElevations.Count == 0)
            {
                return result;
            }

            rawElevations.Sort();

            double elevationTolerance = openStudioConversionContext.Options.ElevationTolerance;
            List<double> elevations = new List<double>();
            foreach (double rawElevation in rawElevations)
            {
                if (elevations.Count == 0 || rawElevation - elevations[elevations.Count - 1] > elevationTolerance)
                {
                    elevations.Add(rawElevation);
                }
            }

            for (int i = 0; i < elevations.Count; i++)
            {
                global::OpenStudio.BuildingStory buildingStory = new global::OpenStudio.BuildingStory(openStudioConversionContext.Target);
                buildingStory.setName(string.Format(CultureInfo.InvariantCulture, "SAM_BuildingStory_{0:00}_{1:0.###}m", i, elevations[i]));
                buildingStory.setNominalZCoordinate(elevations[i]);
                result.Add(elevations[i], buildingStory);
            }

            return result;
        }
    }
}
