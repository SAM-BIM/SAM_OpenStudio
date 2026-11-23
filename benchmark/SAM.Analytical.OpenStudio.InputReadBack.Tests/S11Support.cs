// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Analytical.Benchmark;
using SAM.Analytical.OpenStudio.Benchmark;
using SAM.Analytical.OpenStudio.ResultsReadBack;
using SAM.Analytical.OpenStudio.ResultsReadBack.Cli;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.InputReadBack.Tests
{
    /// <summary>One prepared synthetic S11 run: its directory, model and the objects the steps produced.</summary>
    internal sealed class S11Case
    {
        public string Run;
        public string ModelPath;
        public AnalyticalModel Model;
        public SyntheticRun Synthetic;
        public OpenStudioBenchmarkInputs Inputs;
        public ReferenceDocument Reference;
        public SubjectDocument Subject;
        public CompareDocument Compare;

        public ResultRow Row(string id) => Compare.Rows.SingleOrDefault(x => x.Id == id) ?? throw new AssertionException("no row " + id + "; rows: " + string.Join(", ", Compare.Rows.Select(x => x.Id).Take(80)));

        public string Space(int index) => "space:" + Synthetic.Zones[index].SpaceGuid.ToString("N");

        public string Summary() => Compare.Verdict + ": " + string.Join(" | ", Compare.Rows.Where(x => x.Status != "PASS" && x.Status != "INFO").Take(12).Select(x => x.Id + " " + x.Status + " " + x.Reason));
    }

    internal static class S11Support
    {
        public static string Model(string fixtureId) => TestSupport.Fixture(fixtureId);

        /// <summary>Space A's (FixA) GUID in s08-fix-two.</summary>
        public const string SpaceAGuid = "5080c000-0000-0000-0000-00000000c0a1";

        /// <summary>Space B's (FixB) GUID in s08-fix-two; it shares its first 8 hex digits (5080c000) with space A.</summary>
        public const string SpaceBGuid = "5080c000-0000-0000-0000-00000000c0b1";

        /// <summary>
        /// The signed-off s08-fix-two fixture, unchanged: FixA and FixB share the GUID prefix 5080c000. The importer
        /// associates their zone results by name identity proven unique by the SQL Zones inventory (F-S11-1, #35), so no
        /// re-GUIDed variant is needed.
        /// </summary>
        public static string Two() => Model(TestSupport.Two);

        /// <summary>A text variant of a signed-off fixture, written once to a stable path (so its export is cached).</summary>
        public static string Variant(string fixtureId, string name, params (string From, string To)[] replacements)
        {
            string text = File.ReadAllText(TestSupport.Fixture(fixtureId));
            foreach ((string from, string to) in replacements)
            {
                Assert.That(text, Does.Contain(from), name + ": the fixture must contain " + from);
                text = text.Replace(from, to);
            }

            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "s11-models", name + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (!File.Exists(path) || File.ReadAllText(path) != text)
            {
                File.WriteAllText(path, text);
            }

            return path;
        }

        /// <summary>
        /// Writes the synthetic files, produces b1a.json and sam.json through the production extraction and the benchmark
        /// producer, then runs read, subject and compare IN PROCESS (the separate-process path has its own tests).
        /// <paramref name="afterProduce"/> may edit the run (b1a.json, sam.json) before the reference and subject read it;
        /// <paramref name="beforeCompare"/> may edit the subject, as a mutated subject would be.
        /// </summary>
        public static S11Case Evaluate(SyntheticRun synthetic, string modelPath, string label, DesignDaySource designDaySource = DesignDaySource.None, Action<OpenStudioBenchmarkInputs> mutateInputs = null, Action<S11Case> afterProduce = null, Action<S11Case> beforeCompare = null)
        {
            S11Case result = Prepare(synthetic, modelPath, label, designDaySource, mutateInputs);
            afterProduce?.Invoke(result);
            return Judge(result, beforeCompare);
        }

        public static S11Case Prepare(SyntheticRun synthetic, string modelPath, string label, DesignDaySource designDaySource = DesignDaySource.None, Action<OpenStudioBenchmarkInputs> mutateInputs = null)
        {
            S11Case result = new S11Case { Run = NewRun(label), ModelPath = modelPath, Model = Core.Convert.ToSAM<AnalyticalModel>(modelPath).Single(), Synthetic = synthetic };
            synthetic.Write(result.Run);
            result.Inputs = SyntheticProducer.Produce(result.Model, result.Run, designDaySource, mutateInputs);
            return result;
        }

        public static S11Case Judge(S11Case result, Action<S11Case> beforeCompare = null)
        {
            RunLayout layout = RunLayout.Resolve(result.Run, out List<string> problems);
            Assert.That(problems, Is.Empty);
            result.Reference = ResultReference.Build(result.Model, result.ModelPath, layout.Idf, layout.Sql, layout.Eso, "s11-test");
            result.Subject = SubjectStep.Capture(result.Model, result.ModelPath, layout);
            beforeCompare?.Invoke(result);
            BenchmarkDocument b1a = BenchmarkSerializer.Deserialize(File.ReadAllText(layout.B1a));
            result.Compare = ResultCompare.Run(result.Model, result.Reference, result.Subject, b1a, SamResults.Read(layout.SamResults));
            return result;
        }

        public static string NewRun(string label)
        {
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "s11-runs", label + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>Runs os-results-readback in its own process; returns the exit code with its stdout and stderr in output.</summary>
        public static int Cli(out string output, params string[] arguments)
        {
            string exe = Path.Combine(TestContext.CurrentContext.TestDirectory, "os-results-readback.exe");
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

        /// <summary>The reference SENSIBLE value of a space quantity, for substitution negatives.</summary>
        public static QuantityReference Sensible(ReferenceDocument reference, Guid space, LoadKind kind)
        {
            return reference.Space(space).Quantities[S11Names.Sensible(kind)];
        }

        public static IEnumerable<SpaceSimulationResult> Annual(OpenStudioBenchmarkInputs inputs, Guid space, LoadKind kind)
        {
            return inputs.SpaceResults.Where(x => x.Reference == space.ToString("N") && x.TryGetValue(Analytical.SpaceSimulationResultParameter.LoadType, out string loadType) && loadType == kind.ToString());
        }

        /// <summary>Rewrites b1a.json through the schema types (a test-side edit of the produced document).</summary>
        public static void EditB1a(S11Case run, Action<BenchmarkDocument> edit)
        {
            string path = Path.Combine(run.Run, RunLayout.B1aFile);
            BenchmarkDocument document = BenchmarkSerializer.Deserialize(File.ReadAllText(path));
            edit(document);
            BenchmarkSerializer.Write(path, document);
        }
    }
}
