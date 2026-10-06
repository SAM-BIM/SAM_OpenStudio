// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Core.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Version of the loaded OpenStudio SDK (for example "3.10.0").
        /// </summary>
        /// <returns>OpenStudio SDK semantic version string.</returns>
        public static string OpenStudioVersion()
        {
            return global::OpenStudio.OpenStudioUtilitiesCore.openStudioVersion();
        }
    }
}
