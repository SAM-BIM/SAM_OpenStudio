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
    /// Embedded weather/design-day sources (human-Rhino validation, fix 1): the AnalyticalModel
    /// WeatherData and HeatingDesignDays/CoolingDesignDays feed the conversion when no explicit
    /// _epwPath/ddyPath_ exists; explicit paths override the embedded sources (never merged);
    /// a run without any usable annual EPW source is a blocking error; conversion-only stays
    /// valid with a warning.
    /// </summary>
    [TestFixture]
    public class EmbeddedWeatherDesignDaysTests
    {
        private static string WeatherPath(string extension)
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "resources", "weather", "USA_MA_Boston-Logan.Intl.AP.725090_TMYx.2004-2018" + extension));
        }

        private static string OutputDirectory(string name)
        {
            return Path.Combine(TestContext.CurrentContext.WorkDirectory, "c4_embedded", name + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        }

        private static AnalyticalModel ModelWithEmbeddedWeather()
        {
            string epwPath = WeatherPath(".epw");
            Assert.That(File.Exists(epwPath), Is.True, $"Pinned weather fixture missing: {epwPath}");

            Weather.WeatherData weatherData = Weather.Convert.ToSAM(epwPath);
            Assert.That(weatherData, Is.Not.Null);
            Assert.That(weatherData.WeatherYears, Is.Not.Null.And.Count.GreaterThan(0), "The pinned EPW must import hourly weather years");

            DesignDay heatingDesignDay = weatherData.HeatingDesignDay();
            DesignDay coolingDesignDay = weatherData.CoolingDesignDay();
            Assert.That(heatingDesignDay, Is.Not.Null);
            Assert.That(coolingDesignDay, Is.Not.Null);

            AnalyticalModel analyticalModel = AnalyticalModelFixtures.SingleBox();
            Analytical.Modify.UpdateWeather(analyticalModel, weatherData, new System.Collections.Generic.List<DesignDay> { coolingDesignDay }, new System.Collections.Generic.List<DesignDay> { heatingDesignDay });
            return analyticalModel;
        }

        [Test]
        public void EmbeddedDesignDays_UsedWhenNoDdyPath()
        {
            AnalyticalModel analyticalModel = ModelWithEmbeddedWeather();

            OpenStudioConversionResult result = analyticalModel.ToOpenStudio(WeatherPath(".epw"), OutputDirectory("embedded_dd"), run: false);

            System.Collections.Generic.List<global::OpenStudio.DesignDay> designDays = result.Model.getDesignDays().ToList();
            Assert.That(designDays.Count, Is.EqualTo(2), "One heating + one cooling embedded design day: " + string.Join(" | ", designDays.Select(x => x.nameString())));
            Assert.That(designDays.Count(x => x.dayType() == "WinterDesignDay"), Is.EqualTo(1), "The embedded heating day becomes a WinterDesignDay");
            Assert.That(designDays.Count(x => x.dayType() == "SummerDesignDay"), Is.EqualTo(1), "The embedded cooling day becomes a SummerDesignDay");
            Assert.That(result.Model.getSimulationControl().runSimulationforSizingPeriods(), Is.True, "Embedded design days enable sizing periods");
            Assert.That(result.Diagnostics.Any(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Information && d.Message.Contains("Design-day source: AnalyticalModel heating/cooling design days")), Is.True, "The embedded source is named");
            Assert.That(result.DesignDaySource, Is.EqualTo(Core.OpenStudio.OpenStudioDesignDaySource.EmbeddedModel), "The established basis is reported structurally (provenance reads this, not the diagnostic text)");
        }

        [Test]
        public void EmbeddedDesignDay_ValuesTranslate()
        {
            AnalyticalModel analyticalModel = ModelWithEmbeddedWeather();
            analyticalModel.TryGetValue(AnalyticalModelParameter.HeatingDesignDays, out SAMCollection<DesignDay> heatingDesignDays);
            DesignDay heatingDesignDay = heatingDesignDays.First();

            OpenStudioConversionResult result = analyticalModel.ToOpenStudio(WeatherPath(".epw"), OutputDirectory("embedded_dd_values"), run: false);

            global::OpenStudio.DesignDay designDay = result.Model.getDesignDays().Single(x => x.dayType() == "WinterDesignDay");
            double[] samTemperatures = heatingDesignDay[Weather.WeatherDataType.DryBulbTemperature];
            Assert.That(designDay.maximumDryBulbTemperature(), Is.EqualTo(samTemperatures.Max()).Within(1e-9), "Maximum dry bulb comes from the SAM hourly profile");
            Assert.That(designDay.dailyDryBulbTemperatureRange(), Is.EqualTo(samTemperatures.Max() - samTemperatures.Min()).Within(1e-9), "Daily range is the 24 h spread");
            Assert.That(designDay.month(), Is.EqualTo((int)heatingDesignDay.Month));
            Assert.That(designDay.dayOfMonth(), Is.EqualTo((int)heatingDesignDay.Day));
            Assert.That(result.Diagnostics.Any(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning && d.Message.Contains("approximat")), Is.True, "Every approximation is named in a warning");
        }

        [Test]
        public void EmbeddedDesignDay_ImplausiblePressure_RejectedWithDiagnostic()
        {
            // Some SAM embedded (TRY-derived) design days carry an implausible barometric
            // pressure — a London day reported ~31000 Pa at a 25 m site. EnergyPlus rejects
            // anything more than 10% off the elevation-based standard and substitutes the
            // standard; the converter mirrors that rule and names the rejection instead of
            // propagating a value that only triggers a confusing EnergyPlus warning.
            AnalyticalModel analyticalModel = ModelWithEmbeddedWeather();
            analyticalModel.TryGetValue(AnalyticalModelParameter.HeatingDesignDays, out SAMCollection<DesignDay> heatingDesignDays);
            DesignDay heatingDesignDay = new DesignDay(heatingDesignDays.First());
            for (int i = 0; i < 24; i++)
            {
                heatingDesignDay[Weather.WeatherDataType.AtmosphericPressure, i] = 31000.0;
            }

            Analytical.Modify.UpdateWeather(analyticalModel, null, null, new System.Collections.Generic.List<DesignDay> { heatingDesignDay });

            OpenStudioConversionResult result = analyticalModel.ToOpenStudio(WeatherPath(".epw"), OutputDirectory("dd_bad_pressure"), run: false);

            Assert.That(result.Diagnostics.Any(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning && d.Message.Contains("barometric pressure") && d.Message.Contains("31000")), Is.True, "The rejected implausible pressure is named: " + string.Join(" | ", result.Diagnostics.Select(d => d.Message)));

            // The implausible 31000 Pa (which also happens to be the unset OpenStudio IDD
            // floor) never reaches the IDF: the field carries the elevation-based standard.
            global::OpenStudio.DesignDay designDay = result.Model.getDesignDays().Single(x => x.dayType() == "WinterDesignDay");
            Assert.That(designDay.barometricPressure(), Is.GreaterThan(90000.0).And.LessThan(110000.0), "Elevation-based standard pressure is written in place of the implausible SAM value");
        }

        [Test]
        public void ExplicitDdy_OverridesEmbeddedDesignDays_NoDuplicates()
        {
            AnalyticalModel analyticalModel = ModelWithEmbeddedWeather();

            Core.OpenStudio.OpenStudioConversionOptions options = new Core.OpenStudio.OpenStudioConversionOptions { DdyPath = WeatherPath(".ddy") };
            OpenStudioConversionResult result = analyticalModel.ToOpenStudio(WeatherPath(".epw"), OutputDirectory("explicit_ddy"), openStudioConversionOptions: options, run: false);

            System.Collections.Generic.List<string> names = result.Model.getDesignDays().Select(x => x.nameString()).ToList();
            Assert.That(names.Count, Is.EqualTo(2), "No merge of explicit DDY and embedded days: " + string.Join(" | ", names));
            Assert.That(names.Count(x => System.Text.RegularExpressions.Regex.IsMatch(x, @"Ann\s+Htg\s+99\.6\s*%\s+Condns\s+DB", System.Text.RegularExpressions.RegexOptions.IgnoreCase)), Is.EqualTo(1), "The DDY heating 99.6% day wins");
            Assert.That(names.Count(x => System.Text.RegularExpressions.Regex.IsMatch(x, @"Ann\s+Clg\s+0?\.4\s*%\s+Condns\s+DB\s*=>\s*M(C)?WB", System.Text.RegularExpressions.RegexOptions.IgnoreCase)), Is.EqualTo(1), "The DDY cooling .4% day wins");
            Assert.That(result.Diagnostics.Any(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Information && d.Message.Contains("Design-day source: explicit DDY")), Is.True, "The explicit source is named");
            Assert.That(result.DesignDaySource, Is.EqualTo(Core.OpenStudio.OpenStudioDesignDaySource.Ddy), "An explicit DDY is reported as the established basis, not the embedded model");
        }

        [Test]
        public void RunOptionDdy_OverridesConversionOptionDdy()
        {
            // Two different DDY sources: the conversion option points at the full Boston DDY,
            // the run option at a heating-only extract. The run option must win.
            string[] lines = File.ReadAllLines(WeatherPath(".ddy"));
            System.Text.StringBuilder heatingOnly = new System.Text.StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("SizingPeriod:DesignDay", StringComparison.OrdinalIgnoreCase) && i + 1 < lines.Length && lines[i + 1].Contains("Ann Htg 99.6% Condns DB"))
                {
                    for (int j = i; j < lines.Length; j++)
                    {
                        heatingOnly.AppendLine(lines[j]);

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

            string heatingOnlyPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "embedded_heating_only.ddy");
            File.WriteAllText(heatingOnlyPath, heatingOnly.ToString());

            Core.OpenStudio.OpenStudioConversionOptions options = new Core.OpenStudio.OpenStudioConversionOptions { DdyPath = WeatherPath(".ddy") };
            Core.OpenStudio.OpenStudioRunOptions runOptions = new Core.OpenStudio.OpenStudioRunOptions { DdyPath = heatingOnlyPath };
            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox().ToOpenStudio(WeatherPath(".epw"), OutputDirectory("runoption_ddy"), openStudioConversionOptions: options, openStudioRunOptions: runOptions, run: false);

            System.Collections.Generic.List<string> names = result.Model.getDesignDays().Select(x => x.nameString()).ToList();
            Assert.That(names.Count, Is.EqualTo(1), "The run-option DDY takes precedence over the conversion option: " + string.Join(" | ", names));
            Assert.That(names[0], Does.Contain("Ann Htg 99.6% Condns DB"));
        }

        [Test]
        public void ExplicitEpw_OverridesEmbeddedWeather()
        {
            AnalyticalModel analyticalModel = ModelWithEmbeddedWeather();
            string epwPath = WeatherPath(".epw");

            OpenStudioConversionResult result = analyticalModel.ToOpenStudio(epwPath, OutputDirectory("explicit_epw"), run: false);

            Assert.That(result.Diagnostics.Any(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Information && d.Message.Contains("Annual weather source: explicit EPW")), Is.True, "The explicit source is named");
            Assert.That(File.ReadAllText(result.OswPath), Does.Contain(epwPath.Replace('\\', '/')), "The OSW references the explicit EPW, not the embedded export");
        }

        [Test]
        public void EmbeddedWeather_UsedWhenNoEpw()
        {
            AnalyticalModel analyticalModel = ModelWithEmbeddedWeather();

            OpenStudioConversionResult result = analyticalModel.ToOpenStudio(null, OutputDirectory("embedded_epw"), run: false);

            Assert.That(result.IsValid, Is.True, "Conversion-only with embedded weather is valid: " + string.Join(" | ", result.Diagnostics.Where(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error).Select(d => d.Message)));
            Assert.That(result.Diagnostics.Any(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Information && d.Message.Contains("Annual weather source: AnalyticalModel WeatherData")), Is.True, "The embedded source is named");
            Assert.That(result.Diagnostics.Any(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Information && d.Message.Contains("Ground temperatures taken from the SAM model WeatherData")), Is.True, "Embedded ground temperatures still apply");

            global::OpenStudio.EpwFile epwFile = global::OpenStudio.EpwFile.load(global::OpenStudio.OpenStudioUtilitiesCore.toPath(WeatherPath(".epw"))).get();
            Assert.That(result.Model.getSite().latitude(), Is.EqualTo(epwFile.latitude()).Within(1e-6), "The exported EPW drives the site");
            Assert.That(result.OswPath, Is.Not.Null);
            Assert.That(File.ReadAllText(result.OswPath), Does.Contain("SAM_WeatherData_"), "The OSW references the exported EPW");
        }

        [Test]
        public void ExportedEpw_DaylightSavingHeader_IsEnergyPlusValid()
        {
            // Human-Rhino validation: every embedded-weather run ended with
            //   ** Severe ** Invalid date field=NO
            //   ProcessEPWHeader: Invalid Daylight Saving Period Start Date Field(WeatherFile)=NO
            // The exported header carried an extra empty leading field
            // (HOLIDAYS/DAYLIGHT SAVINGS,,No,0,0,0), shifting "No" into the DST start-date slot.
            AnalyticalModel analyticalModel = ModelWithEmbeddedWeather();

            string epwPath = analyticalModel.EmbeddedAnnualWeatherPath();
            Assert.That(epwPath, Is.Not.Null.And.Not.Empty, "The embedded WeatherData exports an EPW");
            Assert.That(File.Exists(epwPath), Is.True, epwPath);

            string line = File.ReadLines(epwPath).FirstOrDefault(x => x.StartsWith("HOLIDAYS/DAYLIGHT SAVINGS", StringComparison.OrdinalIgnoreCase));
            Assert.That(line, Is.Not.Null, "The exported EPW carries the header EnergyPlus parses");

            // EPW field order after the keyword: leap year observed, daylight saving start day,
            // daylight saving end day, number of holidays.
            string[] fields = line.Split(',');
            Assert.That(fields.Length, Is.GreaterThanOrEqualTo(5), "No empty padding field: " + line);
            Assert.That(fields[1].Trim(), Is.EqualTo("No"), "Field 1 is the leap-year flag: " + line);
            Assert.That(fields[2].Trim(), Is.EqualTo("0"), "Field 2 is the DST start day - EnergyPlus rejects a Yes/No token here with a Severe: " + line);
            Assert.That(fields[3].Trim(), Is.EqualTo("0"), "Field 3 is the DST end day: " + line);
            Assert.That(fields[4].Trim(), Is.EqualTo("0"), "Field 4 is the holiday count: " + line);
        }

        [Test]
        public void EmbeddedWeatherMetadata_WithoutYears_UsedWhenNoEpw()
        {
            Weather.WeatherData weatherData = new Weather.WeatherData(42.36, -71.01, 6.0);
            Weather.GroundTemperature groundTemperature = new Weather.GroundTemperature(0.5, double.NaN, double.NaN, double.NaN, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
            weatherData.SetValue(Weather.WeatherDataParameter.GroundTemperatures, new SAMCollection<Weather.GroundTemperature>(groundTemperature));

            AnalyticalModel analyticalModel = AnalyticalModelFixtures.SingleBox();
            analyticalModel.SetValue(AnalyticalModelParameter.WeatherData, weatherData);

            OpenStudioConversionResult result = analyticalModel.ToOpenStudio(null, OutputDirectory("embedded_metadata"), run: false);

            Assert.That(result.IsValid, Is.True, "Conversion-only without any EPW stays valid");
            Assert.That(result.Diagnostics.Any(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning && d.Message.Contains("No usable annual EPW source")), Is.True, "The missing annual source is a warning when not running");
            Assert.That(result.Model.getSite().latitude(), Is.EqualTo(42.36).Within(1e-9), "Location still comes from the embedded WeatherData");
            Assert.That(result.Model.getSiteGroundTemperatureBuildingSurface().januaryGroundTemperature(), Is.EqualTo(1.0).Within(1e-9), "Ground temperatures still come from the embedded WeatherData");
            Assert.That(result.Diagnostics.Any(d => d.Message.Contains("an EPW path is still required for an annual run")), Is.True, "The metadata fallback names the annual-run limitation");
        }

        [Test]
        public void RunTrue_NoWeatherSource_FailsClearly()
        {
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.SingleBox();

            OpenStudioConversionResult result = analyticalModel.ToOpenStudio(null, OutputDirectory("no_weather_run"), run: true);

            Assert.That(result.Diagnostics.Any(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error && d.Message.Contains("EPW")), Is.True, "A blocking error names the missing EPW source");
            Assert.That(result.RunResult, Is.Null, "The simulation is not executed without a weather source");
            Assert.That(result.OsmPath, Is.Not.Null, "The OSM stays on disk for inspection");
        }

        [Test]
        public void ConversionOnly_NoWeather_SucceedsWithWarning()
        {
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.SingleBox();

            OpenStudioConversionResult result = analyticalModel.ToOpenStudio(null, OutputDirectory("no_weather_convert"), run: false);

            Assert.That(result.IsValid, Is.True, "Conversion-only without weather is valid");
            Assert.That(result.Diagnostics.Any(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning && d.Message.Contains("No usable annual EPW source")), Is.True);
            Assert.That(result.Diagnostics.Any(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error), Is.False, "No error when no annual run was requested");
            Assert.That(result.DesignDaySource, Is.EqualTo(Core.OpenStudio.OpenStudioDesignDaySource.None), "A model with no design days establishes no basis");
            Assert.That(File.Exists(result.OsmPath), Is.True, "The OSM is saved");
        }

        [Test]
        public void InvalidExplicitEpw_FallsBackToEmbeddedWeather_WithWarning()
        {
            AnalyticalModel analyticalModel = ModelWithEmbeddedWeather();

            OpenStudioConversionResult result = analyticalModel.ToOpenStudio(Path.Combine(OutputDirectory("invalid_epw"), "missing.epw"), OutputDirectory("invalid_epw"), run: false);

            Assert.That(result.Diagnostics.Any(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning && d.Message.Contains("The explicit EPW path is not usable")), Is.True, "The rejected explicit path is named");
            Assert.That(result.Diagnostics.Any(d => d.Message.Contains("Annual weather source: AnalyticalModel WeatherData")), Is.True, "The embedded fallback applies");
        }

        [Test]
        [Category("Simulation")]
        public void EmbeddedWeatherAndDesignDays_EndToEnd_RunAndResults()
        {
            // The human Rhino route, fully embedded: no _epwPath, no ddyPath_ — WeatherData
            // and heating/cooling design days come from the AnalyticalModel only.
            AnalyticalModel analyticalModel = ModelWithEmbeddedWeather();

            string outputDirectory = OutputDirectory("embedded_endtoend");
            OpenStudioConversionResult result = analyticalModel.ToOpenStudio(null, outputDirectory, run: true);

            Assert.That(result.RunResult?.Success, Is.True, "The run succeeds with the exported EPW: " + string.Join(" | ", result.Diagnostics.Where(d => d.Severity >= Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning).Select(d => d.Message)));
            Assert.That(result.Diagnostics.Any(d => d.Message.Contains("Annual weather source: AnalyticalModel WeatherData")), Is.True);
            Assert.That(result.Diagnostics.Any(d => d.Message.Contains("Design-day source: AnalyticalModel heating/cooling design days")), Is.True);

            System.Collections.Generic.List<string> environments = new System.Collections.Generic.List<string>();
            using (System.Data.SQLite.SQLiteConnection connection = new System.Data.SQLite.SQLiteConnection("Data Source=" + result.RunResult.SqlPath + ";Read Only=True"))
            {
                connection.Open();
                using (System.Data.SQLite.SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT EnvironmentName, EnvironmentType FROM EnvironmentPeriods";
                    using (System.Data.SQLite.SQLiteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            environments.Add(reader.GetString(0) + " [type " + reader.GetInt32(1) + "]");
                        }
                    }
                }
            }

            TestContext.Out.WriteLine(string.Join("\n", environments));
            Assert.That(environments.Count(x => x.Contains("[type 3]")), Is.EqualTo(1), "One annual environment");
            Assert.That(environments.Count(x => x.IndexOf("HTG", StringComparison.OrdinalIgnoreCase) >= 0), Is.EqualTo(1), "The embedded heating day was simulated");
            Assert.That(environments.Count(x => x.IndexOf("CLG", StringComparison.OrdinalIgnoreCase) >= 0), Is.EqualTo(1), "The embedded cooling day was simulated");

            // The SQL design-day reader (fix 2) reads those same environments without a
            // DateTime exception, and the results attachment (fix 3) maps the annual family.
            System.Collections.Generic.List<DesignDay> designDays = Create.DesignDays(result.RunResult.SqlPath, out System.Collections.Generic.List<string> designDayDiagnostics);
            Assert.That(designDays, Is.Not.Null.And.Count.EqualTo(2), "Create.DesignDays round-trips the embedded days: " + string.Join(" | ", designDayDiagnostics));

            AdjacencyCluster adjacencyCluster = new AdjacencyCluster(analyticalModel.AdjacencyCluster);
            System.Collections.Generic.List<Result> results = Modify.AddResults(adjacencyCluster, result.RunResult.SqlPath, out System.Collections.Generic.List<string> resultDiagnostics);
            Assert.That(results.Count(x => x is SpaceSimulationResult), Is.GreaterThanOrEqualTo(2), "Annual space results attached");
            Assert.That(results.Count(x => x is SurfaceSimulationResult), Is.GreaterThanOrEqualTo(6), "Surface results attached");
            Assert.That(adjacencyCluster.GetResults<SpaceSimulationResult>(adjacencyCluster.GetSpaces().Single()), Is.Not.Null.And.Count.GreaterThanOrEqualTo(2), "The space carries its results");
            TestContext.Out.WriteLine(string.Join("\n", resultDiagnostics));
        }

        [Test]
        public void EmbeddedFingerprint_ChangesWithContent_SameModelGuid()
        {
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.SingleBox();
            Assert.That(Query.EmbeddedWeatherFingerprint(analyticalModel), Is.EqualTo(string.Empty), "Nothing embedded → empty fingerprint");

            string fingerprint_1 = ModelWithEmbeddedWeather().EmbeddedWeatherFingerprint();
            string fingerprint_2 = ModelWithEmbeddedWeather().EmbeddedWeatherFingerprint();
            Assert.That(fingerprint_2, Is.EqualTo(fingerprint_1), "Identical embedded content → identical fingerprint");

            AnalyticalModel analyticalModel_Modified = ModelWithEmbeddedWeather();
            analyticalModel_Modified.TryGetValue(AnalyticalModelParameter.HeatingDesignDays, out SAMCollection<DesignDay> heatingDesignDays);
            DesignDay designDay = new DesignDay(heatingDesignDays.First());
            designDay[Weather.WeatherDataType.DryBulbTemperature, 0] = designDay[Weather.WeatherDataType.DryBulbTemperature, 0] + 1.0;
            Analytical.Modify.UpdateWeather(analyticalModel_Modified, null, null, new System.Collections.Generic.List<DesignDay> { designDay });

            string fingerprint_3 = analyticalModel_Modified.EmbeddedWeatherFingerprint();
            Assert.That(fingerprint_3, Is.Not.EqualTo(fingerprint_1), "Changed design-day content on the same model Guid must change the fingerprint");
        }
    }
}
