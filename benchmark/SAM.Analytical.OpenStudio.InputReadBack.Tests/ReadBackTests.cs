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
