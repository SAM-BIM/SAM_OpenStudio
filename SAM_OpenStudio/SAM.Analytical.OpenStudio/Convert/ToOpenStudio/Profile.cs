// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Converts a SAM Profile to an OpenStudio ScheduleFixedInterval with 8760 hourly values
        /// (expansion rules in docs/SAM_OPENSTUDIO_INTERNAL_CONDITION_MAPPING.md): annual profiles
        /// pass through; daily sub-profiles are cycled to a Monday-first week (SAM_LadybugTools
        /// parity) and tiled across 365 days; shorter sequences are held/averaged to hourly.
        /// Type limits follow the profile type (fractional, temperature or activity level);
        /// fraction values outside [0,1] raise warnings and are never clamped. Cached per Guid.
        /// </summary>
        /// <param name="profile">SAM profile.</param>
        /// <param name="profileType">Semantic usage, selects schedule type limits.</param>
        /// <param name="openStudioConversionContext">Conversion context.</param>
        /// <returns>The schedule, or null (diagnostic raised).</returns>
        public static global::OpenStudio.Schedule ToOpenStudio(this Profile profile, ProfileType profileType, OpenStudioConversionContext openStudioConversionContext)
        {
            if (profile == null || openStudioConversionContext == null)
            {
                return null;
            }

            global::OpenStudio.Schedule cached;
            if (openStudioConversionContext.ScheduleMap.TryGetValue(profile.Guid, out cached))
            {
                return cached;
            }

            string name = Core.OpenStudio.Query.OpenStudioName("Schedule", string.Format("{0}_{1}", profile.Name, profileType), profile.Guid);

            double[] annualValues = AnnualHourlyValues(profile, name, openStudioConversionContext);
            if (annualValues == null)
            {
                return null;
            }

            bool temperature = profileType == ProfileType.Heating || profileType == ProfileType.Cooling;
            if (!temperature)
            {
                foreach (double value in annualValues)
                {
                    if (value < 0 || value > 1)
                    {
                        openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ScheduleMissingProfile, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Fraction profile contains value {0} outside [0,1]; not clamped", value), profile, name);
                        break;
                    }
                }
            }

            global::OpenStudio.ScheduleFixedInterval result = new global::OpenStudio.ScheduleFixedInterval(openStudioConversionContext.Target);
            result.setName(name);
            result.setInterpolatetoTimestep(false);
            result.setScheduleTypeLimits(ScheduleTypeLimits(openStudioConversionContext.Target, temperature ? "Temperature" : "Fractional"));

            global::OpenStudio.Vector vector = new global::OpenStudio.Vector((uint)annualValues.Length);
            for (int i = 0; i < annualValues.Length; i++)
            {
                vector.__setitem__((uint)i, annualValues[i]);
            }

            global::OpenStudio.TimeSeries timeSeries = new global::OpenStudio.TimeSeries(new global::OpenStudio.Date(new global::OpenStudio.MonthOfYear(1), 1), new global::OpenStudio.Time(0, 1, 0, 0), vector, string.Empty);
            if (!result.setTimeSeries(timeSeries))
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ScheduleMissingProfile, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "OpenStudio rejected the annual time series", profile, name);
                result.remove();
                return null;
            }

            openStudioConversionContext.ScheduleMap[profile.Guid] = result;
            if (!openStudioConversionContext.References.Contains(profile.Guid))
            {
                openStudioConversionContext.RegisterModelObject(profile, result);
            }

            return result;
        }

        /// <summary>
        /// Creates (or returns) a constant annual schedule with the given value — used for
        /// people activity levels. Deterministically named; one instance per (name, value).
        /// </summary>
        /// <param name="openStudioConversionContext">Conversion context.</param>
        /// <param name="name">Deterministic schedule name.</param>
        /// <param name="value">Constant value for every hour of the year.</param>
        /// <param name="typeLimitsName">"Fractional", "Temperature" or "ActivityLevel".</param>
        /// <returns>The constant schedule.</returns>
        public static global::OpenStudio.Schedule ToOpenStudio_ConstantSchedule(this OpenStudioConversionContext openStudioConversionContext, string name, double value, string typeLimitsName)
        {
            global::OpenStudio.OptionalScheduleFixedInterval existing = openStudioConversionContext.Target.getScheduleFixedIntervalByName(name);
            if (existing != null && !existing.isNull())
            {
                return existing.get();
            }

            global::OpenStudio.ScheduleFixedInterval result = new global::OpenStudio.ScheduleFixedInterval(openStudioConversionContext.Target);
            result.setName(name);
            result.setInterpolatetoTimestep(false);
            result.setScheduleTypeLimits(ScheduleTypeLimits(openStudioConversionContext.Target, typeLimitsName));

            global::OpenStudio.Vector vector = new global::OpenStudio.Vector(8760, value);
            global::OpenStudio.TimeSeries timeSeries = new global::OpenStudio.TimeSeries(new global::OpenStudio.Date(new global::OpenStudio.MonthOfYear(1), 1), new global::OpenStudio.Time(0, 1, 0, 0), vector, string.Empty);
            result.setTimeSeries(timeSeries);

            return result;
        }

        private static global::OpenStudio.ScheduleTypeLimits ScheduleTypeLimits(global::OpenStudio.Model model, string kind)
        {
            string name = "SAM_ScheduleTypeLimits_" + kind;
            global::OpenStudio.OptionalScheduleTypeLimits existing = model.getScheduleTypeLimitsByName(name);
            if (existing != null && !existing.isNull())
            {
                return existing.get();
            }

            global::OpenStudio.ScheduleTypeLimits result = new global::OpenStudio.ScheduleTypeLimits(model);
            result.setName(name);
            result.setNumericType("Continuous");

            switch (kind)
            {
                case "Fractional":
                    result.setLowerLimitValue(0);
                    result.setUpperLimitValue(1);
                    result.setUnitType("Dimensionless");
                    break;

                case "Temperature":
                    result.setUnitType("Temperature");
                    break;

                case "ActivityLevel":
                    result.setLowerLimitValue(0);
                    result.setUnitType("ActivityLevel");
                    break;
            }

            return result;
        }

        private static double[] ProfileValues(Profile profile)
        {
            // Profile stores an indexed (possibly range-compressed) sequence; Count is the index
            // span, not the value count. The indexer over Min..Max resolves every hour reliably.
            int min = profile.Min;
            int max = profile.Max;
            if (max < min)
            {
                return new double[0];
            }

            double[] result = new double[max - min + 1];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = profile[min + i];
            }

            return result;
        }

        private static double[] AnnualHourlyValues(Profile profile, string openStudioObjectName, OpenStudioConversionContext openStudioConversionContext)
        {
            double[] profileValues = ProfileValues(profile);
            if (profileValues.Length == 8760)
            {
                return profileValues;
            }

            List<Profile> subProfiles = null;
            IEnumerable<Profile> subProfilesEnumerable = profile.GetProfiles();
            if (subProfilesEnumerable != null)
            {
                subProfiles = new List<Profile>(subProfilesEnumerable);
                subProfiles.RemoveAll(x => x == null);
            }

            List<double[]> dailyValues = new List<double[]>();
            if (subProfiles != null && subProfiles.Count > 0)
            {
                foreach (Profile subProfile in subProfiles)
                {
                    double[] day = DayHourlyValues(subProfile, openStudioObjectName, openStudioConversionContext);
                    if (day == null)
                    {
                        return null;
                    }

                    dailyValues.Add(day);
                }

                int index = 0;
                while (dailyValues.Count < 7)
                {
                    dailyValues.Add(dailyValues[index]);
                    index++;
                }
            }
            else
            {
                double[] day = DayHourlyValues(profile, openStudioObjectName, openStudioConversionContext);
                if (day == null)
                {
                    return null;
                }

                for (int i = 0; i < 7; i++)
                {
                    dailyValues.Add(day);
                }
            }

            double[] annual = new double[8760];
            for (int dayIndex = 0; dayIndex < 365; dayIndex++)
            {
                double[] day = dailyValues[dayIndex % 7];
                Array.Copy(day, 0, annual, dayIndex * 24, 24);
            }

            return annual;
        }

        private static double[] DayHourlyValues(Profile profile, string openStudioObjectName, OpenStudioConversionContext openStudioConversionContext)
        {
            double[] profileValues = ProfileValues(profile);
            int count = profileValues.Length;
            if (count <= 0)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ScheduleMissingProfile, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "Profile has no values", profile, openStudioObjectName);
                return null;
            }

            double[] result = new double[24];

            if (24 % count == 0)
            {
                int repeat = 24 / count;
                for (int i = 0; i < count; i++)
                {
                    for (int j = 0; j < repeat; j++)
                    {
                        result[i * repeat + j] = profileValues[i];
                    }
                }

                return result;
            }

            if (count % 24 == 0)
            {
                int block = count / 24;
                for (int i = 0; i < 24; i++)
                {
                    double sum = 0;
                    for (int j = 0; j < block; j++)
                    {
                        sum += profileValues[i * block + j];
                    }

                    result[i] = sum / block;
                }

                return result;
            }

            openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ScheduleMissingProfile, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Profile length {0} is not day-aligned; values tiled cyclically over 24 hours", count), profile, openStudioObjectName);
            for (int i = 0; i < 24; i++)
            {
                result[i] = profileValues[i % count];
            }

            return result;
        }
    }
}
