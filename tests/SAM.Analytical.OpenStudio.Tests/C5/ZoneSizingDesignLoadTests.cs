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
        private static OpenStudioZoneSizingResult Row(string zoneName, string loadType, double? userDesignLoad, double? calculatedDesignLoad = null, string designDayName = "WINTER_DD", string peakTime = "3/2 08:00:00", double? peakTemperature = -3.2, long sourceIndex = 0)
        {
            return new OpenStudioZoneSizingResult(zoneName, loadType, calculatedDesignLoad ?? userDesignLoad, userDesignLoad, 0.04, 0.05, designDayName, peakTime, peakTemperature, 0.00243652, sourceIndex);
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
        public void CalcDesLoad_PopulatesDesignLoad_NotUserDesLoad()
        {
            // CalcDesLoad is the unaltered thermal load from the design-day weather and schedules, which is
            // what the documented result contract defines designLoad to be. UserDesLoad is that load AFTER
            // sizing factors (the real HungaryHouse run applied 1.25, i.e. exactly the values below), so
            // emitting it would fold a user-configured margin into a cross-engine physics comparison.
            List<Space> spaces = Spaces();
            Space space = spaces.First();
            OpenStudioSimulationResultSet resultSet = ResultSet(Row(ThermalZoneName(space), "Heating", userDesignLoad: 1409.83, calculatedDesignLoad: 1127.86));

            List<SpaceSimulationResult> results = resultSet.ToSAM_SpaceDesignLoadResults(spaces);

            Assert.That(DesignLoad(results, LoadType.Heating), Is.EqualTo(1127.86).Within(1e-9), "CalcDesLoad is the calculated load the metric reports");
            Assert.That(DesignLoad(results, LoadType.Heating), Is.Not.EqualTo(1409.83).Within(1e-9), "The post-sizing-factor capacity must not be emitted as the design load");
            Assert.That(resultSet.ZoneSizing.Single().UserDesignLoad, Is.EqualTo(1409.83).Within(1e-9), "…but it stays readable for the sizing-capacity audit");
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
            // No calculated load, even though a post-factor capacity is present: the metric must stay
            // unavailable rather than fall back to the other column or to a zero.
            OpenStudioSimulationResultSet resultSet = ResultSet(new OpenStudioZoneSizingResult(
                ThermalZoneName(space), "Heating", null, 500.0, 0.04, 0.05, "WINTER_DD", "3/2 08:00:00", -3.2, 0.00243652));

            List<SpaceSimulationResult> results = resultSet.ToSAM_SpaceDesignLoadResults(spaces);

            Assert.That(results, Is.Empty, "A row without CalcDesLoad yields no design-load result at all, so the metric stays unavailable rather than becoming 0");
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
            Assert.That(result.GetValue<Core.OpenStudio.ShortDateTime>(SpaceSimulationResultParameter.PeakDate), Is.Not.Null, "The peak time stamp is parsed");
            Assert.That(result.GetValue<string>(SpaceSimulationResultParameter.ZoneName), Is.EqualTo(ThermalZoneName(space)), "The engine zone the row came from is recorded");
        }

        [Test]
        public void PeakTemp_DoesNotPopulateDesignDayTemperature()
        {
            // ZoneSizes.PeakTemp has an unsettled scope: EnergyPlus documents the zone sizing peak
            // temperature as a ZONE value, while on a real run every row equalled the design day's OUTDOOR
            // maximum dry bulb. Mapping it onto a parameter that names it an outdoor design-day
            // temperature would assert an interpretation this converter has no basis for, so it must not.
            List<Space> spaces = Spaces();
            Space space = spaces.First();
            OpenStudioSimulationResultSet resultSet = ResultSet(Row(ThermalZoneName(space), "Heating", 1400.0, peakTemperature: -3.2));

            SpaceSimulationResult result = resultSet.ToSAM_SpaceDesignLoadResults(spaces).Single();

            Assert.That(result.TryGetValue(Analytical.SpaceSimulationResultParameter.DesignDayTemperature, out double _), Is.False, "A populated PeakTemp must not become DesignDayTemperature");
            Assert.That(resultSet.ZoneSizing.Single().PeakTemperature, Is.EqualTo(-3.2).Within(1e-9), "…but the value is still carried verbatim for the audit");
        }

        [Test]
        public void OriginalResultSetConstructorSignatureStillExists()
        {
            // Binary compatibility: adding zone sizing as an OPTIONAL parameter on the original
            // constructor would keep source calls compiling while removing the 26-argument constructor
            // from metadata, so a Grasshopper or third-party assembly compiled against it would throw
            // MissingMethodException. Reflection is required here — a 26-argument source call would also
            // bind happily to an optional parameter and prove nothing.
            System.Type[] original = new System.Type[]
            {
                typeof(IReadOnlyDictionary<string, double>), typeof(IReadOnlyDictionary<string, double>),
                typeof(IReadOnlyDictionary<string, double>), typeof(IReadOnlyDictionary<string, double>),
                typeof(IReadOnlyDictionary<string, int>), typeof(IReadOnlyDictionary<string, int>),
                typeof(double), typeof(int), typeof(double), typeof(int),
                typeof(IReadOnlyDictionary<string, double>), typeof(IReadOnlyDictionary<string, double>),
                typeof(IReadOnlyDictionary<string, double>), typeof(IReadOnlyDictionary<string, double>),
                typeof(IReadOnlyDictionary<string, double>), typeof(IReadOnlyDictionary<string, double>),
                typeof(IReadOnlyDictionary<string, double>), typeof(IReadOnlyDictionary<string, double>),
                typeof(IReadOnlyDictionary<string, double>),
                typeof(IReadOnlyDictionary<string, double[]>), typeof(IReadOnlyDictionary<string, double[]>),
                typeof(IReadOnlyDictionary<string, double[]>),
                typeof(double), typeof(int), typeof(int), typeof(int)
            };

            System.Reflection.ConstructorInfo constructorInfo = typeof(OpenStudioSimulationResultSet).GetConstructor(original);

            Assert.That(constructorInfo, Is.Not.Null, "The original 26-argument constructor must remain in metadata for already-compiled consumers");
            Assert.That(constructorInfo.GetParameters().Length, Is.EqualTo(26));
            Assert.That(constructorInfo.GetParameters().Any(x => x.IsOptional), Is.False, "The preserved overload takes no optional parameters");

            // The zone-sizing overload is a separate, explicit signature — also not optional.
            System.Type[] withZoneSizing = original.Concat(new System.Type[] { typeof(IReadOnlyList<OpenStudioZoneSizingResult>) }).ToArray();
            System.Reflection.ConstructorInfo zoneSizingConstructor = typeof(OpenStudioSimulationResultSet).GetConstructor(withZoneSizing);
            Assert.That(zoneSizingConstructor, Is.Not.Null, "The zone-sizing constructor exists as its own 27-argument signature");
            Assert.That(zoneSizingConstructor.GetParameters().Any(x => x.IsOptional), Is.False, "Two explicit overloads, no optional parameter");

            // The preserved overload really does delegate: it yields an empty, non-null ZoneSizing.
            OpenStudioSimulationResultSet resultSet = (OpenStudioSimulationResultSet)constructorInfo.Invoke(new object[]
            {
                new Dictionary<string, double>(), new Dictionary<string, double>(),
                new Dictionary<string, double>(), new Dictionary<string, double>(),
                new Dictionary<string, int>(), new Dictionary<string, int>(),
                0d, 0, 0d, 0,
                new Dictionary<string, double>(), new Dictionary<string, double>(),
                new Dictionary<string, double>(), new Dictionary<string, double>(),
                new Dictionary<string, double>(), new Dictionary<string, double>(),
                new Dictionary<string, double>(), new Dictionary<string, double>(),
                new Dictionary<string, double>(),
                null, null, null,
                0d, 0, 0, 0
            });

            Assert.That(resultSet.ZoneSizing, Is.Not.Null.And.Empty, "The old signature yields an empty zone-sizing list, never null");
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
                Row(zoneName, "Heating", 1400.0, sourceIndex: 1),
                Row(zoneName, "Heating", 2800.0, sourceIndex: 2));

            List<SpaceSimulationResult> results = resultSet.ToSAM_SpaceDesignLoadResults(spaces, out List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics);

            Assert.That(results.Count, Is.EqualTo(1), "One design load per zone and load type");
            Assert.That(DesignLoad(results, LoadType.Heating), Is.EqualTo(1400.0).Within(1e-9), "The lowest source index wins");
            Assert.That(diagnostics.Count, Is.EqualTo(1), "The collision is reported rather than silently resolved");
            Assert.That(diagnostics[0].Message, Does.Contain("Duplicate zone sizing row"));
        }

        [Test]
        public void DuplicateRows_SurviveInputOrderReversalIdentically()
        {
            // The key alone cannot separate two rows sharing a zone and load type, and List.Sort is not a
            // stable sort, so without the source-index tie-breaker the surviving row would depend on the
            // order the database happened to return.
            List<Space> spaces = Spaces();
            string zoneName = ThermalZoneName(spaces.First());

            OpenStudioZoneSizingResult first = Row(zoneName, "Heating", 1400.0, sourceIndex: 1);
            OpenStudioZoneSizingResult second = Row(zoneName, "Heating", 2800.0, sourceIndex: 2);

            double? forward = DesignLoad(ResultSet(first, second).ToSAM_SpaceDesignLoadResults(spaces), LoadType.Heating);
            double? reversed = DesignLoad(ResultSet(second, first).ToSAM_SpaceDesignLoadResults(spaces), LoadType.Heating);

            Assert.That(reversed, Is.EqualTo(forward), "Reversing the row order must not change which design load is emitted");
            Assert.That(forward, Is.EqualTo(1400.0).Within(1e-9));
        }

        [Test]
        public void DuplicateRows_WithoutSourceIndices_StillSurviveOrderReversalIdentically()
        {
            // The harder case: a caller that supplies no source index leaves duplicates sharing the
            // default, so key AND index are equal and only the payload can separate them. Without that,
            // the unstable sort would let the input order decide which design load is emitted.
            List<Space> spaces = Spaces();
            string zoneName = ThermalZoneName(spaces.First());

            OpenStudioZoneSizingResult low = Row(zoneName, "Heating", 1400.0);
            OpenStudioZoneSizingResult high = Row(zoneName, "Heating", 2800.0);

            double? forward = DesignLoad(ResultSet(low, high).ToSAM_SpaceDesignLoadResults(spaces), LoadType.Heating);
            double? reversed = DesignLoad(ResultSet(high, low).ToSAM_SpaceDesignLoadResults(spaces), LoadType.Heating);

            Assert.That(forward, Is.Not.Null);
            Assert.That(reversed, Is.EqualTo(forward), "With no source index to separate them, the payload must still decide — not the input order");
        }

        [Test]
        public void IndistinguishableRows_CompareEqualAndEitherIsTheSameAnswer()
        {
            List<Space> spaces = Spaces();
            string zoneName = ThermalZoneName(spaces.First());

            OpenStudioZoneSizingResult one = Row(zoneName, "Heating", 1400.0);
            OpenStudioZoneSizingResult two = Row(zoneName, "Heating", 1400.0);

            Assert.That(one.SortSignature, Is.EqualTo(two.SortSignature), "Identical rows are genuinely interchangeable");
            Assert.That(DesignLoad(ResultSet(one, two).ToSAM_SpaceDesignLoadResults(spaces), LoadType.Heating), Is.EqualTo(1400.0).Within(1e-9));
        }

        [Test]
        public void DuplicateRows_OrderInTheResultSetIsTotal()
        {
            OpenStudioZoneSizingResult first = Row("ZONE_A", "Heating", 1.0, sourceIndex: 10);
            OpenStudioZoneSizingResult second = Row("ZONE_A", "Heating", 2.0, sourceIndex: 20);

            List<long> forward = ResultSet(first, second).ZoneSizing.Select(x => x.SourceIndex).ToList();
            List<long> reversed = ResultSet(second, first).ZoneSizing.Select(x => x.SourceIndex).ToList();

            Assert.That(forward, Is.EqualTo(new List<long> { 10, 20 }), "Equal keys are ordered by source index");
            Assert.That(reversed, Is.EqualTo(forward), "…independently of construction order");
        }

        [Test]
        public void ResultSetOrdersZoneSizingDeterministically()
        {
            OpenStudioSimulationResultSet first = ResultSet(Row("ZONE_B", "Heating", 1.0), Row("ZONE_A", "Cooling", 2.0));
            OpenStudioSimulationResultSet second = ResultSet(Row("ZONE_A", "Cooling", 2.0), Row("ZONE_B", "Heating", 1.0));

            Assert.That(first.ZoneSizing.Select(x => x.Key).ToList(), Is.EqualTo(second.ZoneSizing.Select(x => x.Key).ToList()), "Construction order must not leak into the result set");
            Assert.That(first.ZoneSizing.Select(x => x.Key).ToList(), Is.EqualTo(new List<string> { "ZONE_A|Cooling", "ZONE_B|Heating" }));
        }

        /// <summary>
        /// Minimal on-disk EnergyPlus-shaped SQL with a ZoneSizes table, used to exercise the established
        /// <see cref="Create.SpaceSimulationResults(string)"/> consumer path (Modify.AddResults and the
        /// Grasshopper SQL component) offline. No OpenStudio or EnergyPlus involved.
        /// </summary>
        private static string WriteSql(bool withUserDesignLoad, double calculatedDesignLoad, double userDesignLoad)
        {
            string path = System.IO.Path.Combine(TestContext.CurrentContext.WorkDirectory, "zonesizes_" + System.Guid.NewGuid().ToString("N").Substring(0, 8) + ".sql");
            using (System.Data.SQLite.SQLiteConnection connection = new System.Data.SQLite.SQLiteConnection(new System.Data.SQLite.SQLiteConnectionStringBuilder { DataSource = path }.ConnectionString))
            {
                connection.Open();
                using (System.Data.SQLite.SQLiteCommand command = connection.CreateCommand())
                {
                    void Exec(string sql)
                    {
                        command.CommandText = sql;
                        command.ExecuteNonQuery();
                    }

                    Exec("CREATE TABLE Zones (ZoneIndex INTEGER PRIMARY KEY, ZoneName TEXT, FloorArea REAL, Volume REAL)");
                    Exec("CREATE TABLE Surfaces (SurfaceIndex INTEGER PRIMARY KEY, SurfaceName TEXT, ZoneIndex INTEGER)");
                    Exec("CREATE TABLE EnvironmentPeriods (EnvironmentPeriodIndex INTEGER PRIMARY KEY, EnvironmentName TEXT, EnvironmentType INTEGER)");
                    Exec("CREATE TABLE Time (TimeIndex INTEGER PRIMARY KEY, Year INTEGER, Month INTEGER, Day INTEGER, Hour INTEGER, Minute INTEGER, Dst INTEGER, EnvironmentPeriodIndex INTEGER)");
                    Exec("CREATE TABLE ReportDataDictionary (ReportDataDictionaryIndex INTEGER PRIMARY KEY, KeyValue TEXT, Name TEXT, Units TEXT)");
                    Exec("CREATE TABLE ReportData (ReportDataIndex INTEGER PRIMARY KEY, ReportDataDictionaryIndex INTEGER, TimeIndex INTEGER, Value REAL)");
                    Exec("CREATE TABLE NominalLighting (ZoneIndex INTEGER, DesignLevel REAL)");
                    Exec("CREATE TABLE NominalInfiltration (ZoneIndex INTEGER, DesignLevel REAL)");
                    Exec("CREATE TABLE NominalElectricEquipment (ZoneIndex INTEGER, DesignLevel REAL)");
                    Exec(withUserDesignLoad
                        ? "CREATE TABLE ZoneSizes (ZoneName TEXT, LoadType TEXT, CalcDesLoad REAL, UserDesLoad REAL, DesDayName TEXT, PeakHrMin TEXT, PeakTemp REAL, PeakHumRat REAL)"
                        : "CREATE TABLE ZoneSizes (ZoneName TEXT, LoadType TEXT, CalcDesLoad REAL, DesDayName TEXT, PeakHrMin TEXT, PeakTemp REAL, PeakHumRat REAL)");

                    Exec("INSERT INTO Zones VALUES (1, 'ZONE_ONE', 10.0, 30.0)");
                    Exec("INSERT INTO EnvironmentPeriods VALUES (1, 'WINTER_DD', 1)");
                    Exec(withUserDesignLoad
                        ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "INSERT INTO ZoneSizes VALUES ('ZONE_ONE', 'Heating', {0}, {1}, 'WINTER_DD', ' 1/21 10:00', -10.0, 0.001)", calculatedDesignLoad, userDesignLoad)
                        : string.Format(System.Globalization.CultureInfo.InvariantCulture, "INSERT INTO ZoneSizes VALUES ('ZONE_ONE', 'Heating', {0}, 'WINTER_DD', ' 1/21 10:00', -10.0, 0.001)", calculatedDesignLoad));
                }
            }

            return path;
        }

        [Test]
        public void EstablishedSqlConsumerPath_UsesCalcDesLoad_EvenWhenUserDesLoadIsPresent()
        {
            // ONE design-load definition across consumers: Create.SpaceSimulationResults feeds
            // Modify.AddResults and the Grasshopper SQL component, so it must emit the same value the
            // benchmark path does. Otherwise a sizing factor makes the same run report two design loads.
            string path = WriteSql(withUserDesignLoad: true, calculatedDesignLoad: 1127.86, userDesignLoad: 1409.83);

            List<SpaceSimulationResult> results = Create.SpaceSimulationResults(path);

            SpaceSimulationResult result = results.Single(x => x.TryGetValue(Analytical.SpaceSimulationResultParameter.DesignLoad, out double _));
            result.TryGetValue(Analytical.SpaceSimulationResultParameter.DesignLoad, out double designLoad);
            Assert.That(designLoad, Is.EqualTo(1127.86).Within(1e-6), "CalcDesLoad, matching Convert.ToSAM_SpaceDesignLoadResults and the documented contract");
        }

        [Test]
        public void EstablishedSqlConsumerPath_WorksOnTablesWithoutTheUserDesLoadColumn()
        {
            // Older/synthetic ZoneSizes tables have no UserDesLoad column at all; the reader must keep
            // working against them, which it does because it only ever reads CalcDesLoad.
            string path = WriteSql(withUserDesignLoad: false, calculatedDesignLoad: 1127.86, userDesignLoad: 0);

            List<SpaceSimulationResult> results = Create.SpaceSimulationResults(path);

            SpaceSimulationResult result = results.Single(x => x.TryGetValue(Analytical.SpaceSimulationResultParameter.DesignLoad, out double _));
            result.TryGetValue(Analytical.SpaceSimulationResultParameter.DesignLoad, out double designLoad);
            Assert.That(designLoad, Is.EqualTo(1127.86).Within(1e-6), "CalcDesLoad is read whether or not the newer column exists");
        }

        [Test]
        public void NullSpaces_YieldsEmptyResults()
        {
            List<SpaceSimulationResult> results = ResultSet(Row("ZONE_A", "Heating", 1.0)).ToSAM_SpaceDesignLoadResults(null);

            Assert.That(results, Is.Not.Null.And.Empty);
        }
    }
}
