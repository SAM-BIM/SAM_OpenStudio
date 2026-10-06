// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// The trailing deterministic SAM Guid suffix of an OpenStudio object name produced by
        /// Core.OpenStudio.Query.OpenStudioName (SAM_&lt;type&gt;_&lt;name&gt;_&lt;guid8&gt;,
        /// uppercased by EnergyPlus): the last underscore-separated segment when it is exactly
        /// 8 hexadecimal characters. Used to resolve SQL zone/surface names back to SAM object
        /// Guids (prefix match — never a display-name-only match when a Guid is available).
        /// </summary>
        /// <param name="openStudioName">OpenStudio/EnergyPlus object name.</param>
        /// <param name="suffix">Uppercased 8-hex Guid prefix when found.</param>
        /// <returns>True when a Guid suffix was found.</returns>
        public static bool TryGetGuidSuffix(string openStudioName, out string suffix)
        {
            suffix = null;
            if (string.IsNullOrWhiteSpace(openStudioName))
            {
                return false;
            }

            int index = openStudioName.LastIndexOf('_');
            if (index == -1 || index == openStudioName.Length - 1)
            {
                return false;
            }

            string candidate = openStudioName.Substring(index + 1);
            if (candidate.Length != 8)
            {
                return false;
            }

            foreach (char c in candidate)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex)
                {
                    return false;
                }
            }

            suffix = candidate.ToUpperInvariant();
            return true;
        }

        /// <summary>
        /// Guid-prefix (first 8 hex, uppercased) used in deterministic OpenStudio names.
        /// </summary>
        public static string GuidSuffix(this Core.SAMObject sAMObject)
        {
            return sAMObject == null ? null : sAMObject.Guid.ToString("N").Substring(0, 8).ToUpperInvariant();
        }
    }
}
