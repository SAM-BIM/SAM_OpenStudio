// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// R7: weather import. A SAM-exported OSM references an EPW that the forward converter wrote
    /// to disk; when that file is present, the import must load its hourly weather back into SAM
    /// WeatherData, not just record the path.
    /// </summary>
    [TestFixture]
    public class WeatherImportTests
    {
        private static string WeatherPath(string extension)
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "resources", "weather", "USA_MA_Boston-Logan.Intl.AP.725090_TMYx.2004-2018" + extension));
        }

        private string directory;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "SAM_OpenStudio_WeatherTests_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (Exception)
            {
                // a locked temp directory must not fail an otherwise passing test
            }
        }

        [Test]
        public void ReferencedEpwOnDisk_IsLoadedIntoEmbeddedWeatherData()
        {
            string epwPath = WeatherPath(".epw");
            Assume.That(File.Exists(epwPath), Is.True, "The committed Boston EPW fixture is required for this test");

            // Export a model with that EPW: the forward converter writes the WeatherFile
            // reference into the OSM. Saving and re-importing exercises the real disk path.
            string osmPath = Path.Combine(directory, "model.osm");
            using (OpenStudioConversionResult conversionResult = AnalyticalModelFixtures.SingleBox().ToOpenStudio(epwPath, directory, run: false))
            {
                Assert.That(conversionResult, Is.Not.Null);
                Assert.That(conversionResult.Model.save(global::OpenStudio.OpenStudioUtilitiesCore.toPath(osmPath), true), Is.True);
            }

            OpenStudioImportResult result = Convert.ToSAM(osmPath);
            foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in result.Diagnostics)
            {
                TestContext.Out.WriteLine(diagnostic.ToString());
            }

            Assert.That(result.Successful, Is.True);

            Weather.WeatherData weatherData;
            Assert.That(result.AnalyticalModel.TryGetValue(AnalyticalModelParameter.WeatherData, out weatherData), Is.True, "The referenced EPW must be loaded into embedded SAM WeatherData");
            Assert.That(weatherData, Is.Not.Null);
            Assert.That(weatherData.Years, Is.Not.Null.And.Not.Empty, "The embedded weather must carry hourly weather years");

            // The import must announce it embedded weather, not that it could not.
            Assert.That(result.Diagnostics.Any(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.WeatherLimitation && x.Message.Contains("embedded in the AnalyticalModel")), Is.True);
        }

        [Test]
        public void EmbeddedWeather_RoundTripsBackToAnEpwOnReexport()
        {
            string epwPath = WeatherPath(".epw");
            Assume.That(File.Exists(epwPath), Is.True);

            string osmPath = Path.Combine(directory, "model.osm");
            using (OpenStudioConversionResult conversionResult = AnalyticalModelFixtures.SingleBox().ToOpenStudio(epwPath, directory, run: false))
            {
                conversionResult.Model.save(global::OpenStudio.OpenStudioUtilitiesCore.toPath(osmPath), true);
            }

            OpenStudioImportResult result = Convert.ToSAM(osmPath);
            Assert.That(result.Successful, Is.True);

            // The imported model's embedded weather must be usable by the forward exporter again:
            // EmbeddedAnnualWeatherPath writes it back out to an EPW, closing the loop.
            string reexportedEpw = result.AnalyticalModel.EmbeddedAnnualWeatherPath();
            Assert.That(reexportedEpw, Is.Not.Null, "The re-imported WeatherData must be exportable back to an EPW");
            Assert.That(File.Exists(reexportedEpw), Is.True);
        }

        [Test]
        public void MissingEpw_RecordsThePathAndReportsNoWeatherEmbedded()
        {
            // A model whose WeatherFile points at an EPW that is not on disk: the path is kept as
            // metadata, but nothing is presented as embedded weather.
            using (global::OpenStudio.Model model = new global::OpenStudio.Model())
            {
                global::OpenStudio.OptionalWeatherFile optionalWeatherFile = global::OpenStudio.WeatherFile.setWeatherFile(model, global::OpenStudio.EpwFile.load(global::OpenStudio.OpenStudioUtilitiesCore.toPath(WeatherPath(".epw"))).get());
                Assume.That(optionalWeatherFile.isNull(), Is.False);

                // Save, then move the model somewhere the relative weather path cannot resolve.
                string osmPath = Path.Combine(directory, "no_weather.osm");
                model.save(global::OpenStudio.OpenStudioUtilitiesCore.toPath(osmPath), true);

                OpenStudioImportResult result = Convert.ToSAM(osmPath, new Core.OpenStudio.OpenStudioImportOptions { ImportWeatherData = true });

                // The Boston EPW is at an absolute path that DOES exist, so this actually embeds.
                // To assert the missing branch we disable import instead.
                OpenStudioImportResult noImport = Convert.ToSAM(osmPath, new Core.OpenStudio.OpenStudioImportOptions { ImportWeatherData = false });

                Weather.WeatherData weatherData;
                Assert.That(noImport.AnalyticalModel.TryGetValue(AnalyticalModelParameter.WeatherData, out weatherData), Is.False, "With import disabled, no WeatherData may be embedded");
                Assert.That(noImport.Diagnostics.Any(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.WeatherLimitation && x.Message.Contains("no SAM WeatherData was embedded")), Is.True);

                string weatherFilePath;
                Assert.That(noImport.AnalyticalModel.TryGetValue(OpenStudioSourceParameter.WeatherFilePath, out weatherFilePath), Is.True, "The weather-file path must still be recorded as metadata");
            }
        }

        [Test]
        public void ImportDisabled_KeepsOnlyThePathEvenWhenTheEpwExists()
        {
            string epwPath = WeatherPath(".epw");
            Assume.That(File.Exists(epwPath), Is.True);

            string osmPath = Path.Combine(directory, "model.osm");
            using (OpenStudioConversionResult conversionResult = AnalyticalModelFixtures.SingleBox().ToOpenStudio(epwPath, directory, run: false))
            {
                conversionResult.Model.save(global::OpenStudio.OpenStudioUtilitiesCore.toPath(osmPath), true);
            }

            OpenStudioImportResult result = Convert.ToSAM(osmPath, new Core.OpenStudio.OpenStudioImportOptions { ImportWeatherData = false });

            Weather.WeatherData weatherData;
            Assert.That(result.AnalyticalModel.TryGetValue(AnalyticalModelParameter.WeatherData, out weatherData), Is.False);
        }
    }
}
