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
        /// fraction values outside [0,1] raise warnings and are never clamped. Cached per
        /// (Guid, ProfileType): the same profile reused under a different type (e.g. a fraction
        /// profile used for both equipment and infiltration) yields a separate schedule with the
        /// correct type limits and a per-type name.
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

            string cacheKey = string.Format("{0}:{1}", profile.Guid, profileType);

            global::OpenStudio.Schedule cached;
            if (openStudioConversionContext.ScheduleMap.TryGetValue(cacheKey, out cached))
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

            openStudioConversionContext.ScheduleMap[cacheKey] = result;
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
            if (profileValues.Length == 0)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ScheduleMissingProfile, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "Profile has no values", profile, openStudioObjectName);
                return null;
            }

            if (profileValues.Length == 8760)
            {
                return CheckedAnnualValues(profile, profileValues, openStudioObjectName, openStudioConversionContext);
            }

            if (profileValues.Length > 8760)
            {
                // Leap-year (8784) or longer profiles: schedules are 365-day by documented MVP
                // policy — keep the first 8760 hours; never average the year into one day.
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ScheduleMissingProfile, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Profile has {0} values; the first 8760 hours are used (365-day non-leap schedule policy)", profileValues.Length), profile, openStudioObjectName);
                double[] truncated = new double[8760];
                Array.Copy(profileValues, truncated, 8760);
                return CheckedAnnualValues(profile, truncated, openStudioObjectName, openStudioConversionContext);
            }

            List<Profile> subProfiles = null;
            IEnumerable<Profile> subProfilesEnumerable = profile.GetProfiles();
            if (subProfilesEnumerable != null)
            {
                subProfiles = new List<Profile>(subProfilesEnumerable);
                subProfiles.RemoveAll(x => x == null);
            }

            if (subProfiles == null || subProfiles.Count == 0)
            {
                // SAM's own yearly expansion (Profile.GetYearlyValues / the wrapping indexer):
                // the sequence tiles hour-for-hour at its own period — a 24-hour day repeats
                // daily, a 168-hour week repeats weekly; nothing is stretched or averaged.
                double[] tiled = new double[8760];
                for (int i = 0; i < tiled.Length; i++)
                {
                    tiled[i] = profile[i];
                }

                return CheckedAnnualValues(profile, tiled, openStudioObjectName, openStudioConversionContext);
            }

            List<double[]> dailyValues = new List<double[]>();
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

            double[] annual = new double[8760];
            int firstDayOfWeekOffset = openStudioConversionContext.FirstDayOfWeekOffset;
            for (int dayIndex = 0; dayIndex < 365; dayIndex++)
            {
                // Rotate the Monday-first week onto the run calendar: day 0 of the year maps to
                // the sub-profile of its actual weekday (Monday = 0 … Sunday = 6).
                double[] day = dailyValues[(dayIndex + firstDayOfWeekOffset) % 7];
                Array.Copy(day, 0, annual, dayIndex * 24, 24);
            }

            return CheckedAnnualValues(profile, annual, openStudioObjectName, openStudioConversionContext);
        }

        private static double[] CheckedAnnualValues(Profile profile, double[] annualValues, string openStudioObjectName, OpenStudioConversionContext openStudioConversionContext)
        {
            for (int i = 0; i < annualValues.Length; i++)
            {
                if (double.IsNaN(annualValues[i]))
                {
                    openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ScheduleMissingProfile, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, string.Format("Profile has a gap at hour {0}; missing values are never substituted", i), profile, openStudioObjectName);
                    return null;
                }
            }

            return annualValues;
        }

        private static double[] DayHourlyValues(Profile profile, string openStudioObjectName, OpenStudioConversionContext openStudioConversionContext)
        {
            double[] profileValues = ProfileValues(profile);
            if (profileValues.Length == 0)
            {
                openStudioConversionContext.AddDiagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ScheduleMissingProfile, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "Profile has no values", profile, openStudioObjectName);
                return null;
            }

            // One day-type sub-profile expanded to 24 hourly values through the SAM indexer
            // (cyclic within the sub-profile's own index span — SAM GetDailyValues semantics).
            double[] result = new double[24];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = profile[i];
            }

            return result;
        }
    }
}
