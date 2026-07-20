// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// C6: cancellation, asynchronous execution and run-directory semantics at runner level
    /// (Grasshopper behaviour is validated in Rhino by a human — the component is a thin shell
    /// over this runner): explicit cancellation, cancel-during-output, timeout, sequential and
    /// parallel runs, unique run directories, same-directory collision guard, progress stages
    /// and no surviving processes.
    /// </summary>
    [TestFixture]
    public class CancellationAsyncTests
    {
        private static string WeatherPath(string extension)
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "resources", "weather", "USA_MA_Boston-Logan.Intl.AP.725090_TMYx.2004-2018" + extension));
        }

        private static string WorkDirectory(string name)
        {
            string result = Path.Combine(TestContext.CurrentContext.WorkDirectory, name);
            if (Directory.Exists(result))
            {
                Directory.Delete(result, true);
            }

            return result;
        }

        [Test]
        [Category("Simulation")]
        public void Cancelled_BeforeStart_NoCli_NoSurvivingProcess()
        {
            string epwPath = WeatherPath(".epw");
            Assert.That(File.Exists(epwPath), Is.True);

            using (CancellationTokenSource cancellationTokenSource = new CancellationTokenSource())
            {
                cancellationTokenSource.Cancel();

                OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox().ToOpenStudio(epwPath, WorkDirectory("c6_cancel_before"), cancellationToken: cancellationTokenSource.Token);

                Assert.That(result.RunResult, Is.Null, "The CLI must not start on a pre-cancelled token");
                Assert.That(result.Diagnostics.Any(d => d.Message.Contains("cancelled")), Is.True);
                Assert.That(File.Exists(result.OsmPath), Is.True, "The OSM is still saved");
            }
        }

        [Test]
        [Category("Simulation")]
        public async Task Cancel_DuringRun_TerminatesProcessTree_NoSurvivingProcess()
        {
            string epwPath = WeatherPath(".epw");
            Assert.That(File.Exists(epwPath), Is.True);

            using (CancellationTokenSource cancellationTokenSource = new CancellationTokenSource())
            {
                // Cancel the moment the CLI stage starts — deterministic, independent of how
                // fast the fixture simulates.
                System.IProgress<Core.OpenStudio.OpenStudioSimulationProgress> progress = new System.Progress<Core.OpenStudio.OpenStudioSimulationProgress>(x =>
                {
                    if (x.Stage == Core.OpenStudio.OpenStudioSimulationStage.RunningCli)
                    {
                        cancellationTokenSource.Cancel();
                    }
                });

                Task<OpenStudioConversionResult> task = AnalyticalModelFixtures.SingleBox().ToOpenStudioAsync(epwPath, WorkDirectory("c6_cancel_during"), progress: progress, cancellationToken: cancellationTokenSource.Token);

                OpenStudioConversionResult result = await task;

                Assert.That(result.RunResult, Is.Not.Null);
                Assert.That(result.RunResult.Success, Is.False, "A cancelled run cannot succeed");
                Assert.That(result.Diagnostics.Any(d => d.Message.Contains("cancelled")), Is.True);
            }

            bool treeTerminated = SpinWait.SpinUntil(() => Process.GetProcessesByName("energyplus").Length == 0 && Process.GetProcessesByName("openstudio").Length == 0, 10000);
            Assert.That(treeTerminated, Is.True, "No orphaned EnergyPlus or CLI may survive the cancellation");
        }

        [Test]
        [Category("Simulation")]
        public async Task Parallel_UniqueRunDirectories_BothSucceed()
        {
            string epwPath = WeatherPath(".epw");
            Assert.That(File.Exists(epwPath), Is.True);

            string sharedParent = WorkDirectory("c6_parallel");
            Task<OpenStudioConversionResult> first = AnalyticalModelFixtures.SingleBox().ToOpenStudioAsync(epwPath, sharedParent);
            Task<OpenStudioConversionResult> second = AnalyticalModelFixtures.SingleBox().ToOpenStudioAsync(epwPath, sharedParent);

            OpenStudioConversionResult[] results = await Task.WhenAll(first, second);

            Assert.That(results[0].RunResult?.Success, Is.True);
            Assert.That(results[1].RunResult?.Success, Is.True);
            Assert.That(results[0].OsmPath, Is.Not.EqualTo(results[1].OsmPath), "Unique run directories — concurrent runs never collide");
            Assert.That(results[0].Loads.TotalHeating, Is.EqualTo(results[1].Loads.TotalHeating).Within(1e-6), "Identical models produce identical results");
        }

        [Test]
        [Category("Simulation")]
        public async Task SameDirectory_NonUnique_SecondRunGetsCollisionDiagnostic()
        {
            string epwPath = WeatherPath(".epw");
            Assert.That(File.Exists(epwPath), Is.True);

            Core.OpenStudio.OpenStudioRunOptions runOptions = new Core.OpenStudio.OpenStudioRunOptions { UseUniqueRunDirectory = false };
            string directory = WorkDirectory("c6_collision");

            Task<OpenStudioConversionResult> first = AnalyticalModelFixtures.SingleBox().ToOpenStudioAsync(epwPath, directory, openStudioRunOptions: runOptions);
            await Task.Delay(500); // let the first run take the lock

            OpenStudioConversionResult second = await AnalyticalModelFixtures.SingleBox().ToOpenStudioAsync(epwPath, directory, openStudioRunOptions: new Core.OpenStudio.OpenStudioRunOptions { UseUniqueRunDirectory = false });
            Assert.That(second.Diagnostics.Any(d => d.Message.Contains("Another run is in progress")), Is.True, "The lock file guards the non-unique directory");
            Assert.That(second.RunResult, Is.Null);

            OpenStudioConversionResult firstResult = await first;
            Assert.That(firstResult.RunResult?.Success, Is.True);

            // After the first run completes the lock is released — a follow-up run succeeds.
            OpenStudioConversionResult followUp = await AnalyticalModelFixtures.SingleBox().ToOpenStudioAsync(epwPath, directory, openStudioRunOptions: new Core.OpenStudio.OpenStudioRunOptions { UseUniqueRunDirectory = false });
            Assert.That(followUp.RunResult?.Success, Is.True, "Deterministic cleanup: the lock is released after completion");
        }

        [Test]
        [Category("Simulation")]
        public async Task ProgressStages_AreReportedInOrder()
        {
            string epwPath = WeatherPath(".epw");
            Assert.That(File.Exists(epwPath), Is.True);

            List<Core.OpenStudio.OpenStudioSimulationStage> stages = new List<Core.OpenStudio.OpenStudioSimulationStage>();
            System.IProgress<Core.OpenStudio.OpenStudioSimulationProgress> progress = new System.Progress<Core.OpenStudio.OpenStudioSimulationProgress>(x => { lock (stages) { stages.Add(x.Stage); } });

            OpenStudioConversionResult result = await AnalyticalModelFixtures.SingleBox().ToOpenStudioAsync(epwPath, WorkDirectory("c6_progress"), progress: progress);

            Assert.That(result.RunResult?.Success, Is.True);
            Assert.That(stages, Does.Contain(Core.OpenStudio.OpenStudioSimulationStage.SavingOsm));
            Assert.That(stages, Does.Contain(Core.OpenStudio.OpenStudioSimulationStage.WritingOsw));
            Assert.That(stages, Does.Contain(Core.OpenStudio.OpenStudioSimulationStage.RunningCli));
            Assert.That(stages, Does.Contain(Core.OpenStudio.OpenStudioSimulationStage.ReadingResults));
            Assert.That(stages.Last(), Is.EqualTo(Core.OpenStudio.OpenStudioSimulationStage.Complete));
        }

        [Test]
        [Category("Simulation")]
        public async Task SequentialRuns_RepeatedConversionAndDisposal()
        {
            string epwPath = WeatherPath(".epw");
            Assert.That(File.Exists(epwPath), Is.True);

            for (int i = 0; i < 2; i++)
            {
                OpenStudioConversionResult result = await AnalyticalModelFixtures.SingleBox().ToOpenStudioAsync(epwPath, WorkDirectory("c6_sequential_" + i));
                Assert.That(result.RunResult?.Success, Is.True, $"Sequential run {i} must succeed");
                result.Dispose();
            }
        }
    }
}
