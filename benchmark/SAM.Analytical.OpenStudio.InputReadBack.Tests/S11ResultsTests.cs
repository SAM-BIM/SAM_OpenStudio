// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Analytical.OpenStudio.ResultsReadBack;
using SAM.Analytical.OpenStudio.ResultsReadBack.Cli;
using System;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.InputReadBack.Tests
{
    /// <summary>
    /// S11 positive path and the separate-process harness: a consistent synthetic run PASSes every required row with read,
    /// subject and compare each in its own process, and the CLI negatives on copies give their required exits.
    /// </summary>
    [TestFixture]
    public class S11ResultsTests
    {
        [Test]
        public void ConsistentTwoZoneRun_PassesEveryRow_InSeparateProcesses_AndTheNegativesRefuseCopies()
        {
            string model = S11Support.TwoDistinctPrefix();
            S11Case run = S11Support.Prepare(SyntheticRun.For(model, "two-distinct"), model, "two-cli");

            Assert.That(S11Support.Cli(out string read, "read", "--run", run.Run, "--model", model, "--fixture-id", TestSupport.Two), Is.EqualTo(0), read);
            Assert.That(S11Support.Cli(out string subject, "subject", "--run", run.Run, "--model", model), Is.EqualTo(0), subject);
            int exit = S11Support.Cli(out string compare, "compare", "--run", run.Run, "--model", model);
            CompareDocument document = S11Json.Load<CompareDocument>(Path.Combine(run.Run, "compare", "compare.json"));
            Assert.That(exit, Is.EqualTo(0), compare + " " + string.Join(" | ", document.Rows.Where(x => x.Status != "PASS").Take(10).Select(x => x.Id + " " + x.Status + " " + x.Reason)));
            Assert.That(document.Verdict, Is.EqualTo("PASS"));
            Assert.That(document.Failed + document.Blocked, Is.Zero);
            Assert.That(document.Required, Is.GreaterThan(60), "every layer, space, load type and quantity is a row");
            Assert.That(document.Notes, Does.Contain(CompareDocument.S10Limitation));
            Assert.That(document.F3.MeetsOd7, Is.True, document.F3.Detail);

            ReferenceDocument reference = S11Json.Load<ReferenceDocument>(Path.Combine(run.Run, "reference.json"));
            SubjectDocument captured = S11Json.Load<SubjectDocument>(Path.Combine(run.Run, "subject.json"));
            Assert.That(new[] { reference.ProcessId, captured.ProcessId, document.ProcessId }.Distinct().Count(), Is.EqualTo(3), "read, subject and compare ran in three processes");
            Assert.That(reference.SqlSha256Before, Is.EqualTo(reference.SqlSha256After));
            Assert.That(reference.EsoSha256Before, Is.EqualTo(reference.EsoSha256After));
            Assert.That(captured.ClosureIdentical, Is.True, captured.ClosureDetail);
            Assert.That(File.ReadAllText(Path.Combine(run.Run, "compare", "compare.md")), Does.Contain("Verdict: **PASS**"));

            // Negatives on copies, each read/subject/compared fresh; the original stays byte-identical.
            string before = Sha256.Of(Path.Combine(run.Run, "work", "run", "eplusout.sql")) + Sha256.Of(Path.Combine(run.Run, "work", "run", "eplusout.eso")) + Sha256.Of(Path.Combine(run.Run, "b1a.json")) + Sha256.Of(Path.Combine(run.Run, "sam.json"));
            foreach (string caseId in NegativeStep.Cases.Keys)
            {
                string copy = run.Run + "-" + caseId;
                Assert.That(S11Support.Cli(out string negative, "negative", "--run", run.Run, "--model", model, "--case", caseId, "--out", copy), Is.EqualTo(0), negative);
                Assert.That(S11Support.Cli(out string copyRead, "read", "--run", copy, "--model", model, "--fixture-id", TestSupport.Two), Is.EqualTo(0), copyRead);
                if (!File.Exists(Path.Combine(copy, "subject.json")))
                {
                    Assert.That(S11Support.Cli(out string copySubject, "subject", "--run", copy, "--model", model), Is.EqualTo(0), copySubject);
                }

                int copyExit = S11Support.Cli(out string copyCompare, "compare", "--run", copy, "--model", model);
                CompareDocument copyDocument = S11Json.Load<CompareDocument>(Path.Combine(copy, "compare", "compare.json"));
                Assert.That(copyExit, Is.EqualTo(NegativeStep.Cases[caseId]), caseId + ": " + copyCompare + " " + string.Join(" | ", copyDocument.Rows.Where(x => x.Status != "PASS").Take(6).Select(x => x.Id + " " + x.Status + " " + x.Reason)));
                if (caseId == "neg-sensible-cooling-substitution")
                {
                    Assert.That(copyDocument.Blocked, Is.Zero, "a substitution must FAIL, never BLOCK");
                    Assert.That(copyDocument.Rows.Where(x => x.LoadType == "Cooling" && (x.Quantity == "peakLoad" || x.Quantity == "consumption") && x.Layer != "identity").All(x => x.Status == "FAIL"), Is.True, "every cooling value row fails");
                    Assert.That(copyDocument.Inputs["mutation"], Does.Contain("Sensible"));
                }
            }

            string after = Sha256.Of(Path.Combine(run.Run, "work", "run", "eplusout.sql")) + Sha256.Of(Path.Combine(run.Run, "work", "run", "eplusout.eso")) + Sha256.Of(Path.Combine(run.Run, "b1a.json")) + Sha256.Of(Path.Combine(run.Run, "sam.json"));
            Assert.That(after, Is.EqualTo(before), "the negatives never touch the original run");
        }

        [Test]
        public void ConsistentBoxRun_PassesEveryRow()
        {
            string model = S11Support.Model(TestSupport.Box);
            S11Case run = S11Support.Evaluate(SyntheticRun.For(model, "box"), model, "box");

            Assert.That(run.Compare.Verdict, Is.EqualTo("PASS"), run.Summary());
            Assert.That(run.Reference.Spaces, Has.Count.EqualTo(1));
            Assert.That(run.Reference.Checks.Where(x => x.Status == "BLOCKED"), Is.Empty);
            Assert.That(run.Reference.Completeness.All(x => x.Complete && x.Found == 8760), Is.True);
            Assert.That(run.Reference.Checks.Count(x => x.Id == "S11-C7" && x.Status == "PASS"), Is.EqualTo(4), "both meters in both files equal the zones' Total energy");
        }

        [Test]
        public void CompareStep_RefusesInputsTheReadAndSubjectDidNotSee()
        {
            string model = S11Support.Model(TestSupport.Box);
            S11Case run = S11Support.Evaluate(SyntheticRun.For(model, "box"), model, "box-consistency");
            S11Json.Save(run.Reference, Path.Combine(run.Run, RunLayout.ReferenceFile));
            S11Json.Save(run.Subject, Path.Combine(run.Run, RunLayout.SubjectFile));
            File.AppendAllText(Path.Combine(run.Run, "work", "run", "eplusout.eso"), "\n");

            int exit = S11Support.Cli(out string output, "compare", "--run", run.Run, "--model", model);

            Assert.That(exit, Is.EqualTo(ReadBackExit.Infrastructure), output);
            Assert.That(output, Does.Contain("eplusout.eso is not the file the read step saw"));
        }

        // ---- findings on the signed-off two-zone fixture (pinned; owner decisions) ------------------------------------

        /// <summary>
        /// Finding F-S11-1: both s08-fix-two spaces have GUIDs starting 5080c000, and the importer remaps zone-level keys
        /// (setpoint not-met time) onto Ideal Loads keys by the last 8 characters, so space B's unmet hours are lost and
        /// space A's are overwritten. The native identity (geometry) still identifies both zones; S11 FAILS the rows.
        /// </summary>
        [Test]
        public void Finding_SharedGuidPrefix_ImporterLosesUnmetHoursOfOneZone_S11Fails()
        {
            string model = S11Support.Model(TestSupport.Two);
            S11Case run = S11Support.Evaluate(SyntheticRun.For(model, "two"), model, "two-shared-prefix");

            Assert.That(run.Reference.Spaces.All(x => x.IdentityProblem == null), Is.True, "the geometry identity resolves both zones");
            Assert.That(run.Compare.Verdict, Is.EqualTo("FAIL"));
            string b = run.Space(run.Synthetic.Zones.FindIndex(x => x.SpaceGuid == Guid.Parse(S11Support.SpaceBGuid)));
            foreach (string layer in new[] { "samP", "samI", "b1a" })
            {
                Assert.That(run.Row(layer + "|" + b + "|Heating|unmetHours").Status, Is.EqualTo("FAIL"), run.Summary());
            }

            Assert.That(run.Compare.Rows.Where(x => x.Quantity != "unmetHours" && x.Status != "INFO").All(x => x.Status == "PASS"), Is.True, "only the not-met rows are affected: " + run.Summary());
        }

        /// <summary>
        /// Finding F-S11-2: SAM declares SpaceSimulationResult UnmetHours an integer parameter, so a fractional EnergyPlus
        /// not-met time (timestep fractions) is stored as whole hours. S11 compares the exact sum and FAILS the rows.
        /// </summary>
        [Test]
        public void Finding_FractionalNotMetTime_StoredAsWholeHours_S11Fails()
        {
            string model = S11Support.Model(TestSupport.Box);
            S11Case run = S11Support.Evaluate(SyntheticRun.For(model, "box", fractionalUnmet: true), model, "box-fractional-unmet");

            Assert.That(run.Compare.Verdict, Is.EqualTo("FAIL"));
            ResultRow row = run.Row("b1a|" + run.Space(0) + "|Heating|unmetHours");
            Assert.That(row.Status, Is.EqualTo("FAIL"));
            Assert.That(row.Reference, Is.EqualTo(14.5));
            Assert.That(row.Subject, Is.EqualTo(14));
            Assert.That(run.Compare.Rows.Where(x => x.Quantity != "unmetHours" && x.Status != "INFO").All(x => x.Status == "PASS"), Is.True, run.Summary());
        }
    }
}
