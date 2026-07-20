// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        private static readonly string[] DayOfWeekNames = new string[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

        /// <summary>
        /// Applies the simulation settings (C4): building North Axis (explicit option → SAM
        /// AnalyticalModelParameter.NorthAngle [radians → degrees, the TAS/EnergyPlus
        /// clockwise-from-north convention] → OpenStudio default), explicit RunPeriod, timestep,
        /// optional solar distribution and shadow-calculation frequency, YearDescription (first
        /// day of week from the run calendar, calendar year, leap year), daylight saving time
        /// (default OFF per the energy-model convention — SAM carries no DST data), sizing
        /// control (enabled when design days were imported unless overridden) and the requested
        /// output variables at the optioned reporting frequency.
        /// </summary>
        /// <param name="openStudioConversionContext">Conversion context (weather and design days already applied).</param>
        public static void ToOpenStudio_SimulationSettings(this OpenStudioConversionContext openStudioConversionContext)
        {
            if (openStudioConversionContext == null)
            {
                return;
            }

            global::OpenStudio.Model model = openStudioConversionContext.Target;
            Core.OpenStudio.OpenStudioConversionOptions options = openStudioConversionContext.Options;

            // North axis: degrees clockwise from true North (SAM stores radians — same
            // convention as TAS northAngle, see SAM_Tas ToT3D/ToTBD Building.cs).
            double northAngleDegrees = double.NaN;
            if (options.NorthAngleDegrees.HasValue)
            {
                northAngleDegrees = options.NorthAngleDegrees.Value;
            }
            else if (openStudioConversionContext.Source != null && openStudioConversionContext.Source.TryGetValue(AnalyticalModelParameter.NorthAngle, out double northAngleRadians) && !double.IsNaN(northAngleRadians))
            {
                northAngleDegrees = northAngleRadians * 180.0 / Math.PI;
            }

            if (!double.IsNaN(northAngleDegrees))
            {
                model.getBuilding().setNorthAxis(northAngleDegrees);
            }

            if (options.ShadowCalculationFrequencyDays.HasValue)
            {
                model.getShadowCalculation().setShadingCalculationUpdateFrequency(options.ShadowCalculationFrequencyDays.Value);
            }

            global::OpenStudio.RunPeriod runPeriod = model.getRunPeriod();
            runPeriod.setName("SAM_RunPeriod");
            runPeriod.setBeginMonth(options.RunPeriodBeginMonth ?? 1);
            runPeriod.setBeginDayOfMonth(options.RunPeriodBeginDay ?? 1);
            runPeriod.setEndMonth(options.RunPeriodEndMonth ?? 12);
            runPeriod.setEndDayOfMonth(options.RunPeriodEndDay ?? 31);

            // YearDescription is a unique model object flattened onto Model in the C# wrapper.
            int firstDayOfWeekOffset = openStudioConversionContext.FirstDayOfWeekOffset;
            if (firstDayOfWeekOffset < 0 || firstDayOfWeekOffset > 6)
            {
                firstDayOfWeekOffset = 0;
            }

            model.setDayofWeekforStartDay(DayOfWeekNames[firstDayOfWeekOffset]);
            if (options.CalendarYear.HasValue)
            {
                model.setCalendarYear(options.CalendarYear.Value);
            }

            if (options.IsLeapYear.HasValue)
            {
                model.setIsLeapYear(options.IsLeapYear.Value);
            }

            if (options.DaylightSavingsTime)
            {
                // Ensure the object exists (EnergyPlus default dates: 2nd Sunday March →
                // 1st Sunday November).
                model.getRunPeriodControlDaylightSavingTime();
            }
            else
            {
                global::OpenStudio.OptionalRunPeriodControlDaylightSavingTime optionalDaylightSavingTime = model.getOptionalRunPeriodControlDaylightSavingTime();
                if (optionalDaylightSavingTime != null && !optionalDaylightSavingTime.isNull())
                {
                    optionalDaylightSavingTime.get().remove();
                }
            }

            model.getTimestep().setNumberOfTimestepsPerHour(options.TimestepsPerHour ?? 6);

            bool runSizingPeriods = options.RunSizingPeriods ?? openStudioConversionContext.DesignDaysImported;
            global::OpenStudio.SimulationControl simulationControl = model.getSimulationControl();
            simulationControl.setRunSimulationforWeatherFileRunPeriods(true);
            simulationControl.setRunSimulationforSizingPeriods(runSizingPeriods);
            simulationControl.setDoZoneSizingCalculation(runSizingPeriods);
            simulationControl.setDoSystemSizingCalculation(false);
            simulationControl.setDoPlantSizingCalculation(false);

            if (!string.IsNullOrWhiteSpace(options.SolarDistribution))
            {
                // OpenStudio exposes the EnergyPlus Building Solar Distribution through
                // SimulationControl (forward-translated to the Building object).
                simulationControl.setSolarDistribution(options.SolarDistribution);
            }

            string reportingFrequency = string.IsNullOrWhiteSpace(options.OutputVariableFrequency) ? "Hourly" : options.OutputVariableFrequency;
            string[] variableNames = new string[]
            {
                "Zone Ideal Loads Supply Air Total Heating Energy",
                "Zone Ideal Loads Supply Air Total Cooling Energy",
                "Zone Ideal Loads Supply Air Sensible Heating Energy",
                "Zone Ideal Loads Supply Air Sensible Cooling Energy",
                "Zone Mean Air Temperature",
                "Zone Operative Temperature",
                "Zone Air Relative Humidity",
            };

            foreach (string variableName in variableNames)
            {
                global::OpenStudio.OutputVariable outputVariable = new global::OpenStudio.OutputVariable(variableName, model);
                outputVariable.setKeyValue("*");
                outputVariable.setReportingFrequency(reportingFrequency);
            }
        }
    }
}
