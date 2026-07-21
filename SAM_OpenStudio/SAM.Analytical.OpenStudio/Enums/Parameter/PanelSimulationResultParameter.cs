// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.ComponentModel;
using SAM.Core.Attributes;

namespace SAM.Analytical.OpenStudio
{
    [AssociatedTypes(typeof(SurfaceSimulationResult)), Description("SurfaceSimulationResult Parameter")]
    public enum SurfaceSimulationResultParameter
    {
        [ParameterProperties("Zone Index", "Zone Index"), ParameterValue(Core.ParameterType.Integer)] ZoneIndex,
        [ParameterProperties("Zone Name", "Zone Name"), ParameterValue(Core.ParameterType.String)] ZoneName,
        [ParameterProperties("Surface Index", "EnergyPlus SQL SurfaceIndex - the identity of the engine surface this result was read from"), ParameterValue(Core.ParameterType.Integer)] SurfaceIndex,
        [ParameterProperties("Host Surface Name", "EnergyPlus name of the base surface hosting this subsurface (windows/doors); empty for a base surface"), ParameterValue(Core.ParameterType.String)] HostSurfaceName,
    }
}