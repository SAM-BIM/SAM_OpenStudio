// SPDX-License-Identifier: LGPL-3.0-only

namespace SAM.Analytical.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Maps a SAM PanelType to the OpenStudio surface outside boundary condition, mirroring
        /// the established SAM_LadybugTools mapping (BoundaryCondition.cs): underground and
        /// ground-bearing panels → "Ground"; external panels → "Outdoors"; Undefined → "Adiabatic";
        /// internal panel types → "Surface" (resolved by explicit adjacency pairing, never by
        /// geometric matching). Returns null for unmapped panel types — callers must raise a
        /// diagnostic and choose an explicit fallback.
        /// </summary>
        /// <param name="panelType">SAM panel type.</param>
        /// <returns>"Ground", "Outdoors", "Adiabatic", "Surface" or null.</returns>
        public static string OutsideBoundaryCondition(this PanelType panelType)
        {
            switch (panelType)
            {
                case PanelType.UndergroundWall:
                case PanelType.SlabOnGrade:
                case PanelType.UndergroundSlab:
                case PanelType.UndergroundCeiling:
                case PanelType.Floor:
                    return "Ground";

                case PanelType.Wall:
                case PanelType.CurtainWall:
                case PanelType.WallExternal:
                case PanelType.Roof:
                case PanelType.SolarPanel:
                case PanelType.Shade:
                case PanelType.FloorExposed:
                case PanelType.FloorRaised:
                    return "Outdoors";

                case PanelType.Undefined:
                    return "Adiabatic";

                case PanelType.FloorInternal:
                case PanelType.WallInternal:
                case PanelType.Ceiling:
                case PanelType.Air:
                    return "Surface";
            }

            return null;
        }
    }
}
