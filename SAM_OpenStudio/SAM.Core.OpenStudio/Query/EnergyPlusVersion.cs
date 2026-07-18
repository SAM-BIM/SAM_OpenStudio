// SPDX-License-Identifier: LGPL-3.0-only

namespace SAM.Core.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Version of the EnergyPlus engine the loaded OpenStudio SDK targets (for example "24.2.0").
        /// The simulation runner must verify output variable names against this version.
        /// </summary>
        /// <returns>EnergyPlus semantic version string.</returns>
        public static string EnergyPlusVersion()
        {
            return global::OpenStudio.OpenStudioUtilitiesCore.energyPlusVersion();
        }
    }
}
