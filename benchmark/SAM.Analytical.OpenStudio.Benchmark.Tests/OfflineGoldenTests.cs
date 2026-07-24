// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SAM.Analytical.Benchmark;

namespace SAM.Analytical.OpenStudio.Benchmark.Tests
{
    /// <summary>
    /// Offline producer coverage: the C5 engine-neutral result set → B1a benchmark document
    /// mapping, the two provenance hashes, and full schema validation — all with no OpenStudio or
    /// EnergyPlus installed. The result set is built directly (its public constructor is the neutral
    /// C5 type the live route also produces), so nothing here re-extracts or re-converts.
    /// </summary>
    [TestFixture]
    public class OfflineGoldenTests
    {
        private static BenchmarkDocument Golden()
        {
            AnalyticalModel model = BenchmarkFixture.SingleSpaceModel();
            OpenStudioSimulationResultSet resultSet = BenchmarkFixture.ResultSet();
            OpenStudioBenchmarkContext context = BenchmarkFixture.Context(model, resultSet);
            return model.ToBenchmark(context);
        }

        [Test]
        public void Golden_ModelMetrics_MappedWithExplicitUnits()
        {
            BenchmarkModelResult model = Golden().Model;

            AssertAvailable(model.ConsumptionHeating, BenchmarkFixture.AnnualHeatingKwh, MetricUnit.KilowattHour);
            AssertAvailable(model.ConsumptionCooling, BenchmarkFixture.AnnualCoolingKwh, MetricUnit.KilowattHour);
            AssertAvailable(model.PeakHeatingLoad, BenchmarkFixture.PeakHeatingKw, MetricUnit.Kilowatt);
            AssertAvailable(model.PeakHeatingHour, BenchmarkFixture.PeakHeatingHour, MetricUnit.HourOfYear);
            AssertAvailable(model.PeakCoolingLoad, BenchmarkFixture.PeakCoolingKw, MetricUnit.Kilowatt);
            AssertAvailable(model.PeakCoolingHour, BenchmarkFixture.PeakCoolingHour, MetricUnit.HourOfYear);
            AssertAvailable(model.FloorArea, BenchmarkFixture.FloorArea, MetricUnit.SquareMetre);
            AssertAvailable(model.Volume, BenchmarkFixture.SpaceVolume, MetricUnit.CubicMetre);
        }

        [Test]
        public void Golden_SpaceMetrics_PeakLoadsInWatts_UnmetZeroIsMeasured()
        {
            BenchmarkDocument document = Golden();
            Assert.That(document.Spaces, Has.Count.EqualTo(1));

            BenchmarkSpaceResult space = document.Spaces.Single();
            Assert.That(space.Guid, Is.EqualTo(BenchmarkFixture.SpaceGuid.ToString("N")));
            Assert.That(space.Name, Is.EqualTo(BenchmarkFixture.SpaceName));
            AssertAvailable(space.Area, BenchmarkFixture.FloorArea, MetricUnit.SquareMetre);
            AssertAvailable(space.Volume, BenchmarkFixture.SpaceVolume, MetricUnit.CubicMetre);

            // Space peaks are Watts (kW * 1000), coupled with their hour-of-year.
            AssertAvailable(space.Heating.PeakLoad, BenchmarkFixture.PeakHeatingKw * 1000.0, MetricUnit.Watt);
            AssertAvailable(space.Heating.PeakHour, BenchmarkFixture.PeakHeatingHour, MetricUnit.HourOfYear);
            AssertAvailable(space.Cooling.PeakLoad, BenchmarkFixture.PeakCoolingKw * 1000.0, MetricUnit.Watt);
            AssertAvailable(space.Cooling.PeakHour, BenchmarkFixture.PeakCoolingHour, MetricUnit.HourOfYear);

            // A measured zero stays a value of 0 and available; a genuinely non-zero unmet is kept.
            AssertAvailable(space.Heating.UnmetHours, BenchmarkFixture.UnmetHeatingHours, MetricUnit.Hour);
            AssertAvailable(space.Cooling.UnmetHours, BenchmarkFixture.UnmetCoolingHours, MetricUnit.Hour);

            // The v1 headless-annual route runs no sizing period: design loads are unavailable.
            AssertUnavailable(space.Heating.DesignLoad, MetricUnit.Watt);
            AssertUnavailable(space.Cooling.DesignLoad, MetricUnit.Watt);
        }

        [Test]
        public void Golden_Provenance_IsNativeOpenStudioRoute()
        {
            BenchmarkProvenance provenance = Golden().Provenance;

            Assert.That(provenance.Route, Is.EqualTo(BenchmarkRoute.NativeOpenStudio));
            Assert.That(provenance.Engine.Kind, Is.EqualTo(EngineKind.OpenStudio));
            Assert.That(provenance.Engine.Name, Is.EqualTo("EnergyPlus"));
            Assert.That(provenance.CanonicalizationVersion, Is.EqualTo(BenchmarkCanonicalization.CurrentVersion));
            Assert.That(provenance.ResultSources, Is.EqualTo(new[] { Query.Source() }));
            Assert.That(provenance.State, Is.EqualTo(RunState.Success));
        }

        [Test]
        public void Golden_Document_ValidatesWithZeroErrorsAndWarnings()
        {
            BenchmarkValidationResult validation = BenchmarkValidator.Validate(Golden());

            Assert.That(validation.Errors, Is.Empty, "Errors: " + string.Join(" | ", validation.Errors.Select(x => x.ToString())));
            Assert.That(validation.Warnings, Is.Empty, "Warnings: " + string.Join(" | ", validation.Warnings.Select(x => x.ToString())));
            Assert.That(validation.IsValid, Is.True);
        }

        [Test]
        public void Golden_Serializes_AndRoundTrips()
        {
            BenchmarkDocument document = Golden();

            string json = BenchmarkSerializer.Serialize(document);
            Assert.That(json, Does.Contain("\"route\": \"Native-OpenStudio\""));

            BenchmarkDocument reloaded = BenchmarkSerializer.Deserialize(json);
            Assert.That(reloaded.Model.ConsumptionHeating.Value, Is.EqualTo(BenchmarkFixture.AnnualHeatingKwh).Within(1e-9));
            Assert.That(reloaded.Spaces.Single().Heating.PeakLoad.Value, Is.EqualTo(BenchmarkFixture.PeakHeatingKw * 1000.0).Within(1e-9));
        }

        /// <summary>
        /// The two hashes answer different questions: <c>sourceFileHash</c> is byte-identity of the
        /// input file, <c>canonicalModelHash</c> is model-identity under formatting. Re-saving the
        /// same model with different formatting must change the source hash and keep the canonical
        /// hash, and the two must never be equal.
        /// </summary>
        [Test]
        public void Hashes_SourceVariesWithFormatting_CanonicalIsStable()
        {
            AnalyticalModel model = BenchmarkFixture.SingleSpaceModel();
            JsonObject neutral = model.ToJsonObject();

            string compact = neutral.ToJsonString();
            string indented = neutral.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            Assert.That(compact, Is.Not.EqualTo(indented), "The two serializations must differ in bytes");

            string sourceHashCompact = BenchmarkHash.ComputeSha256(Encoding.UTF8.GetBytes(compact));
            string sourceHashIndented = BenchmarkHash.ComputeSha256(Encoding.UTF8.GetBytes(indented));
            Assert.That(sourceHashCompact, Is.Not.EqualTo(sourceHashIndented), "sourceFileHash must track exact bytes");

            AnalyticalModel fromCompact = Core.Convert.ToSAM<AnalyticalModel>(compact).First();
            AnalyticalModel fromIndented = Core.Convert.ToSAM<AnalyticalModel>(indented).First();
            string canonicalFromCompact = BenchmarkCanonicalJson.ComputeSha256(fromCompact.ToJsonObject().ToJsonString());
            string canonicalFromIndented = BenchmarkCanonicalJson.ComputeSha256(fromIndented.ToJsonObject().ToJsonString());

            Assert.That(canonicalFromCompact, Is.EqualTo(canonicalFromIndented), "canonicalModelHash must be formatting-independent");
            Assert.That(canonicalFromCompact, Does.StartWith("sha256:"));
            Assert.That(canonicalFromCompact, Is.Not.EqualTo(sourceHashCompact), "the two hashes must never coincide");
        }

        private static void AssertAvailable(MetricValue metric, double expected, MetricUnit unit)
        {
            Assert.That(metric, Is.Not.Null);
            Assert.That(metric.Available, Is.True, "expected available");
            Assert.That(metric.Unit, Is.EqualTo(unit));
            Assert.That(metric.Value, Is.Not.Null);
            Assert.That(metric.Value.Value, Is.EqualTo(expected).Within(1e-9));
        }

        private static void AssertUnavailable(MetricValue metric, MetricUnit unit)
        {
            Assert.That(metric, Is.Not.Null);
            Assert.That(metric.Available, Is.False, "expected unavailable");
            Assert.That(metric.Unit, Is.EqualTo(unit));
            Assert.That(metric.Value, Is.Null);
        }
    }
}
