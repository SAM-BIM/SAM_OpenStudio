// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Maps an OpenStudio subsurface type to a SAM <see cref="ApertureType"/>, per
        /// docs/openstudio-to-sam-import-audit.md §4.4.
        /// <para>
        /// SAM has exactly two aperture types, so several OpenStudio types collapse onto one.
        /// Where the collapse loses behaviour — an operable window's openability, a tubular
        /// daylighting device's light path — <paramref name="approximated"/> is set so the caller
        /// reports SAM-OSI-APX-001 rather than presenting the mapping as exact.
        /// </para>
        /// <para>
        /// This is the counterpart of <see cref="SubSurfaceType(Aperture, Core.MaterialLibrary)"/>,
        /// which resolves Door versus GlassDoor from the pane material rather than from the SAM
        /// aperture type — so a GlassDoor correctly returns to <see cref="ApertureType.Door"/>
        /// here and the round trip is stable.
        /// </para>
        /// </summary>
        /// <param name="subSurfaceType">OpenStudio subsurface type.</param>
        /// <param name="approximated">True when the mapping discards behaviour SAM cannot carry.</param>
        /// <returns>The SAM aperture type; <see cref="ApertureType.Undefined"/> when unsupported.</returns>
        public static ApertureType SAMApertureType(string subSurfaceType, out bool approximated)
        {
            approximated = false;

            if (string.IsNullOrWhiteSpace(subSurfaceType))
            {
                return ApertureType.Undefined;
            }

            if (string.Equals(subSurfaceType, "FixedWindow", StringComparison.OrdinalIgnoreCase)
                || string.Equals(subSurfaceType, "Skylight", StringComparison.OrdinalIgnoreCase))
            {
                return ApertureType.Window;
            }

            if (string.Equals(subSurfaceType, "OperableWindow", StringComparison.OrdinalIgnoreCase))
            {
                // SAM has no openable-window flag; the opening behaviour is lost, the geometry
                // and construction are not.
                approximated = true;
                return ApertureType.Window;
            }

            if (string.Equals(subSurfaceType, "TubularDaylightDome", StringComparison.OrdinalIgnoreCase)
                || string.Equals(subSurfaceType, "TubularDaylightDiffuser", StringComparison.OrdinalIgnoreCase))
            {
                // Representable only as the glazed opening itself: SAM has no light pipe, so the
                // dome/diffuser pair imports as two independent windows.
                approximated = true;
                return ApertureType.Window;
            }

            if (string.Equals(subSurfaceType, "Door", StringComparison.OrdinalIgnoreCase)
                || string.Equals(subSurfaceType, "GlassDoor", StringComparison.OrdinalIgnoreCase))
            {
                return ApertureType.Door;
            }

            if (string.Equals(subSurfaceType, "OverheadDoor", StringComparison.OrdinalIgnoreCase))
            {
                approximated = true;
                return ApertureType.Door;
            }

            return ApertureType.Undefined;
        }
    }
}
