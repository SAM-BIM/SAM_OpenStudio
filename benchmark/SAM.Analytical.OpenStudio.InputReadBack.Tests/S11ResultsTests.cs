// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Analytical.Benchmark;
using SAM.Analytical.OpenStudio.Benchmark;
using SAM.Analytical.OpenStudio.ResultsReadBack;
using SAM.Analytical.OpenStudio.ResultsReadBack.Cli;
using System;
using System.Collections.Generic;
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
            string model = S11Support.Two();
            S11Case run = S11Support.Prepare(SyntheticRun.For(model, "two"), model, "two-cli");

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

        // ---- F-S11-1 / F-S11-2 integrated: positive regressions on the signed-off fixtures ------------------------------

        /// <summary>
        /// F-S11-1 (SAM_OpenStudio#35): the signed-off s08-fix-two spaces FixA and FixB share the GUID prefix 5080c000.
        /// Through the production import (SAM-P producer objects, SAM-I AddResults) and B1a each space keeps its own
        /// zone-specific fractional unmet hours, and the native and production identities stay distinct and agree. Before
        /// #35 one zone's not-met time overwrote the other's and these rows FAILed; a swap between the zones FAILs too.
        /// </summary>
        [Test]
        public void SignedOffTwoZone_SharedGuidPrefix_EachSpaceKeepsItsOwnResults()
        {
            string model = S11Support.Two();
            S11Case run = S11Support.Evaluate(SyntheticRun.For(model, "two", fractionalUnmet: true), model, "two-shared-prefix");

            Guid a = Guid.Parse(S11Support.SpaceAGuid);
            Guid b = Guid.Parse(S11Support.SpaceBGuid);
            Assert.That(a.ToString("N").Substring(0, 8), Is.EqualTo(b.ToString("N").Substring(0, 8)), "the signed-off fixture's shared prefix");
            Assert.That(run.Synthetic.Zones.Select(x => x.SpaceGuid), Is.EquivalentTo(new[] { a, b }));
            Assert.That(run.Compare.Verdict, Is.EqualTo("PASS"), run.Summary());
            Assert.That(run.Compare.Failed + run.Compare.Blocked, Is.Zero);

            // Native identity: two distinct spaces, zones and Ideal Loads; each is the production key of its own SAM space.
            Assert.That(run.Reference.Spaces.All(x => x.IdentityProblem == null), Is.True);
            foreach (Func<SpaceReference, string> part in new Func<SpaceReference, string>[] { x => x.NativeSpace, x => x.Zone, x => x.IdealLoads })
            {
                Assert.That(run.Reference.Spaces.Select(part).Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(2));
            }

            foreach (ProductionKey key in run.Subject.ProductionKeys)
            {
                SpaceReference native = run.Reference.Space(key.SpaceGuid);
                Assert.That(key.IdealLoads, Is.EqualTo(native.IdealLoads).IgnoreCase, key.SpaceName);
                Assert.That(key.ThermalZone, Is.EqualTo(native.Zone).IgnoreCase, key.SpaceName);
            }

            List<ResultRow> identity = run.Compare.Rows.Where(x => x.Layer == "identity" && (x.Quantity == "native" || x.Quantity == "idealLoadsKey" || x.Quantity == "zoneKey")).ToList();
            Assert.That(identity, Has.Count.EqualTo(6));
            Assert.That(identity.All(x => x.Status == "PASS"), Is.True, run.Summary());

            // Zone-specific fractional unmet hours: exact in the independent reference, and each layer carries its own space's.
            for (int z = 0; z < run.Synthetic.Zones.Count; z++)
            {
                Guid space = run.Synthetic.Zones[z].SpaceGuid;
                double heating = z == 0 ? 14.5 : 7.25;
                double cooling = z == 0 ? 14.75 : 5.25;
                Assert.That(run.Reference.Space(space).Quantities[S11Names.UnmetHeating].Sum, Is.EqualTo(heating));
                Assert.That(run.Reference.Space(space).Quantities[S11Names.UnmetCooling].Sum, Is.EqualTo(cooling));
                foreach (string layer in new[] { "samP", "samI", "b1a" })
                {
                    Assert.That(run.Row(layer + "|" + run.Space(z) + "|Heating|unmetHours").Subject, Is.EqualTo(heating), layer);
                    Assert.That(run.Row(layer + "|" + run.Space(z) + "|Cooling|unmetHours").Subject, Is.EqualTo(cooling), layer);
                }
            }

            // SAM-I: every annual result is attached to the space it references; no zone or space was left unmatched.
            List<SubjectResult> annual = run.Subject.SamI.Where(x => x.IsAnnual).ToList();
            Assert.That(annual, Has.Count.EqualTo(4));
            Assert.That(annual.All(x => x.AttachedTo.HasValue && Guid.Parse(x.Reference) == x.AttachedTo.Value), Is.True);
            Assert.That(annual.Select(x => x.AttachedTo.Value).Distinct(), Is.EquivalentTo(new[] { a, b }));
            Assert.That(run.Subject.SamIDiagnostics.Where(x => x.Contains("matched no SAM space") || x.Contains("No simulation result found")), Is.Empty);
        }

        /// <summary>
        /// F-S11-2 (SAM#219): fractional EnergyPlus not-met time, 14.5 h heating and 14.75 h cooling (binary-exact), survives
        /// the production SQL import, a SAM JSON save and reload (the producer's sam.json, and the imported model) and B1a,
        /// and S11 PASSes. Before SAM#219 the integer metadata rounded half to even (14 and 15) and these rows FAILed.
        /// </summary>
        [Test]
        public void FractionalUnmetHours_SurviveSqlImport_SamSaveReload_AndB1a()
        {
            string model = S11Support.Model(TestSupport.Box);
            S11Case run = S11Support.Evaluate(SyntheticRun.For(model, "box", fractionalUnmet: true), model, "box-fractional-unmet");
            Guid space = run.Synthetic.Zones.Single().SpaceGuid;

            Assert.That(run.Compare.Verdict, Is.EqualTo("PASS"), run.Summary());
            Assert.That(run.Reference.Space(space).Quantities[S11Names.UnmetHeating].Sum, Is.EqualTo(14.5));
            Assert.That(run.Reference.Space(space).Quantities[S11Names.UnmetCooling].Sum, Is.EqualTo(14.75));
            foreach (string layer in new[] { "samP", "samI", "b1a" })
            {
                foreach ((string loadType, double expected) in new[] { ("Heating", 14.5), ("Cooling", 14.75) })
                {
                    ResultRow row = run.Row(layer + "|" + run.Space(0) + "|" + loadType + "|unmetHours");
                    Assert.That(row.Status, Is.EqualTo("PASS"), row.Id);
                    Assert.That(row.Reference, Is.EqualTo(expected), row.Id);
                    Assert.That(row.Subject, Is.EqualTo(expected), row.Id);
                }
            }

            // SQL import (the producer's objects), then saved as sam.json and read back.
            Assert.That(Unmet(S11Support.Annual(run.Inputs, space, LoadKind.Heating)), Is.EqualTo(14.5));
            Assert.That(Unmet(S11Support.Annual(run.Inputs, space, LoadKind.Cooling)), Is.EqualTo(14.75));
            OpenStudioBenchmarkInputs reloaded = OpenStudioBenchmarkInputs.Read(Path.Combine(run.Run, RunLayout.SamResultsFile));
            Assert.That(Unmet(S11Support.Annual(reloaded, space, LoadKind.Heating)), Is.EqualTo(14.5));
            Assert.That(Unmet(S11Support.Annual(reloaded, space, LoadKind.Cooling)), Is.EqualTo(14.75));

            // B1a as written.
            BenchmarkDocument b1a = BenchmarkSerializer.Deserialize(File.ReadAllText(Path.Combine(run.Run, RunLayout.B1aFile)));
            BenchmarkSpaceResult b1aSpace = b1a.Spaces.Single(x => x.Guid == space.ToString("N"));
            Assert.That(b1aSpace.Heating.UnmetHours.Value, Is.EqualTo(14.5));
            Assert.That(b1aSpace.Cooling.UnmetHours.Value, Is.EqualTo(14.75));

            // SAM-I: the SAM_UI/Grasshopper import into the model, saved as SAM JSON and reopened.
            AdjacencyCluster cluster = new AdjacencyCluster(run.Model.AdjacencyCluster);
            Analytical.OpenStudio.Modify.AddResults(cluster, Path.Combine(run.Run, "work", "run", "eplusout.sql"), out List<string> _);
            string saved = Path.Combine(run.Run, "imported-model.json");
            Assert.That(Core.Convert.ToFile(new AnalyticalModel(run.Model, cluster), saved, Core.SAMFileType.Json), Is.True);
            AnalyticalModel reopened = Core.Convert.ToSAM<AnalyticalModel>(saved).Single();
            Space reopenedSpace = reopened.AdjacencyCluster.GetSpaces().Single(x => x.Guid == space);
            List<SpaceSimulationResult> attached = reopened.AdjacencyCluster.GetResults<SpaceSimulationResult>(reopenedSpace);
            Assert.That(Unmet(attached.Where(x => IsLoadType(x, LoadKind.Heating))), Is.EqualTo(14.5));
            Assert.That(Unmet(attached.Where(x => IsLoadType(x, LoadKind.Cooling))), Is.EqualTo(14.75));
        }

        private static bool IsLoadType(SpaceSimulationResult result, LoadKind kind)
        {
            return result.TryGetValue(Analytical.SpaceSimulationResultParameter.LoadType, out string loadType) && loadType == kind.ToString();
        }

        /// <summary>The unmet hours of the single result, read as a double (the stored value, not a rounded one).</summary>
        private static double Unmet(IEnumerable<SpaceSimulationResult> results)
        {
            SpaceSimulationResult result = results.Single();
            Assert.That(result.TryGetValue(Analytical.SpaceSimulationResultParameter.UnmetHours, out double value), Is.True);
            return value;
        }
    }
}
