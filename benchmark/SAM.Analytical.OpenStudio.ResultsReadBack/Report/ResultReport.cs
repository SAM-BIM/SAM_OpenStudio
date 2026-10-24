// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SAM.Analytical.OpenStudio.ResultsReadBack
{
    /// <summary>The human-readable S11 report (compare.md) beside compare.json; byte-stable for the same inputs.</summary>
    public static class ResultReport
    {
        private static readonly CultureInfo ci = CultureInfo.InvariantCulture;

        public static string Markdown(CompareDocument compare, ReferenceDocument reference)
        {
            StringBuilder text = new StringBuilder();
            text.Append("# S11 OpenStudio results round-trip: ").Append(compare.FixtureId).Append("\n\n");
            text.Append("Verdict: **").Append(compare.Verdict).Append("** (exit ").Append(compare.ExitCode.ToString(ci)).Append("): ")
                .Append(compare.Passed.ToString(ci)).Append(" PASS, ").Append(compare.Failed.ToString(ci)).Append(" FAIL, ")
                .Append(compare.Blocked.ToString(ci)).Append(" BLOCKED of ").Append(compare.Required.ToString(ci)).Append(" required rows; ")
                .Append(compare.Informational.ToString(ci)).Append(" informational.\n\n");
            foreach (string note in compare.Notes)
            {
                text.Append("> ").Append(note).Append("\n\n");
            }

            text.Append("Run period: ").Append(reference.RunPeriod).Append(" (").Append(reference.CalendarHours.ToString(ci)).Append(" hours). ");
            text.Append("SQL ").Append(reference.SqlSha256Before).Append(", ESO ").Append(reference.EsoSha256Before).Append(", in.idf ").Append(reference.IdfSha256Before).Append(".\n\n");
            text.Append("F3: ").Append(compare.F3.Detail).Append(".\n\n");

            if (compare.GlobalBlocks.Count > 0)
            {
                text.Append("## Global blocks\n\n");
                foreach (string block in compare.GlobalBlocks)
                {
                    text.Append("- ").Append(block).Append('\n');
                }

                text.Append('\n');
            }

            text.Append("## Checks\n\n| Check | Scope | Status | Detail |\n|---|---|---|---|\n");
            foreach (ReferenceCheck check in reference.Checks)
            {
                text.Append("| ").Append(check.Id).Append(" | ").Append(Cell(check.Scope)).Append(" | ").Append(check.Status).Append(" | ").Append(Cell(check.Detail)).Append(" |\n");
            }

            text.Append("\n## Completeness (per file, against the RunPeriod calendar)\n\n| File | Variable | Key | Entries | Found | Missing | Duplicated | Extra | Complete |\n|---|---|---|---|---|---|---|---|---|\n");
            foreach (CompletenessResult row in reference.Completeness)
            {
                text.Append("| ").Append(row.Channel).Append(" | ").Append(row.Variable).Append(" | ").Append(Cell(row.Key)).Append(" | ")
                    .Append(row.DictionaryEntries.ToString(ci)).Append(" | ").Append(row.Found.ToString(ci)).Append(" | ").Append(row.Missing.ToString(ci)).Append(" | ")
                    .Append(row.Duplicated.ToString(ci)).Append(" | ").Append(row.Extra.ToString(ci)).Append(" | ").Append(row.Complete ? "yes" : "NO").Append(" |\n");
            }

            text.Append("\n## Rows\n\n| Row | Class | Status | Reference | Subject | Delta | Margin | Reason |\n|---|---|---|---|---|---|---|---|\n");
            foreach (ResultRow row in compare.Rows)
            {
                text.Append("| ").Append(row.Id).Append(" | ").Append(row.Class).Append(" | ").Append(row.Status).Append(" | ")
                    .Append(Number(row.Reference)).Append(" | ").Append(Number(row.Subject)).Append(" | ").Append(Number(row.Delta)).Append(" | ")
                    .Append(row.Margin.HasValue ? row.Margin.Value.ToString("G6", ci) : row.SensibleHourDiffers.HasValue ? (row.SensibleHourDiffers.Value ? "hour differs" : "same hour") : string.Empty)
                    .Append(" | ").Append(Cell(row.Reason)).Append(" |\n");
            }

            return text.ToString();
        }

        public static void Save(CompareDocument compare, ReferenceDocument reference, string directory)
        {
            Directory.CreateDirectory(directory);
            S11Json.Save(compare, Path.Combine(directory, "compare.json"));
            File.WriteAllText(Path.Combine(directory, "compare.md"), Markdown(compare, reference), new UTF8Encoding(false));
        }

        private static string Number(double? value) => value.HasValue ? value.Value.ToString("R", ci) : string.Empty;

        private static string Cell(string value) => (value ?? string.Empty).Replace("|", "\\|").Replace("\n", " ");
    }
}
