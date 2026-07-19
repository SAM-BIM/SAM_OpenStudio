// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Core;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// Review P1-01: day-composed (weekly) profiles must align with the run calendar — the EPW
    /// start day of week (Boston TMYx: Sunday) — not a hard-coded Monday-first assumption.
    /// </summary>
    [TestFixture]
    public class ProfileWeeklyTests
    {
        private static string WeatherPath(string extension)
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "resources", "weather", "USA_MA_Boston-Logan.Intl.AP.725090_TMYx.2004-2018" + extension));
        }

        private static InternalCondition WeeklyInternalCondition(out ProfileLibrary profileLibrary)
        {
            double[] weekday = new double[24];
            for (int i = 0; i < 24; i++)
            {
                weekday[i] = 1;
            }

            double[] weekend = new double[24];

            Profile week = new Profile("Office Week", ProfileType.Occupancy);
            for (int i = 0; i < 5; i++)
            {
                week.Add(new Profile("Weekday", ProfileType.Occupancy, weekday));
            }

            week.Add(new Profile("Weekend", ProfileType.Occupancy, weekend));
            week.Add(new Profile("Weekend", ProfileType.Occupancy, weekend));

            profileLibrary = AnalyticalModelFixtures.CreateProfileLibrary();
            profileLibrary.Add(week);

            InternalCondition result = AnalyticalModelFixtures.CreateOfficeInternalCondition();
            result.SetValue(InternalConditionParameter.OccupancyProfileName, "Office Week");
            return result;
        }

        private static global::OpenStudio.Vector ScheduleValues(global::OpenStudio.Model model)
        {
            foreach (global::OpenStudio.ScheduleFixedInterval schedule in model.getScheduleFixedIntervals())
            {
                if (schedule.nameString().Contains("Office_Week"))
                {
                    return schedule.timeSeries().values();
                }
            }

            return null;
        }

        [Test]
        public void WeeklyProfile_AlignsWithWeatherFileStartDay()
        {
            string epwPath = WeatherPath(".epw");
            Assert.That(File.Exists(epwPath), Is.True, $"Pinned weather fixture missing: {epwPath}");

            InternalCondition internalCondition = WeeklyInternalCondition(out ProfileLibrary profileLibrary);
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.SingleBox(internalConditionOverride: internalCondition, profileLibraryOverride: profileLibrary);

            string outputDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "weekly_alignment");
            OpenStudioConversionResult result = analyticalModel.ToOpenStudio(epwPath, outputDirectory, run: false);

            Assert.That(result.IsValid, Is.True);

            global::OpenStudio.Vector values = ScheduleValues(result.Model);
            Assert.That(values, Is.Not.Null, "Weekly occupancy schedule must exist");
            Assert.That(values.size(), Is.EqualTo(8760));

            // The EPW DATA PERIODS line declares 1 Jan = Sunday. Monday values (1) must therefore
            // start on day 1, and the two weekend days (0) must be days 0, 6 and 7 — not days 5,6.
            Assert.That(values.__getitem__(12), Is.EqualTo(0), "1 Jan is a Sunday — weekend value expected");
            Assert.That(values.__getitem__(24 + 12), Is.EqualTo(1), "2 Jan is a Monday — weekday value expected");
            Assert.That(values.__getitem__(5 * 24 + 12), Is.EqualTo(1), "6 Jan is a Friday — weekday value expected");
            Assert.That(values.__getitem__(6 * 24 + 12), Is.EqualTo(0), "7 Jan is a Saturday — weekend value expected");
            Assert.That(values.__getitem__(7 * 24 + 12), Is.EqualTo(0), "8 Jan is a Sunday — weekend value expected");
        }

        [Test]
        public void WeeklyProfile_MondayFirst_WithoutWeatherFile()
        {
            InternalCondition internalCondition = WeeklyInternalCondition(out ProfileLibrary profileLibrary);
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.SingleBox(internalConditionOverride: internalCondition, profileLibraryOverride: profileLibrary);

            OpenStudioConversionResult result = analyticalModel.ToOpenStudio();
            Assert.That(result.IsValid, Is.True);

            global::OpenStudio.Vector values = ScheduleValues(result.Model);
            Assert.That(values, Is.Not.Null, "Weekly occupancy schedule must exist");

            // Geometry-only conversion keeps the documented Monday-first default.
            Assert.That(values.__getitem__(12), Is.EqualTo(1), "Without a weather file, day 0 is treated as Monday");
            Assert.That(values.__getitem__(5 * 24 + 12), Is.EqualTo(0), "Saturday");
            Assert.That(values.__getitem__(6 * 24 + 12), Is.EqualTo(0), "Sunday");
        }

        [Test]
        public void WeeklyProfile_FirstDayOfWeekOption_OverridesDefault()
        {
            InternalCondition internalCondition = WeeklyInternalCondition(out ProfileLibrary profileLibrary);
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.SingleBox(internalConditionOverride: internalCondition, profileLibraryOverride: profileLibrary);

            Core.OpenStudio.OpenStudioConversionOptions options = new Core.OpenStudio.OpenStudioConversionOptions
            {
                FirstDayOfWeek = System.DayOfWeek.Wednesday,
            };

            OpenStudioConversionResult result = analyticalModel.ToOpenStudio(options);
            Assert.That(result.IsValid, Is.True);

            global::OpenStudio.Vector values = ScheduleValues(result.Model);
            Assert.That(values, Is.Not.Null);

            // Day 0 treated as Wednesday (weekday), days 3/4 (Sat/Sun) are the weekend.
            Assert.That(values.__getitem__(12), Is.EqualTo(1), "Wednesday — weekday");
            Assert.That(values.__getitem__(3 * 24 + 12), Is.EqualTo(0), "Saturday — weekend");
            Assert.That(values.__getitem__(4 * 24 + 12), Is.EqualTo(0), "Sunday — weekend");
        }
    }
}
