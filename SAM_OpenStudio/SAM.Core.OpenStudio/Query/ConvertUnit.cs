// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Data;

namespace SAM.Core.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// The single energy-conversion authority (C5): J → kWh. Every annual-energy path
        /// (runner extraction, result sets) must use this — never a local divisor.
        /// </summary>
        /// <param name="joules">Energy [J].</param>
        /// <returns>Energy [kWh].</returns>
        public static double JoulesToKilowattHours(double joules)
        {
            return joules / 3600000.0;
        }

        /// <summary>
        /// Per-interval energy [J] → average power over the interval [W]: J ÷ interval seconds.
        /// At hourly reporting this is numerically J ÷ 3600 — the hour-average watts used for
        /// at-peak gain values in the SpaceSimulationResult path.
        /// </summary>
        /// <param name="joules">Energy over the reporting interval [J].</param>
        /// <param name="intervalSeconds">Interval length [s] (3600 for hourly reporting).</param>
        /// <returns>Average power [W].</returns>
        public static double JoulesPerIntervalToWatts(double joules, double intervalSeconds = 3600)
        {
            return joules / intervalSeconds;
        }

        /// <summary>
        /// Converts a raw ReportData value using the ReportDataDictionary Units as the
        /// authority: "J" becomes hour-average watts (<see cref="JoulesPerIntervalToWatts"/>,
        /// hourly reporting); all other units pass through unchanged.
        /// </summary>
        public static double ConvertUnit(this DataTable dataTable, string name, string keyValue, double value)
        {
            string units = Units(dataTable, name, keyValue);
            if (!string.IsNullOrWhiteSpace(units))
            {
                switch (units.Trim())
                {
                    case "J":
                        value = JoulesPerIntervalToWatts(value);
                        break;
                }
            }

            return value;
        }
    }
}
