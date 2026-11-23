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
    /// The acceptance driver Run-S11-Results.ps1 (readback and negative phases, and the run phase on inert stand-ins)
    /// under Windows PowerShell 5.1 with stand-in commands (S11DriverStandIn.ps1 for the harness, an inert file for the
    /// producer and the OpenStudio CLI): no engine starts, no model is loaded, nothing is built or deployed. The run
    /// directories are stand-in files described by their own run-manifest.json and a stand-in build receipt, whose commits
    /// are real temporary Git checkouts where a test needs them. The driver must stop (70) on any infrastructure or integrity
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
            public string Script = S11DriverTests.Script();
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

        private sealed class ProcessResult
        {
            public int Exit;
            public string Output;
            public string Error;
            public bool TimedOut;
        }

        /// <summary>
        /// Runs a process to completion within <paramref name="timeoutMilliseconds"/>: stdout and stderr are drained
        /// concurrently while it runs, so neither a full pipe nor a blocking read can outlast the timeout. When the process,
        /// or a descendant still holding its output pipes, has not finished in time, the whole process tree is terminated
        /// and the result says <see cref="ProcessResult.TimedOut"/>.
        /// </summary>
        private static ProcessResult RunProcess(ProcessStartInfo info, int timeoutMilliseconds)
        {
            info.UseShellExecute = false;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            Stopwatch watch = Stopwatch.StartNew();
            using (Process process = Process.Start(info))
            {
                System.Threading.Tasks.Task<string> output = process.StandardOutput.ReadToEndAsync();
                System.Threading.Tasks.Task<string> error = process.StandardError.ReadToEndAsync();
                bool exited = process.WaitForExit(timeoutMilliseconds);
                int remaining = Math.Max(0, timeoutMilliseconds - (int)watch.ElapsedMilliseconds);
                bool drained = exited && System.Threading.Tasks.Task.WaitAll(new System.Threading.Tasks.Task[] { output, error }, remaining);
                if (!drained)
                {
                    try { process.Kill(true); }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }

                    process.WaitForExit(30000);
                    System.Threading.Tasks.Task.WaitAll(new System.Threading.Tasks.Task[] { output, error }, 30000);
                    return new ProcessResult
                    {
                        Exit = process.HasExited ? process.ExitCode : -1,
                        Output = output.IsCompletedSuccessfully ? output.Result : string.Empty,
                        Error = error.IsCompletedSuccessfully ? error.Result : string.Empty,
                        TimedOut = true,
                    };
                }

                process.WaitForExit();
                return new ProcessResult { Exit = process.ExitCode, Output = output.Result, Error = error.Result };
            }
        }

        private static string WindowsPowerShell() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

        /// <summary>git in a test checkout (its own identity, no hooks or signing); the test fails on a nonzero exit.</summary>
        private static string Git(string directory, params string[] arguments)
        {
            ProcessStartInfo info = new ProcessStartInfo("git");
            foreach (string argument in new[] { "-C", directory, "-c", "user.name=S11DriverTests", "-c", "user.email=s11-driver-tests@example.invalid", "-c", "commit.gpgsign=false" }.Concat(arguments))
            {
                info.ArgumentList.Add(argument);
            }

            ProcessResult result = RunProcess(info, 60000);
            Assert.That(result.TimedOut, Is.False, "git " + string.Join(" ", arguments) + " did not finish within 60 s; its process tree was terminated");
            Assert.That(result.Exit, Is.EqualTo(0), "git " + string.Join(" ", arguments) + ": " + result.Error);
            return result.Output.Trim();
        }

        private readonly List<string> checkouts = new List<string>();

        /// <summary>
        /// A new directory for test checkouts under the short system temp path: below the test work directory a checkout's
        /// .git\objects paths exceed MAX_PATH, which git refuses without core.longpaths. Removed after the test.
        /// </summary>
        private string NewCheckouts()
        {
            string path = Path.Combine(Path.GetTempPath(), "s11g-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            System.IO.Directory.CreateDirectory(path);
            checkouts.Add(path);
            return path;
        }

        [TearDown]
        public void RemoveCheckouts()
        {
            foreach (string path in checkouts.Where(System.IO.Directory.Exists))
            {
                // Git writes its objects read-only.
                foreach (string file in System.IO.Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
                System.IO.Directory.Delete(path, true);
            }

            checkouts.Clear();
        }

        /// <summary>A real Git checkout of directory (created with a tracked file when empty): everything committed; returns its HEAD.</summary>
        private static string NewRepository(string directory)
        {
            System.IO.Directory.CreateDirectory(directory);
            if (!System.IO.Directory.EnumerateFileSystemEntries(directory).Any()) File.WriteAllText(Path.Combine(directory, "tracked.txt"), "tracked");
            Git(directory, "init", "-q");
            Git(directory, "config", "core.autocrlf", "false");
            Git(directory, "add", "-A");
            Git(directory, "commit", "-q", "--no-verify", "-m", "S11 driver test checkout");
            string head = Git(directory, "rev-parse", "HEAD");
            Assert.That(head, Does.Match("^[0-9a-f]{40}$"));
            Assert.That(Git(directory, "status", "--porcelain", "--untracked-files=all"), Is.Empty);
            return head;
        }

        private static JsonArray Commits(params (string Repository, string Commit)[] commits) => new JsonArray(commits.Select(x => (JsonNode)new JsonObject { ["repository"] = x.Repository, ["commit"] = x.Commit }).ToArray());

        /// <summary>
        /// The inert producer of the run phase: records its arguments, writes stand-in files where benchmark-openstudio
        /// writes its outputs and exits 0. It starts no engine and reads no model.
        /// </summary>
        private const string RunProducer =
            "param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Items)\r\n" +
            "$options = @{}\r\n" +
            "for ($i = 0; $i + 1 -lt $Items.Count; $i += 2) { $options[$Items[$i].TrimStart('-')] = $Items[$i + 1] }\r\n" +
            "Add-Content -LiteralPath (Join-Path $PSScriptRoot 'calls.txt') -Value ('PRODUCER STAND-IN|' + ($Items -join ' '))\r\n" +
            "$run = Join-Path $options['work'] 'run'\r\n" +
            "New-Item -ItemType Directory -Force -Path $run | Out-Null\r\n" +
            "foreach ($file in @('in.idf', 'eplusout.sql', 'eplusout.eso')) { [IO.File]::WriteAllText((Join-Path $run $file), \"stand-in $file\") }\r\n" +
            "[IO.File]::WriteAllText($options['out'], '{ \"stand-in\": \"b1a\" }')\r\n" +
            "[IO.File]::WriteAllText($options['sam-results'], '{ \"stand-in\": \"sam\" }')\r\n" +
            "exit 0\r\n";

        /// <summary>
        /// Two produced runs (stand-in files), their manifests, the stand-in harness and producer, and a receipt listing them
        /// with the commits <paramref name="commits"/> returns (none by default).
        /// </summary>
        private static Driver Arrange(string label, Func<Driver, JsonArray> commits = null, string producer = null)
        {
            Driver driver = new Driver { Directory = S11Support.NewRun("driver-" + label) };
            driver.Root = Path.Combine(driver.Directory, "acc");
            driver.Bin = Path.Combine(driver.Directory, "bin");
            System.IO.Directory.CreateDirectory(driver.Bin);
            driver.Harness = Path.Combine(driver.Bin, "os-results-readback.ps1");
            File.Copy(Path.Combine(TestContext.CurrentContext.TestDirectory, "S11DriverStandIn.ps1"), driver.Harness);
            driver.Producer = Path.Combine(driver.Bin, "benchmark-openstudio.ps1");
            File.WriteAllText(driver.Producer, producer ?? "Add-Content -LiteralPath (Join-Path $PSScriptRoot 'calls.txt') -Value 'PRODUCER STARTED'\r\nexit 99\r\n");
            File.WriteAllText(Path.Combine(driver.Bin, "os-results-readback.dll"), "stand-in harness assembly");
            File.WriteAllText(Path.Combine(driver.Bin, "benchmark-openstudio.dll"), "stand-in producer assembly");

            driver.Receipt = Path.Combine(driver.Bin, "s11-build-receipt.json");
            JsonArray files = new JsonArray();
            foreach (string file in new[] { "benchmark-openstudio.ps1", "benchmark-openstudio.dll", "os-results-readback.ps1", "os-results-readback.dll" })
            {
                files.Add(new JsonObject { ["path"] = Path.Combine(driver.Bin, file), ["sha256"] = Sha(Path.Combine(driver.Bin, file)) });
            }

            File.WriteAllText(driver.Receipt, new JsonObject { ["schema"] = "s11-build-receipt/1", ["commits"] = commits?.Invoke(driver) ?? new JsonArray(), ["files"] = files }.ToJsonString());

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
            ProcessStartInfo info = new ProcessStartInfo(WindowsPowerShell());
            foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", driver.Script, "-Phase", phase, "-Root", driver.Root, "-BuildReceipt", driver.Receipt, "-HarnessExe", driver.Harness, "-BenchmarkExe", driver.Producer }.Concat(extra))
            {
                info.ArgumentList.Add(argument);
            }

            ProcessResult result = RunProcess(info, 120000);
            driver.Exit = result.Exit;
            driver.Output = result.Output + result.Error;
            Assert.That(result.TimedOut, Is.False, "the driver did not finish within 120 s; its process tree was terminated" + Environment.NewLine + driver.Output);

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

        // ---- receipt commits on real Git checkouts (Revision 2 stopped here: the driver's Git helper called itself) -------

        [Test]
        public void ReceiptCheckoutCleanAtItsCommit_Passes([Values("readback", "negative")] string phase)
        {
            Driver driver = Act(Arrange("receipt-clean", d => { string repository = Path.Combine(NewCheckouts(), "repo"); return Commits((repository, NewRepository(repository))); }), phase);

            Assert.That(driver.Exit, Is.EqualTo(0), driver.Describe());
            Assert.That(driver.Calls, Has.Some.StartsWith("two"), "both runs were processed");
        }

        public enum Checkout
        {
            Modified,
            Untracked,
            AnotherCommit,
            Missing,
        }

        [Test]
        public void ReceiptCheckoutNotCleanAtItsCommit_StopsAt70BeforeAnyStep([Values] Checkout checkout, [Values("readback", "negative")] string phase)
        {
            string repository = null;
            Driver driver = Arrange("receipt-" + checkout, d => { repository = Path.Combine(NewCheckouts(), "repo"); return Commits((repository, NewRepository(repository))); });
            switch (checkout)
            {
                case Checkout.Modified: File.AppendAllText(Path.Combine(repository, "tracked.txt"), "x"); break;
                case Checkout.Untracked: File.WriteAllText(Path.Combine(repository, "untracked.txt"), "x"); break;
                case Checkout.AnotherCommit:
                    File.AppendAllText(Path.Combine(repository, "tracked.txt"), "x");
                    Git(repository, "commit", "-q", "--no-verify", "-a", "-m", "another commit");
                    break;
                case Checkout.Missing: System.IO.Directory.Move(repository, repository + "-moved"); break;
            }

            Act(driver, phase);

            Assert.That(driver.Exit, Is.EqualTo(70), driver.Describe());
            Assert.That(driver.Output, Does.Contain(checkout == Checkout.Missing ? "STOPPED: git -C " + repository + " rev-parse HEAD exited" : "STOPPED: " + repository + " is not clean at the receipt commit"));
            Assert.That(driver.Calls, Is.Empty, "a receipt that does not hold stops before any step");
        }

        /// <summary>
        /// The run phase in a workspace of real checkouts: SAM_OpenStudio (a copy of this driver and the signed-off fixtures)
        /// and, unless <paramref name="sam"/> is false, SAM; the receipt lists both. The OpenStudio CLI and the producer are
        /// inert stand-ins, the EPW a stand-in file with its own hash.
        /// </summary>
        private Driver ArrangeRunWorkspace(string label, bool sam, out string workspace, out string runnerHead, out string samHead)
        {
            string root = NewCheckouts(), runnerCommit = null, samCommit = null;
            Driver driver = Arrange(label, d =>
            {
                string runner = Path.Combine(root, "SAM_OpenStudio");
                string script = Path.Combine(runner, "benchmark", "SAM.Analytical.OpenStudio.ResultsReadBack", "Run-S11-Results.ps1");
                string fixtures = Path.Combine(runner, "benchmark", "SAM.Analytical.OpenStudio.InputReadBack", "Fixtures");
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(script));
                System.IO.Directory.CreateDirectory(fixtures);
                File.Copy(d.Script, script);
                foreach (string file in new[] { "SHA256SUMS.txt", "s08-fix-box.json", "s08-fix-two.json" })
                {
                    File.Copy(Path.Combine(Path.GetDirectoryName(Fixture("s08-fix-box")), file), Path.Combine(fixtures, file));
                }

                d.Script = script;
                runnerCommit = NewRepository(runner);
                if (!sam) return Commits((runner, runnerCommit));
                samCommit = NewRepository(Path.Combine(root, "SAM"));
                return Commits((runner, runnerCommit), (Path.Combine(root, "SAM"), samCommit));
            }, RunProducer);

            System.IO.Directory.Delete(driver.Root, true);
            File.WriteAllText(Path.Combine(driver.Bin, "openstudio.ps1"), "Write-Output '3.10.0+stand-in'\r\n");
            File.WriteAllText(Path.Combine(driver.Directory, "stand-in.epw"), "stand-in EPW");
            workspace = root;
            runnerHead = runnerCommit;
            samHead = samCommit;
            return driver;
        }

        private static string[] RunArguments(Driver driver)
        {
            string epw = Path.Combine(driver.Directory, "stand-in.epw");
            return new[] { "-ConfirmEngineRuns", "-WeatherEpw", epw, "-WeatherEpwSha256", Sha(epw), "-OpenStudioCli", Path.Combine(driver.Bin, "openstudio.ps1"), "-OpenStudioCliVersion", "3.10.0+stand-in" };
        }

        [Test]
        public void Run_ReceiptOfRealCheckouts_RecordsTheRunnerAndSamCommits()
        {
            Driver driver = ArrangeRunWorkspace("run-commits", true, out string _, out string runnerHead, out string samHead);
            Act(driver, "run", RunArguments(driver));

            Assert.That(driver.Exit, Is.EqualTo(0), driver.Describe());
            Assert.That((string)driver.Summary("run")["result"], Is.EqualTo("SIMULATED"));
            string[] produced = driver.Calls.Where(x => x.StartsWith("PRODUCER STAND-IN|", StringComparison.Ordinal)).ToArray();
            Assert.That(produced, Has.Length.EqualTo(2), driver.Describe());
            Assert.That(produced, Has.All.Contain("--runner-commit " + runnerHead + " --sam-commit " + samHead));
            foreach (string run in Runs.Select(x => x.Label))
            {
                JsonNode manifest = JsonNode.Parse(File.ReadAllText(driver.RunFile(run, "run-manifest.json")));
                Assert.That((string)manifest["runnerCommit"], Does.Match("^[0-9a-f]{40}$").And.EqualTo(runnerHead));
                Assert.That((string)manifest["samCommit"], Does.Match("^[0-9a-f]{40}$").And.EqualTo(samHead));
            }
        }

        [Test]
        public void Run_WithoutTheSamCheckout_StopsAt70BeforeTheProducer()
        {
            Driver driver = ArrangeRunWorkspace("run-no-sam", false, out string workspace, out string _, out string _);
            Act(driver, "run", RunArguments(driver));

            Assert.That(driver.Exit, Is.EqualTo(70), driver.Describe());
            Assert.That(driver.Output, Does.Contain("STOPPED: git -C " + Path.Combine(workspace, "SAM") + " rev-parse HEAD exited"));
            Assert.That(driver.Calls, Is.Empty, "no producer starts without both commits");
            Assert.That(File.Exists(driver.RunFile("box", "run-manifest.json")), Is.False);
        }

        // ---- the test process runner itself -----------------------------------------------------------------------------

        /// <summary>
        /// A process that never finishes, with a child that inherits its output pipes: the timeout is effective while both
        /// streams are drained, and the parent and the child are both terminated.
        /// </summary>
        [Test]
        public void ProcessThatOutlivesItsTimeout_IsTerminatedWithItsChildren()
        {
            string ids = Path.Combine(NewCheckouts(), "ids.txt");
            string script =
                "$child = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList '-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 600' -NoNewWindow -PassThru; " +
                "Set-Content -LiteralPath '" + ids + "' -Value @($PID, $child.Id); " +
                "Write-Output ('x' * 200000); [Console]::Error.WriteLine('y' * 200000); Start-Sleep -Seconds 600";
            ProcessStartInfo info = new ProcessStartInfo(WindowsPowerShell());
            foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-Command", script }) info.ArgumentList.Add(argument);

            Stopwatch watch = Stopwatch.StartNew();
            ProcessResult result = RunProcess(info, 20000);

            Assert.That(result.TimedOut, Is.True);
            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(90)), "the timeout bounds the wait");
            Assert.That(File.Exists(ids), Is.True, "the parent started its child before the timeout");
            int[] started = File.ReadAllLines(ids).Where(x => x.Trim().Length > 0).Select(x => int.Parse(x.Trim())).ToArray();
            Assert.That(started, Has.Length.EqualTo(2));
            foreach (int id in started)
            {
                Process process;
                try { process = Process.GetProcessById(id); }
                catch (ArgumentException) { continue; }

                using (process)
                {
                    Assert.That(process.WaitForExit(10000), Is.True, "process " + id + " of the timed-out tree is still running");
                }
            }
        }

        // ---- Build-S11-Receipt.ps1: refusals leave no receipt (Revision 2: invalidate before git discovery) ---------------

        private static string ReceiptScript() => Path.Combine(Path.GetDirectoryName(Script()), "Build-S11-Receipt.ps1");

        /// <summary>
        /// Build-S11-Receipt.ps1 on a workspace of four sibling folders under <paramref name="workspace"/>, with an earlier
        /// receipt in place. MSBuild is a path that does not exist, so nothing is ever built: every outcome is a refusal.
        /// </summary>
        private static ProcessResult BuildReceipt(string workspace, string receipt, string gitExe, string path = null)
        {
            File.WriteAllText(receipt, "{ \"schema\": \"s11-build-receipt/1\", \"note\": \"an earlier build's receipt\" }");
            ProcessStartInfo info = new ProcessStartInfo(WindowsPowerShell());
            List<string> arguments = new List<string> { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", ReceiptScript(), "-RunnerRepository", Path.Combine(workspace, "SAM_OpenStudio"), "-BuildReceipt", receipt, "-MSBuild", Path.Combine(workspace, "no-msbuild", "MSBuild.exe") };
            if (gitExe != null) arguments.AddRange(new[] { "-GitExe", gitExe });
            foreach (string argument in arguments) info.ArgumentList.Add(argument);
            if (path != null) info.Environment["PATH"] = path;

            ProcessResult result = RunProcess(info, 120000);
            Assert.That(result.TimedOut, Is.False, "Build-S11-Receipt.ps1 did not finish within 120 s; its process tree was terminated" + Environment.NewLine + result.Output + result.Error);
            return result;
        }

        private static readonly string[] Repositories = { "SAM", "SAM_Validation", "SAM_Tas", "SAM_OpenStudio" };

        [Test]
        public void Receipt_GitMissing_Exits3AndNoReceiptRemains()
        {
            string workspace = NewCheckouts();
            foreach (string repository in Repositories) System.IO.Directory.CreateDirectory(Path.Combine(workspace, repository, ".git"));
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string path = system + ";" + Path.Combine(system, "WindowsPowerShell", "v1.0");
            Assert.That(path.Split(';').Any(x => File.Exists(Path.Combine(x, "git.exe")) || File.Exists(Path.Combine(x, "git.cmd"))), Is.False, "precondition: the child PATH has no git");
            string receipt = Path.Combine(workspace, "s11-build-receipt.json");

            ProcessResult result = BuildReceipt(workspace, receipt, Path.Combine(workspace, "no-git", "git.exe"), path);

            Assert.That(result.Exit, Is.EqualTo(3), result.Output + result.Error);
            Assert.That(result.Output, Does.Contain("REFUSED (no receipt written): git.exe not found"));
            Assert.That(File.Exists(receipt), Is.False, "the earlier receipt must not survive a refused build");
        }

        /// <summary>A git.exe stand-in (a .cmd) that prints a full SHA for rev-parse and exits nonzero for one subcommand.</summary>
        [TestCase("rev-parse", "rev-parse HEAD exited 1")]
        [TestCase("status", "status --porcelain --untracked-files=all exited 128")]
        public void Receipt_GitExitsNonzero_Exits3AndNoReceiptRemains(string failing, string reason)
        {
            string workspace = NewCheckouts();
            foreach (string repository in Repositories) System.IO.Directory.CreateDirectory(Path.Combine(workspace, repository, ".git"));
            string git = Path.Combine(workspace, "stand-in-git", "git.cmd");
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(git));
            File.WriteAllText(git,
                "@echo off\r\n" +
                (failing == "rev-parse" ? "if \"%3\"==\"rev-parse\" (echo 0123456789abcdef0123456789abcdef01234567& exit /b 1)\r\n" : "if \"%3\"==\"rev-parse\" (echo 0123456789abcdef0123456789abcdef01234567& exit /b 0)\r\n") +
                (failing == "status" ? "if \"%3\"==\"status\" exit /b 128\r\n" : string.Empty) +
                "exit /b 0\r\n");
            string receipt = Path.Combine(workspace, "s11-build-receipt.json");

            ProcessResult result = BuildReceipt(workspace, receipt, git);

            Assert.That(result.Exit, Is.EqualTo(3), result.Output + result.Error);
            Assert.That(result.Output, Does.Contain("REFUSED (no receipt written): git -C " + Path.Combine(workspace, "SAM") + " " + reason));
            Assert.That(File.Exists(receipt), Is.False);
        }

        [Test]
        public void Receipt_CheckoutNotClean_Exits3AndNoReceiptRemains()
        {
            string workspace = NewCheckouts();
            foreach (string repository in Repositories) NewRepository(Path.Combine(workspace, repository));
            File.WriteAllText(Path.Combine(workspace, "SAM_Tas", "untracked.txt"), "x");
            string receipt = Path.Combine(workspace, "s11-build-receipt.json");

            ProcessResult result = BuildReceipt(workspace, receipt, null);

            Assert.That(result.Exit, Is.EqualTo(3), result.Output + result.Error);
            Assert.That(result.Output, Does.Contain("REFUSED (no receipt written): " + Path.Combine(workspace, "SAM_Tas") + " has 1 uncommitted or untracked change(s)"));
            Assert.That(File.Exists(receipt), Is.False);
        }

        /// <summary>The positive control of the git checks: real clean checkouts pass them and the build is refused only later.</summary>
        [Test]
        public void Receipt_CleanCheckouts_PassTheGitChecks_LaterRefusalStillLeavesNoReceipt()
        {
            string workspace = NewCheckouts();
            foreach (string repository in Repositories) NewRepository(Path.Combine(workspace, repository));
            string receipt = Path.Combine(workspace, "s11-build-receipt.json");

            ProcessResult result = BuildReceipt(workspace, receipt, null);

            Assert.That(result.Exit, Is.EqualTo(3), result.Output + result.Error);
            Assert.That(result.Output, Does.Contain("REFUSED (no receipt written): MSBuild not found"));
            Assert.That(File.Exists(receipt), Is.False);
        }
    }
}
