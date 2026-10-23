// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical.InputEquivalence;
using System;
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

    /// <summary>
    /// The S07 §5.2 (amendment U1) OUT-OF-ENGINE-SCOPE rows of a fixture: informational, never gating, each with both values,
    /// the difference and the scope reason. <see cref="Differing"/> counts the rows outside the row-3a tolerance (the pre-U1
    /// single-sided differences), <see cref="Equal"/> the rows whose values agree.
    /// </summary>
    public sealed class OutOfEngineScopeSummary
    {
        public string Amendment { get; set; }
        public int Count { get; set; }
        public int Differing { get; set; }
        public int Equal { get; set; }
        public Dictionary<string, int> CountByScope { get; set; }
        public Dictionary<string, int> CountByProperty { get; set; }
        public List<OutOfEngineScopeRow> Rows { get; set; }
    }

    /// <summary>
    /// report.json (schema s09-readback-report/3; /2 added Unsupported, /3 adds OUT-OF-ENGINE-SCOPE): the fixture verdict and
    /// everything that decided it. <see cref="CountByOutcome"/> counts every result once, an S07 §5.2 row as OutOfEngineScope
    /// (never as its numeric MATCH or OUT-OF-TOLERANCE); <see cref="NonPass"/> holds only gating results.
    /// </summary>
    public sealed class OsReport
    {
        public string Schema { get; set; } = "s09-readback-report/3";
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

        /// <summary>The gating results that are not PASS (the count of <see cref="NonPass"/>). An informational row is never one.</summary>
        public int GatedNonPass { get; set; }

        public OutOfEngineScopeSummary OutOfEngineScope { get; set; }
        public List<string> OwnerDecisionsNamed { get; set; }
        public List<string> TechnicalBlockersNamed { get; set; }
        public List<string> Consistency { get; set; }
        public List<string> Unsupported { get; set; }
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
                CountByOutcome = comparison.Results.GroupBy(comparison.ReportedOutcome).OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Count()),
                NonPass = gate.NonPass.Select(x => new ReportItem { Quantity = x.Definition?.Id, Key = x.Input.ObjectKey, Outcome = x.Outcome.ToString(), Status = x.Status.ToString(), Reason = x.Reason }).ToList(),
                GatedNonPass = gate.NonPass.Count,
                OutOfEngineScope = new OutOfEngineScopeSummary
                {
                    Amendment = OsComparison.OpaqueFaceScope,
                    Count = comparison.OutOfEngineScope.Count,
                    Differing = comparison.OutOfEngineScope.Count(x => x.Differs),
                    Equal = comparison.OutOfEngineScope.Count(x => !x.Differs),
                    CountByScope = comparison.OutOfEngineScope.GroupBy(x => Scope(x.ScopeReason)).OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Count()),
                    CountByProperty = comparison.OutOfEngineScope.GroupBy(x => x.Property).OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Count()),
                    Rows = comparison.OutOfEngineScope.ToList()
                },
                OwnerDecisionsNamed = gate.OwnerDecisionsNamed.ToList(),
                TechnicalBlockersNamed = gate.TechnicalBlockersNamed.ToList(),
                Consistency = comparison.Consistency,
                Unsupported = comparison.Unsupported,
                FixtureCriteria = comparison.FixtureCriteria,
                VentilationStates = comparison.VentilationStates,
                Series = comparison.Series,
                Supporting = comparison.Supporting,
                Inputs = inputs
            };
        }

        /// <summary>The scope of a reason: its text before the parenthesised detail.</summary>
        private static string Scope(string reason)
        {
            int detail = reason?.IndexOf(" (", StringComparison.Ordinal) ?? -1;
            return detail > 0 ? reason.Substring(0, detail) : reason;
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
            text.AppendLine(string.Format(CultureInfo.InvariantCulture, "S09 OpenStudio read-back: {0} -> {1} (exit {2}); rule set {3}; gate {4}; coverage {5} ({6} required, {7} results); gated non-PASS {8}; out-of-engine-scope {9} (informational, not gated)", FixtureId, Status, ExitCode, RuleSet, GateStatus, Coverage, Required, Results, GatedNonPass, OutOfEngineScope?.Count ?? 0));
            text.AppendLine("outcomes: " + string.Join(", ", CountByOutcome.Select(x => x.Key + " " + x.Value)));
            foreach (KeyValuePair<string, string> state in VentilationStates)
            {
                text.AppendLine("D-F2 " + state.Key + ": " + state.Value);
            }

            foreach (string item in Consistency)
            {
                text.AppendLine("CONSISTENCY FAIL: " + item);
            }

            foreach (string item in Unsupported ?? new List<string>())
            {
                text.AppendLine("UNSUPPORTED (blocked technical): " + item);
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

            if (OutOfEngineScope != null && OutOfEngineScope.Count > 0)
            {
                text.AppendLine(string.Format(CultureInfo.InvariantCulture, "OUT-OF-ENGINE-SCOPE (informational, not gated; {0}): {1} row(s), {2} differing, {3} equal; by scope: {4}; by property: {5}", OutOfEngineScope.Amendment, OutOfEngineScope.Count, OutOfEngineScope.Differing, OutOfEngineScope.Equal,
                    string.Join(", ", OutOfEngineScope.CountByScope.Select(x => x.Key + " " + x.Value)), string.Join(", ", OutOfEngineScope.CountByProperty.Select(x => x.Key + " " + x.Value))));
                foreach (OutOfEngineScopeRow row in OutOfEngineScope.Rows.Take(12))
                {
                    text.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0} {1}: SAM {2:R}, native {3:R}, |d| {4:R} ({5}); {6}; SAM '{7}', native '{8}'", row.Quantity, row.Key, row.SamValue, row.NativeValue, row.Delta, row.NumericVerdict, row.ScopeReason, row.SamMaterial, row.NativeObject));
                }

                if (OutOfEngineScope.Rows.Count > 12)
                {
                    text.AppendLine("  ... " + (OutOfEngineScope.Rows.Count - 12) + " more in report.json");
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
