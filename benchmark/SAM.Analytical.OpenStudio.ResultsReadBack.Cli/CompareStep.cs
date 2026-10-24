// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical.Benchmark;
using SAM.Analytical.OpenStudio.InputReadBack;
using System.Collections.Generic;
using System.IO;

namespace SAM.Analytical.OpenStudio.ResultsReadBack.Cli
{
    /// <summary>The comparison: library code only. Its inputs must be the files the read and subject steps saw (by hash).</summary>
    internal static class CompareStep
    {
        public static int Run(string runDirectory, string modelPath, TextWriter output, TextWriter error)
        {
            RunLayout layout = Program.Layout(runDirectory, error, out bool ok);
            if (!ok)
            {
                return ReadBackExit.Infrastructure;
            }

            foreach (string file in new[] { layout.Reference, layout.Subject })
            {
                if (!File.Exists(file))
                {
                    error.WriteLine("infrastructure: missing " + file + " (run read and subject first)");
                    return ReadBackExit.Infrastructure;
                }
            }

            AnalyticalModel model = Program.LoadModel(modelPath);
            ReferenceDocument reference = S11Json.Load<ReferenceDocument>(layout.Reference);
            SubjectDocument subject = S11Json.Load<SubjectDocument>(layout.Subject);

            List<string> problems = Consistency(modelPath, layout, reference, subject);
            if (problems.Count > 0)
            {
                foreach (string problem in problems)
                {
                    error.WriteLine("infrastructure: " + problem);
                }

                return ReadBackExit.Infrastructure;
            }

            BenchmarkDocument b1a = BenchmarkSerializer.Deserialize(File.ReadAllText(layout.B1a));
            SamResults samP = SamResults.Read(layout.SamResults);
            CompareDocument compare = ResultCompare.Run(model, reference, subject, b1a, samP);
            compare.Inputs["model"] = Sha256.Of(modelPath);
            compare.Inputs["in.idf"] = reference.IdfSha256Before;
            compare.Inputs["eplusout.sql"] = reference.SqlSha256Before;
            compare.Inputs["eplusout.eso"] = reference.EsoSha256Before;
            compare.Inputs["b1a.json"] = Sha256.Of(layout.B1a);
            compare.Inputs["sam.json"] = Sha256.Of(layout.SamResults);
            compare.Inputs["reference.json"] = Sha256.Of(layout.Reference);
            compare.Inputs["subject.json"] = Sha256.Of(layout.Subject);
            compare.Inputs["readerPid"] = reference.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            compare.Inputs["subjectPid"] = subject.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            compare.Inputs["comparePid"] = compare.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            compare.Inputs["mutation"] = subject.Mutation;
            ResultReport.Save(compare, reference, layout.Compare);
            output.WriteLine("compare: " + compare.Verdict + " (" + compare.Passed + " PASS, " + compare.Failed + " FAIL, " + compare.Blocked + " BLOCKED of " + compare.Required + "); " + Path.Combine(layout.Compare, "compare.json"));
            return compare.ExitCode;
        }

        /// <summary>The reference, the subject and the files on disk must describe the same run; otherwise nothing is judged.</summary>
        internal static List<string> Consistency(string modelPath, RunLayout layout, ReferenceDocument reference, SubjectDocument subject)
        {
            List<string> problems = new List<string>();
            if (reference.Schema != ReferenceDocument.CurrentSchema) problems.Add("reference.json schema " + reference.Schema);
            if (subject.Schema != SubjectDocument.CurrentSchema) problems.Add("subject.json schema " + subject.Schema);
            string sql = Sha256.Of(layout.Sql);
            if (reference.SqlSha256Before != sql || subject.SqlSha256Before != sql || subject.SqlSha256After != sql) problems.Add("eplusout.sql is not the file the read and subject steps saw");
            if (reference.EsoSha256Before != Sha256.Of(layout.Eso)) problems.Add("eplusout.eso is not the file the read step saw");
            if (reference.IdfSha256Before != Sha256.Of(layout.Idf)) problems.Add("in.idf is not the file the read step saw");
            if (subject.B1aSha256 != Sha256.Of(layout.B1a)) problems.Add("b1a.json is not the file the subject step saw");
            if (subject.SamResultsSha256 != Sha256.Of(layout.SamResults)) problems.Add("sam.json is not the file the subject step saw");
            string model = Sha256.Of(modelPath);
            if (reference.ModelSha256 != model || subject.ModelSha256 != model) problems.Add("the model is not the one the read and subject steps used");
            if (reference.ProcessId == subject.ProcessId && reference.ProcessStartedUtc == subject.ProcessStartedUtc) problems.Add("read and subject ran in the same process");
            return problems;
        }
    }
}
