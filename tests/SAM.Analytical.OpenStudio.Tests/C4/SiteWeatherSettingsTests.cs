// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Core;
using System;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// C4: site, weather, design days and simulation settings — north rotation (radians →
    /// degrees, solar-gain shift proven end-to-end), SAM Location precedence, ground-temperature
    /// precedence, DDY import with sizing enablement and the annual environment-period filter,
    /// custom RunPeriod/timestep, leap-year schedules and daylight saving time.
    /// </summary>
    [TestFixture]
    public class SiteWeatherSettingsTests
    {
        private static string WeatherPath(string extension)
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "resources", "weather", "USA_MA_Boston-Logan.Intl.AP.725090_TMYx.2004-2018" + extension));
        }

        private static string Convert_NoRun(AnalyticalModel analyticalModel, Core.OpenStudio.OpenStudioConversionOptions options, out OpenStudioConversionResult result)
        {
            string epwPath = WeatherPath(".epw");
            Assert.That(File.Exists(epwPath), Is.True, $"Pinned weather fixture missing: {epwPath}");

            string outputDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "c4_convert", Guid.NewGuid().ToString("N").Substring(0, 8));
            result = analyticalModel.ToOpenStudio(epwPath, outputDirectory, openStudioConversionOptions: options, run: false);
            return outputDirectory;
        }

        [Test]
        public void NorthAngle_FromSamModel_RotatesBuilding_RadiansToDegrees()
        {
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.SingleBox();
            analyticalModel.SetValue(AnalyticalModelParameter.NorthAngle, Math.PI / 2);

            Convert_NoRun(analyticalModel, null, out OpenStudioConversionResult result);

            Assert.That(result.Model.getBuilding().northAxis(), Is.EqualTo(90).Within(1e-9), "SAM NorthAngle is stored in radians; 90° expected");

            Convert_NoRun(analyticalModel, new Core.OpenStudio.OpenStudioConversionOptions { NorthAngleDegrees = 45 }, out OpenStudioConversionResult overridden);
            Assert.That(overridden.Model.getBuilding().northAxis(), Is.EqualTo(45).Within(1e-9), "The explicit option overrides the SAM value");
        }

        [Test]
        public void Location_OverridesEpwCoordinates()
        {
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.SingleBox();
            AnalyticalModel located = new AnalyticalModel(analyticalModel, new Location("SAM Test Location", -0.1278, 51.5074, 11.0));

            Convert_NoRun(located, null, out OpenStudioConversionResult result);

            global::OpenStudio.Site site = result.Model.getSite();
            Assert.That(site.latitude(), Is.EqualTo(51.5074).Within(1e-9), "SAM Location takes precedence over the EPW header");
            Assert.That(site.longitude(), Is.EqualTo(-0.1278).Within(1e-9));
            Assert.That(site.elevation(), Is.EqualTo(11.0).Within(1e-9));
            Assert.That(result.Diagnostics.Any(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Information && d.Message.Contains("SAM model Location")), Is.True, "The override is never silent");
        }

        [Test]
        public void GroundTemperatures_ComeFromEpwHeader_WhenNoSamWeatherData()
        {
            Convert_NoRun(AnalyticalModelFixtures.SingleBox(), null, out OpenStudioConversionResult result);

            Assert.That(result.Diagnostics.Any(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Information && d.Message.Contains("GROUND TEMPERATURES")), Is.True, "The pinned Boston EPW carries a GROUND TEMPERATURES header");

            string groundLine = File.ReadLines(WeatherPath(".epw")).First(x => x.IndexOf("GROUND TEMPERATURES", StringComparison.OrdinalIgnoreCase) >= 0);
            string[] values = groundLine.Substring(groundLine.IndexOf(',') + 1).Split(',');
            double expectedJanuary = double.Parse(values[5], System.Globalization.CultureInfo.InvariantCulture); // count, depth, conductivity, density, specific heat, then 12 temperatures (first set is 0.5 m)

            Assert.That(result.Model.getSiteGroundTemperatureBuildingSurface().januaryGroundTemperature(), Is.EqualTo(expectedJanuary).Within(1e-9), "EPW-header ground temperatures must reach the model");
        }

        [Test]
        public void GroundTemperatures_SamWeatherData_TakesPrecedence()
        {
            double[] temperatures = Enumerable.Range(1, 12).Select(x => (double)x).ToArray();
            Weather.GroundTemperature groundTemperature = new Weather.GroundTemperature(0.5, double.NaN, double.NaN, double.NaN, temperatures[0], temperatures[1], temperatures[2], temperatures[3], temperatures[4], temperatures[5], temperatures[6], temperatures[7], temperatures[8], temperatures[9], temperatures[10], temperatures[11]);

            Weather.WeatherData weatherData = new Weather.WeatherData(42.36, -71.01, 6.0);
            weatherData.SetValue(Weather.WeatherDataParameter.GroundTemperatures, new SAMCollection<Weather.GroundTemperature>(groundTemperature));

            AnalyticalModel analyticalModel = AnalyticalModelFixtures.SingleBox();
            analyticalModel.SetValue(AnalyticalModelParameter.WeatherData, weatherData);

            Convert_NoRun(analyticalModel, null, out OpenStudioConversionResult result);

            Assert.That(result.Model.getSiteGroundTemperatureBuildingSurface().januaryGroundTemperature(), Is.EqualTo(1.0).Within(1e-9), "SAM WeatherData takes precedence over the EPW header");
            Assert.That(result.Model.getSiteGroundTemperatureBuildingSurface().decemberGroundTemperature(), Is.EqualTo(12.0).Within(1e-9));
            Assert.That(result.Diagnostics.Any(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Information && d.Message.Contains("SAM model WeatherData")), Is.True);
        }

        [Test]
        public void DdyImport_AddsDesignDays_EnablesSizing()
        {
            Core.OpenStudio.OpenStudioConversionOptions options = new Core.OpenStudio.OpenStudioConversionOptions { DdyPath = WeatherPath(".ddy") };

            Convert_NoRun(AnalyticalModelFixtures.SingleBox(), options, out OpenStudioConversionResult result);

            Assert.That(result.Model.getDesignDays().Count, Is.GreaterThanOrEqualTo(2), "Heating 99.6% + cooling 0.4% design days imported");
            Assert.That(result.Model.getDesignDays().Count(x => x.nameString().Contains("99.6%") || x.nameString().Contains("0.4%")), Is.EqualTo(result.Model.getDesignDays().Count), "Default filter keeps only the 99.6/0.4 pair");
            Assert.That(result.Model.getSimulationControl().runSimulationforSizingPeriods(), Is.True, "Sizing periods enabled when design days are imported");
            Assert.That(result.Model.getSimulationControl().doZoneSizingCalculation(), Is.True);
        }

        [Test]
        public void DdyImport_ImportAllDesignDays()
        {
            Core.OpenStudio.OpenStudioConversionOptions options = new Core.OpenStudio.OpenStudioConversionOptions { DdyPath = WeatherPath(".ddy"), ImportAllDesignDays = true };

            Convert_NoRun(AnalyticalModelFixtures.SingleBox(), options, out OpenStudioConversionResult result);

            Assert.That(result.Model.getDesignDays().Count, Is.GreaterThan(2), "All DDY design days imported");
        }

        [Test]
        public void CustomRunPeriod_And_Timestep_And_DaylightSaving()
        {
            Core.OpenStudio.OpenStudioConversionOptions options = new Core.OpenStudio.OpenStudioConversionOptions
            {
                RunPeriodBeginMonth = 6,
                RunPeriodBeginDay = 1,
                RunPeriodEndMonth = 8,
                RunPeriodEndDay = 31,
                TimestepsPerHour = 4,
                DaylightSavingsTime = true,
                CalendarYear = 2024,
                IsLeapYear = true,
            };

            Convert_NoRun(AnalyticalModelFixtures.SingleBox(), options, out OpenStudioConversionResult result);

            global::OpenStudio.RunPeriod runPeriod = result.Model.getRunPeriod();
            Assert.That(runPeriod.getBeginMonth(), Is.EqualTo(6));
            Assert.That(runPeriod.getBeginDayOfMonth(), Is.EqualTo(1));
            Assert.That(runPeriod.getEndMonth(), Is.EqualTo(8));
            Assert.That(runPeriod.getEndDayOfMonth(), Is.EqualTo(31));
            Assert.That(result.Model.getTimestep().numberOfTimestepsPerHour(), Is.EqualTo(4));
            Assert.That(result.Model.getOptionalRunPeriodControlDaylightSavingTime().isNull(), Is.False, "DST object present when optioned on");
            Assert.That(result.Model.calendarYear().get(), Is.EqualTo(2024));
            Assert.That(result.Model.isLeapYear(), Is.True);

            Convert_NoRun(AnalyticalModelFixtures.SingleBox(), null, out OpenStudioConversionResult defaults);
            Assert.That(defaults.Model.getOptionalRunPeriodControlDaylightSavingTime().isNull(), Is.True, "DST stays OFF by default (energy-model convention; SAM carries no DST data)");
        }

        [Test]
        public void LeapYear_Generates8784ValueSchedules()
        {
            Core.OpenStudio.OpenStudioConversionOptions options = new Core.OpenStudio.OpenStudioConversionOptions { IsLeapYear = true };

            Convert_NoRun(AnalyticalModelFixtures.SingleBox(), options, out OpenStudioConversionResult result);

            Assert.That(result.Model.isLeapYear(), Is.True);

            global::OpenStudio.ScheduleFixedInterval schedule = result.Model.getScheduleFixedIntervals().First(x => x.nameString().Contains("Heating"));
            Assert.That((int)schedule.timeSeries().values().size(), Is.EqualTo(8784), "Leap-year runs generate 8784-value schedules (8760 store + repeated 31 Dec)");
        }

        [Test]
        [Category("Simulation")]
        public void NorthRotation_ShiftsSolarGains_EndToEnd()
        {
            string epwPath = WeatherPath(".epw");
            Assert.That(File.Exists(epwPath), Is.True);

            string baselineDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "c4_north_0");
            string rotatedDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "c4_north_180");
            foreach (string directory in new[] { baselineDirectory, rotatedDirectory })
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }

            OpenStudioConversionResult baseline = AnalyticalModelFixtures.SingleBox().ToOpenStudio(epwPath, baselineDirectory);
            OpenStudioConversionResult rotated = AnalyticalModelFixtures.SingleBox().ToOpenStudio(epwPath, rotatedDirectory, openStudioConversionOptions: new Core.OpenStudio.OpenStudioConversionOptions { NorthAngleDegrees = 180 });

            Assert.That(baseline.RunResult?.Success, Is.True);
            Assert.That(rotated.RunResult?.Success, Is.True);
            Assert.That(rotated.Model.getBuilding().northAxis(), Is.EqualTo(180).Within(1e-9));

            // The box's only window faces south at 0°: rotating 180° turns it north — less
            // winter solar gain → strictly more heating, strictly less cooling.
            TestContext.Out.WriteLine($"Heating 0°: {baseline.Loads.TotalHeating:0.0} kWh, 180°: {rotated.Loads.TotalHeating:0.0} kWh; Cooling 0°: {baseline.Loads.TotalCooling:0.0} kWh, 180°: {rotated.Loads.TotalCooling:0.0} kWh");
            Assert.That(rotated.Loads.TotalHeating, Is.GreaterThan(baseline.Loads.TotalHeating), "North-facing window → more heating");
            Assert.That(rotated.Loads.TotalCooling, Is.LessThan(baseline.Loads.TotalCooling), "North-facing window → less cooling");
        }

        [Test]
        [Category("Simulation")]
        public void DesignDaysRun_AnnualResultsExcludeSizingPeriods()
        {
            string epwPath = WeatherPath(".epw");
            Assert.That(File.Exists(epwPath), Is.True);

            string baselineDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "c4_annual_baseline");
            string designDayDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "c4_annual_designdays");
            foreach (string directory in new[] { baselineDirectory, designDayDirectory })
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }

            OpenStudioConversionResult baseline = AnalyticalModelFixtures.SingleBox().ToOpenStudio(epwPath, baselineDirectory);
            OpenStudioConversionResult withDesignDays = AnalyticalModelFixtures.SingleBox().ToOpenStudio(epwPath, designDayDirectory, openStudioConversionOptions: new Core.OpenStudio.OpenStudioConversionOptions { DdyPath = WeatherPath(".ddy") });

            Assert.That(baseline.RunResult?.Success, Is.True);
            Assert.That(withDesignDays.RunResult?.Success, Is.True);
            Assert.That(withDesignDays.Model.getDesignDays().Count, Is.GreaterThanOrEqualTo(2));

            // The environment-period filter must exclude the sizing days from the annual sums:
            // identical model and weather → identical annual loads.
            TestContext.Out.WriteLine($"Baseline: H {baseline.Loads.TotalHeating:0.000} / C {baseline.Loads.TotalCooling:0.000} kWh; with design days: H {withDesignDays.Loads.TotalHeating:0.000} / C {withDesignDays.Loads.TotalCooling:0.000} kWh");
            Assert.That(withDesignDays.Loads.TotalHeating, Is.EqualTo(baseline.Loads.TotalHeating).Within(Math.Max(1.0, baseline.Loads.TotalHeating * 0.01)), "Annual heating must not double-count design days");
            Assert.That(withDesignDays.Loads.TotalCooling, Is.EqualTo(baseline.Loads.TotalCooling).Within(Math.Max(1.0, baseline.Loads.TotalCooling * 0.01)), "Annual cooling must not double-count design days");
        }
    }
}
