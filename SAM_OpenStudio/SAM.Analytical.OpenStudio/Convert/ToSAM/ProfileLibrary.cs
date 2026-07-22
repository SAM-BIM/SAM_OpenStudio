// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>Hours in a non-leap year — the length of every SAM annual profile produced here.</summary>
        private const int AnnualHourCount = 8760;

        /// <summary>
        /// Creates the SAM <see cref="ProfileLibrary"/> for the import. Profiles are converted
        /// lazily, as internal conditions reference them, so a model's unused schedules — and
        /// there are usually many — never enter the library in the first place. Anything that
        /// slips through is removed by
        /// <see cref="Modify.RemoveUnreferencedProfiles(ProfileLibrary, AdjacencyCluster)"/>.
        /// </summary>
        /// <param name="openStudioImportContext">Import context.</param>
        /// <returns>An empty profile library, ready to be populated; never null.</returns>
        public static ProfileLibrary ToSAM_ProfileLibrary(this OpenStudioImportContext openStudioImportContext)
        {
            return new ProfileLibrary("OpenStudio");
        }

        /// <summary>
        /// Converts an OpenStudio schedule into a SAM annual <see cref="Profile"/> of 8760 hourly
        /// values, caching it by schedule name and adding it to
        /// <paramref name="profileLibrary"/>.
        /// <para>
        /// Everything is expanded to hourly because that is what SAM profiles are, and because it
        /// is the exact inverse of the forward direction, which writes SAM profiles out as
        /// 8760-value ScheduleFixedIntervals. A ScheduleRuleset is expanded through OpenStudio's
        /// own <c>getDaySchedules</c>, so weekday/weekend rules, seasonal date ranges and rule
        /// priority are honoured by the engine that defines them rather than reimplemented here —
        /// and the schedule is never flattened to a single constant.
        /// </para>
        /// </summary>
        /// <param name="schedule">OpenStudio schedule; null returns null.</param>
        /// <param name="profileType">SAM profile type (selects the profile's category).</param>
        /// <param name="openStudioImportContext">Import context.</param>
        /// <param name="profileLibrary">Library receiving the profile; may be null.</param>
        /// <returns>The SAM profile, or null when the schedule kind cannot be expanded.</returns>
        public static Profile ToSAM(this global::OpenStudio.Schedule schedule, ProfileType profileType, OpenStudioImportContext openStudioImportContext, ProfileLibrary profileLibrary)
        {
            if (schedule == null || openStudioImportContext == null)
            {
                return null;
            }

            string name = schedule.nameString();
            string cacheKey = string.Format("{0}:{1}", name, profileType);

            Profile cached;
            if (openStudioImportContext.ProfileMap.TryGetValue(cacheKey, out cached))
            {
                return cached;
            }

            string label = OpenStudioImportContext.OpenStudioObjectLabel(schedule);

            double[] values = AnnualHourlyValues(schedule, openStudioImportContext, label);
            if (values == null)
            {
                return null;
            }

            // The profile name carries the type: the same OpenStudio schedule reused as, say,
            // both an occupancy and an equipment profile must not collide in the library, and
            // the forward direction names its schedules the same way.
            string profileName = string.Format("{0}_{1}", name, profileType);

            Profile result = new Profile(profileName, profileType, values);

            Guid guid = openStudioImportContext.ResolveGuid(schedule, typeof(Profile).Name);
            result = new Profile(guid, result, profileName, result.Category);

            Modify.SetOpenStudioSource(result, schedule);

            openStudioImportContext.ProfileMap[cacheKey] = result;
            profileLibrary?.Add(result);
            openStudioImportContext.RegisterCreated();

            return result;
        }

        /// <summary>
        /// Expands any supported schedule into 8760 hourly values. Unsupported kinds return null
        /// with a SAM-OSI-SCH-001 diagnostic — the caller then leaves the load without a profile
        /// rather than substituting a constant that would look like real data.
        /// </summary>
        private static double[] AnnualHourlyValues(global::OpenStudio.Schedule schedule, OpenStudioImportContext openStudioImportContext, string label)
        {
            global::OpenStudio.OptionalScheduleConstant optionalScheduleConstant = global::OpenStudio.OpenStudioModelResources.toScheduleConstant(schedule);
            if (optionalScheduleConstant != null && !optionalScheduleConstant.isNull())
            {
                double value = optionalScheduleConstant.get().value();
                double[] result = new double[AnnualHourCount];
                for (int i = 0; i < AnnualHourCount; i++)
                {
                    result[i] = value;
                }

                return result;
            }

            global::OpenStudio.OptionalScheduleRuleset optionalScheduleRuleset = global::OpenStudio.OpenStudioModelCore.toScheduleRuleset(schedule);
            if (optionalScheduleRuleset != null && !optionalScheduleRuleset.isNull())
            {
                return AnnualHourlyValues(optionalScheduleRuleset.get(), openStudioImportContext, label);
            }

            global::OpenStudio.OptionalScheduleInterval optionalScheduleInterval = global::OpenStudio.OpenStudioModelResources.toScheduleInterval(schedule);
            if (optionalScheduleInterval != null && !optionalScheduleInterval.isNull())
            {
                return AnnualHourlyValues(optionalScheduleInterval.get(), openStudioImportContext, label);
            }

            openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ScheduleUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("Schedule type '{0}' cannot be expanded to hourly values and has no SAM equivalent; the load it drives was imported without a profile (no constant was substituted)", IddTypeName(schedule)), label);
            openStudioImportContext.RegisterSkip();
            return null;
        }

        /// <summary>
        /// Expands a ScheduleRuleset day by day using OpenStudio's own rule resolution, then
        /// samples each day at the end of every hour — the convention EnergyPlus itself uses for
        /// an "Until: HH:MM" day schedule, so a 09:00–17:00 occupancy pattern lands on hours 9–17
        /// rather than being shifted by one.
        /// </summary>
        private static double[] AnnualHourlyValues(global::OpenStudio.ScheduleRuleset scheduleRuleset, OpenStudioImportContext openStudioImportContext, string label)
        {
            global::OpenStudio.ScheduleDayVector scheduleDayVector;
            try
            {
                global::OpenStudio.Date startDate = new global::OpenStudio.Date(new global::OpenStudio.MonthOfYear(1), 1);
                global::OpenStudio.Date endDate = new global::OpenStudio.Date(new global::OpenStudio.MonthOfYear(12), 31);
                scheduleDayVector = scheduleRuleset.getDaySchedules(startDate, endDate);
            }
            catch (Exception exception)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ScheduleUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("The schedule ruleset could not be expanded ({0}: {1}); the load it drives was imported without a profile", exception.GetType().Name, exception.Message), label);
                openStudioImportContext.RegisterSkip();
                return null;
            }

            if (scheduleDayVector == null || scheduleDayVector.Count == 0)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ScheduleUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "The schedule ruleset expanded to no day schedules; the load it drives was imported without a profile", label);
                openStudioImportContext.RegisterSkip();
                return null;
            }

            double[] result = new double[AnnualHourCount];
            int hour = 0;

            for (int day = 0; day < scheduleDayVector.Count && hour < AnnualHourCount; day++)
            {
                global::OpenStudio.ScheduleDay scheduleDay = scheduleDayVector[day];
                for (int h = 1; h <= 24 && hour < AnnualHourCount; h++)
                {
                    result[hour] = scheduleDay == null ? 0 : scheduleDay.getValue(new global::OpenStudio.Time(0, h, 0, 0));
                    hour++;
                }
            }

            if (hour < AnnualHourCount)
            {
                // A 365-day expansion is expected; a shorter one (a ruleset restricted to part of
                // the year) is held at its last value rather than dropping to zero, and reported.
                double last = hour > 0 ? result[hour - 1] : 0;
                for (int i = hour; i < AnnualHourCount; i++)
                {
                    result[i] = last;
                }

                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format(System.Globalization.CultureInfo.InvariantCulture, "The schedule ruleset covers only {0} of {1} hours; the remaining hours were held at the last defined value ({2:G4})", hour, AnnualHourCount, last), label);
            }

            return result;
        }

        /// <summary>
        /// Expands a ScheduleInterval from its time series, resampling onto whole-hour boundaries
        /// using the series' own reporting interval rather than guessing the cadence from the
        /// value count.
        /// <para>
        /// A fixed-interval series reports its interval, so the mapping is exact: an hourly series
        /// maps value-for-hour; a coarser series (a daily value, say) holds each value across
        /// every hour it spans — <em>not</em> spread one value per hour, which would turn 365
        /// daily values into a meaningless 1,2,3… ramp over the first days; a sub-hourly series is
        /// averaged into hourly means (averaged, not sampled, so a fractional schedule keeps its
        /// daily total). Coverage shorter than a year is held at the last value and reported;
        /// leap-year excess is dropped, as SAM profiles are always 8760 hours.
        /// </para>
        /// <para>
        /// A variable-interval series has no fixed cadence to resample deterministically and has
        /// no SAM equivalent; it is reported unsupported (SAM-OSI-SCH-001) and imported without a
        /// profile rather than fabricating one, matching the reverse-coverage manifest.
        /// </para>
        /// </summary>
        private static double[] AnnualHourlyValues(global::OpenStudio.ScheduleInterval scheduleInterval, OpenStudioImportContext openStudioImportContext, string label)
        {
            global::OpenStudio.TimeSeries timeSeries;
            try
            {
                timeSeries = scheduleInterval.timeSeries();
            }
            catch (Exception)
            {
                timeSeries = null;
            }

            if (timeSeries == null)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ScheduleUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "The interval schedule carries no time series; the load it drives was imported without a profile", label);
                openStudioImportContext.RegisterSkip();
                return null;
            }

            global::OpenStudio.Vector vector;
            try
            {
                vector = timeSeries.values();
            }
            catch (Exception)
            {
                vector = null;
            }

            if (vector == null || vector.size() == 0)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ScheduleUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "The interval schedule's time series is empty; the load it drives was imported without a profile", label);
                openStudioImportContext.RegisterSkip();
                return null;
            }

            // The reporting interval is what distinguishes a fixed-interval series (which carries
            // a value here) from a variable-interval one (which does not).
            double intervalHours = 0;
            try
            {
                global::OpenStudio.OptionalTime optionalIntervalLength = timeSeries.intervalLength();
                if (optionalIntervalLength != null && !optionalIntervalLength.isNull())
                {
                    intervalHours = optionalIntervalLength.get().totalHours();
                }
            }
            catch (Exception)
            {
                intervalHours = 0;
            }

            if (intervalHours <= 0)
            {
                // Variable interval (or an interval that could not be read): no fixed cadence to
                // resample. Unsupported by contract - reported, never guessed.
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ScheduleUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "The interval schedule has no fixed reporting interval (a variable-interval schedule); it has no SAM equivalent and the load it drives was imported without a profile", label);
                openStudioImportContext.RegisterSkip();
                return null;
            }

            // OpenStudio.Vector is the SWIG numeric vector: size()/__getitem__, not Count/[].
            int count = (int)vector.size();
            double[] result = new double[AnnualHourCount];

            if (intervalHours < 1)
            {
                // Sub-hourly: average the whole sub-hour block that falls in each hour.
                int perHour = (int)Math.Round(1.0 / intervalHours);
                if (perHour < 1)
                {
                    perHour = 1;
                }

                int filledHours = 0;
                for (int hour = 0; hour < AnnualHourCount; hour++)
                {
                    double sum = 0;
                    int taken = 0;
                    for (int j = 0; j < perHour; j++)
                    {
                        int index = (hour * perHour) + j;
                        if (index >= count)
                        {
                            break;
                        }

                        sum += vector.__getitem__((uint)index);
                        taken++;
                    }

                    if (taken == 0)
                    {
                        break;
                    }

                    result[hour] = sum / taken;
                    filledHours = hour + 1;
                }

                HoldTailAndReportPartial(result, filledHours, openStudioImportContext, label);

                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format(System.Globalization.CultureInfo.InvariantCulture, "The sub-hourly interval schedule ({0:G4} h interval, {1} per hour) was averaged into hourly means; SAM profiles are hour-indexed and cannot carry the sub-hourly detail", intervalHours, perHour), label);
                return result;
            }

            // Hourly (interval == 1) or coarser (interval > 1): each hour takes the value of the
            // interval that spans it. For an hourly series this is value-for-hour and the exact
            // inverse of the forward direction; for a daily series each value holds for its 24
            // hours.
            int filled = 0;
            for (int hour = 0; hour < AnnualHourCount; hour++)
            {
                int index = (int)(hour / intervalHours);
                if (index >= count)
                {
                    break;
                }

                result[hour] = vector.__getitem__((uint)index);
                filled = hour + 1;
            }

            HoldTailAndReportPartial(result, filled, openStudioImportContext, label);
            return result;
        }

        /// <summary>
        /// Fills any hours a schedule did not cover: holds them at the last defined value rather
        /// than dropping them to zero, and reports the partial coverage. A schedule that covers
        /// the whole year (<paramref name="filledHours"/> == 8760) is left untouched and silent.
        /// </summary>
        private static void HoldTailAndReportPartial(double[] result, int filledHours, OpenStudioImportContext openStudioImportContext, string label)
        {
            if (filledHours >= AnnualHourCount)
            {
                return;
            }

            double last = filledHours > 0 ? result[filledHours - 1] : 0;
            for (int i = filledHours; i < AnnualHourCount; i++)
            {
                result[i] = last;
            }

            openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format(System.Globalization.CultureInfo.InvariantCulture, "The interval schedule covers only {0} of {1} hours; the remaining hours were held at the last defined value ({2:G4})", filledHours, AnnualHourCount, last), label);
        }
    }
}
