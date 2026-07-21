// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// R2: OSW import. The seed-versus-executed distinction is the point of these tests — an OSW
    /// is a workflow description, and importing it must never quietly present the seed as the
    /// workflow's output (or vice versa).
    /// <para>
    /// Fixtures are generated on the fly rather than committed as binaries: an OSM written by the
    /// forward converter is exactly the file the round trip is about, and generating it keeps the
    /// test honest when the exporter changes.
    /// </para>
    /// </summary>
    [TestFixture]
    public class OswImportTests
    {
        private string directory;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "SAM_OpenStudio_OswTests_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (Exception)
            {
                // a locked temp directory must not fail an otherwise passing test
            }
        }

        /// <summary>Writes a seed OSM produced by the forward conversion.</summary>
        private string WriteSeedOsm(string relativePath = "seed.osm")
        {
            string path = Path.Combine(directory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            using (OpenStudioConversionResult conversionResult = AnalyticalModelFixtures.TwoAdjacentBoxes().ToOpenStudio())
            {
                Assert.That(conversionResult.Model.save(global::OpenStudio.OpenStudioUtilitiesCore.toPath(path), true), Is.True);
            }

            return path;
        }

        private string WriteOsw(string json, string name = "workflow.osw")
        {
            string path = Path.Combine(directory, name);
            File.WriteAllText(path, json);
            return path;
        }

        [Test]
        public void SeedOnly_ExecutionDisabled_ImportsSeedAndWarnsMeasuresNotApplied()
        {
            WriteSeedOsm();
            string oswPath = WriteOsw("{\n  \"seed_file\": \"seed.osm\",\n  \"steps\": [ { \"measure_dir_name\": \"SomeModelMeasure\", \"arguments\": {} } ]\n}");

            OpenStudioImportResult result = Convert.ToSAM(oswPath);

            Assert.That(result.Successful, Is.True);
            Assert.That(result.AnalyticalModel.AdjacencyCluster.GetSpaces().Count, Is.EqualTo(2));
            Assert.That(result.ResolvedOsmPath, Does.EndWith("seed.osm"));
            Assert.That(result.SourcePath, Is.EqualTo(oswPath));

            Core.OpenStudio.OpenStudioDiagnostic diagnostic = result.Diagnostics.FirstOrDefault(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.OswWorkflowNotExecuted);
            Assert.That(diagnostic, Is.Not.Null, "Not applying the workflow's measures must be stated, never implied");
            Assert.That(diagnostic.Severity, Is.EqualTo(Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning));
            Assert.That(diagnostic.Message, Does.Contain("NOT"));
        }

        [Test]
        public void RelativeSeedPath_ResolvesAgainstTheFilesSubdirectory()
        {
            // The CLI's conventional layout: the OSW names "seed.osm" and the model lives in
            // <root>/files.
            WriteSeedOsm(Path.Combine("files", "seed.osm"));
            string oswPath = WriteOsw("{\n  \"seed_file\": \"seed.osm\",\n  \"steps\": []\n}");

            OpenStudioImportResult result = Convert.ToSAM(oswPath);

            Assert.That(result.Successful, Is.True);
            Assert.That(result.ResolvedOsmPath, Does.Contain("files"));
        }

        [Test]
        public void RelativeSeedPath_ResolvesThroughFilePaths()
        {
            string modelsDirectory = Path.Combine(directory, "models");
            WriteSeedOsm(Path.Combine("models", "seed.osm"));

            string oswPath = WriteOsw(string.Format("{{\n  \"seed_file\": \"seed.osm\",\n  \"file_paths\": [\"{0}\"],\n  \"steps\": []\n}}", modelsDirectory.Replace('\\', '/')));

            OpenStudioImportResult result = Convert.ToSAM(oswPath);

            Assert.That(result.Successful, Is.True);
            Assert.That(result.ResolvedOsmPath, Does.Contain("models"));
        }

        [Test]
        public void MissingSeed_ExecutionDisabled_FailsWithSeedNotFound()
        {
            string oswPath = WriteOsw("{\n  \"seed_file\": \"absent.osm\",\n  \"steps\": []\n}");

            OpenStudioImportResult result = Convert.ToSAM(oswPath);

            Assert.That(result.Successful, Is.False);
            Assert.That(result.AnalyticalModel, Is.Null, "Nothing may be returned when there is no model to import");

            Core.OpenStudio.OpenStudioDiagnostic diagnostic = result.Diagnostics.FirstOrDefault(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.OswSeedNotFound);
            Assert.That(diagnostic, Is.Not.Null);
            Assert.That(diagnostic.Severity, Is.EqualTo(Core.OpenStudio.OpenStudioDiagnosticSeverity.Error));
            Assert.That(diagnostic.Message, Does.Contain("Searched"), "The failure must name where the seed was looked for");
        }

        [Test]
        public void NoSeed_ExecutionDisabled_BlocksExplicitly()
        {
            string oswPath = WriteOsw("{\n  \"steps\": [ { \"measure_dir_name\": \"CreateModel\", \"arguments\": {} } ]\n}");

            OpenStudioImportResult result = Convert.ToSAM(oswPath);

            Assert.That(result.Successful, Is.False);
            Assert.That(result.Diagnostics.Any(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.OswSeedMissing && x.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error), Is.True);
        }

        [Test]
        public void MalformedOsw_FailsWithParseDiagnostic()
        {
            string oswPath = WriteOsw("{ this is not json");

            OpenStudioImportResult result = Convert.ToSAM(oswPath);

            Assert.That(result.Successful, Is.False);
            Assert.That(result.Diagnostics.Any(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.OswParseFailed && x.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error), Is.True);
        }

        [Test]
        public void OswThatIsAJsonArray_FailsWithParseDiagnostic()
        {
            string oswPath = WriteOsw("[]");

            OpenStudioImportResult result = Convert.ToSAM(oswPath);

            Assert.That(result.Successful, Is.False);
            Assert.That(result.Diagnostics.Any(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.OswParseFailed), Is.True);
        }

        [Test]
        public void MissingPath_FailsWithInvalidInput()
        {
            OpenStudioImportResult result = Convert.ToSAM(Path.Combine(directory, "nothing-here.osm"));

            Assert.That(result.Successful, Is.False);
            Assert.That(result.Diagnostics.Any(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.InputPathInvalid), Is.True);
        }

        [Test]
        public void UnsupportedExtension_FailsExplicitly()
        {
            string path = Path.Combine(directory, "model.idf");
            File.WriteAllText(path, "Version,9.6;");

            OpenStudioImportResult result = Convert.ToSAM(path);

            Assert.That(result.Successful, Is.False);
            Assert.That(result.Diagnostics.Any(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.InputExtensionUnsupported), Is.True);
        }

        [Test]
        public void CorruptOsm_FailsWithLoadDiagnosticAndNoEmptyModel()
        {
            string path = Path.Combine(directory, "corrupt.osm");
            File.WriteAllText(path, "this is not an OpenStudio model");

            OpenStudioImportResult result = Convert.ToSAM(path);

            Assert.That(result.AnalyticalModel, Is.Null, "A corrupt OSM must never yield a silently empty AnalyticalModel");
            Assert.That(result.Diagnostics.Any(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.OsmLoadFailed && x.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error), Is.True);
        }

        [Test]
        public void OsmImport_NeedsNoWeatherFile()
        {
            string osmPath = WriteSeedOsm();

            // No EPW anywhere near this call: geometry and analytical data must import without one.
            OpenStudioImportResult result = Convert.ToSAM(osmPath);

            Assert.That(result.Successful, Is.True);
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.ResolvedOsmPath, Is.EqualTo(osmPath));
        }

        [Test]
        public void ParallelImports_OfOneOsw_UseIsolatedRunDirectories()
        {
            WriteSeedOsm();
            string oswPath = WriteOsw("{\n  \"seed_file\": \"seed.osm\",\n  \"steps\": []\n}");

            string failureReason;
            OpenStudioWorkflow workflow = OpenStudioWorkflow.Parse(oswPath, out failureReason);
            Assert.That(workflow, Is.Not.Null, failureReason);

            // The staging step is what provides isolation; running the CLI twice in parallel is a
            // multi-minute integration test, so the isolation itself is what is asserted here.
            string first = workflow.WriteIsolatedCopy(Path.Combine(directory, "run_a"), Path.Combine(directory, "seed.osm"));
            string second = workflow.WriteIsolatedCopy(Path.Combine(directory, "run_b"), Path.Combine(directory, "seed.osm"));

            Assert.That(Path.GetDirectoryName(first), Is.Not.EqualTo(Path.GetDirectoryName(second)));
            Assert.That(File.Exists(first), Is.True);
            Assert.That(File.Exists(second), Is.True);

            foreach (string path in new[] { first, second })
            {
                string json = File.ReadAllText(path);
                Assert.That(json, Does.Contain("run_directory"), "Each copy must pin its own run directory");
                Assert.That(json, Does.Contain("seed.osm"));
            }
        }

        [Test]
        public void IsolatedCopy_PreservesStepsAndArgumentsVerbatim()
        {
            WriteSeedOsm();
            string oswPath = WriteOsw("{\n  \"seed_file\": \"seed.osm\",\n  \"steps\": [ { \"measure_dir_name\": \"SetWindowToWallRatio\", \"arguments\": { \"wwr\": 0.42, \"sillHeight\": 0.8 } } ]\n}");

            string failureReason;
            OpenStudioWorkflow workflow = OpenStudioWorkflow.Parse(oswPath, out failureReason);
            Assert.That(workflow, Is.Not.Null, failureReason);

            string copyPath = workflow.WriteIsolatedCopy(Path.Combine(directory, "run"), Path.Combine(directory, "seed.osm"));
            string json = File.ReadAllText(copyPath);

            Assert.That(json, Does.Contain("SetWindowToWallRatio"));
            Assert.That(json, Does.Contain("0.42"), "Measure arguments must survive staging untouched");
            Assert.That(json, Does.Contain("0.8"));
        }

        [Test]
        public void FindFinalOsm_PrefersTheRecordedOsmPath()
        {
            string runDirectory = Path.Combine(directory, "run-root");
            Directory.CreateDirectory(Path.Combine(runDirectory, "run"));

            string recorded = Path.Combine(runDirectory, "recorded.osm");
            File.WriteAllText(recorded, "OS:Version,");
            File.WriteAllText(Path.Combine(runDirectory, "run", "in.osm"), "OS:Version,");
            File.WriteAllText(Path.Combine(runDirectory, "out.osw"), string.Format("{{ \"osm_path\": \"{0}\" }}", recorded.Replace('\\', '/')));

            string how;
            string found = OpenStudioWorkflow.FindFinalOsm(runDirectory, DateTime.UtcNow.AddMinutes(-1), out how);

            Assert.That(found, Is.EqualTo(recorded));
            Assert.That(how, Does.Contain("osm_path"));
        }

        [Test]
        public void FindFinalOsm_FallsBackToRunInOsm()
        {
            string runDirectory = Path.Combine(directory, "run-root");
            Directory.CreateDirectory(Path.Combine(runDirectory, "run"));

            string inOsm = Path.Combine(runDirectory, "run", "in.osm");
            File.WriteAllText(inOsm, "OS:Version,");

            string how;
            string found = OpenStudioWorkflow.FindFinalOsm(runDirectory, DateTime.UtcNow.AddMinutes(-1), out how);

            Assert.That(found, Is.EqualTo(inOsm));
            Assert.That(how, Does.Contain("in.osm"));
        }

        [Test]
        public void FindFinalOsm_IgnoresStaleModelsFromAnEarlierRun()
        {
            string runDirectory = Path.Combine(directory, "run-root");
            Directory.CreateDirectory(runDirectory);

            string stale = Path.Combine(runDirectory, "stale.osm");
            File.WriteAllText(stale, "OS:Version,");
            File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-2));

            string how;
            string found = OpenStudioWorkflow.FindFinalOsm(runDirectory, DateTime.UtcNow.AddMinutes(-1), out how);

            Assert.That(found, Is.Null, "An OSM older than the run cannot be that run's output");
        }

        [Test]
        public void Cancellation_BeforeWorkflowExecution_ReturnsWithoutRunningTheCli()
        {
            WriteSeedOsm();
            string oswPath = WriteOsw("{\n  \"seed_file\": \"seed.osm\",\n  \"steps\": []\n}");

            using (CancellationTokenSource cancellationTokenSource = new CancellationTokenSource())
            {
                cancellationTokenSource.Cancel();

                Core.OpenStudio.OpenStudioImportOptions options = new Core.OpenStudio.OpenStudioImportOptions { ExecuteWorkflow = true, OutputDirectory = directory };

                OpenStudioImportResult result = Convert.ToSAM(oswPath, options, null, null, cancellationTokenSource.Token);

                Assert.That(result, Is.Not.Null, "Cancellation must return a result carrying diagnostics, not throw");
                Assert.That(result.Diagnostics.Count, Is.GreaterThan(0));
            }
        }
    }
}
