// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.ResultsReadBack
{
    /// <summary>
    /// The files of one S11 run directory: b1a.json and sam.json written by benchmark-openstudio, the EnergyPlus run folder
    /// somewhere below it (exactly one eplusout.sql, with eplusout.eso and in.idf beside it), and the S11 outputs.
    /// </summary>
    public sealed class RunLayout
    {
        public const string B1aFile = "b1a.json";
        public const string SamResultsFile = "sam.json";
        public const string ReferenceFile = "reference.json";
        public const string SubjectFile = "subject.json";
        public const string NegativeFile = "negative.json";
        public const string CompareDirectory = "compare";

        public string Directory { get; private set; }
        public string B1a { get; private set; }
        public string SamResults { get; private set; }
        public string Sql { get; private set; }
        public string Eso { get; private set; }
        public string Idf { get; private set; }
        public string Reference => Path.Combine(Directory, ReferenceFile);
        public string Subject => Path.Combine(Directory, SubjectFile);
        public string Compare => Path.Combine(Directory, CompareDirectory);

        /// <summary>The layout of <paramref name="directory"/>; <paramref name="problems"/> lists every missing or ambiguous file.</summary>
        public static RunLayout Resolve(string directory, out List<string> problems)
        {
            problems = new List<string>();
            RunLayout result = new RunLayout { Directory = Path.GetFullPath(directory) };
            if (!System.IO.Directory.Exists(result.Directory))
            {
                problems.Add("run directory does not exist: " + result.Directory);
                return result;
            }

            result.B1a = Path.Combine(result.Directory, B1aFile);
            result.SamResults = Path.Combine(result.Directory, SamResultsFile);
            foreach (string file in new[] { result.B1a, result.SamResults })
            {
                if (!File.Exists(file))
                {
                    problems.Add("missing " + file);
                }
            }

            List<string> sql = System.IO.Directory.GetFiles(result.Directory, "eplusout.sql", SearchOption.AllDirectories).OrderBy(x => x, System.StringComparer.Ordinal).ToList();
            if (sql.Count != 1)
            {
                problems.Add(sql.Count + " eplusout.sql files below " + result.Directory + " (exactly one is required)");
                return result;
            }

            result.Sql = sql[0];
            string runFolder = Path.GetDirectoryName(result.Sql);
            result.Eso = Path.Combine(runFolder, "eplusout.eso");
            result.Idf = Path.Combine(runFolder, "in.idf");
            foreach (string file in new[] { result.Eso, result.Idf })
            {
                if (!File.Exists(file))
                {
                    problems.Add("missing " + file + " beside eplusout.sql");
                }
            }

            int esoCount = System.IO.Directory.GetFiles(result.Directory, "eplusout.eso", SearchOption.AllDirectories).Length;
            if (esoCount != 1)
            {
                problems.Add(esoCount + " eplusout.eso files below " + result.Directory + " (exactly one is required)");
            }

            return result;
        }
    }
}
