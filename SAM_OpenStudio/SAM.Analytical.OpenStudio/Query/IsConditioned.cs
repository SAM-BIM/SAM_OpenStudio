// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Analytical.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Central conditioned-space decision (plan §12): a space is conditioned when it has an
        /// InternalCondition whose name does not contain "unconditioned" or "external"
        /// (case-insensitive) — matching the established SAM_LadybugTools behaviour. Conditioned
        /// spaces receive a dual-setpoint thermostat and an Ideal Loads air system; unconditioned
        /// spaces keep their geometry and internal gains only.
        /// </summary>
        /// <param name="space">SAM space.</param>
        /// <returns>True when the space is conditioned.</returns>
        public static bool IsConditioned(this Space space)
        {
            InternalCondition internalCondition = space?.InternalCondition;
            if (internalCondition == null)
            {
                return false;
            }

            string name = internalCondition.Name;
            if (name == null)
            {
                return true;
            }

            string lower = name.ToLower();
            return !lower.Contains("unconditioned") && !lower.Contains("external");
        }
    }
}
