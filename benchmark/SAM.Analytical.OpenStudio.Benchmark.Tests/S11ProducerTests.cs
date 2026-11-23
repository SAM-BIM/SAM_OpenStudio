// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SAM.Analytical.Benchmark;

namespace SAM.Analytical.OpenStudio.Benchmark.Tests
{
    /// <summary>
    /// S11 producer rules: the all-zero peak rule, unavailable consumption without zone keys, the F3 Total-basis note, the
    /// three-argument mapping of exactly the captured instances, the closure of the --sam-results file, and its CLI guard.
    /// </summary>
    [TestFixture]
    public class S11ProducerTests
    {
        private static OpenStudioSimulationResultSet ResultSet(double coolingPeakKw, int coolingHour, double coolingKwh, bool coolingKeyed = true, bool heatingKeyed = true)
        {
            string key = BenchmarkFixture.IdealLoadsKey();
            Dictionary<string, double> empty = new Dictionary<string, double>();
            return new OpenStudioSimulationResultSet(
                heatingKeyed ? new Dictionary<string, double> { { key, 10.5 } } : empty,
                coolingKeyed ? new Dictionary<string, double> { { key, coolingKwh } } : empty,
                new Dictionary<string, double> { { key, 3.25 } },
                new Dictionary<string, double> { { key, coolingPeakKw } },
                new Dictionary<string, int> { { key, 100 } },
                new Dictionary<string, int> { { key, coolingHour } },
                3.25, 100, coolingPeakKw, coolingHour,
                new Dictionary<string, double> { { key, 2.0 } },
                new Dictionary<string, double> { { key, 0.0 } },
                empty, empty, empty, empty, empty, empty, empty,
                null, null, null, 1.0, 0, 0, 0);
        }

        private static BenchmarkDocument Document(OpenStudioSimulationResultSet resultSet, RunState state = RunState.Success)
        {
            AnalyticalModel model = BenchmarkFixture.SingleSpaceModel();
            OpenStudioBenchmarkContext context = BenchmarkFixture.Context(model, resultSet);
            context.State = state;
            return model.ToBenchmark(context);
        }

        [Test]
        public void AllZeroPeak_LoadIsAMeasuredZero_HourIsUnavailable()
        {
            BenchmarkDocument document = Document(ResultSet(0, 0, 0));
            BenchmarkConditionResult cooling = document.Spaces.Single().Cooling;

            Assert.That(cooling.PeakLoad.Available, Is.True);
            Assert.That(cooling.PeakLoad.Value, Is.EqualTo(0));
            Assert.That(cooling.PeakHour.Available, Is.False, "an all-zero year ties at every hour: no peak hour");
            Assert.That(document.Model.PeakCoolingLoad.Value, Is.EqualTo(0));
            Assert.That(document.Model.PeakCoolingHour.Available, Is.False);
            Assert.That(document.Model.ConsumptionCooling.Available, Is.True, "a keyed zero consumption is a measured 0 kWh");
            Assert.That(document.Model.ConsumptionCooling.Value, Is.EqualTo(0));
            Assert.That(document.Spaces.Single().Heating.PeakHour.Value, Is.EqualTo(100), "a non-zero peak keeps its hour");
            Assert.That(BenchmarkValidator.Validate(document).IsValid, Is.True);
        }

        [Test]
        public void NoZoneKey_ConsumptionIsUnavailable_NotZero()
        {
            BenchmarkDocument document = Document(ResultSet(2.4, 4602, 0, coolingKeyed: false, heatingKeyed: false));

            Assert.That(document.Model.ConsumptionCooling.Available, Is.False);
            Assert.That(document.Model.ConsumptionCooling.Value, Is.Null);
            Assert.That(document.Model.ConsumptionHeating.Available, Is.False);
            Assert.That(BenchmarkValidator.Validate(document).IsValid, Is.True);
        }

        [Test]
        public void TotalBasisNote_OnSuccessfulDocumentsOnly_ExactlyOnce()
        {
            BenchmarkDocument success = Document(BenchmarkFixture.ResultSet());
            Assert.That(success.Provenance.Notes.Count(x => x == Modify.TotalBasisNote), Is.EqualTo(1));
            Assert.That(Modify.TotalBasisNote, Is.EqualTo("Cooling metrics are EnergyPlus Ideal Loads Supply Air Total cooling (sensible + latent); heating is Total heating."));

            AnalyticalModel model = BenchmarkFixture.SingleSpaceModel();
            OpenStudioBenchmarkContext again = BenchmarkFixture.Context(model, BenchmarkFixture.ResultSet());
            again.Notes.Add(Modify.TotalBasisNote);
            Assert.That(model.ToBenchmark(again).Provenance.Notes.Count(x => x == Modify.TotalBasisNote), Is.EqualTo(1), "a context already carrying the note keeps one");

            BenchmarkDocument failure = Document(null, RunState.Failure);
            Assert.That(failure.Provenance.Notes, Has.None.EqualTo(Modify.TotalBasisNote));
        }

        [Test]
        public void ThreeArgumentToBenchmark_MapsExactlyTheGivenInstances()
        {
            AnalyticalModel model = BenchmarkFixture.SingleSpaceModel();
            OpenStudioBenchmarkContext context = BenchmarkFixture.Context(model, BenchmarkFixture.ResultSet());
            OpenStudioBenchmarkInputs inputs = model.BenchmarkInputs(context);

            Assert.That(BenchmarkSerializer.Serialize(model.ToBenchmark(context, inputs)), Is.EqualTo(BenchmarkSerializer.Serialize(model.ToBenchmark(context))), "the two-argument form is the three-argument form on fresh inputs");

            inputs.SpaceResults.First(x => x.TryGetValue(Analytical.SpaceSimulationResultParameter.LoadType, out string t) && t == "Cooling").SetValue(Analytical.SpaceSimulationResultParameter.Load, 1111.0);
            inputs.ModelResult.SetValue(AnalyticalModelSimulationResultParameter.ConsumptionHeating, 77.0);
            BenchmarkDocument document = model.ToBenchmark(context, inputs);
            Assert.That(document.Spaces.Single().Cooling.PeakLoad.Value, Is.EqualTo(1111.0), "the document follows the instances it is given, not the context's result set");
            Assert.That(document.Model.ConsumptionHeating.Value, Is.EqualTo(77.0));
        }

        [Test]
        public void SamResultsFile_RoundTrips_AndReMapsToByteIdenticalModelAndSpaces()
        {
            AnalyticalModel model = BenchmarkFixture.SingleSpaceModel();
            OpenStudioBenchmarkContext context = BenchmarkFixture.Context(model, BenchmarkFixture.ResultSet());
            OpenStudioBenchmarkInputs inputs = model.BenchmarkInputs(context);
            BenchmarkDocument produced = model.ToBenchmark(context, inputs);

            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "s11-sam-results-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".json");
            StringWriter output = new StringWriter();
            Program.WriteSamResults(inputs, path, output);
            Assert.That(output.ToString(), Does.Contain(BenchmarkHash.ComputeSha256(File.ReadAllBytes(path))));
            byte[] bytes = File.ReadAllBytes(path);
            Assert.That(bytes.Take(3), Is.Not.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }), "UTF-8 without BOM");
            Assert.That(System.Text.Encoding.UTF8.GetString(bytes), Does.Not.Contain("\r\n"));

            OpenStudioBenchmarkInputs read = OpenStudioBenchmarkInputs.Read(path);
            BenchmarkDocument remapped = model.ToBenchmark(context, read);
            JsonObject a = JsonNode.Parse(BenchmarkSerializer.Serialize(produced)) as JsonObject;
            JsonObject b = JsonNode.Parse(BenchmarkSerializer.Serialize(remapped)) as JsonObject;
            Assert.That(b["model"].ToJsonString(), Is.EqualTo(a["model"].ToJsonString()));
            Assert.That(b["spaces"].ToJsonString(), Is.EqualTo(a["spaces"].ToJsonString()));
            Assert.That(read.HeatingConsumptionKeyed && read.CoolingConsumptionKeyed, Is.True);

            read.SpaceResults[0].SetValue(Analytical.SpaceSimulationResultParameter.Load, 1.0);
            JsonObject altered = JsonNode.Parse(BenchmarkSerializer.Serialize(model.ToBenchmark(context, read))) as JsonObject;
            Assert.That(altered["spaces"].ToJsonString(), Is.Not.EqualTo(a["spaces"].ToJsonString()), "an altered object no longer closes");
        }

        [Test]
        public void SamResultsFile_ForeignSchema_IsRejected()
        {
            Assert.Throws<System.Text.Json.JsonException>(() => OpenStudioBenchmarkInputs.FromJsonObject(new JsonObject { ["schema"] = "other/1" }));
        }

        [Test]
        public void SamResultsSameAsOut_IsAUsageError_BeforeAnyRun()
        {
            string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "s11-cli-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(directory);
            string model = Path.Combine(directory, "model.json");
            string weather = Path.Combine(directory, "weather.epw");
            File.WriteAllText(model, "{}");
            File.WriteAllText(weather, "x");
            string output = Path.Combine(directory, "b1a.json");

            StringWriter standardOutput = new StringWriter();
            StringWriter standardError = new StringWriter();
            int exit = Program.Run(new[] { "--model", model, "--weather", weather, "--out", output, "--sam-results", output }, standardOutput, standardError);

            Assert.That(exit, Is.EqualTo((int)BenchmarkExitCode.InvalidUsage), standardError.ToString());
            Assert.That(File.Exists(output), Is.False);
        }
    }
}
