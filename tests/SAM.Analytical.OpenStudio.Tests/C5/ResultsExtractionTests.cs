// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// C5: results extraction and SAM mapping — annual energies, peaks with timestamps, unmet
    /// hours, gains breakdown, options-gated series, one unit authority (kWh), zero-vs-missing
    /// semantics, SAM Guid ↔ zone identity and the AnalyticalModel/Space result mapping,
    /// validated end-to-end on annual EnergyPlus runs.
    /// </summary>
    [TestFixture]
    public class ResultsExtractionTests
    {
        private static string WeatherPath(string extension)
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "resources", "weather", "USA_MA_Boston-Logan.Intl.AP.725090_TMYx.2004-2018" + extension));
        }

        private static OpenStudioConversionResult Run(AnalyticalModel analyticalModel, string name, bool extractTimeSeries = false, Core.OpenStudio.OpenStudioConversionOptions conversionOptions = null)
        {
            string epwPath = WeatherPath(".epw");
            Assert.That(File.Exists(epwPath), Is.True, $"Pinned weather fixture missing: {epwPath}");

            string outputDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, name);
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, true);
            }

            OpenStudioConversionResult result = analyticalModel.ToOpenStudio(epwPath, outputDirectory, openStudioConversionOptions: conversionOptions, openStudioRunOptions: new Core.OpenStudio.OpenStudioRunOptions { ExtractTimeSeries = extractTimeSeries });
            foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in result.Diagnostics)
            {
                TestContext.Out.WriteLine(diagnostic.ToString());
            }

            Assert.That(result.RunResult?.Success, Is.True, "The run must succeed");
            Assert.That(result.Results, Is.Not.Null, "The result set must be extracted");
            return result;
        }

        [Test]
        [Category("Simulation")]
        public void AnnualEnergy_Peaks_UnmetHours_Gains_AndSeries_Extracted()
        {
            OpenStudioConversionResult result = Run(AnalyticalModelFixtures.SingleBox(), "c5_full_extraction", extractTimeSeries: true);
            OpenStudioSimulationResultSet results = result.Results;

            string zoneKey = results.AnnualHeatingEnergy.Keys.Single();

            // Annual energy (kWh authority)
            Assert.That(results.AnnualHeatingEnergy[zoneKey], Is.GreaterThan(0));
            Assert.That(results.AnnualCoolingEnergy[zoneKey], Is.GreaterThan(0));
            Assert.That(results.TotalAnnualHeating, Is.EqualTo(result.Loads.TotalHeating).Within(1e-9), "Result set and legacy summary agree");

            // Peaks with timestamps
            Assert.That(results.PeakHeatingLoad[zoneKey], Is.GreaterThan(0));
            Assert.That(results.PeakCoolingLoad[zoneKey], Is.GreaterThan(0));
            Assert.That(results.PeakHeatingHour[zoneKey], Is.InRange(0, 8759));
            Assert.That(results.PeakCoolingHour[zoneKey], Is.InRange(0, 8759));
            Assert.That(results.PeakHeatingLoadTotal, Is.EqualTo(results.PeakHeatingLoad[zoneKey]).Within(1e-9), "One zone: coincident peak equals the zone peak");
            Assert.That(results.PeakHeatingHourTotal, Is.EqualTo(results.PeakHeatingHour[zoneKey]));

            // Unmet hours (present, non-negative; never missing for a conditioned zone; all
            // per-zone dictionaries share one zone identity — the energy key)
            Assert.That(results.UnmetHeatingHours.ContainsKey(zoneKey), Is.True);
            Assert.That(results.UnmetHeatingHours[zoneKey], Is.GreaterThanOrEqualTo(0));
            Assert.That(results.UnmetCoolingHours[zoneKey], Is.GreaterThanOrEqualTo(0));

            // Gains breakdown (normalised onto the energy key)
            Assert.That(results.LightingGains[zoneKey], Is.GreaterThan(0), "8 W/m² office lighting must appear");
            Assert.That(results.EquipmentGains[zoneKey], Is.GreaterThan(0));
            Assert.That(results.PeopleGains[zoneKey], Is.GreaterThan(0));
            Assert.That(results.WindowSolarGains[zoneKey], Is.GreaterThan(0), "South window in Boston must transmit solar");
            Assert.That(results.InfiltrationGains.ContainsKey(zoneKey), Is.True);
            Assert.That(results.VentilationHeatingEnergy.ContainsKey(zoneKey) || results.VentilationCoolingEnergy.ContainsKey(zoneKey), Is.True);

            // Series (requested)
            Assert.That(results.TemperatureSeries, Is.Not.Null);
            Assert.That(results.TemperatureSeries[zoneKey].Length, Is.EqualTo(8760));
            Assert.That(results.TemperatureSeries[zoneKey].Min(), Is.GreaterThan(-40));
            Assert.That(results.TemperatureSeries[zoneKey].Max(), Is.LessThan(60));
            Assert.That(results.OperativeTemperatureSeries[zoneKey].Length, Is.EqualTo(8760));
            Assert.That(results.RelativeHumiditySeries[zoneKey].Length, Is.EqualTo(8760));

            // Runtime and error counts
            Assert.That(results.RuntimeSeconds, Is.GreaterThan(0));
            Assert.That(results.FatalCount, Is.EqualTo(0));
            Assert.That(results.SevereCount, Is.EqualTo(0));
        }

        [Test]
        [Category("Simulation")]
        public void Series_AreNotLoaded_WhenNotRequested()
        {
            OpenStudioConversionResult result = Run(AnalyticalModelFixtures.SingleBox(), "c5_no_series");
            Assert.That(result.Results.TemperatureSeries, Is.Null);
            Assert.That(result.Results.OperativeTemperatureSeries, Is.Null);
            Assert.That(result.Results.RelativeHumiditySeries, Is.Null);
        }

        [Test]
        [Category("Simulation")]
        public void Units_AnnualSum_MatchesHourlySeries_OneAuthority()
        {
            OpenStudioConversionResult result = Run(AnalyticalModelFixtures.SingleBox(), "c5_units");
            OpenStudioSimulationResultSet results = result.Results;

            using (System.Data.SQLite.SQLiteConnection connection = new System.Data.SQLite.SQLiteConnection("Data Source=" + result.RunResult.SqlPath + ";Read Only=True"))
            {
                connection.Open();
                using (System.Data.SQLite.SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT SUM(rd.Value) FROM ReportData rd JOIN ReportDataDictionary rdd ON rd.ReportDataDictionaryIndex = rdd.ReportDataDictionaryIndex JOIN Time t ON rd.TimeIndex = t.TimeIndex WHERE rdd.Name = @name AND t.EnvironmentPeriodIndex IN (SELECT EnvironmentPeriodIndex FROM EnvironmentPeriods WHERE EnvironmentType = 3)";
                    command.Parameters.AddWithValue("@name", "Zone Ideal Loads Supply Air Total Heating Energy");
                    double joules = (double)command.ExecuteScalar();
                    Assert.That(results.TotalAnnualHeating, Is.EqualTo(Core.OpenStudio.Query.JoulesToKilowattHours(joules)).Within(1e-6), "One J → kWh authority across all extraction paths");
                }
            }
        }

        [Test]
        [Category("Simulation")]
        public void TwoZones_ZeroVsMissing_IdentityMapping()
        {
            OpenStudioConversionResult result = Run(AnalyticalModelFixtures.TwoAdjacentBoxes(spaceBUnconditioned: true), "c5_two_zones");
            OpenStudioSimulationResultSet results = result.Results;

            Assert.That(results.AnnualHeatingEnergy.Count, Is.EqualTo(1), "Only the conditioned zone reports Ideal Loads energy — the unconditioned zone is absent, never zero-filled");

            // The mapping needs the SOURCE model (fixture GUIDs are deterministic).
            List<SpaceSimulationResult> spaceResults = results.ToSAM_SpaceSimulationResults(AnalyticalModelFixtures.TwoAdjacentBoxes(spaceBUnconditioned: true));
            Assert.That(spaceResults.Count, Is.EqualTo(2), "Heating + Cooling result per conditioned space");
            Assert.That(spaceResults.Select(x => x.LoadType()).Distinct().Count(), Is.EqualTo(2));

            SpaceSimulationResult heating = spaceResults.Single(x => x.LoadType() == LoadType.Heating);
            Assert.That(heating.TryGetValue(Analytical.SpaceSimulationResultParameter.Load, out double load), Is.True);
            Assert.That(load, Is.GreaterThan(0));
            Assert.That(heating.TryGetValue(Analytical.SpaceSimulationResultParameter.LoadIndex, out int loadIndex), Is.True);
            Assert.That(loadIndex, Is.InRange(0, 8759));
            Assert.That(heating.TryGetValue(Analytical.SpaceSimulationResultParameter.UnmetHours, out double unmet), Is.True);
            Assert.That(unmet, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        [Category("Simulation")]
        public void AnalyticalModelResult_Mapped()
        {
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.SingleBox();
            OpenStudioConversionResult result = Run(analyticalModel, "c5_model_result");

            AnalyticalModelSimulationResult modelResult = result.Results.ToSAM(analyticalModel);

            Assert.That(modelResult.TryGetValue(AnalyticalModelSimulationResultParameter.ConsumptionHeating, out double consumptionHeating), Is.True);
            Assert.That(consumptionHeating, Is.EqualTo(result.Results.TotalAnnualHeating).Within(1e-9));
            Assert.That(modelResult.TryGetValue(AnalyticalModelSimulationResultParameter.ConsumptionCooling, out double consumptionCooling), Is.True);
            Assert.That(consumptionCooling, Is.EqualTo(result.Results.TotalAnnualCooling).Within(1e-9));
            Assert.That(modelResult.TryGetValue(AnalyticalModelSimulationResultParameter.PeakHeatingLoad, out double peakHeating), Is.True);
            Assert.That(peakHeating, Is.EqualTo(result.Results.PeakHeatingLoadTotal).Within(1e-9));
            Assert.That(modelResult.TryGetValue(AnalyticalModelSimulationResultParameter.PeakHeatingHour, out int peakHeatingHour), Is.True);
            Assert.That(peakHeatingHour, Is.EqualTo(result.Results.PeakHeatingHourTotal));
            Assert.That(modelResult.TryGetValue(AnalyticalModelSimulationResultParameter.FloorArea, out double floorArea), Is.True);
            Assert.That(floorArea, Is.EqualTo(20.0).Within(1e-9));
        }

        [Test]
        [Category("Simulation")]
        public void DesignDayRun_PeaksAndAnnuals_StillExcludeSizingPeriods()
        {
            OpenStudioConversionResult result = Run(AnalyticalModelFixtures.SingleBox(), "c5_designday_exclusion", conversionOptions: new Core.OpenStudio.OpenStudioConversionOptions { DdyPath = WeatherPath(".ddy") });
            OpenStudioConversionResult baseline = Run(AnalyticalModelFixtures.SingleBox(), "c5_designday_baseline");

            Assert.That(result.Results.TotalAnnualHeating, Is.EqualTo(baseline.Results.TotalAnnualHeating).Within(System.Math.Max(1.0, baseline.Results.TotalAnnualHeating * 0.01)));
            Assert.That(result.Results.AnnualHeatingEnergy.Keys.Single(), Is.EqualTo(baseline.Results.AnnualHeatingEnergy.Keys.Single()));
        }
    }
}
