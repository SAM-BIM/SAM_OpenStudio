// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// OSW workflow composition: caller measures become workflow steps with their parent
    /// directories as the measure paths; additional IDF strings are injected through a
    /// generated EnergyPlus measure appended last; unusable measure directories are skipped
    /// with a warning — never silently. The default workflow keeps the historical minimal
    /// shape (seed, weather, empty steps).
    /// </summary>
    [TestFixture]
    public class WorkflowMeasureTests
    {
        private static string WeatherPath(string extension)
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "resources", "weather", "USA_MA_Boston-Logan.Intl.AP.725090_TMYx.2004-2018" + extension));
        }

        private static OpenStudioConversionResult Convert_NoRun(Core.OpenStudio.OpenStudioConversionOptions options)
        {
            string epwPath = WeatherPath(".epw");
            Assert.That(File.Exists(epwPath), Is.True, $"Pinned weather fixture missing: {epwPath}");

            string outputDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "workflow", System.Guid.NewGuid().ToString("N").Substring(0, 8));
            return AnalyticalModelFixtures.SingleBox().ToOpenStudio(epwPath, outputDirectory, openStudioConversionOptions: options, run: false);
        }

        private static string CreateMeasureDirectory(string root, string name)
        {
            string directory = Path.Combine(root, name);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "measure.rb"), "class " + name.Replace(" ", "") + " < OpenStudio::Measure::ModelMeasure\nend\n");
            File.WriteAllText(Path.Combine(directory, "measure.xml"), "<?xml version=\"1.0\"?>\n<measure>\n  <schema_version>3.0</schema_version>\n  <name>" + name + "</name>\n</measure>\n");
            return directory;
        }

        [Test]
        public void NoMeasures_WritesMinimalWorkflow()
        {
            using (OpenStudioConversionResult result = Convert_NoRun(null))
            {
                string osw = File.ReadAllText(result.OswPath);
                Assert.That(osw, Does.Not.Contain("measure_paths"), "No measure paths without measures");
                Assert.That(osw, Does.Contain("\"steps\": []"), "The default workflow keeps the minimal empty-steps shape");
                Assert.That(Directory.Exists(Path.Combine(Path.GetDirectoryName(result.OswPath), "measures")), Is.False, "No measures directory is generated");
            }
        }

        [Test]
        public void MeasurePaths_BecomeWorkflowSteps()
        {
            string measuresRoot = Path.Combine(TestContext.CurrentContext.WorkDirectory, "caller_measures", System.Guid.NewGuid().ToString("N").Substring(0, 8));
            string measureDirectory = CreateMeasureDirectory(measuresRoot, "my_test_measure");
            string missingDirectory = Path.Combine(measuresRoot, "missing_measure");

            Core.OpenStudio.OpenStudioConversionOptions options = new Core.OpenStudio.OpenStudioConversionOptions
            {
                MeasurePaths = new List<string> { measureDirectory, missingDirectory },
            };

            using (OpenStudioConversionResult result = Convert_NoRun(options))
            {
                string osw = File.ReadAllText(result.OswPath);
                Assert.That(osw, Does.Contain("\"measure_dir_name\": \"my_test_measure\""), "The measure becomes a workflow step");
                Assert.That(osw, Does.Contain(measuresRoot.Replace('\\', '/')), "The measure parent directory is a measure path");

                Assert.That(result.Diagnostics.Count(d => d.Code == "SAM-OS-SET-001" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Information && d.Message.Contains("my_test_measure")), Is.EqualTo(1), "Each applied measure is named in a diagnostic");
                Assert.That(result.Diagnostics.Count(d => d.Code == "SAM-OS-SET-001" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning && d.Message.Contains("missing_measure")), Is.EqualTo(1), "A missing measure directory warns — never silently skipped");
            }
        }

        [Test]
        public void AdditionalIdfStrings_GenerateEnergyPlusMeasure()
        {
            Core.OpenStudio.OpenStudioConversionOptions options = new Core.OpenStudio.OpenStudioConversionOptions
            {
                AdditionalIdfStrings = new List<string> { "Output:Variable,*,Site Outdoor Air Dewpoint Temperature,hourly;" },
            };

            using (OpenStudioConversionResult result = Convert_NoRun(options))
            {
                string runDirectory = Path.GetDirectoryName(result.OswPath);
                string measureDirectory = Path.Combine(runDirectory, "measures", "sam_additional_idf_objects");

                Assert.That(File.Exists(Path.Combine(measureDirectory, "measure.rb")), Is.True, "The generated measure script exists");
                Assert.That(File.Exists(Path.Combine(measureDirectory, "measure.xml")), Is.True, "The generated measure.xml exists (required by the CLI)");

                string ruby = File.ReadAllText(Path.Combine(measureDirectory, "measure.rb"));
                Assert.That(ruby, Does.Contain("class SAMAdditionalIdfObjects < OpenStudio::Measure::EnergyPlusMeasure"));
                Assert.That(ruby, Does.Contain("Site Outdoor Air Dewpoint Temperature"), "The IDF string is embedded verbatim");

                string osw = File.ReadAllText(result.OswPath);
                Assert.That(osw, Does.Contain("\"measure_dir_name\": \"sam_additional_idf_objects\""), "The generated measure is a workflow step");

                Assert.That(result.Diagnostics.Count(d => d.Code == "SAM-OS-SET-001" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Information && d.Message.Contains("1 additional IDF string")), Is.EqualTo(1), "The injection is named in a diagnostic");
            }
        }

        [Test]
        [Category("Simulation")]
        public void AdditionalIdfStrings_ReachEnergyPlus()
        {
            string epwPath = WeatherPath(".epw");
            Assert.That(File.Exists(epwPath), Is.True);
            Assert.That(Core.OpenStudio.Query.OpenStudioCliPath(), Is.Not.Null, "OpenStudio CLI required for this test");

            Core.OpenStudio.OpenStudioConversionOptions options = new Core.OpenStudio.OpenStudioConversionOptions
            {
                AdditionalIdfStrings = new List<string> { "Output:Variable,*,Site Outdoor Air Dewpoint Temperature,hourly;" },
            };

            string outputDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "workflow_addstr_run");
            using (OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox().ToOpenStudio(epwPath, outputDirectory, openStudioConversionOptions: options))
            {
                Assert.That(result.RunResult?.Success, Is.True, "A valid IDF string must not break the run");

                string idfPath = Path.Combine(Path.GetDirectoryName(result.OswPath), "run", "in.idf");
                Assert.That(File.Exists(idfPath), Is.True);
                Assert.That(File.ReadAllText(idfPath), Does.Contain("Site Outdoor Air Dewpoint Temperature"), "The injected object reached the EnergyPlus IDF");
            }
        }

        [Test]
        [Category("Simulation")]
        public void InvalidAdditionalIdfString_FailsRun()
        {
            string epwPath = WeatherPath(".epw");
            Assert.That(File.Exists(epwPath), Is.True);
            Assert.That(Core.OpenStudio.Query.OpenStudioCliPath(), Is.Not.Null, "OpenStudio CLI required for this test");

            Core.OpenStudio.OpenStudioConversionOptions options = new Core.OpenStudio.OpenStudioConversionOptions
            {
                AdditionalIdfStrings = new List<string> { "This is not an IDF object" },
            };

            string outputDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "workflow_addstr_invalid");
            using (OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox().ToOpenStudio(epwPath, outputDirectory, openStudioConversionOptions: options))
            {
                Assert.That(result.RunResult?.Success, Is.Not.True, "An unparseable IDF string fails the workflow step — and therefore the run; invalid objects are never silently dropped");
            }
        }
    }
}
