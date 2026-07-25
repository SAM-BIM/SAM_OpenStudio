// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// Per-space design loads come from the route's zone sizing results (SQL <c>ZoneSizes</c>), mapped
    /// onto <see cref="Analytical.SpaceSimulationResultParameter.DesignLoad"/>. Before this existed the
    /// benchmark reported every design load unavailable even though EnergyPlus had sized the zones.
    /// <para>
    /// Fully offline: the result set is fabricated, so no OpenStudio install or EnergyPlus run is
    /// involved.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ZoneSizingDesignLoadTests
    {
        private static OpenStudioZoneSizingResult Row(string zoneName, string loadType, double? userDesignLoad, double? calculatedDesignLoad = null, string designDayName = "WINTER_DD", string peakTime = "3/2 08:00:00", double? peakTemperature = -3.2)
        {
            return new OpenStudioZoneSizingResult(zoneName, loadType, calculatedDesignLoad ?? userDesignLoad, userDesignLoad, 0.04, 0.05, designDayName, peakTime, peakTemperature, 0.00243652);
        }

        private static OpenStudioSimulationResultSet ResultSet(params OpenStudioZoneSizingResult[] rows)
        {
            var empty = new Dictionary<string, double>();
            var emptyHours = new Dictionary<string, int>();
            var emptySeries = new Dictionary<string, double[]>();
            return new OpenStudioSimulationResultSet(
                empty, empty, empty, empty, emptyHours, emptyHours,
                0, 0, 0, 0,
                empty, empty, empty, empty, empty, empty, empty, empty, empty,
                emptySeries, emptySeries, emptySeries,
                1.0, 0, 0, 0,
                rows);
        }

        private static List<Space> Spaces()
        {
            return AnalyticalModelFixtures.SingleBox().AdjacencyCluster.GetSpaces().ToList();
        }

        private static string ThermalZoneName(Space space)
        {
            return Core.OpenStudio.Query.OpenStudioName("ThermalZone", space.Name, space.Guid);
        }

        private static double? DesignLoad(IEnumerable<SpaceSimulationResult> results, LoadType loadType)
        {
            SpaceSimulationResult result = results.FirstOrDefault(x => string.Equals(x.GetValue<string>(Analytical.SpaceSimulationResultParameter.LoadType), loadType.ToString(), System.StringComparison.OrdinalIgnoreCase));
            if (result == null)
            {
                return null;
            }

            return result.TryGetValue(Analytical.SpaceSimulationResultParameter.DesignLoad, out double value) ? value : (double?)null;
        }

        [Test]
        public void UserDesLoad_PopulatesDesignLoad_NotCalcDesLoad()
        {
            // EnergyPlus sizes components with UserDesLoad (after sizing factors); CalcDesLoad is the
            // unaltered calculation kept for audit. The benchmark metric must be the former.
            List<Space> spaces = Spaces();
            Space space = spaces.First();
            OpenStudioSimulationResultSet resultSet = ResultSet(Row(ThermalZoneName(space), "Heating", userDesignLoad: 1409.83, calculatedDesignLoad: 1127.86));

            List<SpaceSimulationResult> results = resultSet.ToSAM_SpaceDesignLoadResults(spaces);

            Assert.That(DesignLoad(results, LoadType.Heating), Is.EqualTo(1409.83).Within(1e-9), "UserDesLoad is the sizing load actually used");
            Assert.That(DesignLoad(results, LoadType.Heating), Is.Not.EqualTo(1127.86).Within(1e-9), "CalcDesLoad must not be emitted as the design load");
        }

        [Test]
        public void HeatingAndCooling_MapIndependently()
        {
            List<Space> spaces = Spaces();
            Space space = spaces.First();
            OpenStudioSimulationResultSet resultSet = ResultSet(Row(ThermalZoneName(space), "Heating", 1400.0));

            List<SpaceSimulationResult> results = resultSet.ToSAM_SpaceDesignLoadResults(spaces);

            Assert.That(DesignLoad(results, LoadType.Heating), Is.EqualTo(1400.0).Within(1e-9));
            Assert.That(DesignLoad(results, LoadType.Cooling), Is.Null, "A zone sized for heating only yields no cooling design load");
        }

        [Test]
        public void SizedZero_IsAnAvailableZero()
        {
            List<Space> spaces = Spaces();
            Space space = spaces.First();
            OpenStudioSimulationResultSet resultSet = ResultSet(Row(ThermalZoneName(space), "Cooling", 0.0));

            List<SpaceSimulationResult> results = resultSet.ToSAM_SpaceDesignLoadResults(spaces);

            Assert.That(DesignLoad(results, LoadType.Cooling), Is.EqualTo(0.0), "A genuinely sized zero is a result, not a missing value");
        }

        [Test]
        public void MissingLoad_StaysUnavailable_NeverZero()
        {
            List<Space> spaces = Spaces();
            Space space = spaces.First();
            OpenStudioSimulationResultSet resultSet = ResultSet(Row(ThermalZoneName(space), "Heating", userDesignLoad: null, calculatedDesignLoad: 500.0));

            List<SpaceSimulationResult> results = resultSet.ToSAM_SpaceDesignLoadResults(spaces);

            Assert.That(results, Is.Empty, "A row without UserDesLoad yields no design-load result at all, so the metric stays unavailable rather than becoming 0");
        }

        [Test]
        public void NoZoneSizing_YieldsNoResultsAndNoDiagnostics()
        {
            List<SpaceSimulationResult> results = ResultSet().ToSAM_SpaceDesignLoadResults(Spaces(), out List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics);

            Assert.That(results, Is.Empty, "A run that performed no sizing simply has no design loads");
            Assert.That(diagnostics, Is.Empty, "Absent sizing is not an anomaly worth warning about");
        }

        [Test]
        public void PeakContext_IsCarriedForAudit()
        {
            List<Space> spaces = Spaces();
            Space space = spaces.First();
            OpenStudioSimulationResultSet resultSet = ResultSet(Row(ThermalZoneName(space), "Heating", 1400.0, designDayName: "LONDON_TRY_ANN_HTG_100%_CONDS_DB", peakTime: "3/2 08:20:00", peakTemperature: -3.2));

            SpaceSimulationResult result = resultSet.ToSAM_SpaceDesignLoadResults(spaces).Single();

            Assert.That(result.GetValue<string>(SpaceSimulationResultParameter.DesignDayName), Is.EqualTo("LONDON_TRY_ANN_HTG_100%_CONDS_DB"), "The governing design day is recorded");
            Assert.That(result.GetValue<double>(Analytical.SpaceSimulationResultParameter.DesignDayTemperature), Is.EqualTo(-3.2).Within(1e-9), "The outdoor temperature at the peak is recorded");
            Assert.That(result.GetValue<Core.OpenStudio.ShortDateTime>(SpaceSimulationResultParameter.PeakDate), Is.Not.Null, "The peak time stamp is parsed");
            Assert.That(result.GetValue<string>(SpaceSimulationResultParameter.ZoneName), Is.EqualTo(ThermalZoneName(space)), "The engine zone the row came from is recorded");
        }

        [Test]
        public void AnnualLoadAndIndex_AreNeverWrittenByTheSizingMapping()
        {
            // The sizing mapping must not be able to relabel a design load as an annual simulated peak.
            List<Space> spaces = Spaces();
            OpenStudioSimulationResultSet resultSet = ResultSet(Row(ThermalZoneName(spaces.First()), "Heating", 1400.0));

            SpaceSimulationResult result = resultSet.ToSAM_SpaceDesignLoadResults(spaces).Single();

            Assert.That(result.TryGetValue(Analytical.SpaceSimulationResultParameter.Load, out double _), Is.False, "Load stays an annual-only result");
            Assert.That(result.TryGetValue(Analytical.SpaceSimulationResultParameter.LoadIndex, out int _), Is.False, "LoadIndex stays an annual-only result");
        }

        [Test]
        public void UnmatchedEnergyPlusZone_RaisesADiagnostic()
        {
            List<Space> spaces = Spaces();
            OpenStudioSimulationResultSet resultSet = ResultSet(Row("SAM_THERMALZONE_A_ZONE_THAT_IS_NOT_IN_THE_MODEL", "Heating", 999.0));

            List<SpaceSimulationResult> results = resultSet.ToSAM_SpaceDesignLoadResults(spaces, out List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics);

            Assert.That(results, Is.Empty);
            Assert.That(diagnostics.Count, Is.EqualTo(1), "An engine zone that matches no source space is named, not silently dropped");
            Assert.That(diagnostics[0].Severity, Is.EqualTo(Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning));
            Assert.That(diagnostics[0].Message, Does.Contain("matches no source SAM space"));
        }

        [Test]
        public void DuplicateZoneAndLoadTypeRows_FirstWinsExplicitly()
        {
            List<Space> spaces = Spaces();
            Space space = spaces.First();
            string zoneName = ThermalZoneName(space);

            // Deterministic: the result set orders rows ordinally by ZoneName|LoadType, so "first" is
            // stable whatever order the SQL reader returned them in.
            OpenStudioSimulationResultSet resultSet = ResultSet(
                Row(zoneName, "Heating", 1400.0),
                Row(zoneName, "Heating", 2800.0));

            List<SpaceSimulationResult> results = resultSet.ToSAM_SpaceDesignLoadResults(spaces, out List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics);

            Assert.That(results.Count, Is.EqualTo(1), "One design load per zone and load type");
            Assert.That(DesignLoad(results, LoadType.Heating), Is.EqualTo(1400.0).Within(1e-9), "The first row wins");
            Assert.That(diagnostics.Count, Is.EqualTo(1), "The collision is reported rather than silently resolved");
            Assert.That(diagnostics[0].Message, Does.Contain("Duplicate zone sizing row"));
        }

        [Test]
        public void ResultSetOrdersZoneSizingDeterministically()
        {
            OpenStudioSimulationResultSet first = ResultSet(Row("ZONE_B", "Heating", 1.0), Row("ZONE_A", "Cooling", 2.0));
            OpenStudioSimulationResultSet second = ResultSet(Row("ZONE_A", "Cooling", 2.0), Row("ZONE_B", "Heating", 1.0));

            Assert.That(first.ZoneSizing.Select(x => x.Key).ToList(), Is.EqualTo(second.ZoneSizing.Select(x => x.Key).ToList()), "Construction order must not leak into the result set");
            Assert.That(first.ZoneSizing.Select(x => x.Key).ToList(), Is.EqualTo(new List<string> { "ZONE_A|Cooling", "ZONE_B|Heating" }));
        }

        [Test]
        public void NullSpaces_YieldsEmptyResults()
        {
            List<SpaceSimulationResult> results = ResultSet(Row("ZONE_A", "Heating", 1.0)).ToSAM_SpaceDesignLoadResults(null);

            Assert.That(results, Is.Not.Null.And.Empty);
        }
    }
}
