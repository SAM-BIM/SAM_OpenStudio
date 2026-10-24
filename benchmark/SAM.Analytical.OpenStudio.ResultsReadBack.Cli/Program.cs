// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical.OpenStudio.InputReadBack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.ResultsReadBack.Cli
{
    /// <summary>
    /// os-results-readback: the S11 OpenStudio results round-trip harness. Each step runs in its own process.
    /// <list type="bullet">
    /// <item>read --run &lt;run dir&gt; --model &lt;fixture.json&gt; [--fixture-id id]: the independent SQL/ESO reference (reference.json)</item>
    /// <item>subject --run &lt;run dir&gt; --model &lt;fixture.json&gt;: the code under test (subject.json)</item>
    /// <item>compare --run &lt;run dir&gt; --model &lt;fixture.json&gt;: the verdict (compare/compare.json, compare.md)</item>
    /// <item>negative --run &lt;run dir&gt; --model &lt;fixture.json&gt; --case &lt;id&gt; --out &lt;new dir&gt;: a mutated copy</item>
    /// </list>
    /// Exit codes follow S08/S09: 0 PASS, 10 FAIL, 11 BLOCKED technical, 64 usage, 70 infrastructure. read, subject and
    /// negative return 0 when they completed (their findings are judged by compare).
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args == null || args.Length == 0 || (args.Length - 1) % 2 != 0)
            {
                return Usage();
            }

            Dictionary<string, string> options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i + 1 < args.Length; i += 2)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal) || options.ContainsKey(args[i].Substring(2)))
                {
                    return Usage();
                }

                options[args[i].Substring(2)] = args[i + 1];
            }

            try
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "read":
                        return ReadStep.Run(Required(options, "run"), Required(options, "model"), Optional(options, "fixture-id"), Console.Out, Console.Error);
                    case "subject":
                        return SubjectStep.Run(Required(options, "run"), Required(options, "model"), Console.Out, Console.Error);
                    case "compare":
                        return CompareStep.Run(Required(options, "run"), Required(options, "model"), Console.Out, Console.Error);
                    case "negative":
                        return NegativeStep.Run(Required(options, "run"), Required(options, "model"), Required(options, "case"), Required(options, "out"), Console.Out);
                    default:
                        return Usage();
                }
            }
            catch (UsageException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return Usage();
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("infrastructure: " + exception);
                return ReadBackExit.Infrastructure;
            }
        }

        /// <summary>The frozen fixture model (the SAM inputs: expected spaces, areas, volumes).</summary>
        internal static AnalyticalModel LoadModel(string modelPath)
        {
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException("model not found", modelPath);
            }

            AnalyticalModel model = Core.Convert.ToSAM<AnalyticalModel>(modelPath)?.FirstOrDefault();
            if (model == null)
            {
                throw new InvalidDataException(modelPath + " is not a SAM AnalyticalModel");
            }

            return model;
        }

        /// <summary>The run layout, or the infrastructure problems that stop a step.</summary>
        internal static RunLayout Layout(string runDirectory, TextWriter error, out bool ok)
        {
            RunLayout layout = RunLayout.Resolve(runDirectory, out List<string> problems);
            foreach (string problem in problems)
            {
                error.WriteLine("infrastructure: " + problem);
            }

            ok = problems.Count == 0;
            return layout;
        }

        private static string Required(Dictionary<string, string> options, string name)
        {
            if (!options.TryGetValue(name, out string value) || string.IsNullOrWhiteSpace(value))
            {
                throw new UsageException("missing --" + name);
            }

            return value;
        }

        private static string Optional(Dictionary<string, string> options, string name)
        {
            return options.TryGetValue(name, out string value) ? value : null;
        }

        private static int Usage()
        {
            Console.Error.WriteLine("usage: os-results-readback read --run <run dir> --model <fixture.json> [--fixture-id id]");
            Console.Error.WriteLine("       os-results-readback subject --run <run dir> --model <fixture.json>");
            Console.Error.WriteLine("       os-results-readback compare --run <run dir> --model <fixture.json>");
            Console.Error.WriteLine("       os-results-readback negative --run <run dir> --model <fixture.json> --case <" + string.Join("|", NegativeStep.Cases.Keys) + "> --out <new dir>");
            return ReadBackExit.Usage;
        }

        private sealed class UsageException : Exception
        {
            public UsageException(string message) : base(message)
            {
            }
        }
    }
}
