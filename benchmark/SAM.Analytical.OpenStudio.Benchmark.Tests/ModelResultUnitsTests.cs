// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using NUnit.Framework;

namespace SAM.Analytical.OpenStudio.Benchmark.Tests
{
    /// <summary>
    /// The model result's units (SAM_Tas#92, option A): OpenStudio has always written consumption in kWh and peak loads in
    /// kW; it now also marks the result, so SAM's reader never has to infer the units from the producer, and the source
    /// it records stays the one SAM recognises for older, unmarked OpenStudio results. Fully offline: the result set is
    /// fabricated.
    /// </summary>
    [TestFixture]
    public class ModelResultUnitsTests
    {
        private static OpenStudioSimulationResultSet ResultSet()
        {
            var heating = new Dictionary<string, double> { ["Zone A"] = 10_000.5, ["Zone B"] = 5_227.34 };
            var cooling = new Dictionary<string, double> { ["Zone A"] = 2_523.75 };
            var empty = new Dictionary<string, double>();
            var emptyHours = new Dictionary<string, int>();
            var emptySeries = new Dictionary<string, double[]>();
            return new OpenStudioSimulationResultSet(
                heating, cooling, empty, empty, emptyHours, emptyHours,
                38.72740625, 205, 21.456875, 4602,
                empty, empty, empty, empty, empty, empty, empty, empty, empty,
                emptySeries, emptySeries, emptySeries,
                1.0, 0, 0, 0,
                new List<OpenStudioZoneSizingResult>());
        }

        [Test]
        public void ModelResult_IsMarked_AndReadAsStored()
        {
            AnalyticalModelSimulationResult result = ResultSet().ToSAM((AnalyticalModel)null);

            Assert.That(result.TryGetValue(AnalyticalModelSimulationResultParameter.ResultUnitsVersion, out int version), Is.True);
            Assert.That(version, Is.EqualTo(AnalyticalModelSimulationResult.CurrentResultUnitsVersion));
            Assert.That(result.ResultUnitsState(), Is.EqualTo(ResultUnitsState.Marked));

            Assert.That(result.ConsumptionOrPeakLoad(AnalyticalModelSimulationResultParameter.ConsumptionHeating), Is.EqualTo(15_227.84).Within(1e-9));
            Assert.That(result.ConsumptionOrPeakLoad(AnalyticalModelSimulationResultParameter.ConsumptionCooling), Is.EqualTo(2_523.75));
            Assert.That(result.ConsumptionOrPeakLoad(AnalyticalModelSimulationResultParameter.PeakHeatingLoad), Is.EqualTo(38.72740625));
            Assert.That(result.ConsumptionOrPeakLoad(AnalyticalModelSimulationResultParameter.PeakCoolingLoad), Is.EqualTo(21.456875));
        }

        [Test]
        public void ModelResultSource_IsTheProducerNameSamReadsUnmarkedOpenStudioResultsBy()
        {
            AnalyticalModelSimulationResult result = ResultSet().ToSAM((AnalyticalModel)null);

            // A rename of this assembly would make every persisted (unmarked) OpenStudio result an unknown producer.
            Assert.That(typeof(global::SAM.Analytical.OpenStudio.Convert).Assembly.GetName().Name, Is.EqualTo(AnalyticalModelSimulationResult.OpenStudioSource));
            Assert.That(result.Source, Is.EqualTo(AnalyticalModelSimulationResult.OpenStudioSource));

            // An older OpenStudio result, written before the marker, reads the same.
            result.RemoveValue(AnalyticalModelSimulationResultParameter.ResultUnitsVersion);
            Assert.That(result.ResultUnitsState(), Is.EqualTo(ResultUnitsState.LegacyOpenStudio));
            Assert.That(result.ConsumptionOrPeakLoad(AnalyticalModelSimulationResultParameter.ConsumptionHeating), Is.EqualTo(15_227.84).Within(1e-9));
            Assert.That(result.ConsumptionOrPeakLoad(AnalyticalModelSimulationResultParameter.PeakHeatingLoad), Is.EqualTo(38.72740625));
        }
    }
}
