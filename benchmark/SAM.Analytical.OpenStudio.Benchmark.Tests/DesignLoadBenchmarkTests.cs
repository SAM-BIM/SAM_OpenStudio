// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SAM.Analytical.Benchmark;

namespace SAM.Analytical.OpenStudio.Benchmark.Tests
{
    /// <summary>
    /// <c>designLoad</c> reaches the neutral benchmark document from the route's zone sizing results.
    /// Every per-space design load was previously unavailable — 28 of the 66 unavailable metrics in the
    /// recorded cross-engine runs — even though EnergyPlus had sized the zones and written the loads to
    /// the SQL <c>ZoneSizes</c> table. Fully offline: the result set is fabricated.
    /// </summary>
    [TestFixture]
    public class DesignLoadBenchmarkTests
    {
        private static OpenStudioZoneSizingResult Row(string zoneName, string loadType, double? userDesignLoad, double? calculatedDesignLoad)
        {
            return new OpenStudioZoneSizingResult(zoneName, loadType, calculatedDesignLoad, userDesignLoad, 0.04, 0.05, "WINTER_DD", "3/2 08:00:00", -3.2, 0.00243652);
        }

        private static BenchmarkSpaceResult Space(AnalyticalModel model, OpenStudioSimulationResultSet resultSet)
        {
            OpenStudioBenchmarkContext context = BenchmarkFixture.Context(model, resultSet);
            context.SpaceDesignLoadResults = resultSet.ToSAM_SpaceDesignLoadResults(model.AdjacencyCluster.GetSpaces());

            BenchmarkDocument document = model.ToBenchmark(context);
            Assert.That(BenchmarkValidator.Validate(document).IsValid, Is.True, "The document must stay schema-valid");
            return document.Spaces.First();
        }

        private static OpenStudioSimulationResultSet WithZoneSizing(OpenStudioSimulationResultSet source, params OpenStudioZoneSizingResult[] rows)
        {
            // The fixture result set carries the annual measurements; re-create it with sizing rows added
            // so both families are present exactly as a real run has them.
            return new OpenStudioSimulationResultSet(
                source.AnnualHeatingEnergy, source.AnnualCoolingEnergy,
                source.PeakHeatingLoad, source.PeakCoolingLoad,
                source.PeakHeatingHour, source.PeakCoolingHour,
                source.PeakHeatingLoadTotal, source.PeakHeatingHourTotal,
                source.PeakCoolingLoadTotal, source.PeakCoolingHourTotal,
                source.UnmetHeatingHours, source.UnmetCoolingHours,
                source.PeopleGains, source.LightingGains, source.EquipmentGains,
                source.WindowSolarGains, source.InfiltrationGains,
                source.VentilationHeatingEnergy, source.VentilationCoolingEnergy,
                source.TemperatureSeries, source.OperativeTemperatureSeries, source.RelativeHumiditySeries,
                source.RuntimeSeconds, source.WarningCount, source.SevereCount, source.FatalCount,
                rows);
        }

        [Test]
        public void SizedZone_ReportsDesignLoadFromUserDesLoad()
        {
            AnalyticalModel model = BenchmarkFixture.SingleSpaceModel();
            Analytical.Space space = model.AdjacencyCluster.GetSpaces().First();
            string zoneName = Core.OpenStudio.Query.OpenStudioName("ThermalZone", space.Name, space.Guid);

            OpenStudioSimulationResultSet resultSet = WithZoneSizing(
                BenchmarkFixture.ResultSet(),
                Row(zoneName, "Heating", userDesignLoad: 1409.83, calculatedDesignLoad: 1127.86));

            BenchmarkSpaceResult result = Space(model, resultSet);

            Assert.That(result.Heating.DesignLoad.Available, Is.True, "The design load is no longer unavailable");
            Assert.That(result.Heating.DesignLoad.Value, Is.EqualTo(1409.83).Within(1e-9), "UserDesLoad is emitted, not CalcDesLoad");
            Assert.That(result.Heating.DesignLoad.Unit, Is.EqualTo(MetricUnit.Watt), "ZoneSizes loads are already W");
        }

        [Test]
        public void DesignLoadDoesNotDisturbTheAnnualPeak()
        {
            AnalyticalModel model = BenchmarkFixture.SingleSpaceModel();
            Analytical.Space space = model.AdjacencyCluster.GetSpaces().First();
            string zoneName = Core.OpenStudio.Query.OpenStudioName("ThermalZone", space.Name, space.Guid);

            OpenStudioSimulationResultSet baseline = BenchmarkFixture.ResultSet();
            BenchmarkSpaceResult without = Space(model, baseline);
            BenchmarkSpaceResult with = Space(model, WithZoneSizing(baseline, Row(zoneName, "Heating", 1409.83, 1127.86)));

            Assert.That(with.Heating.PeakLoad.Available, Is.EqualTo(without.Heating.PeakLoad.Available), "peakLoad availability is untouched by sizing results");
            Assert.That(with.Heating.PeakLoad.Value, Is.EqualTo(without.Heating.PeakLoad.Value), "peakLoad still comes from the annual weather run");
            Assert.That(with.Heating.PeakHour.Value, Is.EqualTo(without.Heating.PeakHour.Value), "peakHour still comes from the annual weather run");
        }

        [Test]
        public void UnsizedLoadType_StaysUnavailable()
        {
            AnalyticalModel model = BenchmarkFixture.SingleSpaceModel();
            Analytical.Space space = model.AdjacencyCluster.GetSpaces().First();
            string zoneName = Core.OpenStudio.Query.OpenStudioName("ThermalZone", space.Name, space.Guid);

            OpenStudioSimulationResultSet resultSet = WithZoneSizing(BenchmarkFixture.ResultSet(), Row(zoneName, "Heating", 1409.83, 1127.86));

            BenchmarkSpaceResult result = Space(model, resultSet);

            Assert.That(result.Cooling.DesignLoad.Available, Is.False, "A zone sized for heating only leaves the cooling design load unavailable");
            Assert.That(result.Cooling.DesignLoad.Value, Is.Null, "Unavailable stays null, never 0");
        }

        [Test]
        public void SizedZero_IsReportedAsAnAvailableZero()
        {
            AnalyticalModel model = BenchmarkFixture.SingleSpaceModel();
            Analytical.Space space = model.AdjacencyCluster.GetSpaces().First();
            string zoneName = Core.OpenStudio.Query.OpenStudioName("ThermalZone", space.Name, space.Guid);

            OpenStudioSimulationResultSet resultSet = WithZoneSizing(BenchmarkFixture.ResultSet(), Row(zoneName, "Cooling", userDesignLoad: 0.0, calculatedDesignLoad: 0.0));

            BenchmarkSpaceResult result = Space(model, resultSet);

            Assert.That(result.Cooling.DesignLoad.Available, Is.True, "A genuinely sized zero is a measurement");
            Assert.That(result.Cooling.DesignLoad.Value, Is.EqualTo(0.0));
        }

        [Test]
        public void NoZoneSizing_LeavesDesignLoadsUnavailableAsBefore()
        {
            AnalyticalModel model = BenchmarkFixture.SingleSpaceModel();

            BenchmarkSpaceResult result = Space(model, BenchmarkFixture.ResultSet());

            Assert.That(result.Heating.DesignLoad.Available, Is.False, "A run without sizing keeps the previous, honest behaviour");
            Assert.That(result.Cooling.DesignLoad.Available, Is.False);
        }
    }
}
