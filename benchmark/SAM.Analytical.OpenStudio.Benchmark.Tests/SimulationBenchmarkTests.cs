// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.IO;
using NUnit.Framework;
using SAM.Analytical.Benchmark;

namespace SAM.Analytical.OpenStudio.Benchmark.Tests
{
    /// <summary>
    /// Live end-to-end producer coverage (requires the OpenStudio CLI + EnergyPlus; self-ignored
    /// otherwise). Runs the headless route on the SingleBox fixture and pins the annual-consumption
    /// stored magnitude against the raw SQL: the document stores kWh (J ÷ 3 600 000, the one J→kWh
    /// authority), never Wh (J ÷ 3600). This is the gating unit decision for every downstream
    /// comparison — the TAS producer (B2) must store consumption in the same kWh magnitude.
    /// </summary>
    [TestFixture]
    public class SimulationBenchmarkTests
    {
        [Test]
        [Category("Simulation")]
        public void SingleBox_AnnualConsumption_IsStoredInKilowattHours_NotWattHours()
        {
            if (Core.OpenStudio.Query.OpenStudioCliPath(null) == null)
            {
                Assert.Ignore("OpenStudio CLI not found; the live consumption-unit pin needs an OpenStudio/EnergyPlus install.");
            }

            string epwPath = WeatherFile("USA_MA_Boston-Logan.Intl.AP.725090_TMYx.2004-2018.epw");
            Assert.That(File.Exists(epwPath), Is.True, "Pinned weather fixture missing: " + epwPath);

            AnalyticalModel model = BenchmarkFixture.SingleBox();

            string outputDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "benchmark_live_singlebox");
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, true);
            }

            using (OpenStudioConversionResult conversionResult = model.ToOpenStudio(epwPath, outputDirectory))
            {
                foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in conversionResult.Diagnostics)
                {
                    TestContext.Out.WriteLine(diagnostic.ToString());
                }

                Assert.That(conversionResult.RunResult?.Success, Is.True, "The headless run must succeed");
                Assert.That(conversionResult.Results, Is.Not.Null, "A result set must be extracted");

                // Observe the raw SQL: annual Ideal Loads heating energy is stored in Joules.
                double rawJoules = ReadAnnualHeatingJoules(conversionResult.RunResult.SqlPath);
                Assert.That(rawJoules, Is.GreaterThan(0), "The conditioned box must report heating energy");

                double expectedKilowattHours = rawJoules / 3_600_000.0;   // the pinned magnitude
                double wattHours = rawJoules / 3_600.0;                   // the rejected alternative

                // The C5 result set already carries kWh (one J→kWh authority).
                Assert.That(conversionResult.Results.TotalAnnualHeating, Is.EqualTo(expectedKilowattHours).Within(1e-6),
                    "Result set annual heating is kWh (J / 3 600 000)");

                BenchmarkDocument document = model.ToBenchmark(BuildContext(model, epwPath, conversionResult));

                MetricValue consumptionHeating = document.Model.ConsumptionHeating;
                Assert.That(consumptionHeating.Unit, Is.EqualTo(MetricUnit.KilowattHour), "Consumption is stored with the kWh token");
                Assert.That(consumptionHeating.Available, Is.True);
                Assert.That(consumptionHeating.Value.Value, Is.EqualTo(expectedKilowattHours).Within(1e-6),
                    "The stored consumption magnitude is kWh");
                Assert.That(consumptionHeating.Value.Value, Is.Not.EqualTo(wattHours).Within(1.0),
                    "The stored consumption magnitude is NOT Wh");

                BenchmarkValidationResult validation = BenchmarkValidator.Validate(document);
                Assert.That(validation.IsValid, Is.True, "Errors: " + string.Join(" | ", System.Linq.Enumerable.Select(validation.Errors, x => x.ToString())));

                TestContext.Out.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "PINNED consumptionHeating: raw SQL = {0} J -> stored {1} kWh (Wh would have been {2}).",
                    rawJoules, consumptionHeating.Value.Value, wattHours));
            }
        }

        private static OpenStudioBenchmarkContext BuildContext(AnalyticalModel model, string epwPath, OpenStudioConversionResult conversionResult)
        {
            string neutralJson = model.ToJsonObject().ToJsonString();

            return new OpenStudioBenchmarkContext
            {
                SourceModelName = model.Name,
                SourceModelGuid = model.Guid.ToString("N"),
                SourceFileHash = BenchmarkHash.ComputeSha256(neutralJson),
                CanonicalModelHash = BenchmarkCanonicalJson.ComputeSha256(neutralJson),
                CanonicalizationVersion = BenchmarkCanonicalization.CurrentVersion,
                SamCommit = "0123456789abcdef",
                RunnerCommit = "fedcba9876543210",
                EngineName = "EnergyPlus",
                EngineVersion = Core.OpenStudio.Query.EnergyPlusVersion(),
                SdkVersion = Core.OpenStudio.Query.OpenStudioVersion(),
                WeatherIdentity = Path.GetFileNameWithoutExtension(epwPath),
                WeatherHash = BenchmarkHash.ComputeSha256(File.ReadAllBytes(epwPath)),
                DesignDaySource = DesignDaySource.None,
                RunTimestampUtc = DateTimeOffset.UtcNow,
                DurationSeconds = conversionResult.Results.RuntimeSeconds,
                State = RunState.Success,
                ResultSet = conversionResult.Results,
            };
        }

        private static double ReadAnnualHeatingJoules(string sqlPath)
        {
            using (System.Data.SQLite.SQLiteConnection connection = new System.Data.SQLite.SQLiteConnection("Data Source=" + sqlPath + ";Read Only=True"))
            {
                connection.Open();
                using (System.Data.SQLite.SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText =
                        "SELECT SUM(rd.Value) FROM ReportData rd " +
                        "JOIN ReportDataDictionary rdd ON rd.ReportDataDictionaryIndex = rdd.ReportDataDictionaryIndex " +
                        "JOIN Time t ON rd.TimeIndex = t.TimeIndex " +
                        "WHERE rdd.Name = @name AND t.EnvironmentPeriodIndex IN " +
                        "(SELECT EnvironmentPeriodIndex FROM EnvironmentPeriods WHERE EnvironmentType = 3)";
                    command.Parameters.AddWithValue("@name", "Zone Ideal Loads Supply Air Total Heating Energy");
                    object value = command.ExecuteScalar();
                    return value == null || value == DBNull.Value ? 0 : System.Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                }
            }
        }

        /// <summary>Resolves a weather fixture by walking up from the test directory to find tests\resources\weather.</summary>
        private static string WeatherFile(string fileName)
        {
            DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "tests", "resources", "weather", fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return Path.Combine("tests", "resources", "weather", fileName);
        }
    }
}
