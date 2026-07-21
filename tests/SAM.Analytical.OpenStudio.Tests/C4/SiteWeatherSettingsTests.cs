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
        public void Location_InvalidCoordinates_KeepEpwSite_AndWarn()
        {
            // Review P2-03: a SAM Location carrying NaN or out-of-range coordinates must not
            // reach OS:Site — the EPW site is kept and the rejection is named in a warning.
            Convert_NoRun(AnalyticalModelFixtures.SingleBox(), null, out OpenStudioConversionResult baseline);
            double epwLatitude = baseline.Model.getSite().latitude();
            double epwLongitude = baseline.Model.getSite().longitude();

            AnalyticalModel nanModel = new AnalyticalModel(AnalyticalModelFixtures.SingleBox(), new Location("NaN Location", -0.1278, double.NaN, 11.0));
            Convert_NoRun(nanModel, null, out OpenStudioConversionResult nanResult);

            Assert.That(nanResult.Model.getSite().latitude(), Is.EqualTo(epwLatitude).Within(1e-9), "NaN latitude: the EPW site is kept");
            Assert.That(nanResult.Model.getSite().longitude(), Is.EqualTo(epwLongitude).Within(1e-9));
            Assert.That(nanResult.Diagnostics.Any(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning && d.Message.Contains("Location") && d.Message.Contains("NaN")), Is.True, "The rejected values are named");
            Assert.That(nanResult.Diagnostics.Any(d => d.Message.Contains("Site coordinates taken from the SAM model Location")), Is.False, "No override-info diagnostic for a rejected Location");

            AnalyticalModel outOfRangeModel = new AnalyticalModel(AnalyticalModelFixtures.SingleBox(), new Location("Out Of Range", 200.0, 51.5074, 11.0));
            Convert_NoRun(outOfRangeModel, null, out OpenStudioConversionResult outOfRangeResult);

            Assert.That(outOfRangeResult.Model.getSite().longitude(), Is.EqualTo(epwLongitude).Within(1e-9), "Longitude 200 is out of range: the EPW site is kept");
            Assert.That(outOfRangeResult.Diagnostics.Any(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning && d.Message.Contains("200")), Is.True);
        }

        [Test]
        public void Location_NonFiniteElevation_KeepsEpwElevation_ButOverridesCoordinates()
        {
            // Valid latitude/longitude with a NaN elevation: coordinates override, the EPW
            // elevation stays (never a NaN into OS:Site), and the message says so.
            Convert_NoRun(AnalyticalModelFixtures.SingleBox(), null, out OpenStudioConversionResult baseline);
            double epwElevation = baseline.Model.getSite().elevation();

            AnalyticalModel model = new AnalyticalModel(AnalyticalModelFixtures.SingleBox(), new Location("No Elevation", -0.1278, 51.5074, double.NaN));
            Convert_NoRun(model, null, out OpenStudioConversionResult result);

            Assert.That(result.Model.getSite().latitude(), Is.EqualTo(51.5074).Within(1e-9), "Valid coordinates still override");
            Assert.That(result.Model.getSite().elevation(), Is.EqualTo(epwElevation).Within(1e-9), "NaN elevation: the EPW elevation is kept");
            Assert.That(result.Diagnostics.Any(d => d.Message.Contains("SAM model Location") && d.Message.Contains("elevation kept from the EPW header")), Is.True);
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
            // Review P1-02: ASHRAE DDYs name the annual pair "… Ann Htg 99.6% Condns DB" and
            // "… Ann Clg .4% Condns DB=>MWB" (no leading zero). The default import must select
            // exactly that pair — never the humidification (Hum_n) or wind (Htg Wind) 99.6% days.
            Core.OpenStudio.OpenStudioConversionOptions options = new Core.OpenStudio.OpenStudioConversionOptions { DdyPath = WeatherPath(".ddy") };

            Convert_NoRun(AnalyticalModelFixtures.SingleBox(), options, out OpenStudioConversionResult result);

            System.Collections.Generic.List<string> names = result.Model.getDesignDays().Select(x => x.nameString()).ToList();
            Assert.That(names.Count, Is.EqualTo(2), "Exactly the heating 99.6% + cooling .4% pair: " + string.Join(" | ", names));
            Assert.That(names.Count(x => System.Text.RegularExpressions.Regex.IsMatch(x, @"Ann\s+Htg\s+99\.6\s*%\s+Condns\s+DB", System.Text.RegularExpressions.RegexOptions.IgnoreCase)), Is.EqualTo(1), "The annual heating 99.6% dry-bulb day is imported");
            Assert.That(names.Count(x => System.Text.RegularExpressions.Regex.IsMatch(x, @"Ann\s+Clg\s+0?\.4\s*%\s+Condns\s+DB\s*=>\s*M(C)?WB", System.Text.RegularExpressions.RegexOptions.IgnoreCase)), Is.EqualTo(1), "The annual cooling .4% DB=>MWB day is imported");
            Assert.That(names.Any(x => x.IndexOf("Hum_n", StringComparison.OrdinalIgnoreCase) >= 0 || x.IndexOf("Wind", StringComparison.OrdinalIgnoreCase) >= 0), Is.False, "No humidification or wind design days in the default pair");
            Assert.That(result.Model.getSimulationControl().runSimulationforSizingPeriods(), Is.True, "Sizing periods enabled when design days are imported");
            Assert.That(result.Model.getSimulationControl().doZoneSizingCalculation(), Is.True);
        }

        [Test]
        public void DdyImport_MissingCoolingDay_ImportsHeatingAndWarns()
        {
            // A DDY carrying only the heating day (site-specific files exist without the
            // cooling row): the heating day imports and the missing cooling side is reported —
            // never a silent fallback onto humidification/wind days.
            string[] lines = File.ReadAllLines(WeatherPath(".ddy"));
            System.Text.StringBuilder heatingOnly = new System.Text.StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("SizingPeriod:DesignDay", StringComparison.OrdinalIgnoreCase) && i + 1 < lines.Length && lines[i + 1].Contains("Ann Htg 99.6% Condns DB"))
                {
                    for (int j = i; j < lines.Length; j++)
                    {
                        heatingOnly.AppendLine(lines[j]);

                        // The object terminator ';' precedes an inline "!- field name" comment.
                        string code = lines[j];
                        int commentIndex = code.IndexOf('!');
                        if (commentIndex >= 0)
                        {
                            code = code.Substring(0, commentIndex);
                        }

                        if (code.TrimEnd().EndsWith(";"))
                        {
                            break;
                        }
                    }

                    break;
                }
            }

            string heatingOnlyPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "c4_heating_only.ddy");
            File.WriteAllText(heatingOnlyPath, heatingOnly.ToString());

            Core.OpenStudio.OpenStudioConversionOptions options = new Core.OpenStudio.OpenStudioConversionOptions { DdyPath = heatingOnlyPath };
            Convert_NoRun(AnalyticalModelFixtures.SingleBox(), options, out OpenStudioConversionResult result);

            Assert.That(result.Model.getDesignDays().Count, Is.EqualTo(1), "The heating day imports on its own");
            Assert.That(result.Model.getDesignDays().First().nameString(), Does.Contain("Ann Htg 99.6% Condns DB"));
            Assert.That(result.Diagnostics.Any(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning && d.Message.Contains("cooling")), Is.True, "The missing cooling design day is reported");
        }

        [Test]
        public void DdyImport_ImportAllDesignDays()
        {
            Core.OpenStudio.OpenStudioConversionOptions options = new Core.OpenStudio.OpenStudioConversionOptions { DdyPath = WeatherPath(".ddy"), ImportAllDesignDays = true };

            Convert_NoRun(AnalyticalModelFixtures.SingleBox(), options, out OpenStudioConversionResult result);

            Assert.That(result.Model.getDesignDays().Count, Is.GreaterThan(2), "All DDY design days imported");
        }

        [Test]
        public void NonHourlyOutputFrequency_WarnsThatPeaksAssumeHourly()
        {
            // Review P2-02 (mitigation): the extractor converts per-row energies with a fixed
            // 3600 s interval — a non-hourly reporting frequency mis-scales peaks and series.
            // Until frequency-aware extraction lands, the conversion must say so out loud.
            Core.OpenStudio.OpenStudioConversionOptions options = new Core.OpenStudio.OpenStudioConversionOptions { OutputVariableFrequency = "Timestep" };

            Convert_NoRun(AnalyticalModelFixtures.SingleBox(), options, out OpenStudioConversionResult result);

            Assert.That(result.IsValid, Is.True, "A non-hourly frequency stays usable (annual sums are unaffected)");
            Assert.That(result.Diagnostics.Count(d => d.Code == "SAM-OS-RUN-003" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning && d.Message.Contains("Timestep")), Is.EqualTo(1), "One warning naming the non-hourly frequency");
            Assert.That(result.Model.getOutputVariables().All(x => x.reportingFrequency() == "Timestep"), Is.True, "The requested frequency is still honoured on the Output:Variable requests");

            Convert_NoRun(AnalyticalModelFixtures.SingleBox(), new Core.OpenStudio.OpenStudioConversionOptions { OutputVariableFrequency = "Hourly" }, out OpenStudioConversionResult hourly);
            Assert.That(hourly.Diagnostics.Any(d => d.Code == "SAM-OS-RUN-003"), Is.False, "The default hourly frequency stays silent");
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
        public void ShadingCalculationMethod_DefaultsToPixelCounting()
        {
            // PixelCounting default: PolygonClipping flags every non-convex casting surface as a
            // severe DetermineShadowingCombinations error; PixelCounting has no concavity
            // limitation (same default as Ladybug Tools' FullInterior workflows).
            Convert_NoRun(AnalyticalModelFixtures.SingleBox(), null, out OpenStudioConversionResult result);
            Assert.That(result.Model.getShadowCalculation().shadingCalculationMethod(), Is.EqualTo("PixelCounting"), "PixelCounting is the default shading calculation method");

            Convert_NoRun(AnalyticalModelFixtures.SingleBox(), new Core.OpenStudio.OpenStudioConversionOptions { ShadingCalculationMethod = "PolygonClipping" }, out OpenStudioConversionResult overridden);
            Assert.That(overridden.Model.getShadowCalculation().shadingCalculationMethod(), Is.EqualTo("PolygonClipping"), "An explicit method is honoured");

            Convert_NoRun(AnalyticalModelFixtures.SingleBox(), new Core.OpenStudio.OpenStudioConversionOptions { ShadingCalculationMethod = null }, out OpenStudioConversionResult untouched);
            Assert.That(untouched.Model.getShadowCalculation().shadingCalculationMethod(), Is.EqualTo("PolygonClipping"), "Null leaves the OpenStudio default (PolygonClipping) untouched");
        }

        [Test]
        public void ShadingCalculationMethod_InvalidValue_Warns()
        {
            Convert_NoRun(AnalyticalModelFixtures.SingleBox(), new Core.OpenStudio.OpenStudioConversionOptions { ShadingCalculationMethod = "NotAMethod" }, out OpenStudioConversionResult result);

            Assert.That(result.Diagnostics.Count(d => d.Code == "SAM-OS-SET-001" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning && d.Message.Contains("NotAMethod")), Is.EqualTo(1), "One warning naming the rejected method");
            Assert.That(result.IsValid, Is.True, "A rejected method stays a warning — the OpenStudio default applies");
        }

        [Test]
        public void NonConvexCasting_PolygonClippingNamesThePanel_PixelCountingAdvisesAboutGpuFallback()
        {
            // Real-model reproduction (HungaryHouse): an L-shaped exposed roof is non-convex
            // and casts shadows — PolygonClipping reports it as a severe
            // DetermineShadowingCombinations error. The conversion names the SAM panel so it
            // can be split into convex parts; PixelCounting itself has no concavity
            // limitation, but a GPU-less machine silently falls back to PolygonClipping, so an
            // Information-level advisory is still emitted.
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.LShapedBox();

            Convert_NoRun(analyticalModel, new Core.OpenStudio.OpenStudioConversionOptions { ShadingCalculationMethod = "PolygonClipping" }, out OpenStudioConversionResult polygonClipping);
            Assert.That(polygonClipping.Diagnostics.Count(d => d.Code == "SAM-OS-GEO-003" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning), Is.EqualTo(1), "Exactly the L-shaped roof is named (slab is ground-coupled, walls are convex)");

            Convert_NoRun(analyticalModel, null, out OpenStudioConversionResult pixelCounting);
            Assert.That(pixelCounting.Diagnostics.Count(d => d.Code == "SAM-OS-GEO-003" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Information && d.Message.Contains("GPU")), Is.EqualTo(1), "PixelCounting (default): advisory about the no-GPU PolygonClipping fallback");
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
            Assert.That(withDesignDays.Model.getDesignDays().Count, Is.EqualTo(2), "Exactly the heating 99.6% + cooling .4% pair (review P1-02)");

            // Review P1-02: the sizing environments actually simulated must be the heating
            // 99.6% dry-bulb day AND the cooling .4% DB=>MWB day — no humidification/wind days.
            System.Collections.Generic.List<string> sizingEnvironments = new System.Collections.Generic.List<string>();
            using (System.Data.SQLite.SQLiteConnection connection = new System.Data.SQLite.SQLiteConnection("Data Source=" + withDesignDays.RunResult.SqlPath + ";Read Only=True"))
            {
                connection.Open();
                using (System.Data.SQLite.SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT EnvironmentName FROM EnvironmentPeriods WHERE EnvironmentType IN (1, 2)";
                    using (System.Data.SQLite.SQLiteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            sizingEnvironments.Add(reader.GetString(0));
                        }
                    }
                }
            }

            Assert.That(sizingEnvironments.Count, Is.EqualTo(2), "Two sizing environments: " + string.Join(" | ", sizingEnvironments));
            Assert.That(sizingEnvironments.Count(x => x.IndexOf("HTG 99.6%", StringComparison.OrdinalIgnoreCase) >= 0 && x.IndexOf("WIND", StringComparison.OrdinalIgnoreCase) < 0), Is.EqualTo(1), "Heating sizing environment present");
            Assert.That(sizingEnvironments.Count(x => x.IndexOf("CLG .4%", StringComparison.OrdinalIgnoreCase) >= 0), Is.EqualTo(1), "Cooling sizing environment present");
            Assert.That(sizingEnvironments.Any(x => x.IndexOf("HUM_N", StringComparison.OrdinalIgnoreCase) >= 0 || x.IndexOf("WIND", StringComparison.OrdinalIgnoreCase) >= 0), Is.False, "No humidification/wind sizing environments");

            // The environment-period filter must exclude the sizing days from the annual sums:
            // identical model and weather → identical annual loads.
            TestContext.Out.WriteLine($"Baseline: H {baseline.Loads.TotalHeating:0.000} / C {baseline.Loads.TotalCooling:0.000} kWh; with design days: H {withDesignDays.Loads.TotalHeating:0.000} / C {withDesignDays.Loads.TotalCooling:0.000} kWh");
            Assert.That(withDesignDays.Loads.TotalHeating, Is.EqualTo(baseline.Loads.TotalHeating).Within(Math.Max(1.0, baseline.Loads.TotalHeating * 0.01)), "Annual heating must not double-count design days");
            Assert.That(withDesignDays.Loads.TotalCooling, Is.EqualTo(baseline.Loads.TotalCooling).Within(Math.Max(1.0, baseline.Loads.TotalCooling * 0.01)), "Annual cooling must not double-count design days");
        }
    }
}
