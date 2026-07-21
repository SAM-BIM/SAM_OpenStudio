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
        /// Expands a ScheduleInterval (fixed or variable) from its time series. Hourly and
        /// coarser intervals map directly; a sub-hourly series is averaged into hourly means and
        /// reported as an approximation, because a SAM profile has no sub-hourly slot to put the
        /// detail in.
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

            // OpenStudio.Vector is the SWIG numeric vector: size()/__getitem__, not Count/[].
            int count = (int)vector.size();
            double[] result = new double[AnnualHourCount];

            if (count == AnnualHourCount || count == AnnualHourCount + 24)
            {
                // Exactly hourly (a leap year contributes 24 extra hours, which SAM drops).
                for (int i = 0; i < AnnualHourCount; i++)
                {
                    result[i] = vector.__getitem__((uint)i);
                }

                return result;
            }

            if (count > AnnualHourCount)
            {
                // Sub-hourly: average whole blocks into each hour. Averaging, not sampling, so a
                // fractional schedule keeps its correct daily total.
                int perHour = count / AnnualHourCount;
                if (perHour < 1)
                {
                    perHour = 1;
                }

                for (int i = 0; i < AnnualHourCount; i++)
                {
                    double sum = 0;
                    int taken = 0;
                    for (int j = 0; j < perHour; j++)
                    {
                        int index = (i * perHour) + j;
                        if (index >= count)
                        {
                            break;
                        }

                        sum += vector.__getitem__((uint)index);
                        taken++;
                    }

                    result[i] = taken == 0 ? 0 : sum / taken;
                }

                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("The sub-hourly interval schedule ({0} values, {1} per hour) was averaged into hourly means; SAM profiles are hour-indexed and cannot carry the sub-hourly detail", count, perHour), label);
                return result;
            }

            // Shorter than a year: hold the pattern by repeating it, and say so.
            for (int i = 0; i < AnnualHourCount; i++)
            {
                result[i] = vector.__getitem__((uint)(i % count));
            }

            openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("The interval schedule carries {0} values, fewer than the {1} hours of a year; the pattern was tiled to fill the year", count, AnnualHourCount), label);
            return result;
        }
    }
}
