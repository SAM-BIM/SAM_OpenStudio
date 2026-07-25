// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SAM.Analytical.Benchmark;

namespace SAM.Analytical.OpenStudio.Benchmark
{
    /// <summary>
    /// <c>benchmark-openstudio</c>: the B1b OpenStudio benchmark producer. Loads a SAM
    /// AnalyticalModel, runs the headless OpenStudio/EnergyPlus route (SAM_OpenStudio's own
    /// <c>ToOpenStudio</c> pipeline), and emits a B1a schema-v1 benchmark document
    /// (<c>route = Native-OpenStudio</c>). The two provenance hashes are computed with the B1a
    /// helpers exactly as the schema requires: <c>sourceFileHash</c> over the raw model bytes and
    /// <c>canonicalModelHash</c> over the neutral SAM model BEFORE any OpenStudio translation.
    /// <para>
    /// Argument parsing, invariant-culture, exception mapping and exit codes are delegated to the
    /// shared benchmark CLI host (<see cref="BenchmarkCliHost"/>) so this producer honours the same
    /// contract as every other producer: <c>0</c> success, <c>2</c> usage, <c>3</c> input/IO/
    /// serialization, <c>4</c> validation, <c>5</c> producer failure (see <see cref="BenchmarkExitCode"/>).
    /// </para>
    /// </summary>
    public static class Program
    {
        private static readonly Regex CommitPattern = new Regex("^[0-9a-f]{7,64}$", RegexOptions.CultureInvariant);

        private static readonly string[] RequiredOptions = { "model", "weather", "out" };

        public static int Main(string[] args)
        {
            return Run(args, Console.Out, Console.Error);
        }

        /// <summary>
        /// Testable entry point: the shared host drives parsing, culture, help and exception mapping;
        /// <see cref="Execute"/> carries the producer logic. Writers are injectable so CLI tests can
        /// capture the output without touching the process <see cref="Console"/>.
        /// </summary>
        internal static int Run(string[] args, TextWriter standardOutput, TextWriter standardError)
        {
            return BenchmarkCliHost.Run(
                args,
                Usage,
                RequiredOptions,
                (arguments, _) => Execute(arguments, standardOutput),
                standardOutput,
                standardError);
        }

        private static int Execute(BenchmarkArguments arguments, TextWriter standardOutput)
        {
            // Path validation throws mapped exceptions: a missing input file is a FileNotFoundException
            // (input/IO), a bad output directory a DirectoryNotFoundException (input/IO).
            string modelPath = BenchmarkCliPaths.ValidateInputFile(arguments.RequireOption("model"));
            string weatherPath = BenchmarkCliPaths.ValidateInputFile(arguments.RequireOption("weather"));
            string outputPath = BenchmarkCliPaths.ValidateOutputFile(arguments.RequireOption("out"));

            // Provenance hashes (B1a helpers, exactly per SCHEMA.md "Canonical model hashing").
            string sourceFileHash = BenchmarkHash.ComputeSha256(File.ReadAllBytes(modelPath));

            AnalyticalModel model = LoadModel(modelPath);

            // The canonical hash covers the neutral loaded model BEFORE any engine-specific mutation.
            string neutralJson = model.ToJsonObject().ToJsonString();
            string canonicalModelHash = BenchmarkCanonicalJson.ComputeSha256(neutralJson);

            OpenStudioBenchmarkContext context = new OpenStudioBenchmarkContext
            {
                SourceModelName = string.IsNullOrWhiteSpace(model.Name) ? "model" : model.Name,
                SourceModelGuid = model.Guid.ToString("N"),
                SourceFileHash = sourceFileHash,
                CanonicalModelHash = canonicalModelHash,
                CanonicalizationVersion = BenchmarkCanonicalization.CurrentVersion,
                SamCommit = ResolveCommit(arguments.GetOption("sam-commit"), "SAM_COMMIT", typeof(AnalyticalModel), allowLocalGit: false),
                RunnerCommit = ResolveCommit(arguments.GetOption("runner-commit"), "RUNNER_COMMIT", typeof(Program), allowLocalGit: true),
                EngineName = "EnergyPlus",
                EngineVersion = SafeVersion(Core.OpenStudio.Query.EnergyPlusVersion),
                SdkVersion = SafeVersion(Core.OpenStudio.Query.OpenStudioVersion),
                WeatherIdentity = Path.GetFileNameWithoutExtension(weatherPath),
                WeatherHash = BenchmarkHash.ComputeSha256(File.ReadAllBytes(weatherPath)),
                // Design-day basis derived from the source model (as the TAS producer does), then
                // replaced below by what the route actually established. Never hardcoded: the B3
                // comparator treats a designDaySource difference as provenance-incompatible, so a
                // fixed value would report a genuinely paired cross-engine run as incomparable.
                DesignDaySource = ResolveDesignDaySource(model),
                RunTimestampUtc = DateTimeOffset.UtcNow,
            };

            if (string.IsNullOrEmpty(context.EngineVersion))
            {
                context.EngineVersion = null;
                context.Notes.Add("EnergyPlus version was unavailable from the loaded OpenStudio SDK at run time.");
            }

            string workDirectory = arguments.GetOption("work") ?? Path.Combine(Path.GetTempPath(), "sam_benchmark_openstudio_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(workDirectory);

            Stopwatch stopwatch = Stopwatch.StartNew();
            using (OpenStudioConversionResult conversionResult = model.ToOpenStudio(weatherPath, workDirectory))
            {
                stopwatch.Stop();

                bool success = conversionResult?.RunResult?.Success == true && conversionResult.Results != null;
                context.State = success ? RunState.Success : RunState.Failure;
                context.DurationSeconds = conversionResult?.Results != null && conversionResult.Results.RuntimeSeconds > 0
                    ? conversionResult.Results.RuntimeSeconds
                    : stopwatch.Elapsed.TotalSeconds;
                context.ResultSet = conversionResult?.Results;

                // The route reports its own design-day outcome (explicit DDY / embedded model /
                // none), which is the provenance-grade answer: embedded days that were all skipped
                // as invalid must not be reported as an embedded basis. The model-derived value
                // above stands in only when no conversion result exists at all.
                if (conversionResult != null)
                {
                    DesignDaySource modelDesignDaySource = context.DesignDaySource;
                    context.DesignDaySource = ToBenchmarkDesignDaySource(conversionResult.DesignDaySource);

                    if (context.DesignDaySource == DesignDaySource.None && modelDesignDaySource != DesignDaySource.None)
                    {
                        context.Notes.Add("The source model carries design days but the OpenStudio route imported none; designDaySource records the run's actual basis (None).");
                    }
                }

                if (!success)
                {
                    context.Notes.Add("The OpenStudio/EnergyPlus run did not complete successfully; measurements are unavailable.");
                }

                // Record what the conversion itself reported, on EVERY run rather than only failures.
                // The route documents its own approximations (hourly humidity collapsed to a constant
                // dew point, hourly solar replaced by ASHRAEClearSky, a substituted barometric pressure,
                // the weather/design-day/ground-temperature basis); a successful document that omitted
                // them presented the comparison as cleaner than it was. This also normalizes the
                // producer-generated notes added above, so both arrays are deterministic.
                Modify.ApplyConversionDiagnostics(context, conversionResult?.Diagnostics);

                BenchmarkDocument document = model.ToBenchmark(context);
                return Emit(document, outputPath, success, standardOutput);
            }
        }

        /// <summary>
        /// Validates, writes and reports a produced document, returning the shared exit code: a
        /// document that fails schema validation throws <see cref="BenchmarkValidationException"/>
        /// (host maps to <see cref="BenchmarkExitCode.ValidationFailure"/>); a valid document whose
        /// run did not succeed returns <see cref="BenchmarkExitCode.ProducerFailure"/>; otherwise
        /// <see cref="BenchmarkExitCode.Success"/>. Internal so the producer's exit-code decisions
        /// are exercised offline without an OpenStudio install.
        /// </summary>
        internal static int Emit(BenchmarkDocument document, string outputPath, bool runSucceeded, TextWriter standardOutput)
        {
            BenchmarkValidationResult validation = BenchmarkValidator.Validate(document);
            if (!validation.IsValid)
            {
                throw new BenchmarkValidationException(validation);
            }

            BenchmarkSerializer.Write(outputPath, document);
            standardOutput.WriteLine("Wrote " + outputPath + " (state=" + (runSucceeded ? RunState.Success : RunState.Failure) + ", route=Native-OpenStudio).");
            return runSucceeded ? (int)BenchmarkExitCode.Success : (int)BenchmarkExitCode.ProducerFailure;
        }

        /// <summary>
        /// Loads the source model. A file that exists but does not carry a SAM AnalyticalModel —
        /// whether it deserializes to nothing or the SAM deserializer throws part way through — is a
        /// deserialization failure, reported as <see cref="System.Text.Json.JsonException"/> so the
        /// shared host maps it to input/IO/serialization (exit 3), never a producer failure (5).
        /// </summary>
        private static AnalyticalModel LoadModel(string modelPath)
        {
            AnalyticalModel model;
            try
            {
                model = Core.Convert.ToSAM<AnalyticalModel>(modelPath)?.FirstOrDefault();
            }
            catch (Exception exception) when (!(exception is IOException) && !(exception is UnauthorizedAccessException) && !(exception is System.Text.Json.JsonException))
            {
                throw new System.Text.Json.JsonException("The model file could not be read as a SAM AnalyticalModel: " + modelPath, exception);
            }

            if (model == null)
            {
                throw new System.Text.Json.JsonException("The model file did not deserialize to a SAM AnalyticalModel: " + modelPath);
            }

            return model;
        }

        /// <summary>
        /// The design-day basis derivable from the source model before the run: <see
        /// cref="DesignDaySource.EmbeddedModel"/> only when the model actually carries heating or
        /// cooling design days, otherwise <see cref="DesignDaySource.None"/> — the same derivation
        /// the TAS producer uses, so both documents populate the field on the same terms. This
        /// producer passes no DDY to the route, so <see cref="DesignDaySource.Ddy"/> can only come
        /// from the route's own report (an explicit DDY set through the conversion/run options).
        /// </summary>
        internal static DesignDaySource ResolveDesignDaySource(AnalyticalModel analyticalModel)
        {
            bool hasDesignDays = HasDesignDays(analyticalModel, AnalyticalModelParameter.HeatingDesignDays)
                || HasDesignDays(analyticalModel, AnalyticalModelParameter.CoolingDesignDays);
            return hasDesignDays ? DesignDaySource.EmbeddedModel : DesignDaySource.None;
        }

        private static bool HasDesignDays(AnalyticalModel analyticalModel, AnalyticalModelParameter analyticalModelParameter)
        {
            return analyticalModel != null
                && analyticalModel.TryGetValue(analyticalModelParameter, out Core.SAMCollection<DesignDay> designDays)
                && designDays != null
                && designDays.Count > 0;
        }

        /// <summary>Maps the design-day basis the route established onto the B1a provenance enum.</summary>
        internal static DesignDaySource ToBenchmarkDesignDaySource(Core.OpenStudio.OpenStudioDesignDaySource openStudioDesignDaySource)
        {
            switch (openStudioDesignDaySource)
            {
                case Core.OpenStudio.OpenStudioDesignDaySource.Ddy:
                    return DesignDaySource.Ddy;

                case Core.OpenStudio.OpenStudioDesignDaySource.EmbeddedModel:
                    return DesignDaySource.EmbeddedModel;

                default:
                    return DesignDaySource.None;
            }
        }

        private static string SafeVersion(Func<string> version)
        {
            try
            {
                return version();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Resolves a 7-64 lowercase-hex commit: explicit argument, then environment variable, then
        /// the assembly's InformationalVersion SHA (the <c>+&lt;sha&gt;</c> suffix CI stamps), then —
        /// only for the runner — <c>git rev-parse HEAD</c> from the executable's directory. Returns
        /// null when none resolve; validation then rejects the document with a precise message.
        /// </summary>
        private static string ResolveCommit(string explicitValue, string environmentVariable, Type assemblyType, bool allowLocalGit)
        {
            string candidate = Normalize(explicitValue);
            if (candidate != null)
            {
                return candidate;
            }

            candidate = Normalize(Environment.GetEnvironmentVariable(environmentVariable));
            if (candidate != null)
            {
                return candidate;
            }

            candidate = Normalize(InformationalVersionSha(assemblyType));
            if (candidate != null)
            {
                return candidate;
            }

            if (allowLocalGit)
            {
                candidate = Normalize(GitHead(AppContext.BaseDirectory));
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            value = value.Trim().ToLowerInvariant();
            return CommitPattern.IsMatch(value) ? value : null;
        }

        private static string InformationalVersionSha(Type assemblyType)
        {
            try
            {
                object[] attributes = assemblyType.Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
                if (attributes.Length == 0)
                {
                    return null;
                }

                string informationalVersion = ((System.Reflection.AssemblyInformationalVersionAttribute)attributes[0]).InformationalVersion;
                int plus = informationalVersion?.IndexOf('+') ?? -1;
                return plus >= 0 ? informationalVersion.Substring(plus + 1) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string GitHead(string directory)
        {
            try
            {
                ProcessStartInfo processStartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse HEAD",
                    WorkingDirectory = directory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using (Process process = Process.Start(processStartInfo))
                {
                    if (process == null)
                    {
                        return null;
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(5000);
                    return process.ExitCode == 0 ? output.Trim() : null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private const string Usage =
            "Usage: benchmark-openstudio --model <model.json> --weather <weather.epw> --out <benchmark-OpenStudio.json>\n" +
            "                            [--work <run-directory>] [--sam-commit <sha>] [--runner-commit <sha>]\n" +
            "\n" +
            "Exit codes: 0 success, 2 usage error, 3 input/IO/serialization error, 4 validation failure, 5 producer failure.";
    }
}
