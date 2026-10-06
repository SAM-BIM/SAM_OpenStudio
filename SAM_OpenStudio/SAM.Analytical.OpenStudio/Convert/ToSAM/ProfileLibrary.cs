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
        /// daily total). Hours outside a partial series use its configured out-of-range value and
        /// are reported; leap day is skipped without shifting March-December, as SAM profiles are
        /// always 8760 hours.
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
            long intervalSeconds = 0;
            try
            {
                global::OpenStudio.OptionalTime optionalIntervalLength = timeSeries.intervalLength();
                if (optionalIntervalLength != null && !optionalIntervalLength.isNull())
                {
                    intervalSeconds = optionalIntervalLength.get().totalSeconds();
                }
            }
            catch (Exception)
            {
                intervalSeconds = 0;
            }

            if (intervalSeconds <= 0)
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

            long seriesStartSeconds;
            int sourceYear;
            int sourceMonth;
            int sourceDayOfMonth;
            try
            {
                global::OpenStudio.DateTime startDateTime = timeSeries.startDateTime();
                seriesStartSeconds = startDateTime.toEpoch();
                global::OpenStudio.Date startDate = startDateTime.date();
                sourceYear = startDate.year();
                sourceMonth = startDate.monthOfYear().value();
                sourceDayOfMonth = (int)startDate.dayOfMonth();
            }
            catch (Exception exception)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ScheduleUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("The fixed-interval schedule's start date could not be read ({0}: {1}); the load it drives was imported without a profile", exception.GetType().Name, exception.Message), label);
                openStudioImportContext.RegisterSkip();
                return null;
            }

            long seriesEndSeconds = seriesStartSeconds + (count * intervalSeconds);
            double outOfRangeValue = timeSeries.outOfRangeValue();
            try
            {
                global::OpenStudio.OptionalScheduleFixedInterval optionalScheduleFixedInterval = global::OpenStudio.OpenStudioModelResources.toScheduleFixedInterval(scheduleInterval);
                if (optionalScheduleFixedInterval != null && !optionalScheduleFixedInterval.isNull())
                {
                    outOfRangeValue = optionalScheduleFixedInterval.get().outOfRangeValue();
                }
            }
            catch (Exception)
            {
                // The TimeSeries value above is the same default and remains a safe fallback.
            }

            global::OpenStudio.Date januaryFirst = new global::OpenStudio.Date(new global::OpenStudio.MonthOfYear(1), 1, sourceYear);
            long sourceYearStartSeconds = new global::OpenStudio.DateTime(januaryFirst).toEpoch();
            long nextYearStartSeconds = sourceYearStartSeconds + (System.DateTime.IsLeapYear(sourceYear) ? 366L : 365L) * 24L * 60L * 60L;
            bool inferredLeapSeries = !System.DateTime.IsLeapYear(sourceYear)
                && sourceMonth == 1
                && sourceDayOfMonth == 1
                && seriesEndSeconds - seriesStartSeconds >= 366L * 24L * 60L * 60L;
            long coveredSeconds = 0;

            // SAM's annual index is a non-leap Jan-Dec calendar. Map every canonical month/day
            // back to the TimeSeries calendar instead of copying the first 8760 values: that
            // skips 29 February without shifting March-December, honours a non-January start,
            // and also handles a series that crosses into the following calendar year.
            System.DateTime canonicalDate = new System.DateTime(2001, 1, 1);
            int resultHour = 0;
            for (int day = 0; day < 365; day++)
            {
                System.DateTime monthAndDay = canonicalDate.AddDays(day);
                int sourceDay = new System.DateTime(sourceYear, monthAndDay.Month, monthAndDay.Day).DayOfYear - 1;
                if (inferredLeapSeries && monthAndDay.Month > 2)
                {
                    // ScheduleFixedInterval stores month/day but not the year. OpenStudio therefore
                    // reconstructs an assumed non-leap year even when 366 days of values were set;
                    // the duration is the remaining evidence that the source included 29 February.
                    sourceDay++;
                }
                int nextSourceDay = new System.DateTime(sourceYear + 1, monthAndDay.Month, monthAndDay.Day).DayOfYear - 1;

                for (int hour = 0; hour < 24; hour++)
                {
                    long sourceHourStart = sourceYearStartSeconds + (((sourceDay * 24L) + hour) * 60L * 60L);
                    long nextSourceHourStart = nextYearStartSeconds + (((nextSourceDay * 24L) + hour) * 60L * 60L);

                    long sourceOverlap = OverlapSeconds(sourceHourStart, sourceHourStart + 3600L, seriesStartSeconds, seriesEndSeconds);
                    long nextSourceOverlap = OverlapSeconds(nextSourceHourStart, nextSourceHourStart + 3600L, seriesStartSeconds, seriesEndSeconds);
                    long selectedHourStart = nextSourceOverlap > sourceOverlap ? nextSourceHourStart : sourceHourStart;

                    result[resultHour++] = AverageFixedIntervalHour(vector, count, intervalSeconds, seriesStartSeconds, seriesEndSeconds, selectedHourStart, outOfRangeValue);
                    coveredSeconds += Math.Max(sourceOverlap, nextSourceOverlap);
                }
            }

            if (coveredSeconds < AnnualHourCount * 3600L)
            {
                double coveredHours = coveredSeconds / 3600.0;
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format(System.Globalization.CultureInfo.InvariantCulture, "The interval schedule covers {0:G6} of {1} annual hours; its configured out-of-range value ({2:G4}) was used outside that coverage", coveredHours, AnnualHourCount, outOfRangeValue), label);
            }

            if (intervalSeconds < 3600L)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format(System.Globalization.CultureInfo.InvariantCulture, "The sub-hourly interval schedule ({0:G4} h interval) was averaged into hourly means; SAM profiles are hour-indexed and cannot carry the sub-hourly detail", intervalSeconds / 3600.0), label);
            }

            return result;
        }

        /// <summary>Returns the duration shared by two half-open time ranges, in seconds.</summary>
        private static long OverlapSeconds(long start_1, long end_1, long start_2, long end_2)
        {
            return Math.Max(0L, Math.Min(end_1, end_2) - Math.Max(start_1, start_2));
        }

        /// <summary>
        /// Averages the piecewise-constant OpenStudio reporting intervals over one SAM hour.
        /// Weighting by overlap (rather than grouping or sampling values) also handles reporting
        /// intervals and start times that do not fall on whole-hour boundaries.
        /// </summary>
        private static double AverageFixedIntervalHour(global::OpenStudio.Vector vector, int count, long intervalSeconds, long seriesStartSeconds, long seriesEndSeconds, long hourStartSeconds, double outOfRangeValue)
        {
            long hourEndSeconds = hourStartSeconds + 3600L;
            long cursor = hourStartSeconds;
            double weightedValue = 0;

            while (cursor < hourEndSeconds)
            {
                double value = outOfRangeValue;
                long segmentEnd = hourEndSeconds;

                if (cursor < seriesStartSeconds)
                {
                    segmentEnd = Math.Min(segmentEnd, seriesStartSeconds);
                }
                else if (cursor < seriesEndSeconds)
                {
                    int index = (int)((cursor - seriesStartSeconds) / intervalSeconds);
                    if (index >= 0 && index < count)
                    {
                        value = vector.__getitem__((uint)index);
                        segmentEnd = Math.Min(segmentEnd, seriesStartSeconds + ((index + 1L) * intervalSeconds));
                    }
                }

                weightedValue += value * (segmentEnd - cursor);
                cursor = segmentEnd;
            }

            return weightedValue / 3600.0;
        }
    }
}
