// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Applies the documented MVP simulation settings: annual run period (1 Jan – 31 Dec),
        /// 6 timesteps per hour, weather-file run only (no sizing runs — Ideal Loads needs none),
        /// solar-distribution and shadow-calculation left at OpenStudio defaults, and the plan's
        /// requested hourly output variables (§11).
        /// </summary>
        /// <param name="openStudioConversionContext">Conversion context.</param>
        public static void ToOpenStudio_SimulationSettings(this OpenStudioConversionContext openStudioConversionContext)
        {
            if (openStudioConversionContext == null)
            {
                return;
            }

            global::OpenStudio.Model model = openStudioConversionContext.Target;

            global::OpenStudio.RunPeriod runPeriod = model.getRunPeriod();
            runPeriod.setName("SAM_RunPeriod_Annual");
            runPeriod.setBeginMonth(1);
            runPeriod.setBeginDayOfMonth(1);
            runPeriod.setEndMonth(12);
            runPeriod.setEndDayOfMonth(31);

            model.getTimestep().setNumberOfTimestepsPerHour(6);

            global::OpenStudio.SimulationControl simulationControl = model.getSimulationControl();
            simulationControl.setRunSimulationforWeatherFileRunPeriods(true);
            simulationControl.setRunSimulationforSizingPeriods(false);
            simulationControl.setDoZoneSizingCalculation(false);
            simulationControl.setDoSystemSizingCalculation(false);
            simulationControl.setDoPlantSizingCalculation(false);

            string[] variableNames = new string[]
            {
                "Zone Ideal Loads Supply Air Total Heating Energy",
                "Zone Ideal Loads Supply Air Total Cooling Energy",
                "Zone Ideal Loads Supply Air Sensible Heating Energy",
                "Zone Ideal Loads Supply Air Sensible Cooling Energy",
                "Zone Mean Air Temperature",
                "Zone Operative Temperature",
            };

            foreach (string variableName in variableNames)
            {
                global::OpenStudio.OutputVariable outputVariable = new global::OpenStudio.OutputVariable(variableName, model);
                outputVariable.setKeyValue("*");
                outputVariable.setReportingFrequency("Hourly");
            }
        }
    }
}
