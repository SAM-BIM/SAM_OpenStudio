// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SAM.Analytical.Benchmark;

namespace SAM.Analytical.OpenStudio.Benchmark.Tests
{
    /// <summary>
    /// Conversion diagnostics must reach the neutral benchmark document on EVERY run. Previously they
    /// were recorded only when the EnergyPlus run failed, and through an Error-only filter, so a
    /// SUCCESSFUL document reported <c>"warnings": []</c> even when the route had explicitly named its
    /// approximations (hourly humidity collapsed to a constant dew point, hourly solar replaced by
    /// ASHRAEClearSky, a substituted barometric pressure, the weather/design-day basis). A document that
    /// hides those presents a cross-engine comparison as cleaner than it was.
    /// <para>
    /// These tests drive the mapping helpers directly with fabricated diagnostics, so they are fully
    /// offline: no OpenStudio install, no EnergyPlus run, no conversion.
    /// </para>
    /// </summary>
    [TestFixture]
    public class DiagnosticPropagationTests
    {
        private static Core.OpenStudio.OpenStudioDiagnostic Diagnostic(string code, Core.OpenStudio.OpenStudioDiagnosticSeverity severity, string message)
        {
            return new Core.OpenStudio.OpenStudioDiagnostic(code, severity, message);
        }

        private static Core.OpenStudio.OpenStudioDiagnostic Warning(string message)
        {
            return Diagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, message);
        }

        /// <summary>A design-day/weather-basis Information diagnostic: benchmark-relevant, becomes a note.</summary>
        private static Core.OpenStudio.OpenStudioDiagnostic BasisInformation(string message)
        {
            return Diagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, message);
        }

        /// <summary>A per-object modelling Information diagnostic: routine detail, must not be copied.</summary>
        private static Core.OpenStudio.OpenStudioDiagnostic RoutineInformation(string message)
        {
            return Diagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, message);
        }

        private static OpenStudioBenchmarkContext SuccessfulContext()
        {
            AnalyticalModel model = BenchmarkFixture.SingleSpaceModel();
            return BenchmarkFixture.Context(model, BenchmarkFixture.ResultSet());
        }

        [Test]
        public void SuccessfulRun_WarningReachesWarnings()
        {
            OpenStudioBenchmarkContext context = SuccessfulContext();
            Assert.That(context.State, Is.EqualTo(RunState.Success), "The regression this guards is specifically about SUCCESSFUL runs");

            Modify.ApplyConversionDiagnostics(context, new List<Core.OpenStudio.OpenStudioDiagnostic>
            {
                Warning("the hourly solar profile was approximated by the ASHRAEClearSky model")
            });

            Assert.That(context.Warnings.Count, Is.EqualTo(1), "A successful run must not discard converter warnings: " + string.Join(" | ", context.Warnings));
            Assert.That(context.Warnings[0], Does.Contain("ASHRAEClearSky"));
            Assert.That(context.Warnings[0], Does.Contain(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue), "The stable code travels with the message");
        }

        [Test]
        public void FailedRun_RecordsBothWarningAndError()
        {
            OpenStudioBenchmarkContext context = SuccessfulContext();
            context.State = RunState.Failure;

            Modify.ApplyConversionDiagnostics(context, new List<Core.OpenStudio.OpenStudioDiagnostic>
            {
                Warning("a warning that must survive a failed run"),
                Diagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "the OpenStudio CLI failed")
            });

            Assert.That(context.Warnings.Count, Is.EqualTo(2), "Both severities are recorded: " + string.Join(" | ", context.Warnings));
            Assert.That(context.Warnings.Any(x => x.Contains("must survive")), Is.True);
            Assert.That(context.Warnings.Any(x => x.Contains("CLI failed")), Is.True);
        }

        [Test]
        public void BenchmarkRelevantInformation_ReachesNotes()
        {
            OpenStudioBenchmarkContext context = SuccessfulContext();

            Modify.ApplyConversionDiagnostics(context, new List<Core.OpenStudio.OpenStudioDiagnostic>
            {
                BasisInformation("Design-day source: AnalyticalModel heating/cooling design days"),
                Diagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ResultExtractionLimitation, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "peaks assume hourly reporting"),
                Diagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.SimulationSettingsUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "the requested shading calculation method was substituted")
            });

            Assert.That(context.Notes.Count, Is.EqualTo(3), "Weather/design-day basis, result-extraction limits and substituted settings all describe the simulation basis: " + string.Join(" | ", context.Notes));
            Assert.That(context.Notes.Any(x => x.Contains("Design-day source")), Is.True);
            Assert.That(context.Warnings, Is.Empty, "Information diagnostics are notes, never warnings");
        }

        [Test]
        public void RoutineInformation_IsNotPropagated()
        {
            OpenStudioBenchmarkContext context = SuccessfulContext();

            Modify.ApplyConversionDiagnostics(context, new List<Core.OpenStudio.OpenStudioDiagnostic>
            {
                RoutineInformation("Frame data on an opaque door is not converted"),
                Diagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryVerticesCleaned, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "duplicate vertices removed"),
                Diagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.InternalConditionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "equipment view coefficient is not converted")
            });

            Assert.That(context.Notes, Is.Empty, "Per-object modelling detail is not the simulation basis and must not become notes: " + string.Join(" | ", context.Notes));
            Assert.That(context.Warnings, Is.Empty);
        }

        [Test]
        public void RoutineInformationCodes_StillPropagateTheirWarnings()
        {
            // The allow-list narrows NOTES only. A warning is material whatever family raised it.
            OpenStudioBenchmarkContext context = SuccessfulContext();

            Modify.ApplyConversionDiagnostics(context, new List<Core.OpenStudio.OpenStudioDiagnostic>
            {
                Diagnostic(Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "an aperture shade is not converted")
            });

            Assert.That(context.Warnings.Count, Is.EqualTo(1), "Non-allow-listed codes still deliver their warnings");
        }

        [Test]
        public void DuplicateWarningsAndNotes_AppearOnce()
        {
            OpenStudioBenchmarkContext context = SuccessfulContext();

            Modify.ApplyConversionDiagnostics(context, new List<Core.OpenStudio.OpenStudioDiagnostic>
            {
                Warning("the same approximation reported twice"),
                Warning("the same approximation reported twice"),
                BasisInformation("Ground temperatures taken from the EPW GROUND TEMPERATURES header"),
                BasisInformation("Ground temperatures taken from the EPW GROUND TEMPERATURES header")
            });

            Assert.That(context.Warnings.Count, Is.EqualTo(1), "Exact duplicates collapse: " + string.Join(" | ", context.Warnings));
            Assert.That(context.Notes.Count, Is.EqualTo(1), "Exact duplicates collapse: " + string.Join(" | ", context.Notes));
        }

        [Test]
        public void WarningsAndNotes_AreOrdinalSorted()
        {
            OpenStudioBenchmarkContext context = SuccessfulContext();

            Modify.ApplyConversionDiagnostics(context, new List<Core.OpenStudio.OpenStudioDiagnostic>
            {
                Warning("zulu"),
                Warning("alpha"),
                Warning("Zulu"),
                Warning("Alpha"),
                BasisInformation("second"),
                BasisInformation("first")
            });

            Assert.That(context.Warnings, Is.EqualTo(context.Warnings.OrderBy(x => x, System.StringComparer.Ordinal).ToList()), "Warnings are ordinal-sorted: " + string.Join(" | ", context.Warnings));
            Assert.That(context.Notes, Is.EqualTo(context.Notes.OrderBy(x => x, System.StringComparer.Ordinal).ToList()), "Notes are ordinal-sorted: " + string.Join(" | ", context.Notes));
        }

        [Test]
        public void SameDiagnosticsInAnyOrder_ProduceIdenticalArrays()
        {
            var diagnostics = new List<Core.OpenStudio.OpenStudioDiagnostic>
            {
                Warning("beta approximation"),
                Warning("alpha approximation"),
                BasisInformation("Annual weather source: AnalyticalModel WeatherData"),
                BasisInformation("Design-day source: explicit DDY")
            };

            OpenStudioBenchmarkContext first = SuccessfulContext();
            Modify.ApplyConversionDiagnostics(first, diagnostics);

            OpenStudioBenchmarkContext second = SuccessfulContext();
            Modify.ApplyConversionDiagnostics(second, Enumerable.Reverse(diagnostics).ToList());

            Assert.That(second.Warnings, Is.EqualTo(first.Warnings), "Byte-stable regardless of diagnostic order");
            Assert.That(second.Notes, Is.EqualTo(first.Notes), "Byte-stable regardless of diagnostic order");
        }

        [Test]
        public void ExistingProducerNotes_ArePreserved()
        {
            OpenStudioBenchmarkContext context = SuccessfulContext();
            context.Notes.Add("EnergyPlus version was unavailable from the loaded OpenStudio SDK at run time.");
            context.Notes.Add("The OpenStudio/EnergyPlus run did not complete successfully; measurements are unavailable.");
            context.Warnings.Add("A producer-generated warning.");

            Modify.ApplyConversionDiagnostics(context, new List<Core.OpenStudio.OpenStudioDiagnostic>
            {
                BasisInformation("Design-day source: explicit DDY (Boston.ddy)"),
                Warning("a converter warning")
            });

            Assert.That(context.Notes.Any(x => x.Contains("EnergyPlus version was unavailable")), Is.True, "Producer notes survive propagation: " + string.Join(" | ", context.Notes));
            Assert.That(context.Notes.Any(x => x.Contains("did not complete successfully")), Is.True);
            Assert.That(context.Notes.Any(x => x.Contains("Design-day source")), Is.True, "Propagated and producer notes are combined");
            Assert.That(context.Warnings.Any(x => x.Contains("A producer-generated warning.")), Is.True);
            Assert.That(context.Notes, Is.EqualTo(context.Notes.OrderBy(x => x, System.StringComparer.Ordinal).ToList()), "The combined list is ordered too");
        }

        [Test]
        public void NullAndEmptyDiagnostics_LeaveValidEmptyArrays()
        {
            OpenStudioBenchmarkContext nullDiagnostics = SuccessfulContext();
            Modify.ApplyConversionDiagnostics(nullDiagnostics, null);
            Assert.That(nullDiagnostics.Warnings, Is.Empty);
            Assert.That(nullDiagnostics.Notes, Is.Empty);

            AnalyticalModel model = BenchmarkFixture.SingleSpaceModel();
            OpenStudioBenchmarkContext emptyDiagnostics = BenchmarkFixture.Context(model, BenchmarkFixture.ResultSet());
            Modify.ApplyConversionDiagnostics(emptyDiagnostics, new List<Core.OpenStudio.OpenStudioDiagnostic>());
            Assert.That(emptyDiagnostics.Warnings, Is.Empty);
            Assert.That(emptyDiagnostics.Notes, Is.Empty);

            // A null entry and a whitespace-only message are dropped rather than serialized: the schema
            // rejects empty list entries.
            OpenStudioBenchmarkContext dirty = SuccessfulContext();
            dirty.Notes.Add("   ");
            dirty.Warnings.Add(null);
            Modify.ApplyConversionDiagnostics(dirty, new List<Core.OpenStudio.OpenStudioDiagnostic> { null });
            Assert.That(dirty.Warnings, Is.Empty, "Null diagnostics and null entries are skipped");
            Assert.That(dirty.Notes, Is.Empty, "Whitespace-only entries are skipped");

            BenchmarkDocument document = model.ToBenchmark(emptyDiagnostics);
            Assert.That(document.Provenance.Warnings, Is.Not.Null.And.Empty, "Empty arrays still serialize as valid empty arrays");
            Assert.That(document.Provenance.Notes, Is.Not.Null.And.Empty);
            Assert.That(BenchmarkValidator.Validate(document).IsValid, Is.True, "An empty-diagnostic document stays schema-valid");
        }

        [Test]
        public void SuccessfulDocument_CarriesNonEmptyWarningsAndNotes()
        {
            // Integration-level mapping check: the propagated diagnostics survive into a SUCCESSFUL,
            // schema-valid benchmark document — the exact case that previously emitted empty arrays.
            AnalyticalModel model = BenchmarkFixture.SingleSpaceModel();
            OpenStudioBenchmarkContext context = BenchmarkFixture.Context(model, BenchmarkFixture.ResultSet());
            Modify.ApplyConversionDiagnostics(context, new List<Core.OpenStudio.OpenStudioDiagnostic>
            {
                Warning("the hourly humidity profile was approximated as a constant dew point"),
                BasisInformation("Annual weather source: AnalyticalModel WeatherData")
            });

            BenchmarkDocument document = model.ToBenchmark(context);

            Assert.That(document.Provenance.State, Is.EqualTo(RunState.Success));
            Assert.That(document.Provenance.Warnings.Any(x => x.Contains("constant dew point")), Is.True, "A successful document can now carry converter warnings: " + string.Join(" | ", document.Provenance.Warnings));
            Assert.That(document.Provenance.Notes.Any(x => x.Contains("Annual weather source")), Is.True);
            Assert.That(BenchmarkValidator.Validate(document).IsValid, Is.True, "Propagated diagnostics keep the document schema-valid: " + string.Join(" | ", BenchmarkValidator.Validate(document).Issues.Select(x => x.Message)));
        }
    }
}
