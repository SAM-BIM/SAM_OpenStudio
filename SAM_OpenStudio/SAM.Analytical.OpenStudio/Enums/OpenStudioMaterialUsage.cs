// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Analytical.OpenStudio
{
    /// <summary>
    /// Explicit construction-usage context for material conversion (review P1-06). A SAM
    /// GasMaterial in an opaque construction is an air cavity (OpenStudio AirGap /
    /// OS:Material:AirGap); in a fenestration pane construction it is a window gas
    /// (OpenStudio.Gas / OS:WindowMaterial:Gas). Usage is never inferred from material names.
    /// </summary>
    public enum OpenStudioMaterialUsage
    {
        /// <summary>Layer of an opaque Construction (wall, floor, roof).</summary>
        OpaqueConstruction,

        /// <summary>Pane layer of an ApertureConstruction (window, door, glass door).</summary>
        FenestrationConstruction,
    }
}
