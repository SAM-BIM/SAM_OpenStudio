// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical.InputEquivalence;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SAM.Analytical.OpenStudio.InputReadBack
{
    /// <summary>One gating result that is not PASS, as reported.</summary>
    public sealed class ReportItem
    {
        public string Quantity { get; set; }
        public string Key { get; set; }
        public string Outcome { get; set; }
        public string Status { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>report.json (schema s09-readback-report/1): the fixture verdict and everything that decided it.</summary>
    public sealed class OsReport
    {
        public string Schema { get; set; } = "s09-readback-report/1";
        public string FixtureId { get; set; }
        public string RuleSet { get; set; }
        public string Status { get; set; }
        public int ExitCode { get; set; }
        public string GateStatus { get; set; }
        public string Coverage { get; set; }
        public int Required { get; set; }
        public int Results { get; set; }
        public List<string> MissingComparisons { get; set; }
        public Dictionary<string, int> CountByOutcome { get; set; }
        public List<ReportItem> NonPass { get; set; }
        public List<string> OwnerDecisionsNamed { get; set; }
        public List<string> TechnicalBlockersNamed { get; set; }
        public List<string> Consistency { get; set; }
        public List<string> FixtureCriteria { get; set; }
        public Dictionary<string, string> VentilationStates { get; set; }
        public List<SeriesSummary> Series { get; set; }
        public List<string> Supporting { get; set; }
        public Dictionary<string, string> Inputs { get; set; }

        public static OsReport From(OsComparison comparison, Dictionary<string, string> inputs)
        {
            GateReport gate = comparison.Gate;
            return new OsReport
            {
                FixtureId = comparison.FixtureId,
                RuleSet = comparison.RuleSet.ToString(),
                Status = comparison.Status.ToString(),
                ExitCode = ReadBackExit.Of(comparison.Status),
                GateStatus = gate.Status.ToString(),
                Coverage = gate.Coverage.ToString(),
                Required = comparison.Required.Count,
                Results = comparison.Results.Count,
                MissingComparisons = gate.MissingComparisons.Select(x => x.ToString()).ToList(),
                CountByOutcome = gate.CountByOutcome.OrderBy(x => x.Key.ToString()).ToDictionary(x => x.Key.ToString(), x => x.Value),
                NonPass = gate.NonPass.Select(x => new ReportItem { Quantity = x.Definition?.Id, Key = x.Input.ObjectKey, Outcome = x.Outcome.ToString(), Status = x.Status.ToString(), Reason = x.Reason }).ToList(),
                OwnerDecisionsNamed = gate.OwnerDecisionsNamed.ToList(),
                TechnicalBlockersNamed = gate.TechnicalBlockersNamed.ToList(),
                Consistency = comparison.Consistency,
                FixtureCriteria = comparison.FixtureCriteria,
                VentilationStates = comparison.VentilationStates,
                Series = comparison.Series,
                Supporting = comparison.Supporting,
                Inputs = inputs
            };
        }

        public void Save(string directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "report.json"), JsonSerializer.Serialize(this, Json.Options));
            File.WriteAllText(Path.Combine(directory, "report.txt"), Text());
        }

        public static OsReport Load(string path)
        {
            return JsonSerializer.Deserialize<OsReport>(File.ReadAllText(path), Json.Options);
        }

        public string Text()
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "S09 OpenStudio read-back: {0} -> {1} (exit {2}); rule set {3}; gate {4}; coverage {5} ({6} required, {7} results)", FixtureId, Status, ExitCode, RuleSet, GateStatus, Coverage, Required, Results));
            text.AppendLine("outcomes: " + string.Join(", ", CountByOutcome.Select(x => x.Key + " " + x.Value)));
            foreach (KeyValuePair<string, string> state in VentilationStates)
            {
                text.AppendLine("D-F2 " + state.Key + ": " + state.Value);
            }

            foreach (string item in Consistency)
            {
                text.AppendLine("CONSISTENCY FAIL: " + item);
            }

            foreach (string item in FixtureCriteria)
            {
                text.AppendLine("D-FIX: " + item);
            }

            foreach (IGrouping<string, ReportItem> group in NonPass.GroupBy(x => x.Outcome + " / " + x.Status).OrderBy(x => x.Key))
            {
                text.AppendLine("NOT PASS " + group.Key + ": " + group.Count());
                foreach (ReportItem item in group.Take(12))
                {
                    text.AppendLine("  " + item.Quantity + " " + item.Key + ": " + item.Reason);
                }

                if (group.Count() > 12)
                {
                    text.AppendLine("  ... " + (group.Count() - 12) + " more in report.json");
                }
            }

            foreach (string missing in MissingComparisons)
            {
                text.AppendLine("MISSING: " + missing);
            }

            return text.ToString();
        }
    }
}
