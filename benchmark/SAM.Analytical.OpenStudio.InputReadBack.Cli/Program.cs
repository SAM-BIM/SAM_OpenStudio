// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;

namespace SAM.Analytical.OpenStudio.InputReadBack.Cli
{
    /// <summary>
    /// os-input-readback: the S09 OpenStudio native-input read-back harness.
    /// <list type="bullet">
    /// <item>export --model &lt;frozen.json&gt; --epw &lt;file.epw&gt; --out &lt;new dir&gt; [--label x]</item>
    /// <item>read --run &lt;export dir&gt; (a separate process: reads model.osm and in.idf as text into native.json)</item>
    /// <item>compare --run &lt;export dir&gt; --fixture-id &lt;id&gt; [--rule-set interim|provisional|approved] [--out dir]</item>
    /// </list>
    /// Exit codes follow S08: 0 PASS, 10 FAIL, 11 BLOCKED technical, 12 BLOCKED owner, 13 NOT RUN, 14 accepted
    /// limitation, 64 usage, 70 infrastructure.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return Usage();
            }

            Dictionary<string, string> options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i + 1 < args.Length; i += 2)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal))
                {
                    return Usage();
                }

                options[args[i].Substring(2)] = args[i + 1];
            }

            try
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "export":
                        return Export.Run(Required(options, "model"), Required(options, "epw"), Required(options, "out"), Optional(options, "label"));
                    case "read":
                        return ReadBackRead.Run(Required(options, "run"));
                    case "negative":
                        return Negative.Run(Required(options, "run"), Required(options, "case"), Required(options, "out"));
                    case "compare":
                        return ReadBackCompare.Run(Required(options, "run"), Required(options, "fixture-id"), Optional(options, "rule-set") ?? "approved", Optional(options, "out"));
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
            Console.Error.WriteLine("usage: os-input-readback export --model <frozen.json> --epw <file.epw> --out <new dir> [--label x]");
            Console.Error.WriteLine("       os-input-readback read --run <export dir>");
            Console.Error.WriteLine("       os-input-readback compare --run <export dir> --fixture-id <id> [--rule-set interim|provisional|approved] [--out dir]");
            Console.Error.WriteLine("       os-input-readback negative --run <export dir> --case <" + string.Join("|", Negative.Cases) + "> --out <new dir>");
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
