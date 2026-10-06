// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;

namespace SAM.Analytical.OpenStudio.Benchmark
{
    public static partial class Modify
    {
        /// <summary>
        /// The <see cref="Core.OpenStudio.OpenStudioDiagnosticCodes"/> families whose
        /// <see cref="Core.OpenStudio.OpenStudioDiagnosticSeverity.Information"/> diagnostics describe the
        /// SIMULATION BASIS or a material transformation of it, and therefore belong in the benchmark
        /// document's notes. An allow-list of stable codes is used rather than message matching, so a
        /// reworded diagnostic cannot silently change what the benchmark records.
        /// <list type="bullet">
        /// <item><c>WeatherDataIssue</c> (SAM-OS-RUN-002) — names the annual weather source, the
        /// design-day source, the number/identity of imported design days, and the ground-temperature
        /// source. These decide what the run actually simulated and sized against.</item>
        /// <item><c>ResultExtractionLimitation</c> (SAM-OS-RUN-003) — qualifies the measurements the
        /// benchmark itself publishes (e.g. peaks assuming hourly reporting under a non-hourly
        /// reporting frequency).</item>
        /// <item><c>SimulationSettingsUnsupported</c> (SAM-OS-SET-001) — a rejected or substituted
        /// simulation setting changes the basis the numbers were produced on.</item>
        /// </list>
        /// Everything else is deliberately excluded. Geometry, adjacency, material, construction,
        /// schedule, internal-condition and HVAC codes report per-object modelling detail: there can be
        /// hundreds per model, they say nothing about the run basis, and copying them would turn the
        /// notes array into conversion chatter. Their Warning and Error variants still reach
        /// <see cref="OpenStudioBenchmarkContext.Warnings"/>, so nothing that matters is dropped.
        /// </summary>
        private static readonly HashSet<string> BenchmarkRelevantInformationCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue,
            Core.OpenStudio.OpenStudioDiagnosticCodes.ResultExtractionLimitation,
            Core.OpenStudio.OpenStudioDiagnosticCodes.SimulationSettingsUnsupported
        };

        /// <summary>
        /// Records the conversion's own diagnostics on the benchmark context, then makes both arrays
        /// deterministic. Called for EVERY run, successful or not: the neutral schema provides
        /// <c>warnings</c>/<c>notes</c> precisely so a reader can see what the route approximated, and a
        /// successful run previously discarded all of it — a document claiming success while silently
        /// omitting, say, that the hourly solar profile was replaced by ASHRAEClearSky overstates what
        /// was compared.
        /// </summary>
        /// <remarks>
        /// Severity mapping: <c>Warning</c> and <c>Error</c> both become warnings (an Error that did not
        /// stop the run is still something the reader must see). Only the allow-listed
        /// <see cref="BenchmarkRelevantInformationCodes"/> become notes. Producer-generated entries
        /// already present on the context are preserved and normalized together with the propagated
        /// ones. Message text is taken verbatim from the diagnostic's own
        /// <see cref="Core.OpenStudio.OpenStudioDiagnostic.ToString"/>, never rewritten.
        /// </remarks>
        /// <param name="openStudioBenchmarkContext">Context whose Warnings/Notes are appended and normalized.</param>
        /// <param name="diagnostics">Conversion diagnostics; null or empty is valid and leaves empty arrays.</param>
        internal static void ApplyConversionDiagnostics(OpenStudioBenchmarkContext openStudioBenchmarkContext, IEnumerable<Core.OpenStudio.OpenStudioDiagnostic> diagnostics)
        {
            if (openStudioBenchmarkContext == null)
            {
                return;
            }

            openStudioBenchmarkContext.Warnings.AddRange(EnumerateWarningDiagnostics(diagnostics));
            openStudioBenchmarkContext.Notes.AddRange(EnumerateBenchmarkRelevantInformationDiagnostics(diagnostics));

            NormalizeDiagnosticMessages(openStudioBenchmarkContext.Warnings);
            NormalizeDiagnosticMessages(openStudioBenchmarkContext.Notes);
        }

        /// <summary>
        /// The formatted <c>Warning</c> and <c>Error</c> diagnostics, in source order. Errors are
        /// included because a diagnostic severe enough to be an Error is never less relevant than a
        /// Warning, and a run can complete with Errors recorded.
        /// </summary>
        internal static IEnumerable<string> EnumerateWarningDiagnostics(IEnumerable<Core.OpenStudio.OpenStudioDiagnostic> diagnostics)
        {
            if (diagnostics == null)
            {
                yield break;
            }

            foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in diagnostics)
            {
                if (diagnostic == null)
                {
                    continue;
                }

                if (diagnostic.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning
                    || diagnostic.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)
                {
                    yield return diagnostic.ToString();
                }
            }
        }

        /// <summary>
        /// The formatted <c>Information</c> diagnostics whose code is in
        /// <see cref="BenchmarkRelevantInformationCodes"/>, in source order. Routine progress,
        /// object-count and per-object modelling messages are not benchmark-relevant and are skipped.
        /// </summary>
        internal static IEnumerable<string> EnumerateBenchmarkRelevantInformationDiagnostics(IEnumerable<Core.OpenStudio.OpenStudioDiagnostic> diagnostics)
        {
            if (diagnostics == null)
            {
                yield break;
            }

            foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in diagnostics)
            {
                if (diagnostic == null || diagnostic.Severity != Core.OpenStudio.OpenStudioDiagnosticSeverity.Information)
                {
                    continue;
                }

                if (diagnostic.Code != null && BenchmarkRelevantInformationCodes.Contains(diagnostic.Code))
                {
                    yield return diagnostic.ToString();
                }
            }
        }

        /// <summary>
        /// Makes a warning/note list byte-stable in place: drops null and whitespace-only entries, drops
        /// exact ordinal duplicates (the same conversion decision reported twice must not appear twice),
        /// and sorts with <see cref="StringComparer.Ordinal"/>. Entries are trimmed of surrounding
        /// whitespace only — the diagnostic text itself is never rewritten.
        /// </summary>
        internal static void NormalizeDiagnosticMessages(List<string> messages)
        {
            if (messages == null)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var normalized = new List<string>(messages.Count);
            foreach (string message in messages)
            {
                if (string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                string trimmed = message.Trim();
                if (seen.Add(trimmed))
                {
                    normalized.Add(trimmed);
                }
            }

            normalized.Sort(StringComparer.Ordinal);

            messages.Clear();
            messages.AddRange(normalized);
        }
    }
}
