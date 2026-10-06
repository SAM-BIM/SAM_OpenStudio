// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Core.OpenStudio
{
    /// <summary>
    /// Namespaced <c>OS:AdditionalProperties</c> feature names carrying SAM object identity
    /// through an OSM round trip.
    /// <para>
    /// The deterministic object name <c>SAM_&lt;Type&gt;_&lt;Name&gt;_&lt;GuidFirst8&gt;</c>
    /// (<see cref="Query.OpenStudioName(string, string, System.Guid)"/>) is a stable *display and
    /// matching* key, not an identity: 8 hex characters are 32 bits of a 128-bit Guid and cannot
    /// reconstruct it. These features are therefore the only lossless identity channel, and the
    /// reverse importer restores a SAM Guid from <see cref="Guid"/> alone.
    /// </para>
    /// <para>
    /// Writing them is additive: <c>AdditionalProperties</c> is a separate OSM object attached to
    /// the owner, so no existing field, object name or EnergyPlus input changes. Third-party OSM
    /// files simply carry none of them, and their absence is never a diagnostic.
    /// </para>
    /// </summary>
    public static class OpenStudioIdentityKeys
    {
        /// <summary>Full SAM Guid, <c>Guid.ToString("D")</c>.</summary>
        public const string Guid = "SAM.Guid";

        /// <summary>SAM runtime type name (for example "Space", "Panel", "Construction").</summary>
        public const string Type = "SAM.Type";

        /// <summary>Original, unsanitised SAM object name.</summary>
        public const string Name = "SAM.Name";
    }
}
