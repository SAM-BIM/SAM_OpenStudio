// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Core.OpenStudio
{
    /// <summary>
    /// Where the design days in a converted OpenStudio model actually came from. Mirrors the
    /// documented design-day precedence of the SAM → OpenStudio route (explicit DDY file, then
    /// the design days embedded in the AnalyticalModel, then none — never merged) and records the
    /// outcome rather than the intent: a source that supplied no usable design day reports
    /// <see cref="None"/>. Consumed by provenance reporting (the B1b benchmark producer's
    /// <c>provenance.designDaySource</c>) so the recorded basis is what the route did.
    /// </summary>
    public enum OpenStudioDesignDaySource
    {
        /// <summary>No design day reached the model; sizing periods stay disabled unless overridden.</summary>
        None = 0,

        /// <summary>Design days were imported from an explicit DDY file.</summary>
        Ddy = 1,

        /// <summary>Design days were translated from the AnalyticalModel HeatingDesignDays/CoolingDesignDays.</summary>
        EmbeddedModel = 2,
    }
}
