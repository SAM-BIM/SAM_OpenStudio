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
    /// </summary>
    public static class Program
    {
        private static readonly Regex CommitPattern = new Regex("^[0-9a-f]{7,64}$", RegexOptions.CultureInvariant);

        public static int Main(string[] args)
        {
            try
            {
                if (!TryParseArguments(args, out Arguments arguments, out string parseError))
                {
                    Console.Error.WriteLine(parseError);
                    Console.Error.WriteLine();
                    Console.Error.WriteLine(Usage);
                    return (int)ExitCode.Usage;
                }

                return Run(arguments);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Unexpected error: " + exception);
                return (int)ExitCode.Unexpected;
            }
        }

        private static int Run(Arguments arguments)
        {
            if (!File.Exists(arguments.ModelPath))
            {
                Console.Error.WriteLine("Model file not found: " + arguments.ModelPath);
                return (int)ExitCode.Input;
            }

            if (!File.Exists(arguments.WeatherPath))
            {
                Console.Error.WriteLine("Weather file not found: " + arguments.WeatherPath);
                return (int)ExitCode.Input;
            }

            // Provenance hashes (B1a helpers, exactly per SCHEMA.md "Canonical model hashing").
            string sourceFileHash = BenchmarkHash.ComputeSha256(File.ReadAllBytes(arguments.ModelPath));

            AnalyticalModel model = Core.Convert.ToSAM<AnalyticalModel>(arguments.ModelPath)?.FirstOrDefault();
            if (model == null)
            {
                Console.Error.WriteLine("The model file did not deserialize to a SAM AnalyticalModel: " + arguments.ModelPath);
                return (int)ExitCode.Input;
            }

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
                SamCommit = ResolveCommit(arguments.SamCommit, "SAM_COMMIT", typeof(AnalyticalModel), allowLocalGit: false),
                RunnerCommit = ResolveCommit(arguments.RunnerCommit, "RUNNER_COMMIT", typeof(Program), allowLocalGit: true),
                EngineName = "EnergyPlus",
                EngineVersion = SafeVersion(Core.OpenStudio.Query.EnergyPlusVersion),
                SdkVersion = SafeVersion(Core.OpenStudio.Query.OpenStudioVersion),
                WeatherIdentity = Path.GetFileNameWithoutExtension(arguments.WeatherPath),
                WeatherHash = BenchmarkHash.ComputeSha256(File.ReadAllBytes(arguments.WeatherPath)),
                DesignDaySource = DesignDaySource.None,
                RunTimestampUtc = DateTimeOffset.UtcNow,
            };

            if (string.IsNullOrEmpty(context.EngineVersion))
            {
                context.EngineVersion = null;
                context.Notes.Add("EnergyPlus version was unavailable from the loaded OpenStudio SDK at run time.");
            }

            string workDirectory = arguments.WorkDirectory ?? Path.Combine(Path.GetTempPath(), "sam_benchmark_openstudio_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(workDirectory);

            Stopwatch stopwatch = Stopwatch.StartNew();
            using (OpenStudioConversionResult conversionResult = model.ToOpenStudio(arguments.WeatherPath, workDirectory))
            {
                stopwatch.Stop();

                bool success = conversionResult?.RunResult?.Success == true && conversionResult.Results != null;
                context.State = success ? RunState.Success : RunState.Failure;
                context.DurationSeconds = conversionResult?.Results != null && conversionResult.Results.RuntimeSeconds > 0
                    ? conversionResult.Results.RuntimeSeconds
                    : stopwatch.Elapsed.TotalSeconds;
                context.ResultSet = conversionResult?.Results;

                if (!success)
                {
                    context.Notes.Add("The OpenStudio/EnergyPlus run did not complete successfully; measurements are unavailable.");
                    foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in EnumerateErrors(conversionResult))
                    {
                        context.Warnings.Add(diagnostic.ToString());
                    }
                }

                BenchmarkDocument document = model.ToBenchmark(context);

                BenchmarkValidationResult validation = BenchmarkValidator.Validate(document);
                if (!validation.IsValid)
                {
                    Console.Error.WriteLine("The produced benchmark document is invalid:");
                    foreach (ValidationIssue issue in validation.Errors)
                    {
                        Console.Error.WriteLine("  [" + issue.Code + "] " + issue.Path + ": " + issue.Message);
                    }

                    return (int)ExitCode.Validation;
                }

                BenchmarkSerializer.Write(arguments.OutputPath, document);
                Console.Out.WriteLine("Wrote " + arguments.OutputPath + " (state=" + context.State + ", route=Native-OpenStudio).");
                return success ? (int)ExitCode.Success : (int)ExitCode.Run;
            }
        }

        private static IEnumerable<Core.OpenStudio.OpenStudioDiagnostic> EnumerateErrors(OpenStudioConversionResult conversionResult)
        {
            if (conversionResult?.Diagnostics == null)
            {
                yield break;
            }

            foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in conversionResult.Diagnostics)
            {
                if (diagnostic != null && diagnostic.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)
                {
                    yield return diagnostic;
                }
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

        private static bool TryParseArguments(string[] args, out Arguments arguments, out string error)
        {
            arguments = new Arguments();
            error = null;

            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                switch (key)
                {
                    case "--model":
                    case "--weather":
                    case "--out":
                    case "--work":
                    case "--sam-commit":
                    case "--runner-commit":
                        if (i + 1 >= args.Length)
                        {
                            error = "Missing value for " + key + ".";
                            return false;
                        }

                        string value = args[++i];
                        switch (key)
                        {
                            case "--model": arguments.ModelPath = value; break;
                            case "--weather": arguments.WeatherPath = value; break;
                            case "--out": arguments.OutputPath = value; break;
                            case "--work": arguments.WorkDirectory = value; break;
                            case "--sam-commit": arguments.SamCommit = value; break;
                            case "--runner-commit": arguments.RunnerCommit = value; break;
                        }

                        break;

                    case "-h":
                    case "--help":
                        error = Usage;
                        return false;

                    default:
                        error = "Unknown argument: " + key + ".";
                        return false;
                }
            }

            if (string.IsNullOrWhiteSpace(arguments.ModelPath) || string.IsNullOrWhiteSpace(arguments.WeatherPath) || string.IsNullOrWhiteSpace(arguments.OutputPath))
            {
                error = "--model, --weather and --out are all required.";
                return false;
            }

            return true;
        }

        private const string Usage =
            "Usage: benchmark-openstudio --model <model.json> --weather <weather.epw> --out <benchmark-OpenStudio.json>\n" +
            "                            [--work <run-directory>] [--sam-commit <sha>] [--runner-commit <sha>]\n" +
            "\n" +
            "Exit codes: 0 success, 1 unexpected error, 2 usage error, 3 input error, 4 run failure, 5 invalid document.";

        private sealed class Arguments
        {
            public string ModelPath { get; set; }

            public string WeatherPath { get; set; }

            public string OutputPath { get; set; }

            public string WorkDirectory { get; set; }

            public string SamCommit { get; set; }

            public string RunnerCommit { get; set; }
        }

        private enum ExitCode
        {
            Success = 0,
            Unexpected = 1,
            Usage = 2,
            Input = 3,
            Run = 4,
            Validation = 5,
        }
    }
}
