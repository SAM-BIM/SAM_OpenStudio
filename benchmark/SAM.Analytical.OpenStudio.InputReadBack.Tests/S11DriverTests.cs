// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SAM.Analytical.OpenStudio.InputReadBack.Tests
{
    /// <summary>
    /// The acceptance driver Run-S11-Results.ps1 (readback and negative phases) under Windows PowerShell 5.1 with
    /// stand-in commands (S11DriverStandIn.ps1 for the harness, an inert file for the producer): no engine starts, no
    /// model is loaded, nothing is built or deployed. The run directories are stand-in files described by their own
    /// run-manifest.json and a stand-in build receipt. The driver must stop (70) on any infrastructure or integrity
    /// failure without reading an earlier report, collect numerical FAIL/BLOCKED for both fixtures and finish nonzero, and
    /// fail on an unexpected negative outcome or a changed original.
    /// </summary>
    [TestFixture]
    public class S11DriverTests
    {
        private static readonly (string Fixture, string Label)[] Runs = { ("s08-fix-box", "box"), ("s08-fix-two", "two") };

        private sealed class Driver
        {
            public string Directory;
            public string Root;
            public string Bin;
            public string Receipt;
            public string Harness;
            public string Producer;
            public JsonObject Scenario = new JsonObject();
            public int Exit;
            public string Output;

            public string Run(string label) => Path.Combine(Root, label);

            public string RunFile(string label, string file) => Path.Combine(Run(label), file);

            public List<string> Calls => File.Exists(Path.Combine(Bin, "calls.txt")) ? File.ReadAllLines(Path.Combine(Bin, "calls.txt")).ToList() : new List<string>();

            public JsonNode Summary(string phase) => JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "summary-" + phase + ".json")));

            public void Set(string key, object values)
            {
                Scenario[key] = JsonSerializer.SerializeToNode(values);
            }

            public string Describe() => "exit " + Exit + Environment.NewLine + Output + Environment.NewLine + "calls: " + string.Join(", ", Calls);
        }

        private static string Script()
        {
            DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "benchmark", "SAM.Analytical.OpenStudio.ResultsReadBack", "Run-S11-Results.ps1");
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }

            throw new FileNotFoundException("Run-S11-Results.ps1 not found above " + TestContext.CurrentContext.TestDirectory);
        }

        private static string Fixture(string fixtureId) => Path.Combine(Path.GetDirectoryName(Script()), "..", "SAM.Analytical.OpenStudio.InputReadBack", "Fixtures", fixtureId + ".json");

        private static string Sha(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                return string.Concat(sha.ComputeHash(stream).Select(x => x.ToString("x2")));
            }
        }

        /// <summary>Two produced runs (stand-in files), their manifests, the stand-in harness and producer, and a receipt listing them.</summary>
        private static Driver Arrange(string label)
        {
            Driver driver = new Driver { Directory = S11Support.NewRun("driver-" + label) };
            driver.Root = Path.Combine(driver.Directory, "acc");
            driver.Bin = Path.Combine(driver.Directory, "bin");
            System.IO.Directory.CreateDirectory(driver.Bin);
            driver.Harness = Path.Combine(driver.Bin, "os-results-readback.ps1");
            File.Copy(Path.Combine(TestContext.CurrentContext.TestDirectory, "S11DriverStandIn.ps1"), driver.Harness);
            driver.Producer = Path.Combine(driver.Bin, "benchmark-openstudio.ps1");
            File.WriteAllText(driver.Producer, "Add-Content -LiteralPath (Join-Path $PSScriptRoot 'calls.txt') -Value 'PRODUCER STARTED'\r\nexit 99\r\n");
            File.WriteAllText(Path.Combine(driver.Bin, "os-results-readback.dll"), "stand-in harness assembly");
            File.WriteAllText(Path.Combine(driver.Bin, "benchmark-openstudio.dll"), "stand-in producer assembly");

            driver.Receipt = Path.Combine(driver.Bin, "s11-build-receipt.json");
            JsonArray files = new JsonArray();
            foreach (string file in new[] { "benchmark-openstudio.ps1", "benchmark-openstudio.dll", "os-results-readback.ps1", "os-results-readback.dll" })
            {
                files.Add(new JsonObject { ["path"] = Path.Combine(driver.Bin, file), ["sha256"] = Sha(Path.Combine(driver.Bin, file)) });
            }

            File.WriteAllText(driver.Receipt, new JsonObject { ["schema"] = "s11-build-receipt/1", ["commits"] = new JsonArray(), ["files"] = files }.ToJsonString());

            foreach ((string fixture, string run) in Runs)
            {
                string folder = Path.Combine(driver.Run(run), "work", "run");
                System.IO.Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "in.idf"), "stand-in in.idf " + run);
                File.WriteAllText(Path.Combine(folder, "eplusout.sql"), "stand-in eplusout.sql " + run);
                File.WriteAllText(Path.Combine(folder, "eplusout.eso"), "stand-in eplusout.eso " + run);
                File.WriteAllText(driver.RunFile(run, "b1a.json"), "{ \"stand-in\": \"b1a " + run + "\" }");
                File.WriteAllText(driver.RunFile(run, "sam.json"), "{ \"stand-in\": \"sam " + run + "\" }");
                JsonObject manifest = new JsonObject
                {
                    ["schema"] = "s11-run-manifest/1", ["label"] = run, ["fixture"] = fixture, ["fixtureSha256"] = Sha(Fixture(fixture)),
                    ["receiptSha256"] = Sha(driver.Receipt), ["benchmarkDll"] = Sha(Path.Combine(driver.Bin, "benchmark-openstudio.dll")), ["harnessDll"] = Sha(Path.Combine(driver.Bin, "os-results-readback.dll")),
                    ["idf"] = Path.Combine(folder, "in.idf"), ["idfSha256"] = Sha(Path.Combine(folder, "in.idf")),
                    ["sql"] = Path.Combine(folder, "eplusout.sql"), ["sqlSha256"] = Sha(Path.Combine(folder, "eplusout.sql")),
                    ["eso"] = Path.Combine(folder, "eplusout.eso"), ["esoSha256"] = Sha(Path.Combine(folder, "eplusout.eso")),
                    ["b1aSha256"] = Sha(driver.RunFile(run, "b1a.json")), ["samResultsSha256"] = Sha(driver.RunFile(run, "sam.json")),
                };
                File.WriteAllText(driver.RunFile(run, "run-manifest.json"), manifest.ToJsonString());

                // An earlier attempt's PASS report: never to be read as the result of a later step.
                System.IO.Directory.CreateDirectory(Path.Combine(driver.Run(run), "compare"));
                string old = Path.Combine(driver.Run(run), "compare", "compare.json");
                File.WriteAllText(old, new JsonObject { ["Schema"] = "s11-compare/1", ["Verdict"] = "PASS", ["ExitCode"] = 0, ["Required"] = 85, ["Passed"] = 85, ["Failed"] = 0, ["Blocked"] = 0, ["F3"] = new JsonObject { ["MeetsOd7"] = true } }.ToJsonString());
                File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-1));
            }

            return driver;
        }

        private static Driver Act(Driver driver, string phase, params string[] extra)
        {
            File.WriteAllText(Path.Combine(driver.Bin, "scenario.json"), driver.Scenario.ToJsonString());
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            ProcessStartInfo info = new ProcessStartInfo(powershell) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Script(), "-Phase", phase, "-Root", driver.Root, "-BuildReceipt", driver.Receipt, "-HarnessExe", driver.Harness, "-BenchmarkExe", driver.Producer }.Concat(extra))
            {
                info.ArgumentList.Add(argument);
            }

            using (Process process = Process.Start(info))
            {
                System.Threading.Tasks.Task<string> error = process.StandardError.ReadToEndAsync();
                string output = process.StandardOutput.ReadToEnd();
                Assert.That(process.WaitForExit(120000), Is.True, "the driver did not finish");
                driver.Exit = process.ExitCode;
                driver.Output = output + error.Result;
            }

            Assert.That(driver.Calls, Has.None.EqualTo("PRODUCER STARTED"), "the producer (engine) must never start in these phases");
            return driver;
        }

        // ---- readback ----------------------------------------------------------------------------------------------------

        [Test]
        public void Readback_FullyPassingControl_Exits0()
        {
            Driver driver = Act(Arrange("rb-pass"), "readback");

            Assert.That(driver.Exit, Is.EqualTo(0), driver.Describe());
            Assert.That(driver.Calls, Is.EqualTo(new[] { "box|read", "box|subject", "box|compare", "two|read", "two|subject", "two|compare" }));
            JsonNode summary = driver.Summary("readback");
            Assert.That((string)summary["result"], Is.EqualTo("PASS"));
            Assert.That(summary["runs"].AsArray().Select(x => (string)x["verdict"]), Is.EqualTo(new[] { "PASS", "PASS" }));
            Assert.That((bool)summary["original-unchanged:box"] && (bool)summary["original-unchanged:two"], Is.True);
        }

        /// <summary>The review reproduction: both comparator calls return 70 while old PASS reports exist.</summary>
        [Test]
        public void Readback_InfrastructureErrorWithOldPassReport_StopsAt70AndReadsNoReport()
        {
            Driver driver = Arrange("rb-infra");
            driver.Set("box|compare", new { exit = 70, write = false });
            driver.Set("two|compare", new { exit = 70, write = false });
            Act(driver, "readback");

            Assert.That(driver.Exit, Is.EqualTo(70), driver.Describe());
            Assert.That(driver.Output, Does.Contain("STOPPED: compare box exit 70"));
            Assert.That(driver.Output, Does.Not.Contain("PASS"), "no old report may be presented as a result");
            Assert.That(driver.Calls, Has.None.StartsWith("two|"), "the phase stops at the first infrastructure failure");
            Assert.That((string)driver.Summary("readback")["result"], Is.EqualTo("STOPPED"));
            Assert.That(driver.Summary("readback")["runs"].AsArray(), Is.Empty);
        }

        public static IEnumerable<TestCaseData> StaleOrInconsistentReports()
        {
            yield return new TestCaseData("box|read", new { exit = 70 }, "read box exit 70").SetName("Readback_ReadInfrastructureError_Stops");
            yield return new TestCaseData("box|subject", new { exit = 64 }, "subject box exit 64").SetName("Readback_SubjectUsageError_Stops");
            yield return new TestCaseData("box|subject", new { write = false }, "subject box wrote no").SetName("Readback_SubjectExit0WithoutOutput_Stops");
            yield return new TestCaseData("box|compare", new { write = false }, "compare box did not write").SetName("Readback_CompareExit0WithOnlyAnOldReport_Stops");
            yield return new TestCaseData("box|compare", new { exit = 0, verdict = "FAIL" }, "compare box exit 0 but its report says FAIL").SetName("Readback_ExitDisagreesWithVerdict_Stops");
            yield return new TestCaseData("box|compare", new { exit = 10, verdict = "PASS" }, "compare box exit 10 but its report says PASS").SetName("Readback_FailExitWithPassReport_Stops");
            yield return new TestCaseData("box|compare", new { wrongInputs = true }, "compare box judged a eplusout.sql other than").SetName("Readback_ReportJudgedOtherFiles_Stops");
        }

        [TestCaseSource(nameof(StaleOrInconsistentReports))]
        public void Readback_StepThatDidNotCompleteOrReportFreshly_StopsAt70(string key, object scenario, string reason)
        {
            Driver driver = Arrange("rb-stale");
            driver.Set(key, scenario);
            Act(driver, "readback");

            Assert.That(driver.Exit, Is.EqualTo(70), driver.Describe());
            Assert.That(driver.Output, Does.Contain("STOPPED: " + reason));
            Assert.That(driver.Calls, Has.None.StartsWith("two|"));
        }

        [Test]
        public void Readback_NumericFail_CollectsBothFixturesAndExits10()
        {
            Driver driver = Arrange("rb-fail");
            driver.Set("box|compare", new { exit = 10 });
            Act(driver, "readback");

            Assert.That(driver.Exit, Is.EqualTo(10), driver.Describe());
            JsonNode summary = driver.Summary("readback");
            Assert.That(summary["runs"].AsArray().Select(x => (string)x["label"] + " " + (string)x["verdict"]), Is.EqualTo(new[] { "box FAIL", "two PASS" }));
            Assert.That((string)summary["result"], Is.EqualTo("FAIL"));
        }

        [Test]
        public void Readback_Blocked_CollectsBothFixturesAndExits11()
        {
            Driver driver = Arrange("rb-blocked");
            driver.Set("two|compare", new { exit = 11 });
            Act(driver, "readback");

            Assert.That(driver.Exit, Is.EqualTo(11), driver.Describe());
            Assert.That(driver.Summary("readback")["runs"].AsArray().Select(x => (string)x["verdict"]), Is.EqualTo(new[] { "PASS", "BLOCKED" }));
        }

        [Test]
        public void Readback_FailAndBlocked_Exits10()
        {
            Driver driver = Arrange("rb-fail-blocked");
            driver.Set("box|compare", new { exit = 11 });
            driver.Set("two|compare", new { exit = 10 });
            Act(driver, "readback");

            Assert.That(driver.Exit, Is.EqualTo(10), driver.Describe());
        }

        [Test]
        public void Readback_Od7MetByNeitherFixture_IsNotPass()
        {
            Driver driver = Arrange("rb-od7");
            driver.Set("box|compare", new { od7 = false });
            driver.Set("two|compare", new { od7 = false });
            Act(driver, "readback");

            Assert.That(driver.Exit, Is.EqualTo(11), driver.Describe());
            Assert.That(driver.Output, Does.Contain("OD-7 not met"));
        }

        // ---- changed inputs and build ------------------------------------------------------------------------------------

        public enum Change
        {
            Idf,
            Sql,
            Eso,
            B1a,
            SamResults,
            ModelHashRecorded,
            ReceiptOfAnotherBuild,
            HarnessAssembly,
            HarnessNotInReceipt,
            ManifestPointsOutsideTheRun,
        }

        [Test]
        public void ChangedInputOrBuild_BeforeProcessing_StopsAt70BeforeAnyStep([Values] Change change, [Values("readback", "negative")] string phase)
        {
            Driver driver = Arrange("changed");
            string run = driver.Run("two");
            string folder = Path.Combine(run, "work", "run");
            JsonObject manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(run, "run-manifest.json"))).AsObject();
            switch (change)
            {
                case Change.Idf: File.AppendAllText(Path.Combine(folder, "in.idf"), "x"); break;
                case Change.Sql: File.AppendAllText(Path.Combine(folder, "eplusout.sql"), "x"); break;
                case Change.Eso: File.AppendAllText(Path.Combine(folder, "eplusout.eso"), "x"); break;
                case Change.B1a: File.AppendAllText(Path.Combine(run, "b1a.json"), "x"); break;
                case Change.SamResults: File.AppendAllText(Path.Combine(run, "sam.json"), "x"); break;
                case Change.ModelHashRecorded: manifest["fixtureSha256"] = new string('0', 64); break;
                case Change.ReceiptOfAnotherBuild: manifest["receiptSha256"] = new string('1', 64); break;
                case Change.HarnessAssembly: File.AppendAllText(Path.Combine(driver.Bin, "os-results-readback.dll"), "x"); break;
                case Change.HarnessNotInReceipt:
                    JsonObject receipt = JsonNode.Parse(File.ReadAllText(driver.Receipt)).AsObject();
                    JsonArray files = receipt["files"].AsArray();
                    files.Remove(files.First(x => ((string)x["path"]).EndsWith("os-results-readback.ps1", StringComparison.Ordinal)));
                    File.WriteAllText(driver.Receipt, receipt.ToJsonString());
                    break;
                case Change.ManifestPointsOutsideTheRun:
                    string elsewhere = Path.Combine(driver.Directory, "eplusout.sql");
                    File.Copy(Path.Combine(folder, "eplusout.sql"), elsewhere);
                    manifest["sql"] = elsewhere;
                    break;
            }

            File.WriteAllText(Path.Combine(run, "run-manifest.json"), manifest.ToJsonString());
            Act(driver, phase);

            Assert.That(driver.Exit, Is.EqualTo(70), driver.Describe());
            Assert.That(driver.Output, Does.Contain("STOPPED"));
            Assert.That(driver.Calls, Has.None.StartsWith("two"), "nothing of the changed run is processed");
            if (change == Change.HarnessAssembly || change == Change.HarnessNotInReceipt)
            {
                Assert.That(driver.Calls, Is.Empty, "a receipt that does not hold stops before any step");
            }
        }

        [Test]
        public void Readback_OriginalChangedByAStep_StopsAt70()
        {
            Driver driver = Arrange("rb-touch");
            driver.Set("box|subject", new { touch = driver.RunFile("box", "sam.json") });
            Act(driver, "readback");

            Assert.That(driver.Exit, Is.EqualTo(70), driver.Describe());
            Assert.That(driver.Output, Does.Contain("STOPPED: box after readback: sam.json is not the file the run recorded"));
            Assert.That((bool)driver.Summary("readback")["original-unchanged:box"], Is.False);
        }

        // ---- negative ----------------------------------------------------------------------------------------------------

        [Test]
        public void Negative_EveryCaseAsRequired_Exits0()
        {
            Driver driver = Act(Arrange("neg-pass"), "negative");

            Assert.That(driver.Exit, Is.EqualTo(0), driver.Describe());
            JsonNode summary = driver.Summary("negative");
            Assert.That(summary["runs"].AsArray(), Has.Count.EqualTo(8));
            Assert.That(summary["runs"].AsArray().All(x => (bool)x["asRequired"] && (bool)x["evidence"]), Is.True, summary.ToJsonString());
            Assert.That(driver.Calls.Count(x => x.EndsWith("|subject", StringComparison.Ordinal)), Is.EqualTo(6), "the substitution cases use their captured subject");
        }

        [Test]
        public void Negative_MutationNotDetected_Exits10()
        {
            Driver driver = Arrange("neg-undetected");
            driver.Set("box-neg-duplicate-hour-both|compare", new { exit = 0 });
            Act(driver, "negative");

            Assert.That(driver.Exit, Is.EqualTo(10), driver.Describe());
            Assert.That(driver.Output, Does.Contain("box-neg-duplicate-hour-both").And.Contain("NOT AS REQUIRED"));
            Assert.That(driver.Calls, Does.Contain("two-neg-sensible-cooling-substitution|compare"), "an unexpected outcome is collected, not a stop");
        }

        [Test]
        public void Negative_ControlFails_Exits10AndNoMutationOutcomeIsEvidence()
        {
            Driver driver = Arrange("neg-control");
            driver.Set("box-ctl-unmutated-copy|compare", new { exit = 10 });
            Act(driver, "negative");

            Assert.That(driver.Exit, Is.EqualTo(10), driver.Describe());
            JsonArray runs = driver.Summary("negative")["runs"].AsArray();
            JsonNode[] box = runs.Where(x => ((string)x["label"]).StartsWith("box-", StringComparison.Ordinal)).ToArray();
            Assert.That(box.Skip(1).All(x => (bool)x["asRequired"]), Is.True, "the mutations still gave their exits");
            Assert.That(box.All(x => !(bool)x["evidence"]), Is.True, "but without a passing control none of them is evidence");
            Assert.That(driver.Output, Does.Contain("box control did not PASS"));
        }

        [Test]
        public void Negative_CopyNotMutated_IsNotAsRequired()
        {
            Driver driver = Arrange("neg-unmutated");
            driver.Set("box-neg-delete-hour-both|negative", new { mutate = false });
            Act(driver, "negative");

            Assert.That(driver.Exit, Is.EqualTo(10), driver.Describe());
            JsonNode record = driver.Summary("negative")["runs"].AsArray().Single(x => (string)x["label"] == "box-neg-delete-hour-both");
            Assert.That((int)record["exit"], Is.EqualTo(11), "the stand-in compare still reports BLOCKED");
            Assert.That((bool)record["asRequired"], Is.False);
            Assert.That((bool)record["copyAsDesigned"], Is.False);
        }

        [Test]
        public void Negative_RecordDisagreesWithTheDriver_StopsAt70()
        {
            Driver driver = Arrange("neg-record");
            driver.Set("box-neg-sensible-cooling-substitution|negative", new { expectedExit = 0 });
            Act(driver, "negative");

            Assert.That(driver.Exit, Is.EqualTo(70), driver.Describe());
            Assert.That(driver.Output, Does.Contain("not 'neg-sensible-cooling-substitution' with exit 10"));
        }

        [Test]
        public void Negative_CompareInfrastructureError_StopsAt70()
        {
            Driver driver = Arrange("neg-infra");
            driver.Set("box-neg-delete-hour-both|compare", new { exit = 70 });
            Act(driver, "negative");

            Assert.That(driver.Exit, Is.EqualTo(70), driver.Describe());
            Assert.That(driver.Output, Does.Contain("STOPPED: compare box-neg-delete-hour-both exit 70"));
            Assert.That(driver.Calls, Has.None.StartsWith("box-neg-duplicate"));
        }

        [Test]
        public void Negative_OriginalChangedByACase_StopsAt70()
        {
            Driver driver = Arrange("neg-touch");
            driver.Set("box-neg-delete-hour-both|negative", new { touch = Path.Combine(driver.Run("box"), "work", "run", "eplusout.sql") });
            Act(driver, "negative");

            Assert.That(driver.Exit, Is.EqualTo(70), driver.Describe());
            Assert.That(driver.Output, Does.Contain("STOPPED: box after neg-delete-hour-both: eplusout.sql is not the file the run recorded"));
            Assert.That((bool)driver.Summary("negative")["original-unchanged:box"], Is.False);
        }

        // ---- run phase guard ---------------------------------------------------------------------------------------------

        [Test]
        public void Run_WithoutEngineConfirmation_RefusesBeforeCreatingAnything()
        {
            Driver driver = Arrange("run-guard");
            System.IO.Directory.Delete(driver.Root, true);
            Act(driver, "run");

            Assert.That(driver.Exit, Is.EqualTo(70), driver.Describe());
            Assert.That(driver.Output, Does.Contain("-ConfirmEngineRuns"));
            Assert.That(System.IO.Directory.Exists(driver.Root), Is.False);
        }
    }
}
