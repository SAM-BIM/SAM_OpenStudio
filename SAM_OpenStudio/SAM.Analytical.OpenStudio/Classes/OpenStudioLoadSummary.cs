// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;

namespace SAM.Analytical.OpenStudio
{
    /// <summary>
    /// Annual Ideal Loads results extracted from the EnergyPlus SQLite output, in kWh.
    /// Zone keys are the EnergyPlus report keys (the Ideal Loads air system names, uppercased —
    /// one system per conditioned zone, so the entry count equals the conditioned-zone count).
    /// Immutable.
    /// </summary>
    public sealed class OpenStudioLoadSummary
    {
        /// <summary>Annual total heating energy [kWh] over all conditioned zones.</summary>
        public double TotalHeating { get; }

        /// <summary>Annual total cooling energy [kWh] over all conditioned zones.</summary>
        public double TotalCooling { get; }

        /// <summary>Annual heating energy [kWh] per report key.</summary>
        public IReadOnlyDictionary<string, double> ZoneHeating { get; }

        /// <summary>Annual cooling energy [kWh] per report key.</summary>
        public IReadOnlyDictionary<string, double> ZoneCooling { get; }

        /// <summary>Creates an immutable load summary.</summary>
        /// <param name="zoneHeating">Annual heating energy [kWh] per report key.</param>
        /// <param name="zoneCooling">Annual cooling energy [kWh] per report key.</param>
        public OpenStudioLoadSummary(IDictionary<string, double> zoneHeating, IDictionary<string, double> zoneCooling)
        {
            Dictionary<string, double> heating = zoneHeating == null ? new Dictionary<string, double>() : new Dictionary<string, double>(zoneHeating);
            Dictionary<string, double> cooling = zoneCooling == null ? new Dictionary<string, double>() : new Dictionary<string, double>(zoneCooling);

            ZoneHeating = heating;
            ZoneCooling = cooling;

            double totalHeating = 0;
            foreach (KeyValuePair<string, double> keyValuePair in heating)
            {
                totalHeating += keyValuePair.Value;
            }

            double totalCooling = 0;
            foreach (KeyValuePair<string, double> keyValuePair in cooling)
            {
                totalCooling += keyValuePair.Value;
            }

            TotalHeating = totalHeating;
            TotalCooling = totalCooling;
        }

        /// <summary>True when both totals are finite, non-negative numbers.</summary>
        public bool IsFinite
        {
            get
            {
                return !double.IsNaN(TotalHeating) && !double.IsInfinity(TotalHeating) && TotalHeating >= 0
                    && !double.IsNaN(TotalCooling) && !double.IsInfinity(TotalCooling) && TotalCooling >= 0;
            }
        }
    }
}
