// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Core;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// Review P1-02: profiles longer than 8760 hours (leap years) and multi-day flat sequences
    /// must follow SAM's native expansion semantics (explicit truncation with a warning, or
    /// cyclic tiling at the profile's own period) — never silently block-averaged into one
    /// synthetic "average day".
    /// </summary>
    [TestFixture]
    public class ProfileAnnualTests
    {
        private static OpenStudioConversionResult Convert(Profile profile)
        {
            ProfileLibrary profileLibrary = AnalyticalModelFixtures.CreateProfileLibrary();
            profileLibrary.Add(profile);

            InternalCondition internalCondition = AnalyticalModelFixtures.CreateOfficeInternalCondition();
            internalCondition.SetValue(InternalConditionParameter.OccupancyProfileName, profile.Name);

            return AnalyticalModelFixtures.SingleBox(internalConditionOverride: internalCondition, profileLibraryOverride: profileLibrary).ToOpenStudio();
        }

        private static global::OpenStudio.Vector ScheduleValues(global::OpenStudio.Model model, string namePart)
        {
            foreach (global::OpenStudio.ScheduleFixedInterval schedule in model.getScheduleFixedIntervals())
            {
                if (schedule.nameString().Contains(namePart))
                {
                    return schedule.timeSeries().values();
                }
            }

            return null;
        }

        [Test]
        public void LeapYearProfile_IsTruncatedWithWarning_NotSquashed()
        {
            double[] leap = new double[8784];
            for (int i = 0; i < leap.Length; i++)
            {
                int hour = i % 24;
                leap[i] = hour >= 8 && hour < 18 ? 1 : 0;
            }

            leap[1] = 1; // marker: any averaging of 366-value blocks would dilute this to ~0.42

            OpenStudioConversionResult result = Convert(new Profile("Leap Occupancy", ProfileType.Occupancy, leap));

            global::OpenStudio.Vector values = ScheduleValues(result.Model, "Leap_Occupancy");
            Assert.That(values, Is.Not.Null, "Leap-year schedule must exist");
            Assert.That(values.size(), Is.EqualTo(8760), "Schedules are 365-day (documented non-leap policy)");

            Assert.That(values.__getitem__(0), Is.EqualTo(0), "Hour 0 must be the source value, not a 366-value average");
            Assert.That(values.__getitem__(1), Is.EqualTo(1), "The marker hour must survive — the profile must be truncated, not averaged");
            Assert.That(values.__getitem__(12), Is.EqualTo(1), "Midday occupied");
            Assert.That(values.__getitem__(8759), Is.EqualTo(leap[8759]), "Last hour equals source hour 8759");

            Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OS-SCH-001" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning && d.Message.Contains("8784")), Is.True, "Truncation must be reported as a warning, never silent");
        }

        [Test]
        public void WeeklyFlatProfile_IsTiledHourForHour_NotAveraged()
        {
            // Flat 168-hour weekly sequence (no sub-profiles): occupied 08:00–17:59 on the first
            // five days only. SAM's own yearly expansion (GetYearlyValues / the wrapping indexer)
            // tiles this hour-for-hour at a 168-hour period.
            double[] week = new double[168];
            for (int day = 0; day < 5; day++)
            {
                for (int hour = 8; hour < 18; hour++)
                {
                    week[day * 24 + hour] = 1;
                }
            }

            OpenStudioConversionResult result = Convert(new Profile("Flat Week", ProfileType.Occupancy, week));

            global::OpenStudio.Vector values = ScheduleValues(result.Model, "Flat_Week");
            Assert.That(values, Is.Not.Null, "Weekly schedule must exist");
            Assert.That(values.size(), Is.EqualTo(8760));

            Assert.That(values.__getitem__(12), Is.EqualTo(1), "Day 0 noon occupied — not a 7-day average (~0.71)");
            Assert.That(values.__getitem__(5 * 24 + 12), Is.EqualTo(0), "Day 5 noon unoccupied");
            Assert.That(values.__getitem__(6 * 24 + 12), Is.EqualTo(0), "Day 6 noon unoccupied");
            Assert.That(values.__getitem__(7 * 24 + 12), Is.EqualTo(1), "Day 7 starts the second weekly cycle — occupied again");
        }

        [Test]
        public void DailyProfile_TilesUnchanged()
        {
            double[] daily = new double[24];
            for (int i = 8; i <= 17; i++)
            {
                daily[i] = 1;
            }

            OpenStudioConversionResult result = Convert(new Profile("Daily", ProfileType.Occupancy, daily));

            global::OpenStudio.Vector values = ScheduleValues(result.Model, "Daily");
            Assert.That(values, Is.Not.Null);
            Assert.That(values.__getitem__(3), Is.EqualTo(0));
            Assert.That(values.__getitem__(10), Is.EqualTo(1));
            Assert.That(values.__getitem__(364 * 24 + 10), Is.EqualTo(1));
        }

        [Test]
        public void ProfileWithGaps_RaisesError_NeverSilentNaN()
        {
            Profile profile = new Profile("Gappy", ProfileType.Occupancy);
            profile.Update(0, 1.0);
            profile.Update(5, 1.0);

            OpenStudioConversionResult result = Convert(profile);

            Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OS-SCH-001" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error && d.Message.Contains("gap")), Is.True, "A profile with missing hours must raise a diagnosed error — NaN must never reach the schedule");
        }
    }
}
