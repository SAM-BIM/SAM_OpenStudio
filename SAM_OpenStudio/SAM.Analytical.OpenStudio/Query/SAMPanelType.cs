// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Derives the SAM <see cref="PanelType"/> from an OpenStudio surface type and outside
        /// boundary condition, per docs/openstudio-to-sam-import-audit.md §4.3.
        /// <para>
        /// Both inputs are required: neither alone determines the SAM type. A "Wall" is
        /// <see cref="PanelType.WallExternal"/> outdoors, <see cref="PanelType.WallInternal"/>
        /// against another surface and <see cref="PanelType.UndergroundWall"/> against ground —
        /// the same surface type in three different SAM roles.
        /// </para>
        /// <para>
        /// This is the inverse of <see cref="OutsideBoundaryCondition(PanelType)"/> and
        /// <see cref="SurfaceType(Panel)"/> and must stay consistent with them: the round-trip
        /// tests assert that a SAM panel type survives export and re-import.
        /// </para>
        /// </summary>
        /// <param name="surfaceType">OpenStudio surface type ("Wall", "Floor", "RoofCeiling").</param>
        /// <param name="outsideBoundaryCondition">OpenStudio outside boundary condition.</param>
        /// <param name="adiabatic">True when the boundary condition is Adiabatic and the panel must carry <see cref="PanelParameter.Adiabatic"/>.</param>
        /// <param name="supported">False when the boundary condition has no SAM equivalent and a documented fallback was applied.</param>
        /// <returns>The SAM panel type; <see cref="PanelType.Undefined"/> when the surface type itself is unrecognised.</returns>
        public static PanelType SAMPanelType(string surfaceType, string outsideBoundaryCondition, out bool adiabatic, out bool supported)
        {
            adiabatic = false;
            supported = true;

            SurfaceGroup surfaceGroup = SurfaceGroup.Undefined;
            if (string.Equals(surfaceType, "Wall", StringComparison.OrdinalIgnoreCase))
            {
                surfaceGroup = SurfaceGroup.Wall;
            }
            else if (string.Equals(surfaceType, "Floor", StringComparison.OrdinalIgnoreCase))
            {
                surfaceGroup = SurfaceGroup.Floor;
            }
            else if (string.Equals(surfaceType, "RoofCeiling", StringComparison.OrdinalIgnoreCase))
            {
                surfaceGroup = SurfaceGroup.RoofCeiling;
            }

            if (surfaceGroup == SurfaceGroup.Undefined)
            {
                supported = false;
                return PanelType.Undefined;
            }

            BoundaryGroup boundaryGroup = SAMBoundaryGroup(outsideBoundaryCondition, out supported);

            switch (boundaryGroup)
            {
                case BoundaryGroup.Outdoors:
                    switch (surfaceGroup)
                    {
                        case SurfaceGroup.Wall:
                            return PanelType.WallExternal;

                        case SurfaceGroup.Floor:
                            return PanelType.FloorExposed;

                        case SurfaceGroup.RoofCeiling:
                            return PanelType.Roof;
                    }

                    break;

                case BoundaryGroup.Ground:
                    switch (surfaceGroup)
                    {
                        case SurfaceGroup.Wall:
                            return PanelType.UndergroundWall;

                        case SurfaceGroup.Floor:
                            return PanelType.SlabOnGrade;

                        case SurfaceGroup.RoofCeiling:
                            return PanelType.UndergroundCeiling;
                    }

                    break;

                case BoundaryGroup.Adiabatic:
                    // Adiabatic keeps the INTERNAL panel type and records adiabatic separately,
                    // exactly as the forward direction reads it back
                    // (Analytical.Query.Adiabatic → "Adiabatic" regardless of PanelType). Mapping
                    // it to a dedicated "adiabatic type" would lose the wall/floor/ceiling role.
                    adiabatic = true;
                    switch (surfaceGroup)
                    {
                        case SurfaceGroup.Wall:
                            return PanelType.WallInternal;

                        case SurfaceGroup.Floor:
                            return PanelType.FloorInternal;

                        case SurfaceGroup.RoofCeiling:
                            return PanelType.Ceiling;
                    }

                    break;

                case BoundaryGroup.Surface:
                    switch (surfaceGroup)
                    {
                        case SurfaceGroup.Wall:
                            return PanelType.WallInternal;

                        case SurfaceGroup.Floor:
                            return PanelType.FloorInternal;

                        case SurfaceGroup.RoofCeiling:
                            return PanelType.Ceiling;
                    }

                    break;
            }

            supported = false;
            return PanelType.Undefined;
        }

        /// <summary>
        /// Classifies an OpenStudio outside boundary condition string into the four groups SAM
        /// can represent. The unsupported "other side" conditions (OtherSideCoefficients,
        /// OtherSideConditionsModel) fall back to Adiabatic — the only safe choice, since SAM has
        /// no way to carry an externally specified surface temperature — and set
        /// <paramref name="supported"/> false so the caller reports SAM-OSI-BC-001.
        /// </summary>
        /// <param name="outsideBoundaryCondition">OpenStudio outside boundary condition.</param>
        /// <param name="supported">False when a documented fallback was applied.</param>
        internal static BoundaryGroup SAMBoundaryGroup(string outsideBoundaryCondition, out bool supported)
        {
            supported = true;

            if (string.IsNullOrWhiteSpace(outsideBoundaryCondition))
            {
                supported = false;
                return BoundaryGroup.Adiabatic;
            }

            if (string.Equals(outsideBoundaryCondition, "Outdoors", StringComparison.OrdinalIgnoreCase))
            {
                return BoundaryGroup.Outdoors;
            }

            if (string.Equals(outsideBoundaryCondition, "Surface", StringComparison.OrdinalIgnoreCase))
            {
                return BoundaryGroup.Surface;
            }

            if (string.Equals(outsideBoundaryCondition, "Adiabatic", StringComparison.OrdinalIgnoreCase))
            {
                return BoundaryGroup.Adiabatic;
            }

            // "Ground", "Foundation", "GroundFCfactorMethod", "GroundSlabPreprocessorAverage",
            // "GroundBasementPreprocessorAverageWall" and the rest of the preprocessor family all
            // mean "in contact with ground" for SAM's purposes.
            if (outsideBoundaryCondition.StartsWith("Ground", StringComparison.OrdinalIgnoreCase)
                || string.Equals(outsideBoundaryCondition, "Foundation", StringComparison.OrdinalIgnoreCase))
            {
                return BoundaryGroup.Ground;
            }

            supported = false;
            return BoundaryGroup.Adiabatic;
        }

        /// <summary>OpenStudio surface-type groups recognised by the importer.</summary>
        internal enum SurfaceGroup
        {
            /// <summary>Unrecognised surface type.</summary>
            Undefined,

            /// <summary>OpenStudio "Wall".</summary>
            Wall,

            /// <summary>OpenStudio "Floor".</summary>
            Floor,

            /// <summary>OpenStudio "RoofCeiling".</summary>
            RoofCeiling,
        }

        /// <summary>Outside-boundary-condition groups SAM can represent.</summary>
        internal enum BoundaryGroup
        {
            /// <summary>Exposed to the outdoor environment.</summary>
            Outdoors,

            /// <summary>In contact with ground (any of the Ground/Foundation family).</summary>
            Ground,

            /// <summary>No heat transfer across the boundary.</summary>
            Adiabatic,

            /// <summary>Paired with another surface (interzone).</summary>
            Surface,
        }
    }
}
