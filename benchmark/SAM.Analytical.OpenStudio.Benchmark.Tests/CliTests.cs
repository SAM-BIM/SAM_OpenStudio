// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.IO;
using NUnit.Framework;
using SAM.Analytical.Benchmark;

namespace SAM.Analytical.OpenStudio.Benchmark.Tests
{
    /// <summary>
    /// The producer honours the shared benchmark CLI contract (<see cref="BenchmarkCliHost"/>):
    /// exit <c>0</c> success, <c>2</c> usage, <c>3</c> input/IO/serialization, <c>4</c> validation,
    /// <c>5</c> producer failure. Everything here runs offline: the parse/usage/input paths through
    /// <see cref="Program.Run"/>, and the validation/producer exit decisions through
    /// <see cref="Program.Emit"/> (both reached without an OpenStudio/EnergyPlus install; the full
    /// live route is exercised by the <c>Simulation</c>-category test).
    /// </summary>
    [TestFixture]
    public class CliTests
    {
        [Test]
        public void Help_ReturnsSuccess_AndPrintsUsageOnceToStdout()
        {
            StringWriter output = new StringWriter();
            StringWriter error = new StringWriter();

            int exitCode = Program.Run(new[] { "--help" }, output, error);

            Assert.That(exitCode, Is.EqualTo((int)BenchmarkExitCode.Success));
            Assert.That(error.ToString(), Is.Empty, "help must not write to stderr");
            Assert.That(output.ToString(), Does.Contain("Usage: benchmark-openstudio"));
            Assert.That(CountOccurrences(output.ToString(), "Usage: benchmark-openstudio"), Is.EqualTo(1), "usage must print exactly once");
        }

        [Test]
        public void MissingRequiredOption_ReturnsUsageError()
        {
            StringWriter error = new StringWriter();

            int exitCode = Program.Run(new[] { "--model", "model.json" }, new StringWriter(), error);

            Assert.That(exitCode, Is.EqualTo((int)BenchmarkExitCode.InvalidUsage));
            Assert.That(error.ToString(), Does.Contain("Missing required option"));
        }

        [Test]
        public void NonexistentModelFile_ReturnsInputError()
        {
            string missing = Path.Combine(Path.GetTempPath(), "sam-benchmark-missing-" + Guid.NewGuid().ToString("N") + ".json");
            string outputPath = Path.Combine(Path.GetTempPath(), "sam-benchmark-out-" + Guid.NewGuid().ToString("N") + ".json");
            StringWriter error = new StringWriter();

            int exitCode = Program.Run(
                new[] { "--model", missing, "--weather", missing, "--out", outputPath },
                new StringWriter(),
                error);

            Assert.That(exitCode, Is.EqualTo((int)BenchmarkExitCode.InputOutputOrSerializationFailure));
            Assert.That(File.Exists(outputPath), Is.False);
        }

        [Test]
        public void ModelFileWithNoSamModel_ReturnsInputError()
        {
            string model = Path.GetTempFileName();
            string weather = Path.GetTempFileName();
            string outputPath = Path.Combine(Path.GetTempPath(), "sam-benchmark-out-" + Guid.NewGuid().ToString("N") + ".json");

            // A well-formed JSON document that carries no SAM object: ToSAM yields nothing, so the
            // producer reports a deserialization failure (exit 3) before ever reaching the run stage.
            File.WriteAllText(model, "[]");
            StringWriter error = new StringWriter();
            try
            {
                int exitCode = Program.Run(
                    new[] { "--model", model, "--weather", weather, "--out", outputPath },
                    new StringWriter(),
                    error);

                Assert.That(exitCode, Is.EqualTo((int)BenchmarkExitCode.InputOutputOrSerializationFailure));
                Assert.That(File.Exists(outputPath), Is.False);
            }
            finally
            {
                File.Delete(model);
                File.Delete(weather);
            }
        }

        [Test]
        public void Emit_InvalidDocument_ReturnsValidationFailure_AndWritesNothing()
        {
            string outputPath = Path.Combine(Path.GetTempPath(), "sam-benchmark-out-" + Guid.NewGuid().ToString("N") + ".json");
            StringWriter error = new StringWriter();

            // An empty document fails schema validation; routed through the shared host that is exit 4.
            int exitCode = BenchmarkCliHost.Run(
                Array.Empty<string>(),
                "usage",
                Array.Empty<string>(),
                (_, _) => Program.Emit(new BenchmarkDocument(), outputPath, runSucceeded: true, new StringWriter()),
                new StringWriter(),
                error);

            Assert.That(exitCode, Is.EqualTo((int)BenchmarkExitCode.ValidationFailure));
            Assert.That(File.Exists(outputPath), Is.False, "an invalid document must not be written");
        }

        [Test]
        public void Emit_ValidDocument_WithFailedRun_ReturnsProducerFailure_AndWritesDocument()
        {
            string outputPath = Path.Combine(Path.GetTempPath(), "sam-benchmark-out-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                int exitCode = Program.Emit(BenchmarkFixture.GoldenDocument(), outputPath, runSucceeded: false, new StringWriter());

                Assert.That(exitCode, Is.EqualTo((int)BenchmarkExitCode.ProducerFailure));
                Assert.That(File.Exists(outputPath), Is.True, "a valid document is still written for a failed run");
            }
            finally
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
        }

        [Test]
        public void Emit_ValidDocument_WithSucceededRun_ReturnsSuccess()
        {
            string outputPath = Path.Combine(Path.GetTempPath(), "sam-benchmark-out-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                int exitCode = Program.Emit(BenchmarkFixture.GoldenDocument(), outputPath, runSucceeded: true, new StringWriter());

                Assert.That(exitCode, Is.EqualTo((int)BenchmarkExitCode.Success));
                Assert.That(File.Exists(outputPath), Is.True);
            }
            finally
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            int index = 0;
            while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }
    }
}
