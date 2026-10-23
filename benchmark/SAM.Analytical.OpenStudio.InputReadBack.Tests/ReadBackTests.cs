// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Analytical.InputEquivalence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SAM.Analytical.OpenStudio.InputReadBack.Tests
{
    /// <summary>
    /// The S09 read-back end to end, offline: export, fresh-process read and compare on the S08 signed-off fixtures with the
    /// Budapest EPW. Each fixture has complete S07 coverage. Opaque optics on faces EnergyPlus does not use are
    /// OUT-OF-ENGINE-SCOPE (S07 §5.2, amendment U1): informational, not gated, never MATCH. Every gated quantity passes, so
    /// both fixtures PASS scoped native-input conformance (exit 0); this is not simulation equivalence. The lighting split
    /// is the S07 §5.1 T-LIGHT-SPLIT of the authored p. The ventilation policy holds hour by hour, and every targeted
    /// mutation is refused.
    /// </summary>
    [TestFixture]
    public partial class ReadBackTests
    {
        private string boxRun;
        private string twoRun;
        private OsReport box;
        private OsReport two;

        [OneTimeSetUp]
        public void ExportBothFixtures()
        {
            Assert.That(Hashing.Sha256(TestSupport.Fixture(TestSupport.Box)), Is.EqualTo(TestSupport.BoxSha256), "the S08 signed-off box fixture, byte for byte");
            Assert.That(Hashing.Sha256(TestSupport.Fixture(TestSupport.Two)), Is.EqualTo(TestSupport.TwoSha256), "the S08 signed-off two-zone fixture, byte for byte");
            box = TestSupport.Pipeline(TestSupport.Fixture(TestSupport.Box), TestSupport.Box, "box", out boxRun);
            two = TestSupport.Pipeline(TestSupport.Fixture(TestSupport.Two), TestSupport.Two, "two", out twoRun);
        }

        private static string Ic(Guid space, string what) => "space:" + space.ToString("D") + ".ic." + what;

        /// <summary>
        /// The gating non-PASS items of a fixture; must be empty. Since S07 §5.2 (amendment U1) no gated item is excepted:
        /// the unused-face optics rows are OUT-OF-ENGINE-SCOPE and never in <see cref="OsReport.NonPass"/>. The lighting rows
        /// have no exception (S07 §5.1 removed the view-coefficient comparison and its DOCUMENTED-DEFAULT).
        /// </summary>
        private static List<ReportItem> Unexplained(OsReport report)
        {
            return report.NonPass.ToList();
        }

        private static readonly string[] LightingRows = { "lighting.returnAir", "lighting.radiantTotal", "lighting.visible", "lighting.radiant", "lighting.convective" };

        /// <summary>
        /// Every T-LIGHT-SPLIT row of a space, recomputed in this process from the run's frozen model and the snapshot the
        /// fresh-process reader wrote (the report lists only the non-PASS rows). The verdict itself is the CLI's.
        /// </summary>
        private static Dictionary<string, QuantityResult> LightingResults(string run, string fixtureId, Guid space)
        {
            AnalyticalModel model = Core.Convert.ToSAM<AnalyticalModel>(Path.Combine(run, "frozen-model.json")).Single();
            NativeSnapshot snapshot = NativeSnapshot.Load(Path.Combine(run, "native.json"));
            OsComparison comparison = OsComparison.Run(model, snapshot, Path.Combine(run, "weather.epw"), fixtureId, GateRuleSet.Approved);
            return LightingRows.ToDictionary(x => x, x => comparison.Results.Single(r => r.Input.ObjectKey == Ic(space, x)));
        }

        [TestCase(TestSupport.Box, "5080c000-0000-0000-0000-00000000c001", 0.37)]
        [TestCase(TestSupport.Two, "5080c000-0000-0000-0000-00000000c0a1", 0.34)]
        [TestCase(TestSupport.Two, "5080c000-0000-0000-0000-00000000c0b1", 0.41)]
        public void TheAuthoredP_IsWrittenAsTheAcceptedLightingSplit(string fixtureId, string spaceId, double p)
        {
            // S07 §5.1 expected vectors (tolerance 0.001): F_V, F_R, F_C.
            Dictionary<double, double[]> vectors = new Dictionary<double, double[]>
            {
                [0.37] = new[] { 0.155130, 0.214870, 0.63 },
                [0.34] = new[] { 0.142552, 0.197448, 0.66 },
                [0.41] = new[] { 0.171901, 0.238099, 0.59 },
            };

            Dictionary<string, QuantityResult> rows = LightingResults(fixtureId == TestSupport.Box ? boxRun : twoRun, fixtureId, Guid.Parse(spaceId));
            Assert.That(rows["lighting.returnAir"].Outcome, Is.EqualTo(EquivalenceOutcome.Match), rows["lighting.returnAir"].Reason);
            Assert.That(rows["lighting.returnAir"].Input.Native.Kind, Is.EqualTo(ReaderKind.Zero), "blank reads as 0");
            foreach (string row in new[] { "lighting.radiantTotal", "lighting.visible", "lighting.radiant", "lighting.convective" })
            {
                Assert.That(rows[row].Outcome, Is.EqualTo(EquivalenceOutcome.AcceptedTransformation), row + ": " + rows[row].Reason);
                Assert.That(rows[row].Input.TransformationId, Is.EqualTo(OsComparison.LightSplit));
            }

            Assert.That(rows["lighting.radiantTotal"].Input.Expected.Value, Is.EqualTo(p));
            Assert.That(rows["lighting.visible"].Input.Expected.Value, Is.EqualTo(vectors[p][0]).Within(1e-6));
            Assert.That(rows["lighting.radiant"].Input.Expected.Value, Is.EqualTo(vectors[p][1]).Within(1e-6));
            Assert.That(rows["lighting.convective"].Input.Expected.Value, Is.EqualTo(vectors[p][2]).Within(1e-12));
            Assert.That(rows["lighting.visible"].Input.Native.Value, Is.EqualTo(vectors[p][0]).Within(0.001));
            Assert.That(rows["lighting.radiant"].Input.Native.Value, Is.EqualTo(vectors[p][1]).Within(0.001));
        }

        [Test]
        public void AnAuthoredZero_IsPreserved_AndReadsBackAllConvective()
        {
            AnalyticalModel model = TestSupport.Load(TestSupport.Box);
            TestSupport.ChangeCondition(model, TestSupport.BoxSpace, ic => ic.SetValue(InternalConditionParameter.LightingRadiantProportion, 0.0));
            OsReport report = TestSupport.Pipeline(TestSupport.Write(model, "box-p0-zero"), TestSupport.Box, "box-p-zero", out string run);
            Assert.That(Unexplained(report), Is.Empty, string.Join(" | ", Unexplained(report).Select(x => x.Key + " " + x.Outcome + " " + x.Reason)));
            Assert.That(report.ExitCode, Is.EqualTo(ReadBackExit.Pass), "every gated row passes; the unused-face optics are informational (S07 §5.2)");

            Dictionary<string, QuantityResult> rows = LightingResults(run, TestSupport.Box, TestSupport.BoxSpace);
            foreach (string row in new[] { "lighting.returnAir", "lighting.radiantTotal", "lighting.visible", "lighting.radiant" })
            {
                Assert.That(rows[row].Input.Expected.State, Is.EqualTo(SamInputState.Zero), row);
                Assert.That(rows[row].Input.Native.Kind, Is.EqualTo(ReaderKind.Zero), row);
                Assert.That(rows[row].Outcome, Is.EqualTo(EquivalenceOutcome.Match), row);
            }

            Assert.That(rows["lighting.convective"].Input.Expected.Value, Is.EqualTo(1.0));
            Assert.That(rows["lighting.convective"].Outcome, Is.EqualTo(EquivalenceOutcome.AcceptedTransformation));
        }

        [TestCase(0.49)]
        [TestCase(0.0)]
        public void TheComfortCoefficient_LeavesEveryLightingRowUnchanged(double coefficient)
        {
            AnalyticalModel model = TestSupport.Load(TestSupport.Box);
            TestSupport.ChangeCondition(model, TestSupport.BoxSpace, ic => ic.SetValue(InternalConditionParameter.LightingViewCoefficient, coefficient));
            string label = "box-c-" + coefficient.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            OsReport report = TestSupport.Pipeline(TestSupport.Write(model, label), TestSupport.Box, label, out string run);
            Assert.That(Unexplained(report), Is.Empty, string.Join(" | ", Unexplained(report).Select(x => x.Key + " " + x.Outcome + " " + x.Reason)));
            Assert.That(report.Required, Is.EqualTo(box.Required), "the view coefficient adds no compared row");
            Assert.That(report.CountByOutcome, Is.EqualTo(box.CountByOutcome));

            Dictionary<string, QuantityResult> baseline = LightingResults(boxRun, TestSupport.Box, TestSupport.BoxSpace);
            Dictionary<string, QuantityResult> rows = LightingResults(run, TestSupport.Box, TestSupport.BoxSpace);
            foreach (string row in LightingRows)
            {
                Assert.That(rows[row].Outcome, Is.EqualTo(baseline[row].Outcome), row);
                Assert.That(rows[row].Input.Expected.ToString(), Is.EqualTo(baseline[row].Input.Expected.ToString()), row);
                Assert.That(rows[row].Input.Native.Value, Is.EqualTo(baseline[row].Input.Native.Value), row);
            }
        }

        /// <summary>An unstated (absent or NaN) p keeps S-UNSPEC on every lighting row: the p₀ split is DOCUMENTED-DEFAULT, never PASS.</summary>
        [TestCase("absent")]
        [TestCase("nan")]
        public void AnUnstatedP_IsADocumentedDefault_NeverPass(string what)
        {
            AnalyticalModel model = TestSupport.Load(TestSupport.Box);
            TestSupport.ChangeCondition(model, TestSupport.BoxSpace, ic =>
            {
                if (what == "nan")
                {
                    ic.SetValue(InternalConditionParameter.LightingRadiantProportion, double.NaN);
                }
                else
                {
                    ic.RemoveValue(InternalConditionParameter.LightingRadiantProportion);
                }
            });

            string label = "box-p-" + what;
            OsReport report = TestSupport.Pipeline(TestSupport.Write(model, label), TestSupport.Box, label, out string run);
            Assert.That(report.ExitCode, Is.EqualTo(ReadBackExit.BlockedOwner), report.Status);
            Assert.That(report.OutOfEngineScope.Count, Is.EqualTo(box.OutOfEngineScope.Count), "the informational rows coexist and stay visible; they neither hide nor add a blocker");
            List<ReportItem> lighting = Unexplained(report);
            Assert.That(lighting.Select(x => x.Key).OrderBy(x => x), Is.EqualTo(LightingRows.Select(x => Ic(TestSupport.BoxSpace, x)).OrderBy(x => x)), string.Join(" | ", lighting.Select(x => x.Key + " " + x.Outcome)));
            Assert.That(lighting.All(x => x.Outcome == "DocumentedDefault" && x.Status == "BlockedOwnerDecision"), Is.True, string.Join(" | ", lighting.Select(x => x.Key + " " + x.Outcome + " " + x.Status)));

            Dictionary<string, QuantityResult> rows = LightingResults(run, TestSupport.Box, TestSupport.BoxSpace);
            Assert.That(rows.Values.All(x => x.Input.Expected.State == SamInputState.Unspecified), Is.True, "no expected value is computed from p0 as if SAM had stated it");
            Assert.That(rows["lighting.radiantTotal"].Input.ListedDefault, Is.EqualTo(0.576));
            Assert.That(rows["lighting.visible"].Input.Native.Value, Is.EqualTo(0.2415).Within(1e-9));
            Assert.That(rows["lighting.radiant"].Input.Native.Value, Is.EqualTo(0.3345).Within(1e-9));
        }

        [TestCase("neg-lighting-previous-mapping", TestSupport.Box, "lighting.visible")]
        [TestCase("neg-lighting-previous-mapping", TestSupport.Two, "lighting.visible")]
        [TestCase("neg-lighting-visible-zero", TestSupport.Box, "lighting.visible")]
        [TestCase("neg-lighting-visible-zero", TestSupport.Two, "lighting.visible")]
        [TestCase("neg-lighting-radiant-is-p", TestSupport.Box, "lighting.radiant")]
        [TestCase("neg-lighting-radiant-is-p", TestSupport.Two, "lighting.radiant")]
        [TestCase("neg-lighting-return-air", TestSupport.Box, "lighting.returnAir")]
        [TestCase("neg-lighting-return-air", TestSupport.Two, "lighting.returnAir")]
        [TestCase("neg-lighting-swapped", TestSupport.Box, "lighting.visible")]
        [TestCase("neg-lighting-swapped", TestSupport.Two, "lighting.visible")]
        public void AnIncorrectLightingPartition_Fails_OnTheLightingRows(string caseId, string fixture, string failingRow)
        {
            OsReport original = fixture == TestSupport.Box ? box : two;
            string run = TestSupport.NewDirectory(caseId + "-" + fixture);
            Assert.That(TestSupport.Cli(out string output, "negative", "--run", fixture == TestSupport.Box ? boxRun : twoRun, "--case", caseId, "--out", run), Is.EqualTo(0), output);
            OsReport report = TestSupport.ReadAndCompare(run, fixture);
            Assert.That(report.ExitCode, Is.EqualTo(ReadBackExit.Fail), caseId + ": " + report.Status);
            List<ReportItem> failed = report.NonPass.Where(x => x.Status == "Fail").ToList();
            Assert.That(failed, Is.Not.Empty);
            Assert.That(failed.All(x => x.Key.Contains(".ic.lighting.")), Is.True, string.Join(" | ", failed.Select(x => x.Key + " " + x.Reason)));
            Assert.That(failed.Any(x => x.Key.EndsWith(".ic." + failingRow, StringComparison.Ordinal)), Is.True, string.Join(" | ", failed.Select(x => x.Key)));
            Assert.That(report.Consistency, Is.Empty, "the OSM is not compared for the split; the lighting rows refuse it");
            Assert.That(report.Required, Is.EqualTo(original.Required));
        }

        /// <summary>A box export with an authored p (fresh-process read and compare).</summary>
        private static OsReport BoxWithP(double p, out string run)
        {
            AnalyticalModel model = TestSupport.Load(TestSupport.Box);
            TestSupport.ChangeCondition(model, TestSupport.BoxSpace, ic => ic.SetValue(InternalConditionParameter.LightingRadiantProportion, p));
            string label = "box-p-" + p.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            return TestSupport.Pipeline(TestSupport.Write(model, label), TestSupport.Box, label, out run);
        }

        /// <summary>A CLI negative (or control) case applied to a copy of <paramref name="source"/>, read and compared fresh.</summary>
        private static OsReport Mutated(string source, string caseId, out string run)
        {
            run = TestSupport.NewDirectory(caseId);
            Assert.That(TestSupport.Cli(out string output, "negative", "--run", source, "--case", caseId, "--out", run), Is.EqualTo(0), output);
            return TestSupport.ReadAndCompare(run, TestSupport.Box);
        }

        /// <summary>The boundaries p = 1 and p = 0 written by the writer are valid splits: every lighting row passes.</summary>
        [TestCase(1.0)]
        [TestCase(0.0)]
        public void TheBoundarySplits_PEqualOneAndZero_AreValid(double p)
        {
            OsReport report = BoxWithP(p, out string run);
            Assert.That(report.ExitCode, Is.EqualTo(ReadBackExit.Pass), report.Status);
            Assert.That(Unexplained(report), Is.Empty, string.Join(" | ", Unexplained(report).Select(x => x.Key + " " + x.Outcome + " " + x.Reason)));
            Assert.That(report.Unsupported, Is.Empty, string.Join(" | ", report.Unsupported));

            Dictionary<string, QuantityResult> rows = LightingResults(run, TestSupport.Box, TestSupport.BoxSpace);
            Assert.That(rows.Values.All(x => x.IsPass), Is.True, string.Join(" | ", rows.Select(x => x.Key + " " + x.Value.Outcome + " " + x.Value.Reason)));
            Assert.That(rows["lighting.radiantTotal"].Input.Native.Value, Is.EqualTo(p).Within(OsQuantities.LightingFractionBoundary), "the writer's split sums to p within the floating-point boundary");
        }

        /// <summary>
        /// Independent review of 31af6022: a native split shifted by 0.0004 per field is within the 0.001 tolerance of every
        /// row, and the reviewed head accepted it (exit 12). On p = 1 it sums to 1.0008; on p = 0 Fraction Radiant is
        /// -0.0004. The domain guard now fails every lighting row, named, before any tolerance is applied.
        /// </summary>
        [TestCase(1.0, "neg-lighting-sum-above-one", "exceeds 1")]
        [TestCase(0.0, "neg-lighting-radiant-to-visible", "Fraction Radiant -0.0004 is outside [0, 1]")]
        public void AnInvalidNativeSplit_FailsBeforeAnyTolerance(double p, string caseId, string fragment)
        {
            BoxWithP(p, out string source);
            OsReport report = Mutated(source, caseId, out string run);
            Assert.That(report.ExitCode, Is.EqualTo(ReadBackExit.Fail), report.Status);

            List<ReportItem> lighting = report.NonPass.Where(x => x.Key.Contains(".ic.lighting.")).ToList();
            Assert.That(lighting.Select(x => x.Key).OrderBy(x => x), Is.EqualTo(LightingRows.Select(x => Ic(TestSupport.BoxSpace, x)).OrderBy(x => x)));
            Assert.That(lighting.All(x => x.Outcome == "ExportDefect" && x.Status == "Fail" && x.Reason.Contains(fragment)), Is.True, string.Join(" | ", lighting.Select(x => x.Outcome + " " + x.Reason)));
            Assert.That(report.NonPass.Where(x => x.Status == "Fail").All(x => x.Key.Contains(".ic.lighting.")), Is.True, "only the lighting rows fail");

            Dictionary<string, QuantityResult> rows = LightingResults(run, TestSupport.Box, TestSupport.BoxSpace);
            Assert.That(rows.Values.Any(x => x.IsPass), Is.False, "no lighting row is accepted");
            Assert.That(rows.Values.All(x => x.Comparison == null), Is.True, "no tolerance was applied");
        }

        /// <summary>
        /// Independent review of 31af6022: with Return Air Fraction Calculated from Plenum Temperature = Yes the reviewed
        /// head still accepted the static split (exit 12). Yes, an uninterpretable value and an unreadable field now block
        /// technically (exit 11), named, with no lighting row accepted.
        /// </summary>
        [TestCase("neg-lighting-plenum-yes", "= Yes: the return-air share follows the plenum temperature at run time")]
        [TestCase("neg-lighting-plenum-unknown", "= 'Maybe' is neither Yes nor No (uninterpretable)")]
        [TestCase("neg-lighting-plenum-relabelled", "is unreadable")]
        public void APlenumCalculatedOrUninterpretableReturnAir_BlocksTechnically(string caseId, string fragment)
        {
            OsReport report = Mutated(boxRun, caseId, out string run);
            Assert.That(report.ExitCode, Is.EqualTo(ReadBackExit.BlockedTechnical), report.Status);
            Assert.That(report.Consistency, Is.Empty, string.Join(" | ", report.Consistency));
            Assert.That(report.Unsupported.Single(), Does.StartWith("lighting: " + "space:" + TestSupport.BoxSpace.ToString("D") + ": Lights '").And.Contain(fragment));
            Assert.That(report.Required, Is.EqualTo(box.Required));

            List<ReportItem> lighting = report.NonPass.Where(x => x.Key.Contains(".ic.lighting.")).ToList();
            Assert.That(lighting.Select(x => x.Key).OrderBy(x => x), Is.EqualTo(LightingRows.Select(x => Ic(TestSupport.BoxSpace, x)).OrderBy(x => x)));
            Assert.That(lighting.All(x => x.Outcome == "UnverifiedUnreadable" && x.Status == "BlockedTechnical" && x.Reason.Contains(fragment)), Is.True, string.Join(" | ", lighting.Select(x => x.Outcome + " " + x.Reason)));
            Assert.That(LightingResults(run, TestSupport.Box, TestSupport.BoxSpace).Values.Any(x => x.IsPass), Is.False, "no lighting row is accepted");
        }

        /// <summary>The valid control: an explicit No (with coefficients, which EnergyPlus ignores under No) changes nothing.</summary>
        [Test]
        public void AnExplicitNoPlenumSetting_IsTheUnchangedControl()
        {
            OsReport report = Mutated(boxRun, "ctl-lighting-plenum-no", out string run);
            Assert.That(report.Inputs["mutation"], Does.StartWith("ctl-lighting-plenum-no"));
            Assert.That(report.ExitCode, Is.EqualTo(box.ExitCode));
            Assert.That(report.Unsupported, Is.Empty, string.Join(" | ", report.Unsupported));
            Assert.That(report.CountByOutcome, Is.EqualTo(box.CountByOutcome));
            Assert.That(Unexplained(report), Is.Empty);

            Dictionary<string, QuantityResult> baseline = LightingResults(boxRun, TestSupport.Box, TestSupport.BoxSpace);
            Dictionary<string, QuantityResult> rows = LightingResults(run, TestSupport.Box, TestSupport.BoxSpace);
            foreach (string row in LightingRows)
            {
                Assert.That(rows[row].Outcome, Is.EqualTo(baseline[row].Outcome), row);
            }
        }

        /// <summary>
        /// S07 §5.2 (amendment U1): with the unused-face optics OUT-OF-ENGINE-SCOPE, every gated row of both fixtures is in
        /// the PASS allow-list, so both PASS (exit 0) with complete coverage. This is scoped native-input conformance, not
        /// simulation equivalence and not Phase B.
        /// </summary>
        [TestCase(TestSupport.Box, 319)]
        [TestCase(TestSupport.Two, 592)]
        public void BothFixtures_HaveCompleteCoverage_AndPassWithTheUnusedFaceRowsInformational(string fixtureId, int required)
        {
            OsReport report = fixtureId == TestSupport.Box ? box : two;
            Assert.That(report.Schema, Is.EqualTo("s09-readback-report/3"));
            Assert.That(report.Coverage, Is.EqualTo("Complete"), report.FixtureId);
            Assert.That(report.Required, Is.EqualTo(required));
            Assert.That(report.Results, Is.EqualTo(required));
            Assert.That(report.MissingComparisons, Is.Empty, report.FixtureId);
            Assert.That(report.Consistency, Is.Empty, report.FixtureId + ": " + string.Join(" | ", report.Consistency));
            Assert.That(report.FixtureCriteria, Is.Empty, report.FixtureId);
            Assert.That(report.Unsupported, Is.Empty, report.FixtureId);
            Assert.That(Unexplained(report), Is.Empty, report.FixtureId + ": " + string.Join(" | ", Unexplained(report).Select(x => x.Key + " " + x.Outcome + " " + x.Reason)));
            Assert.That(report.GatedNonPass, Is.EqualTo(0));
            Assert.That(report.Status, Is.EqualTo(GateStatus.Pass.ToString()), report.FixtureId);
            Assert.That(report.GateStatus, Is.EqualTo(GateStatus.Pass.ToString()));
            Assert.That(report.ExitCode, Is.EqualTo(ReadBackExit.Pass));
            Assert.That(report.CountByOutcome.Keys, Has.No.Member("MappingDifference"), "the S09-only OS-MAT-SINGLE-SIDED listing is replaced by S07 §5.2");
            Assert.That(report.CountByOutcome.Values.Sum(), Is.EqualTo(report.Results), "every result is counted once");
            Assert.That(report.CountByOutcome[OsComparison.OutOfEngineScopeOutcome], Is.EqualTo(report.OutOfEngineScope.Count));
            Assert.That(report.Supporting.Single(x => x.StartsWith(OsComparison.OpaqueFaceScope + ":", StringComparison.Ordinal)), Does.Contain("OUT-OF-ENGINE-SCOPE (informational, not gated)").And.Contain("Every used face"));
            Assert.That(report.Series.All(x => x.Count == 8760), Is.True, "every hourly series covers the year");
        }

        // ---- opaque face optics (revision 8) -------------------------------------------------------------------------

        private static readonly string[] Optics = { "SolarReflectance", "LightReflectance", "Emissivity" };

        /// <summary>
        /// Each panel side's zone-face rows, derived here from the frozen fixture alone: SAM lists a construction inside →
        /// outside from its first space in GetSpaces() order, so a side's innermost layer is SAM's first layer on the
        /// first space's side (its internal face) and SAM's last layer on the other side (its external face).
        /// </summary>
        private static List<string> ZoneFaceKeys(string fixtureId)
        {
            AnalyticalModel model = TestSupport.Load(fixtureId);
            AdjacencyCluster cluster = model.AdjacencyCluster;
            List<Space> spaces = cluster.GetSpaces();
            List<string> result = new List<string>();
            foreach (Panel panel in cluster.GetPanels())
            {
                List<Space> related = cluster.GetRelatedObjects<Space>(panel).OrderBy(x => spaces.FindIndex(s => s.Guid == x.Guid)).ToList();
                int count = panel.Construction.ConstructionLayers.Count;
                for (int i = 0; i < related.Count; i++)
                {
                    string face = i == 0 ? "internal" : "external";
                    result.AddRange(Optics.Select(x => "panel:" + panel.Guid.ToString("D") + "@space:" + related[i].Guid.ToString("D") + "#layer" + count + "." + face + x));
                }
            }

            return result;
        }

        /// <summary>Every 3a result of a run, recomputed in this process from the run's frozen model and the fresh-process snapshot.</summary>
        private static Dictionary<string, QuantityResult> LayerResults(string run, string fixtureId)
        {
            AnalyticalModel model = Core.Convert.ToSAM<AnalyticalModel>(Path.Combine(run, "frozen-model.json")).Single();
            NativeSnapshot snapshot = NativeSnapshot.Load(Path.Combine(run, "native.json"));
            OsComparison comparison = OsComparison.Run(model, snapshot, Path.Combine(run, "weather.epw"), fixtureId, GateRuleSet.Approved);
            return comparison.Results.Where(x => x.Definition?.Id == "3a").ToDictionary(x => x.Input.ObjectKey, x => x);
        }

        /// <summary>
        /// Revision 8: every face whose optics EnergyPlus uses is now the authored one. Box: 6 zone faces; two-zone: 12 (zone
        /// A sees SAM's internal faces, zone B the partition's external Timber face). The rows the writer could not write
        /// before (18 / 33) all pass, and every remaining non-PASS row is on a face EnergyPlus does not use.
        /// </summary>
        [TestCase(TestSupport.Box, 18)]
        [TestCase(TestSupport.Two, 36)]
        public void EveryUsedZoneFace_HoldsTheAuthoredFace(string fixtureId, int rows)
        {
            List<string> keys = ZoneFaceKeys(fixtureId);
            Assert.That(keys.Count, Is.EqualTo(rows));
            Dictionary<string, QuantityResult> results = LayerResults(fixtureId == TestSupport.Box ? boxRun : twoRun, fixtureId);
            foreach (string key in keys)
            {
                Assert.That(results.ContainsKey(key), Is.True, key);
                Assert.That(results[key].IsPass, Is.True, key + ": " + results[key].Outcome + " " + results[key].Reason);
                Assert.That(results[key].Input.Listing, Is.EqualTo(ContractListing.None), key + ": a used face is never a listed difference");
                Assert.That(results[key].GatesFixture, Is.True, key + ": a used face is never informational");
            }

            OsReport report = fixtureId == TestSupport.Box ? box : two;
            Assert.That(report.NonPass.Select(x => x.Key).Intersect(keys), Is.Empty);
        }

        /// <summary>
        /// Review of 901e592a: Plasterboard's internal light reflectance unstated, absent or NaN. The writer writes the
        /// external value on the zone face and reports it (SAM-OS-MAT-004); S09 keeps S-UNSPEC on exactly those zone-face
        /// rows, never a match. S07 lists no default for an internal face, so a native value where SAM states none is an
        /// EXPORT-DEFECT (FAIL) on exactly those rows: the inheritance is declared by the writer, not accepted by S07.
        /// </summary>
        [TestCase("absent")]
        [TestCase("nan")]
        public void AnUnstatedZoneFaceValue_KeepsItsUnspecifiedProvenance(string what)
        {
            AnalyticalModel model = TestSupport.Load(TestSupport.Box);
            Core.MaterialLibrary materialLibrary = model.MaterialLibrary;
            Core.OpaqueMaterial plasterboard = materialLibrary.GetMaterials().OfType<Core.OpaqueMaterial>().Single(x => x.Name == "FIX_Plasterboard");
            if (what == "nan")
            {
                plasterboard.SetValue(OpaqueMaterialParameter.InternalLightReflectance, double.NaN);
            }
            else
            {
                plasterboard.RemoveValue(OpaqueMaterialParameter.InternalLightReflectance);
            }

            materialLibrary.Add(plasterboard);
            model = new AnalyticalModel(model, model.AdjacencyCluster, materialLibrary, model.ProfileLibrary);
            string label = "box-unstated-light-" + what;
            OsReport report = TestSupport.Pipeline(TestSupport.Write(model, label), TestSupport.Box, label, out string run);

            Assert.That(report.Inputs["writerDiagnostics"], Does.Contain("Information SAM-OS-MAT-004 Zone face: InternalLightReflectance not stated"));
            Dictionary<string, QuantityResult> results = LayerResults(run, TestSupport.Box);
            List<QuantityResult> zoneLight = ZoneFaceKeys(TestSupport.Box).Where(x => x.EndsWith(".internalLightReflectance", StringComparison.Ordinal)).Select(x => results[x]).ToList();
            List<QuantityResult> unstated = zoneLight.Where(x => x.Input.Expected.State == SamInputState.Unspecified).ToList();
            Assert.That(unstated.Count, Is.EqualTo(5), "the four walls and the roof have Plasterboard on the zone face; the floor has Screed");
            Assert.That(unstated.All(x => !x.IsPass && x.Outcome == EquivalenceOutcome.ExportDefect), Is.True, string.Join(" | ", unstated.Select(x => x.Input.ObjectKey + " " + x.Outcome)));
            Assert.That(zoneLight.Except(unstated).All(x => x.IsPass), Is.True, "the Screed zone face is unaffected");

            Assert.That(report.ExitCode, Is.EqualTo(ReadBackExit.Fail), report.Status);
            List<ReportItem> failed = report.NonPass.Where(x => x.Status == GateStatus.Fail.ToString()).ToList();
            Assert.That(failed.Select(x => x.Key).OrderBy(x => x), Is.EqualTo(unstated.Select(x => x.Input.ObjectKey).OrderBy(x => x)), "exactly the unstated zone-face rows fail");
            Assert.That(failed.All(x => x.Reason.Contains("S-UNSPEC")), Is.True);
        }

        /// <summary>A copy of a fixture run whose IDF is changed by <paramref name="mutate"/>, read and compared fresh.</summary>
        private static OsReport MutatedIdf(string source, string fixtureId, string label, Func<string, AnalyticalModel, string> mutate)
        {
            string run = TestSupport.NewDirectory(label);
            Directory.CreateDirectory(run);
            foreach (string file in new[] { "frozen-model.json", "weather.epw", "model.osm", "in.idf", "export.json" })
            {
                File.Copy(Path.Combine(source, file), Path.Combine(run, file));
            }

            string idfPath = Path.Combine(run, "in.idf");
            string idf = File.ReadAllText(idfPath);
            string mutated = mutate(idf, TestSupport.Load(fixtureId));
            Assert.That(mutated, Is.Not.EqualTo(idf), "the mutation applied");
            File.WriteAllText(idfPath, mutated);
            ExportManifest manifest = ExportManifest.Load(Path.Combine(run, "export.json"));
            manifest.Mutation = "test: " + label;
            manifest.IdfSha256 = Hashing.Sha256(idfPath);
            manifest.Save(Path.Combine(run, "export.json"));
            return TestSupport.ReadAndCompare(run, fixtureId);
        }

        /// <summary>
        /// Swapped-face regression: each zone-face variant given its material's external-face optics, which is what the
        /// writer wrote before revision 8 (and what a selector choosing the wrong face would write). The external values
        /// come from the frozen model, never from the writer. Exactly the zone-face rows SAM needs internal FAIL.
        /// </summary>
        [TestCase(TestSupport.Box, 18)]
        [TestCase(TestSupport.Two, 33)]
        public void ASwappedZoneFace_Fails_OnExactlyTheZoneFaceRows(string fixtureId, int failingRows)
        {
            OsReport report = MutatedIdf(fixtureId == TestSupport.Box ? boxRun : twoRun, fixtureId, "swapped-zone-face-" + fixtureId, (idf, model) =>
            {
                foreach (IdfObject variant in IdfText.Parse(idf).Where(x => x.Type == "Material" && x.Name.Contains("_Internal")))
                {
                    Core.OpaqueMaterial material = model.MaterialLibrary.GetMaterials().OfType<Core.OpaqueMaterial>().Single(x => variant.Name.Contains("_" + x.Name + "_"));
                    material.TryGetValue(OpaqueMaterialParameter.ExternalEmissivity, out double emissivity);
                    material.TryGetValue(OpaqueMaterialParameter.ExternalSolarReflectance, out double solar);
                    material.TryGetValue(OpaqueMaterialParameter.ExternalLightReflectance, out double light);
                    idf = Cli.Negative.SetField(idf, "Material", variant.Name, "Thermal Absorptance", emissivity.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    idf = Cli.Negative.SetField(idf, "Material", variant.Name, "Solar Absorptance", (1 - solar).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    idf = Cli.Negative.SetField(idf, "Material", variant.Name, "Visible Absorptance", (1 - light).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                }

                return idf;
            });

            Assert.That(report.ExitCode, Is.EqualTo(ReadBackExit.Fail), report.Status);
            List<ReportItem> failed = report.NonPass.Where(x => x.Status == GateStatus.Fail.ToString()).ToList();
            List<string> zoneFaces = ZoneFaceKeys(fixtureId);
            Assert.That(failed.Count, Is.EqualTo(failingRows), string.Join(" | ", failed.Select(x => x.Key)));
            Assert.That(failed.All(x => x.Quantity == "3a" && zoneFaces.Contains(x.Key) && x.Key.Contains(".internal")), Is.True, string.Join(" | ", failed.Select(x => x.Key + " " + x.Reason)));
            Assert.That(report.Consistency, Is.Empty);
        }

        /// <summary>A wrong opaque optical value that is neither authored face is never explained as the single-sided difference.</summary>
        [Test]
        public void AWrongOpaqueOpticalValue_Fails()
        {
            OsReport report = MutatedIdf(boxRun, TestSupport.Box, "box-plasterboard-absorptance", (idf, model) =>
            {
                IdfObject plasterboard = IdfText.Parse(idf).First(x => x.Type == "Material" && x.Name.Contains("Plasterboard"));
                return Cli.Negative.SetField(idf, "Material", plasterboard.Name, "Solar Absorptance", "0.5");
            });

            Assert.That(report.ExitCode, Is.EqualTo(ReadBackExit.Fail), report.Status);
            List<ReportItem> failed = report.NonPass.Where(x => x.Status == GateStatus.Fail.ToString()).ToList();
            Assert.That(failed, Is.Not.Empty);
            Assert.That(failed.All(x => x.Quantity == "3a" && x.Key.Contains("SolarReflectance")), Is.True, string.Join(" | ", failed.Select(x => x.Key + " " + x.Reason)));
        }

        /// <summary>
        /// The box with its ground floor built as one Screed layer (faces differ): only the zone face is used, so the writer
        /// uses the internal face and S09 compares it exactly; its outer face is the ground surface outer face (S07 §5.2).
        /// </summary>
        [Test]
        public void ASingleLayerGroundFloor_UsesItsZoneFace()
        {
            OsReport report = BoxWithSingleLayer(PanelType.SlabOnGrade, "FIX_Screed", 0.065, "box-single-floor", out string run);
            Assert.That(report.Inputs["writerDiagnostics"], Does.Not.Contain("SAM-OS-MAT-003"));
            Assert.That(report.Unsupported, Is.Empty, string.Join(" | ", report.Unsupported));
            Assert.That(report.NonPass, Is.Empty, string.Join(" | ", report.NonPass.Select(x => x.Key + " " + x.Reason)));
            Assert.That(report.ExitCode, Is.EqualTo(ReadBackExit.Pass), report.Status);
            List<OutOfEngineScopeRow> floor = report.OutOfEngineScope.Rows.Where(x => x.LayerCount == 1).ToList();
            Assert.That(floor.Select(x => x.Property).OrderBy(x => x), Is.EqualTo(new[] { "externalEmissivity", "externalLightReflectance", "externalSolarReflectance" }), "the single layer's outer face only");
            Assert.That(floor.All(x => x.ScopeReason == "ground surface outer face" && x.SamMaterial == "FIX_Screed" && x.Differs), Is.True, string.Join(" | ", floor.Select(x => x.Key + " " + x.ScopeReason)));
        }

        /// <summary>The box with its roof built as one Plasterboard layer (faces differ): the writer refuses it (SAM-OS-MAT-003) and S09 names it as unsupported; never PASS.</summary>
        [Test]
        public void ASingleLayerOutdoorRoof_IsRefused_AndNamedUnsupported()
        {
            OsReport report = BoxWithSingleLayer(PanelType.Roof, "FIX_Plasterboard", 0.015, "box-single-roof", out string run);
            Assert.That(report.Inputs["writerDiagnostics"], Does.Contain("Error SAM-OS-MAT-003"));
            Assert.That(report.Unsupported.Count(x => x.StartsWith("optics: ", StringComparison.Ordinal) && x.Contains("single-layer construction")), Is.EqualTo(1), string.Join(" | ", report.Unsupported));
            Assert.That(report.ExitCode, Is.AnyOf(ReadBackExit.Fail, ReadBackExit.BlockedTechnical), report.Status);
        }

        private static OsReport BoxWithSingleLayer(PanelType panelType, string materialName, double thickness, string label, out string run)
        {
            AnalyticalModel model = TestSupport.Load(TestSupport.Box);
            Construction construction = new Construction(Guid.NewGuid(), "S09_SINGLE_" + materialName, new List<ConstructionLayer> { new ConstructionLayer(materialName, thickness) });
            AdjacencyCluster cluster = model.AdjacencyCluster;
            Panel panel = cluster.GetPanels().Single(x => x.PanelType == panelType);
            Assert.That(cluster.AddObject(Analytical.Create.Panel(panel, construction)), Is.True);
            model = new AnalyticalModel(model, cluster);
            Assert.That(model.AdjacencyCluster.GetPanels().Single(x => x.Guid == panel.Guid).Construction.Name, Is.EqualTo(construction.Name), "the panel carries the single-layer construction");
            return TestSupport.Pipeline(TestSupport.Write(model, label), TestSupport.Box, label, out run);
        }

        [Test]
        public void V2Box_DeliversTheRequirementTimesTheProfile_EveryHour()
        {
            Assert.That(box.VentilationStates.Values.Single(), Is.EqualTo("V2"));
            SeriesSummary delivered = box.Series.Single(x => x.Key == Ic(TestSupport.BoxSpace, "ventilation.delivered"));
            Assert.That(delivered.NotPass, Is.EqualTo(0), delivered.FirstNotPass);
            Assert.That(delivered.Outcome, Is.AnyOf("Match", "WithinTolerance"));
            Assert.That(box.NonPass.Any(x => x.Quantity == "8a" || x.Quantity == "8b" || x.Key.Contains("schedule.ventilation")), Is.False);
        }

        [Test]
        public void V1TwoZones_KeepTheirRequirement_AndDeliverNothing_UnderTheVerifiedInactiveTransformation()
        {
            foreach (Guid space in new[] { TestSupport.SpaceA, TestSupport.SpaceB })
            {
                Assert.That(two.VentilationStates["space:" + space.ToString("D")], Is.EqualTo("V1"));
                Assert.That(two.Supporting.Single(x => x.StartsWith("space:" + space.ToString("D") + ": " + OsComparison.InactiveVentilation, StringComparison.Ordinal)), Does.Contain(" applied"));
                Assert.That(two.Series.Single(x => x.Key == Ic(space, "ventilation.delivered")).Outcome, Is.EqualTo("AcceptedTransformation"));
            }

            Assert.That(two.CountByOutcome["AcceptedTransformation"], Is.EqualTo(14), "8b, 6.hourly and 6.eflh of the two V1 zones (6), and the four T-LIGHT-SPLIT rows of each zone (8)");
            Assert.That(two.NonPass.Any(x => x.Quantity == "8a"), Is.False, "the requirement is read back and matches");
        }

        [Test]
        public void V2ChangedBackToV1_ReadsBackAsNoDelivery()
        {
            AnalyticalModel model = TestSupport.Load(TestSupport.Box);
            TestSupport.ChangeCondition(model, TestSupport.BoxSpace, ic => ic.RemoveValue(InternalConditionParameter.VentilationProfileName));
            OsReport report = TestSupport.Pipeline(TestSupport.Write(model, "box-v1"), "box-v1", "box-v1", out string _);
            Assert.That(report.VentilationStates.Values.Single(), Is.EqualTo("V1"));
            Assert.That(report.Series.Single(x => x.Key == Ic(TestSupport.BoxSpace, "ventilation.delivered")).Outcome, Is.EqualTo("AcceptedTransformation"));
            Assert.That(Unexplained(report), Is.Empty, string.Join(" | ", Unexplained(report).Select(x => x.Key + " " + x.Reason)));
        }

        [TestCase("missing")]
        [TestCase("wrong-type")]
        public void ASelectionThatDoesNotResolve_IsAnInvalidInput_NeverInactive(string what)
        {
            AnalyticalModel model = TestSupport.Load(TestSupport.Box);
            TestSupport.ChangeCondition(model, TestSupport.BoxSpace, ic => ic.SetProfileName(ProfileType.Ventilation, what == "missing" ? "s09-missing" : ic.GetProfileName(ProfileType.Occupancy)));
            OsReport report = TestSupport.Pipeline(TestSupport.Write(model, "box-" + what), "box-" + what, "box-" + what, out string _);
            Assert.That(report.ExitCode, Is.EqualTo(ReadBackExit.BlockedTechnical), what + ": " + report.Status);
            ReportItem delivered = report.NonPass.Single(x => x.Key == Ic(TestSupport.BoxSpace, "ventilation.delivered"));
            Assert.That(delivered.Outcome, Is.EqualTo("InputInvalid"), delivered.Reason);
            Assert.That(report.Supporting.Any(x => x.Contains(OsComparison.InactiveVentilation + " applied")), Is.False, "a selection is never exempted as inactive");
            Assert.That(report.VentilationStates.Values.Single(), Does.EndWith("INVALID selection"));
        }

        [Test]
        public void TheSpaceOverride_IsTheRequirement_AndIsDeliveredByTheSelectedProfile()
        {
            AnalyticalModel model = TestSupport.Load(TestSupport.Box);
            TestSupport.ChangeSpace(model, TestSupport.BoxSpace, space => space.SetValue(SpaceParameter.OutsideSupplyAirFlow, 0.05));
            OsReport report = TestSupport.Pipeline(TestSupport.Write(model, "box-override"), "box-override", "box-override", out string _);
            Assert.That(report.VentilationStates.Values.Single(), Does.StartWith("V2 (per-space OutsideSupplyAirFlow 0.05"));
            Assert.That(report.Supporting.Any(x => x.Contains("set on the space (per-space override)")), Is.True);
            Assert.That(report.Series.Single(x => x.Key == Ic(TestSupport.BoxSpace, "ventilation.delivered")).NotPass, Is.EqualTo(0));
            Assert.That(Unexplained(report), Is.Empty, string.Join(" | ", Unexplained(report).Select(x => x.Key + " " + x.Reason)));
        }

        [TestCase("neg-identity-swap-names", TestSupport.Two)]
        [TestCase("neg-schedule-swap", TestSupport.Box)]
        [TestCase("neg-v1-unscheduled", TestSupport.Two)]
        [TestCase("neg-missing-dsoa-link", TestSupport.Box)]
        [TestCase("neg-missing-schedule-object", TestSupport.Box)]
        [TestCase("neg-people-density", TestSupport.Box)]
        [TestCase("neg-layer-conductivity", TestSupport.Two)]
        [TestCase("neg-osm-dsoa-schedule", TestSupport.Box)]
        [TestCase("neg-zone-multiplier", TestSupport.Box)]
        [TestCase("neg-window-multiplier", TestSupport.Box)]
        [TestCase("neg-north-axis", TestSupport.Box)]
        [TestCase("neg-zone-multiplier-both", TestSupport.Box)]
        [TestCase("neg-window-multiplier-both", TestSupport.Box)]
        [TestCase("neg-north-axis-both", TestSupport.Box)]
        [TestCase("neg-zone-multiplier", TestSupport.Two)]
        [TestCase("neg-window-multiplier", TestSupport.Two)]
        [TestCase("neg-north-axis", TestSupport.Two)]
        public void EveryTargetedMutation_Fails(string caseId, string fixture)
        {
            string source = fixture == TestSupport.Box ? boxRun : twoRun;
            string run = TestSupport.NewDirectory(caseId);
            Assert.That(TestSupport.Cli(out string output, "negative", "--run", source, "--case", caseId, "--out", run), Is.EqualTo(0), output);
            OsReport report = TestSupport.ReadAndCompare(run, fixture);
            Assert.That(report.ExitCode, Is.EqualTo(ReadBackExit.Fail), caseId + ": " + report.Status + " " + string.Join(" | ", report.NonPass.Where(x => x.Status != "BlockedOwnerDecision").Take(3).Select(x => x.Key + " " + x.Reason)));
            Assert.That(report.Inputs["mutation"], Does.StartWith(caseId));
        }

        /// <summary>
        /// The reviewed head (d6d1cc5b) left these reports unchanged (BLOCKED owner, exit 12). With the OSM made to agree,
        /// no OSM/IDF disagreement is left to catch them: the refusal must come from the check against SAM intent.
        /// </summary>
        [TestCase("neg-zone-multiplier-both", "multiplicity: IDF Zone '", "Multiplier 2")]
        [TestCase("neg-window-multiplier-both", "multiplicity: IDF FenestrationSurface:Detailed '", "Multiplier 2")]
        [TestCase("neg-north-axis-both", "orientation: Zone '", "is rotated by 90 deg")]
        public void MultiplicityAndOrientation_AreRefusedAgainstSamIntent(string caseId, string prefix, string fragment)
        {
            string run = TestSupport.NewDirectory(caseId);
            Assert.That(TestSupport.Cli(out string output, "negative", "--run", boxRun, "--case", caseId, "--out", run), Is.EqualTo(0), output);
            OsReport report = TestSupport.ReadAndCompare(run, TestSupport.Box);
            Assert.That(report.ExitCode, Is.EqualTo(ReadBackExit.Fail), report.Status);
            Assert.That(report.Consistency, Is.Not.Empty);
            Assert.That(report.Consistency.All(x => x.StartsWith(prefix, StringComparison.Ordinal) && x.Contains(fragment) && x.Contains("SAM states")), Is.True, string.Join(" | ", report.Consistency));
            Assert.That(report.Required, Is.EqualTo(box.Required), "coverage is unchanged; the verdict changes");
        }

        /// <summary>
        /// The reviewed head (6f7e6b7d) skipped the OSM/IDF multiplier check when the IDF object had no OSM object of its
        /// name, and kept BLOCKED owner, exit 12, with complete coverage. A proven missing counterpart is now an OSM/IDF
        /// disagreement (FAIL), on both fixtures; coverage is unchanged and nothing else is reported.
        /// </summary>
        [TestCase("neg-osm-missing-thermal-zone", TestSupport.Box, "multiplicity: IDF Zone '", "has no OS:ThermalZone in the OSM")]
        [TestCase("neg-osm-missing-thermal-zone", TestSupport.Two, "multiplicity: IDF Zone '", "has no OS:ThermalZone in the OSM")]
        [TestCase("neg-osm-missing-subsurfaces", TestSupport.Box, "multiplicity: IDF FenestrationSurface:Detailed '", "has no OS:SubSurface in the OSM")]
        [TestCase("neg-osm-missing-subsurfaces", TestSupport.Two, "multiplicity: IDF FenestrationSurface:Detailed '", "has no OS:SubSurface in the OSM")]
        public void AMissingOsmCounterpart_FailsAsAnOsmIdfDisagreement(string caseId, string fixture, string prefix, string fragment)
        {
            OsReport original = fixture == TestSupport.Box ? box : two;
            string run = TestSupport.NewDirectory(caseId);
            Assert.That(TestSupport.Cli(out string output, "negative", "--run", fixture == TestSupport.Box ? boxRun : twoRun, "--case", caseId, "--out", run), Is.EqualTo(0), output);
            OsReport report = TestSupport.ReadAndCompare(run, fixture);
            Assert.That(report.ExitCode, Is.EqualTo(ReadBackExit.Fail), report.Status + " " + string.Join(" | ", report.Consistency.Concat(report.Unsupported)));
            Assert.That(report.Consistency, Is.Not.Empty);
            Assert.That(report.Consistency.All(x => x.StartsWith(prefix, StringComparison.Ordinal) && x.Contains(fragment)), Is.True, string.Join(" | ", report.Consistency));
            Assert.That(report.Unsupported, Is.Empty, string.Join(" | ", report.Unsupported));
            Assert.That(report.Coverage, Is.EqualTo("Complete"));
            Assert.That(report.Required, Is.EqualTo(original.Required), "coverage is unchanged; the verdict changes");
            Assert.That(report.Supporting.Single(x => x.StartsWith("multiplicity:", StringComparison.Ordinal)), Does.Not.Contain("as SAM states"));
        }

        /// <summary>Both fixtures: every IDF zone and subsurface has exactly one OSM object of its name, whose blank Multiplier is the documented default 1.</summary>
        [Test]
        public void BothFixtures_MatchEachIdfZoneAndSubsurfaceToOneOsmObject()
        {
            Assert.That(box.Supporting.Single(x => x.StartsWith("multiplicity:", StringComparison.Ordinal)), Does.StartWith("multiplicity: 1 IDF zone(s) (1 with their OS:ThermalZone), 0 ZoneGroup(s), 2 IDF subsurface(s) (2 with their OS:SubSurface) read"));
            Assert.That(two.Supporting.Single(x => x.StartsWith("multiplicity:", StringComparison.Ordinal)), Does.StartWith("multiplicity: 2 IDF zone(s) (2 with their OS:ThermalZone), 0 ZoneGroup(s), 1 IDF subsurface(s) (1 with their OS:SubSurface) read"));
        }

        /// <summary>
        /// Multiplicity or orientation the reader cannot interpret blocks the fixture (BLOCKED technical, exit 11), never
        /// PASS and never assumed. This includes an OSM counterpart whose identity is ambiguous or unreadable, and an OSM
        /// Multiplier field the file labels otherwise (an unknown layout, never read as the default).
        /// </summary>
        [TestCase("zone-multiplier-1.5")]
        [TestCase("north-axis-text")]
        [TestCase("coordinate-system-unknown")]
        [TestCase("world-with-north-axis")]
        [TestCase("osm-thermal-zone-duplicated")]
        [TestCase("osm-thermal-zone-name-unlabelled")]
        [TestCase("osm-subsurface-multiplier-relabelled")]
        public void UninterpretableMultiplicityOrOrientation_BlocksTechnical(string what)
        {
            string run = TestSupport.NewDirectory("unsupported-" + what);
            Directory.CreateDirectory(run);
            foreach (string file in new[] { "frozen-model.json", "weather.epw", "model.osm", "in.idf", "export.json" })
            {
                File.Copy(Path.Combine(boxRun, file), Path.Combine(run, file));
            }

            string idfPath = Path.Combine(run, "in.idf"), osmPath = Path.Combine(run, "model.osm");
            string idf = File.ReadAllText(idfPath), osm = File.ReadAllText(osmPath);
            List<IdfObject> objects = IdfText.Parse(idf);
            string zone = objects.First(x => x.Type == "Zone").Name;
            string building = objects.First(x => x.Type == "Building").Name;
            const string coordinateSystem = @"Relative(,\s*!- Coordinate System)";
            switch (what)
            {
                case "zone-multiplier-1.5":
                    idf = Cli.Negative.SetField(idf, "Zone", zone, "Multiplier", "1.5");
                    break;
                case "north-axis-text":
                    idf = Cli.Negative.SetField(idf, "Building", building, "North Axis", "east");
                    break;
                case "coordinate-system-unknown":
                    idf = Regex.Replace(idf, coordinateSystem, "Sideways$1");
                    break;
                case "world-with-north-axis":
                    idf = Regex.Replace(idf, coordinateSystem, "World$1");
                    idf = Cli.Negative.SetField(idf, "Building", building, "North Axis", "90");
                    osm = Cli.Negative.SetField(osm, "OS:Building", IdfText.Parse(osm).First(x => x.Type == "OS:Building").Value(1), "North Axis", "90", true);
                    break;
                case "osm-thermal-zone-duplicated":
                {
                    // A second OS:ThermalZone of the same name (a new handle): the counterpart is ambiguous.
                    int start = osm.IndexOf("OS:ThermalZone,", StringComparison.Ordinal);
                    int end = osm.IndexOf('\n', osm.IndexOf(';', start)) + 1;
                    string copy = new Regex(@"\{[0-9a-f-]{36}\}").Replace(osm.Substring(start, end - start), "{00000000-0000-0000-0000-0000000000a1}", 1);
                    osm = osm.Insert(start, copy + (osm.Contains("\r\n") ? "\r\n" : "\n"));
                    break;
                }
                case "osm-thermal-zone-name-unlabelled":
                    osm = new Regex(@"(OS:ThermalZone,\r?\n[^\r\n]*\r?\n[^!\r\n]*)!- Name").Replace(osm, "$1!- Zone Label", 1);
                    break;
                case "osm-subsurface-multiplier-relabelled":
                    osm = new Regex(@"(OS:SubSurface,[^;]*?)!- Multiplier").Replace(osm, "$1!- Copies", 1);
                    break;
            }

            Assert.That(idf + osm, Is.Not.EqualTo(File.ReadAllText(idfPath) + File.ReadAllText(osmPath)), "the mutation applied");
            File.WriteAllText(idfPath, idf);
            File.WriteAllText(osmPath, osm);
            ExportManifest manifest = ExportManifest.Load(Path.Combine(run, "export.json"));
            manifest.Mutation = "test: " + what;
            manifest.IdfSha256 = Hashing.Sha256(idfPath);
            manifest.OsmSha256 = Hashing.Sha256(osmPath);
            manifest.Save(Path.Combine(run, "export.json"));

            OsReport report = TestSupport.ReadAndCompare(run, TestSupport.Box);
            Assert.That(report.ExitCode, Is.EqualTo(ReadBackExit.BlockedTechnical), what + ": " + report.Status + " " + string.Join(" | ", report.Consistency.Concat(report.Unsupported)));
            Assert.That(report.Consistency, Is.Empty, string.Join(" | ", report.Consistency));
            Assert.That(report.Unsupported, Is.Not.Empty);
        }

        [Test]
        public void BothFixtures_AreUnrotatedAndSingleCopy_AsSamStates()
        {
            foreach (OsReport report in new[] { box, two })
            {
                Assert.That(report.Unsupported, Is.Empty, report.FixtureId + ": " + string.Join(" | ", report.Unsupported));
                Assert.That(report.Supporting.Single(x => x.StartsWith("orientation:", StringComparison.Ordinal)), Does.EndWith("the native building is unrotated, as SAM states"));
                Assert.That(report.Supporting.Single(x => x.StartsWith("multiplicity:", StringComparison.Ordinal)), Does.EndWith("every effective multiplier is 1, as SAM states"));
            }
        }

        /// <summary>A negative copy of the box run, read in a fresh process; the copy keeps the original export's identity.</summary>
        private string ReadNegative(string caseId, out ExportManifest manifest, out NativeSnapshot snapshot)
        {
            string run = TestSupport.NewDirectory(caseId);
            Assert.That(TestSupport.Cli(out string output, "negative", "--run", boxRun, "--case", caseId, "--out", run), Is.EqualTo(0), output);
            Assert.That(TestSupport.Cli(out string read, "read", "--run", run), Is.EqualTo(0), read);
            manifest = ExportManifest.Load(Path.Combine(run, "export.json"));
            snapshot = NativeSnapshot.Load(Path.Combine(run, "native.json"));
            Assert.That(ProcessIdentity.IsRecorded(manifest.ProcessId, manifest.ProcessStartedUtc) && ProcessIdentity.IsRecorded(snapshot.ProcessId, snapshot.ProcessStartedUtc), Is.True, "both identities are recorded");
            return run;
        }

        /// <summary>
        /// Build 39603021836 (head e0a6ea19): compare wrote no report for one negative. Windows reuses the id of an exited
        /// process, and a negative copy keeps the id of an export that exited long before, so a fresh reader can carry it;
        /// the reviewed head refused that (exit 70, no report). Reproduced by giving the copy's export the reader's id: the
        /// reader is still a different process (another creation time, started after the export finished) and is judged.
        /// </summary>
        [Test]
        public void AReusedProcessId_IsNotTheExportProcess()
        {
            string run = ReadNegative("neg-people-density", out ExportManifest manifest, out NativeSnapshot snapshot);
            manifest.ProcessId = snapshot.ProcessId;
            manifest.Save(Path.Combine(run, "export.json"));

            int exit = TestSupport.Compare(run, TestSupport.Box, out string output);
            Assert.That(exit, Is.EqualTo(ReadBackExit.Fail), () => TestSupport.Evidence(run, "compare", exit, output));
            OsReport report = OsReport.Load(Path.Combine(run, "compare-approved", "report.json"));
            Assert.That(report.Inputs["exportPid"], Is.EqualTo(report.Inputs["readerPid"]), "the id is shared");
            Assert.That(report.Inputs["exportProcessStartedUtc"], Is.Not.EqualTo(report.Inputs["readerProcessStartedUtc"]), "the processes are not");
            Assert.That(report.Inputs["mutation"], Does.StartWith("neg-people-density"));
        }

        /// <summary>A read not proven to be a fresh process after the export is still an infrastructure failure (exit 70, no report).</summary>
        [TestCase("same-process")]
        [TestCase("identity-not-recorded")]
        [TestCase("read-before-the-export-finished")]
        public void AReadNotProvenFresh_IsAnInfrastructureFailure(string what)
        {
            string run = ReadNegative("neg-people-density", out ExportManifest manifest, out NativeSnapshot snapshot);
            string expected = "the reader did not run in a fresh process after the export";
            switch (what)
            {
                case "same-process":
                    manifest.ProcessId = snapshot.ProcessId;
                    manifest.ProcessStartedUtc = snapshot.ProcessStartedUtc;
                    break;
                case "identity-not-recorded":
                    manifest.ProcessStartedUtc = default;
                    expected = "the export or reader process identity is not recorded";
                    break;
                case "read-before-the-export-finished":
                    manifest.FinishedUtc = snapshot.StartedUtc;
                    break;
            }

            manifest.Save(Path.Combine(run, "export.json"));
            int exit = TestSupport.Compare(run, TestSupport.Box, out string output);
            Assert.That(exit, Is.EqualTo(ReadBackExit.Infrastructure), what + ": " + output);
            Assert.That(output, Does.Contain("infrastructure: " + expected), what);
            Assert.That(Directory.Exists(Path.Combine(run, "compare-approved")), Is.False, "no report");
        }

        [Test]
        public void TheOriginalRuns_AreUnchangedByTheNegatives()
        {
            foreach (string run in new[] { boxRun, twoRun })
            {
                ExportManifest manifest = ExportManifest.Load(Path.Combine(run, "export.json"));
                Assert.That(Hashing.Sha256(Path.Combine(run, "in.idf")), Is.EqualTo(manifest.IdfSha256));
                Assert.That(Hashing.Sha256(Path.Combine(run, "model.osm")), Is.EqualTo(manifest.OsmSha256));
                Assert.That(manifest.Mutation, Is.Null);
            }
        }
    }
}
