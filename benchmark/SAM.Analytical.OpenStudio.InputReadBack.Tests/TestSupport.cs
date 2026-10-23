// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace SAM.Analytical.OpenStudio.InputReadBack.Tests
{
    internal static class TestSupport
    {
        public const string Box = "s08-fix-box";
        public const string Two = "s08-fix-two";
        public const string BoxSha256 = "45524172b32f85df89511dd344141023de999faa9552a199e6b49062612acd08";
        public const string TwoSha256 = "20da9b586345e9fc67dc6308e6da1578b9bc41b3c694dbdab37e9c677959baa0";
        public const string BudapestSha256 = "472ffb4e2f10867600949a392ce46891fc1bd05f2fc8d8917a99bca7d26209c3";

        public static readonly Guid BoxSpace = Guid.Parse("5080c000-0000-0000-0000-00000000c001");
        public static readonly Guid SpaceA = Guid.Parse("5080c000-0000-0000-0000-00000000c0a1");
        public static readonly Guid SpaceB = Guid.Parse("5080c000-0000-0000-0000-00000000c0b1");

        public static string Fixture(string id)
        {
            string path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", id + ".json");
            Assert.That(File.Exists(path), Is.True, "fixture missing: " + path);
            return path;
        }

        public static AnalyticalModel Load(string id)
        {
            return Core.Convert.ToSAM<AnalyticalModel>(Fixture(id)).Single();
        }

        /// <summary>The SAM#212 Budapest TMYx EPW from the sibling SAM checkout, decompressed once and checked against the S05/S08 hash.</summary>
        public static string Budapest()
        {
            string target = Path.Combine(TestContext.CurrentContext.WorkDirectory, "s09-weather", "budapest.epw");
            if (!File.Exists(target))
            {
                DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
                string source = null;
                while (directory != null && source == null)
                {
                    string candidate = Path.Combine(directory.FullName, "SAM", "SAM", "SAM.Tests", "Resources", "Weather", "HUN_BU_Budapest.Met.Center.128400_TMYx.epw.gz");
                    source = File.Exists(candidate) ? candidate : null;
                    directory = directory.Parent;
                }

                Assert.That(source, Is.Not.Null, "the sibling SAM checkout with the SAM#212 weather files was not found");
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (FileStream input = File.OpenRead(source))
                using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
                using (FileStream output = File.Create(target))
                {
                    gzip.CopyTo(output);
                }
            }

            Assert.That(Hashing.Sha256(target), Is.EqualTo(BudapestSha256), "the Budapest EPW is not the S05/S08 file");
            return target;
        }

        public static string NewDirectory(string name)
        {
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "s09-runs", name + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            return path;
        }

        /// <summary>Runs os-input-readback in its own process; returns the exit code, with its labelled stdout and stderr in output.</summary>
        public static int Cli(out string output, params string[] arguments)
        {
            string exe = Path.Combine(TestContext.CurrentContext.TestDirectory, "os-input-readback.exe");
            Assert.That(File.Exists(exe), Is.True, exe);
            ProcessStartInfo info = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = TestContext.CurrentContext.TestDirectory
            };

            foreach (string argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            using (Process process = Process.Start(info))
            {
                System.Threading.Tasks.Task<string> error = process.StandardError.ReadToEndAsync();
                output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                output = "[stdout]" + Environment.NewLine + output + "[stderr]" + Environment.NewLine + error.Result;
                return process.ExitCode;
            }
        }

        /// <summary>export, read and compare (approved rules), each in a fresh process. Returns the report.</summary>
        public static OsReport Pipeline(string modelPath, string fixtureId, string label, out string run)
        {
            run = NewDirectory(label);
            Assert.That(Cli(out string export, "export", "--model", modelPath, "--epw", Budapest(), "--out", run, "--label", label), Is.EqualTo(0), export);
            return ReadAndCompare(run, fixtureId);
        }

        public static OsReport ReadAndCompare(string run, string fixtureId)
        {
            int read = Cli(out string readOutput, "read", "--run", run);
            Assert.That(read, Is.EqualTo(0), () => Evidence(run, "read", read, readOutput));
            int exit = Compare(run, fixtureId, out string compare);
            string reportPath = Path.Combine(run, "compare-approved", "report.json");
            Assert.That(File.Exists(reportPath), Is.True, () => Evidence(run, "compare", exit, compare) + Environment.NewLine + "no report was written: " + reportPath);
            OsReport report = OsReport.Load(reportPath);
            Assert.That(exit, Is.EqualTo(report.ExitCode), () => Evidence(run, "compare", exit, compare));
            return report;
        }

        /// <summary>compare (approved rules) in a fresh process, writing to compare-approved; returns the exit code.</summary>
        public static int Compare(string run, string fixtureId, out string output)
        {
            return Cli(out output, "compare", "--run", run, "--fixture-id", fixtureId, "--rule-set", "approved", "--out", Path.Combine(run, "compare-approved"));
        }

        /// <summary>
        /// A failed CLI step: its exit code and output, the process identities and times of the run, and the run's files.
        /// Also written to s09-diagnostics (bounded), which CI keeps when the step fails.
        /// </summary>
        public static string Evidence(string run, string step, int exit, string output)
        {
            const int limit = 16000;
            StringBuilder text = new StringBuilder();
            text.AppendLine(step + " exit " + exit + " (" + run + ")");
            text.AppendLine("output:");
            text.AppendLine(output == null || output.Length <= limit ? output : output.Substring(0, limit) + "... (" + output.Length + " characters)");
            try
            {
                string manifestPath = Path.Combine(run, "export.json");
                if (File.Exists(manifestPath))
                {
                    ExportManifest manifest = ExportManifest.Load(manifestPath);
                    text.AppendLine(string.Format(CultureInfo.InvariantCulture, "export: schema {0}, pid {1}, process started {2:O}, started {3:O}, finished {4:O}, mutation {5}", manifest.Schema, manifest.ProcessId, manifest.ProcessStartedUtc, manifest.StartedUtc, manifest.FinishedUtc, manifest.Mutation));
                }

                string snapshotPath = Path.Combine(run, "native.json");
                if (File.Exists(snapshotPath))
                {
                    NativeSnapshot snapshot = NativeSnapshot.Load(snapshotPath);
                    text.AppendLine(string.Format(CultureInfo.InvariantCulture, "reader: schema {0}, pid {1}, process started {2:O}, started {3:O}, finished {4:O}", snapshot.Schema, snapshot.ProcessId, snapshot.ProcessStartedUtc, snapshot.StartedUtc, snapshot.FinishedUtc));
                }

                foreach (string file in Directory.Exists(run) ? Directory.GetFiles(run, "*", SearchOption.AllDirectories) : new string[0])
                {
                    text.AppendLine(string.Format(CultureInfo.InvariantCulture, "file {0} {1} bytes, written {2:O}", Path.GetRelativePath(run, file), new FileInfo(file).Length, File.GetLastWriteTimeUtc(file)));
                }
            }
            catch (Exception exception)
            {
                text.AppendLine("run evidence unreadable: " + exception.Message);
            }

            string diagnostics = Path.Combine(TestContext.CurrentContext.WorkDirectory, "s09-diagnostics");
            Directory.CreateDirectory(diagnostics);
            File.WriteAllText(Path.Combine(diagnostics, Path.GetFileName(run) + "-" + step + ".txt"), text.ToString());
            return text.ToString();
        }

        public static string Write(AnalyticalModel model, string name)
        {
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "s09-models", name + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Core.Convert.ToFile(model, path, Core.SAMFileType.Json);
            return path;
        }

        /// <summary>Changes one space's internal condition and stores it back (the setter clones the condition).</summary>
        public static void ChangeCondition(AnalyticalModel model, Guid spaceGuid, Action<InternalCondition> change)
        {
            AdjacencyCluster cluster = model.AdjacencyCluster;
            Space space = cluster.GetSpaces().Single(x => x.Guid == spaceGuid);
            InternalCondition internalCondition = space.InternalCondition;
            change(internalCondition);
            space.InternalCondition = internalCondition;
            cluster.AddObject(space);
        }

        public static void ChangeSpace(AnalyticalModel model, Guid spaceGuid, Action<Space> change)
        {
            AdjacencyCluster cluster = model.AdjacencyCluster;
            Space space = cluster.GetSpaces().Single(x => x.Guid == spaceGuid);
            change(space);
            cluster.AddObject(space);
        }
    }
}
