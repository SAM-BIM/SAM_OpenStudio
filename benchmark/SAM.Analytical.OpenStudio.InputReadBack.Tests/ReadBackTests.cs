// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Analytical.InputEquivalence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.InputReadBack.Tests
{
    /// <summary>
    /// The S09 read-back end to end, offline: export, fresh-process read and compare on the S08 signed-off fixtures with the
    /// Budapest EPW. Each fixture has complete S07 coverage, and every non-PASS item is one of the two named owner
    /// decisions (single-sided opaque optics, the lighting visible-fraction default). The ventilation policy holds hour by
    /// hour, and every targeted mutation is refused.
    /// </summary>
    [TestFixture]
    public class ReadBackTests
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

        /// <summary>The non-PASS items of a fixture, minus the two named owner decisions; must be empty.</summary>
        private static List<ReportItem> Unexplained(OsReport report)
        {
            return report.NonPass.Where(x => !(x.Outcome == "MappingDifference" && x.Quantity == "3a" && x.Key.Contains(".internal"))
                                          && !(x.Outcome == "DocumentedDefault" && x.Key.EndsWith(".ic.lighting.visible", StringComparison.Ordinal))).ToList();
        }

        [Test]
        public void BothFixtures_HaveCompleteCoverage_AndOnlyTheTwoNamedOwnerDecisionsRemain()
        {
            foreach (OsReport report in new[] { box, two })
            {
                Assert.That(report.Coverage, Is.EqualTo("Complete"), report.FixtureId);
                Assert.That(report.MissingComparisons, Is.Empty, report.FixtureId);
                Assert.That(report.Consistency, Is.Empty, report.FixtureId + ": " + string.Join(" | ", report.Consistency));
                Assert.That(report.FixtureCriteria, Is.Empty, report.FixtureId);
                Assert.That(Unexplained(report), Is.Empty, report.FixtureId + ": " + string.Join(" | ", Unexplained(report).Select(x => x.Key + " " + x.Outcome + " " + x.Reason)));
                Assert.That(report.Status, Is.EqualTo(GateStatus.BlockedOwnerDecision.ToString()), report.FixtureId + ": never PASS while the named differences are not accepted");
                Assert.That(report.ExitCode, Is.EqualTo(ReadBackExit.BlockedOwner));
                Assert.That(report.Series.All(x => x.Count == 8760), Is.True, "every hourly series covers the year");
            }
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

            Assert.That(two.CountByOutcome["AcceptedTransformation"], Is.EqualTo(6), "8b, 6.hourly and 6.eflh of the two V1 zones");
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
        public void EveryTargetedMutation_Fails(string caseId, string fixture)
        {
            string source = fixture == TestSupport.Box ? boxRun : twoRun;
            string run = TestSupport.NewDirectory(caseId);
            Assert.That(TestSupport.Cli(out string output, "negative", "--run", source, "--case", caseId, "--out", run), Is.EqualTo(0), output);
            OsReport report = TestSupport.ReadAndCompare(run, fixture);
            Assert.That(report.ExitCode, Is.EqualTo(ReadBackExit.Fail), caseId + ": " + report.Status + " " + string.Join(" | ", report.NonPass.Where(x => x.Status != "BlockedOwnerDecision").Take(3).Select(x => x.Key + " " + x.Reason)));
            Assert.That(report.Inputs["mutation"], Does.StartWith(caseId));
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
