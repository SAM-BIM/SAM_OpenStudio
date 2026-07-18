// SPDX-License-Identifier: LGPL-3.0-only

namespace SAM.Analytical.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Maps a SAM panel to the OpenStudio surface type ("Wall", "Floor" or "RoofCeiling").
        /// Mirrors SAM_LadybugTools Face.cs: a floor-group panel whose (space-oriented) normal
        /// points upward acts as the ceiling of the space below and becomes "RoofCeiling".
        /// Panels outside the Wall/Floor/Roof groups (for example Air) are classified from the
        /// panel normal.
        /// </summary>
        /// <param name="panel">SAM panel with its normal oriented out of the owning space.</param>
        /// <returns>"Wall", "Floor" or "RoofCeiling"; null when the panel is null.</returns>
        public static string SurfaceType(this Panel panel)
        {
            if (panel == null)
            {
                return null;
            }

            PanelType panelType = panel.PanelType;
            PanelGroup panelGroup = panelType.PanelGroup();

            if (panelGroup == PanelGroup.Floor && Analytical.Query.PanelType(panel.Normal) == PanelType.Roof)
            {
                return "RoofCeiling";
            }

            switch (panelGroup)
            {
                case PanelGroup.Wall:
                    return "Wall";

                case PanelGroup.Floor:
                    return "Floor";

                case PanelGroup.Roof:
                    return "RoofCeiling";
            }

            switch (Analytical.Query.PanelType(panel.Normal))
            {
                case PanelType.Roof:
                    return "RoofCeiling";

                case PanelType.Floor:
                    return "Floor";

                default:
                    return "Wall";
            }
        }
    }
}
