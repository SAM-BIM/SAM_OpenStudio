// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// Review P1-05: the CLI runner timeout must actually fire while the process is running
    /// (asynchronous output reads + process-tree termination) — never block on synchronous
    /// ReadToEnd until the process exits by itself.
    /// </summary>
    [TestFixture]
    public class CliRunnerTests
    {
        private static string WeatherPath(string extension)
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "resources", "weather", "USA_MA_Boston-Logan.Intl.AP.725090_TMYx.2004-2018" + extension));
        }

        [Test]
        [Category("Simulation")]
        public void CliTimeout_KillsProcess_AndReportsTimeout()
        {
            string epwPath = WeatherPath(".epw");
            Assert.That(File.Exists(epwPath), Is.True, $"Pinned weather fixture missing: {epwPath}");
            Assert.That(Core.OpenStudio.Query.OpenStudioCliPath(), Is.Not.Null, "OpenStudio CLI required for this test");

            string outputDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "cli_timeout");
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, true);
            }

            Core.OpenStudio.OpenStudioRunOptions runOptions = new Core.OpenStudio.OpenStudioRunOptions
            {
                TimeoutSeconds = 1,
            };

            Stopwatch stopwatch = Stopwatch.StartNew();
            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox().ToOpenStudio(epwPath, outputDirectory, null, runOptions);
            stopwatch.Stop();

            Assert.That(result.RunResult, Is.Not.Null, "A run result must be produced even on timeout");
            Assert.That(result.RunResult.Success, Is.False, "A 1 s timeout cannot complete a full annual run");
            Assert.That(result.RunResult.ExitCode, Is.EqualTo(-1), "Timeout must be reported as exit code -1");
            Assert.That(stopwatch.Elapsed.TotalSeconds, Is.LessThan(120), "The timeout must fire while the process runs — before the fix the call blocked for the process's whole lifetime");
            Assert.That(result.Diagnostics.Any(d => d.Message.Contains("TIMEOUT")), Is.True, "A TIMEOUT diagnostic must be recorded");

            // Process-tree termination (Job Object): terminated children linger briefly in the
            // process table while their handles drain, so poll with a bounded budget instead of
            // demanding an instant zero. An orphaned EnergyPlus would outlive this budget.
            bool treeTerminated = System.Threading.SpinWait.SpinUntil(() => Process.GetProcessesByName("energyplus").Length == 0 && Process.GetProcessesByName("openstudio").Length == 0, 10000);
            Assert.That(treeTerminated, Is.True, "The process tree must be terminated — no orphaned EnergyPlus or CLI may outlive the run");
        }
    }
}
