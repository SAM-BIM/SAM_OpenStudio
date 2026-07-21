// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.ComponentModel;
using SAM.Core.Attributes;

namespace SAM.Analytical.OpenStudio
{
    /// <summary>
    /// Provenance recorded on every SAM object produced by an OpenStudio → SAM import.
    /// <para>
    /// SAM object names are structural (a Panel's name comes from its Construction, for example)
    /// and cannot carry the OpenStudio name, while the OpenStudio handle has no SAM counterpart
    /// at all. Both are kept here so an imported model can always be traced back to the object it
    /// came from — for adjacency debugging, for diagnostics, and for a second import that has to
    /// recognise objects it produced earlier.
    /// </para>
    /// </summary>
    [AssociatedTypes(typeof(Panel), typeof(Aperture), typeof(Space), typeof(Construction), typeof(ApertureConstruction), typeof(InternalCondition), typeof(Profile), typeof(AnalyticalModel)), Description("OpenStudio Source Parameter")]
    public enum OpenStudioSourceParameter
    {
        /// <summary>Name of the OpenStudio object this SAM object was imported from.</summary>
        [ParameterProperties("OpenStudio Source Name", "Name of the OpenStudio object this SAM object was imported from"), ParameterValue(Core.ParameterType.String)] SourceName,

        /// <summary>OpenStudio handle (UUID) of the source object — unique where names are not.</summary>
        [ParameterProperties("OpenStudio Source Handle", "OpenStudio handle (UUID) of the source object"), ParameterValue(Core.ParameterType.String)] SourceHandle,

        /// <summary>OpenStudio IDD object type of the source object (for example "OS:Surface").</summary>
        [ParameterProperties("OpenStudio Source Type", "OpenStudio object type the SAM object was imported from"), ParameterValue(Core.ParameterType.String)] SourceType,

        /// <summary>Name of the OpenStudio ThermalZone the source space belonged to.</summary>
        [ParameterProperties("OpenStudio Thermal Zone Name", "Name of the OpenStudio ThermalZone the source space belonged to"), ParameterValue(Core.ParameterType.String)] ThermalZoneName,

        /// <summary>Shading surface group type ("Site", "Building" or "Space") for imported shade panels.</summary>
        [ParameterProperties("OpenStudio Shading Group Type", "Shading surface group type (Site, Building or Space) of an imported shade panel"), ParameterValue(Core.ParameterType.String)] ShadingGroupType,

        /// <summary>Name of the shading surface group an imported shade panel belonged to.</summary>
        [ParameterProperties("OpenStudio Shading Group Name", "Name of the shading surface group an imported shade panel belonged to"), ParameterValue(Core.ParameterType.String)] ShadingGroupName,

        /// <summary>Weather file referenced by the source model (a reference, not embedded weather data).</summary>
        [ParameterProperties("OpenStudio Weather File Path", "Weather file referenced by the source OpenStudio model - a reference only, never embedded hourly weather"), ParameterValue(Core.ParameterType.String)] WeatherFilePath,

        /// <summary>OSM version recorded in the source file before version translation.</summary>
        [ParameterProperties("OpenStudio Version", "OSM version recorded in the source file before version translation"), ParameterValue(Core.ParameterType.String)] OpenStudioVersion,

        /// <summary>Path of the OSM actually converted.</summary>
        [ParameterProperties("OpenStudio Source Path", "Path of the OSM actually converted"), ParameterValue(Core.ParameterType.String)] SourcePath,
    }
}
