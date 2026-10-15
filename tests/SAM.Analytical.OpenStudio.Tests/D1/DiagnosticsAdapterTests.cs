// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Core.OpenStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// D1: additive SAM.Core.Diagnostics adapters for OpenStudioDiagnostic (unified Diagnostics, Phase 4A).
    /// The legacy diagnostics, their text and every engineering result must be unchanged; the adapters only read.
    /// None of these tests needs the OpenStudio CLI or EnergyPlus.
    /// </summary>
    [TestFixture]
    public class DiagnosticsAdapterTests
    {
        private static readonly Guid spaceGuid = new Guid("3f2a9c1e-5b7d-4e08-a1c6-0d9e8f7a6b51");

        private static Core.OpenStudio.OpenStudioDiagnostic Create(string code, Core.OpenStudio.OpenStudioDiagnosticSeverity severity, string message = "message", Guid? guid = null, string type = null, string name = null)
        {
            return new Core.OpenStudio.OpenStudioDiagnostic(code, severity, message, guid, type, name);
        }

        /// <summary>One diagnostic for every code of both code classes at every severity, so the table cannot hide a case.</summary>
        private static List<Core.OpenStudio.OpenStudioDiagnostic> AllCodesAllSeverities()
        {
            List<string> codes = new List<string>();
            foreach (Type type in new[] { typeof(Core.OpenStudio.OpenStudioDiagnosticCodes), typeof(Core.OpenStudio.OpenStudioImportDiagnosticCodes) })
            {
                codes.AddRange(type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(x => x.IsLiteral).Select(x => (string)x.GetRawConstantValue()));
            }

            codes.Add("SAM-FUTURE-999");

            List<Core.OpenStudio.OpenStudioDiagnostic> result = new List<Core.OpenStudio.OpenStudioDiagnostic>();
            foreach (string code in codes)
            {
                foreach (Core.OpenStudio.OpenStudioDiagnosticSeverity severity in Enum.GetValues(typeof(Core.OpenStudio.OpenStudioDiagnosticSeverity)))
                {
                    result.Add(Create(code, severity, code + " " + severity));
                }
            }

            return result;
        }

        private static string NormalisedOsm(global::OpenStudio.Model model, string name)
        {
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, name + ".osm");
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            Assert.That(model.save(global::OpenStudio.OpenStudioUtilitiesCore.toPath(path), true), Is.True);

            // OpenStudio assigns a random handle to every object; compare everything else.
            return Regex.Replace(File.ReadAllText(path), @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", "{handle}");
        }

        [Test]
        public void Severity_MapsOneToOne_AndUndefinedIsError()
        {
            Assert.That(Core.OpenStudio.OpenStudioDiagnosticSeverity.Information.ToDiagnosticSeverity(), Is.EqualTo(Core.DiagnosticSeverity.Info));
            Assert.That(Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning.ToDiagnosticSeverity(), Is.EqualTo(Core.DiagnosticSeverity.Warning));
            Assert.That(Core.OpenStudio.OpenStudioDiagnosticSeverity.Error.ToDiagnosticSeverity(), Is.EqualTo(Core.DiagnosticSeverity.Error));
            Assert.That(((Core.OpenStudio.OpenStudioDiagnosticSeverity)99).ToDiagnosticSeverity(), Is.EqualTo(Core.DiagnosticSeverity.Error), "An undefined severity must never be downgraded");

            foreach (Core.OpenStudio.OpenStudioDiagnosticSeverity severity in Enum.GetValues(typeof(Core.OpenStudio.OpenStudioDiagnosticSeverity)))
            {
                Assert.That((int)severity.ToDiagnosticSeverity(), Is.EqualTo((int)severity), "The numeric values of the two enums agree");
            }
        }

        [Test]
        public void ToDiagnostic_PreservesCodeMessageIdentityAndIndex()
        {
            Core.OpenStudio.OpenStudioDiagnostic source = Create(Core.OpenStudio.OpenStudioDiagnosticCodes.ScheduleMissingProfile, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "profile 'Office' missing.", spaceGuid, "Space", "SAM_Space_Office_3f2a9c1e");
            string before = source.ToString();

            Core.Diagnostic diagnostic = source.ToDiagnostic(7);

            Assert.That(diagnostic.Code, Is.EqualTo("SAM-OS-SCH-001"));
            Assert.That(diagnostic.Message, Is.EqualTo("profile 'Office' missing."), "The message is carried verbatim (no capitalisation, no prefix)");
            Assert.That(diagnostic.Severity, Is.EqualTo(Core.DiagnosticSeverity.Error));
            Assert.That(diagnostic.Subject, Is.EqualTo("Space " + spaceGuid));
            Assert.That(diagnostic.SourceIndex, Is.EqualTo(7));
            Assert.That(source.ToString(), Is.EqualTo(before), "The original diagnostic is not modified");
        }

        [Test]
        public void ToDiagnostic_SubjectFallsBackToOpenStudioNameThenNull()
        {
            Assert.That(Create("C", Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "m", null, null, "SAM_Space_X_1").ToDiagnostic().Subject, Is.EqualTo("SAM_Space_X_1"));
            Assert.That(Create("C", Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "m").ToDiagnostic().Subject, Is.Null);
            Assert.That(Create("C", Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "m", null, "Panel").ToDiagnostic().Subject, Is.EqualTo("Panel"));
            Assert.That(((Core.OpenStudio.OpenStudioDiagnostic)null).ToDiagnostic(), Is.Null);
        }

        [Test]
        public void Detail_WarningsAndErrorsAreAlwaysSummary_InformationFollowsTheCodeTable()
        {
            foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in AllCodesAllSeverities())
            {
                if (diagnostic.Severity != Core.OpenStudio.OpenStudioDiagnosticSeverity.Information)
                {
                    Assert.That(diagnostic.GetDiagnosticDetail(), Is.EqualTo(Core.DiagnosticDetail.Summary), diagnostic.Code);
                }
            }

            Assert.That(Create(Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information).GetDiagnosticDetail(), Is.EqualTo(Core.DiagnosticDetail.Summary));
            Assert.That(Create(Core.OpenStudio.OpenStudioImportDiagnosticCodes.OswWorkflowExecuted, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information).GetDiagnosticDetail(), Is.EqualTo(Core.DiagnosticDetail.Summary));
            Assert.That(Create(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information).GetDiagnosticDetail(), Is.EqualTo(Core.DiagnosticDetail.Standard));
            Assert.That(Create(Core.OpenStudio.OpenStudioDiagnosticCodes.InternalConditionUnsupportedParameter, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information).GetDiagnosticDetail(), Is.EqualTo(Core.DiagnosticDetail.Detailed));
            Assert.That(Create(Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryVerticesCleaned, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information).GetDiagnosticDetail(), Is.EqualTo(Core.DiagnosticDetail.Detailed));
            Assert.That(Create("SAM-FUTURE-999", Core.OpenStudio.OpenStudioDiagnosticSeverity.Information).GetDiagnosticDetail(), Is.EqualTo(Core.DiagnosticDetail.Standard), "Unknown and future codes are Standard");
            Assert.That(((Core.OpenStudio.OpenStudioDiagnostic)null).GetDiagnosticDetail(), Is.EqualTo(Core.DiagnosticDetail.Summary));
        }

        [Test]
        public void Filter_KeepsEveryWarningAndError_InOrder_AtEveryLevel()
        {
            List<Core.OpenStudio.OpenStudioDiagnostic> source = AllCodesAllSeverities();
            List<Core.OpenStudio.OpenStudioDiagnostic> expected = source.Where(x => x.Severity != Core.OpenStudio.OpenStudioDiagnosticSeverity.Information).ToList();

            foreach (Core.DiagnosticDetail detail in new[] { Core.DiagnosticDetail.Summary, Core.DiagnosticDetail.Standard, Core.DiagnosticDetail.Detailed })
            {
                List<Core.OpenStudio.OpenStudioDiagnosticRecord> kept = source.CreateDiagnosticRecords().FilterDiagnosticRecords(detail);
                List<Core.OpenStudio.OpenStudioDiagnostic> keptHigh = kept.Where(x => x.Source.Severity != Core.OpenStudio.OpenStudioDiagnosticSeverity.Information).Select(x => x.Source).ToList();

                Assert.That(keptHigh.Count, Is.EqualTo(expected.Count), detail.ToString());
                for (int i = 0; i < expected.Count; i++)
                {
                    Assert.That(keptHigh[i], Is.SameAs(expected[i]), "Warnings and Errors are the same instances in the same order at " + detail);
                }

                Assert.That(kept.Where(x => x.Source.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Information).All(x => x.Diagnostic.Detail <= detail), Is.True, "No Information above the level is kept at " + detail);
            }
        }

        [Test]
        public void Filter_InformationGrowsMonotonically_AndDetailedEqualsTheLegacyList()
        {
            List<Core.OpenStudio.OpenStudioDiagnostic> source = AllCodesAllSeverities();

            List<string> summary = source.FormatDiagnostics(Core.DiagnosticDetail.Summary);
            List<string> standard = source.FormatDiagnostics(Core.DiagnosticDetail.Standard);
            List<string> detailed = source.FormatDiagnostics(Core.DiagnosticDetail.Detailed);

            Assert.That(summary.Count, Is.LessThan(standard.Count));
            Assert.That(standard.Count, Is.LessThan(detailed.Count));

            // Each level is an order-preserving subsequence of the next.
            Assert.That(IsSubsequence(summary, standard), Is.True);
            Assert.That(IsSubsequence(standard, detailed), Is.True);

            List<string> legacy = source.ConvertAll(x => x.ToString());
            Assert.That(detailed, Is.EqualTo(legacy), "Detailed is exactly the legacy list, text and order");
            Assert.That(source.FormatDiagnostics(), Is.EqualTo(legacy), "The default level is Detailed");
        }

        private static bool IsSubsequence(List<string> sub, List<string> full)
        {
            int j = 0;
            foreach (string line in full)
            {
                if (j < sub.Count && sub[j] == line)
                {
                    j++;
                }
            }

            return j == sub.Count;
        }

        [Test]
        public void Severity_IsNeverInferredFromText()
        {
            Core.OpenStudio.OpenStudioDiagnostic information = Create(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "Error: fatal Warning: failed");
            Core.OpenStudio.OpenStudioDiagnostic warning = Create(Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "all good, information only");

            Assert.That(information.ToDiagnostic().Severity, Is.EqualTo(Core.DiagnosticSeverity.Info));
            Assert.That(warning.ToDiagnostic().Severity, Is.EqualTo(Core.DiagnosticSeverity.Warning));

            List<string> summary = new[] { information, warning }.FormatDiagnostics(Core.DiagnosticDetail.Summary);
            Assert.That(summary, Is.EqualTo(new[] { warning.ToString() }), "A Warning is kept at Summary and an Information with alarming text is not");
        }

        [Test]
        public void NullAndEmptyInputs_AreHandled()
        {
            Assert.That(((IEnumerable<Core.OpenStudio.OpenStudioDiagnostic>)null).FormatDiagnostics(), Is.Empty);
            Assert.That(((IEnumerable<Core.OpenStudio.OpenStudioDiagnostic>)null).ToDiagnostics().Count, Is.EqualTo(0));
            Assert.That(new List<Core.OpenStudio.OpenStudioDiagnostic>().ToDiagnostics().HasErrors, Is.False);
            Assert.That(((IEnumerable<Core.OpenStudio.OpenStudioDiagnosticRecord>)null).FilterDiagnosticRecords(Core.DiagnosticDetail.Detailed), Is.Empty);

            List<Core.OpenStudio.OpenStudioDiagnostic> withNull = new List<Core.OpenStudio.OpenStudioDiagnostic> { null, Create("C", Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "e"), null };
            Core.Diagnostics diagnostics = withNull.ToDiagnostics();
            Assert.That(diagnostics.Count, Is.EqualTo(1));
            Assert.That(diagnostics.First().SourceIndex, Is.EqualTo(1), "The source index is the position in the original list");
            Assert.That(withNull.FormatDiagnostics().Count, Is.EqualTo(1));
        }

        [Test]
        public void ToDiagnostics_CollectsAtTheRequestedLevel_AndCanBeRefilteredFromDetailed()
        {
            List<Core.OpenStudio.OpenStudioDiagnostic> source = AllCodesAllSeverities();

            Core.Diagnostics detailed = source.ToDiagnostics();
            Assert.That(detailed.Detail, Is.EqualTo(Core.DiagnosticDetail.Detailed));
            Assert.That(detailed.Count, Is.EqualTo(source.Count));
            Assert.That(detailed.HasErrors, Is.True);

            Core.Diagnostics summary = source.ToDiagnostics(Core.DiagnosticDetail.Summary);
            Assert.That(summary.Count, Is.EqualTo(source.FormatDiagnostics(Core.DiagnosticDetail.Summary).Count));
            Assert.That(detailed.Filter(Core.DiagnosticDetail.Summary).Count, Is.EqualTo(summary.Count), "Detailed re-filtered at Summary equals collecting at Summary");
            Assert.That(summary.Count(x => x.Severity != Core.DiagnosticSeverity.Info), Is.EqualTo(detailed.Count(x => x.Severity != Core.DiagnosticSeverity.Info)), "Warnings and Errors are never dropped");
        }

        [Test]
        public void LegacyText_IsUnchanged_Golden()
        {
            Assert.That(Create("SAM-OS-RUN-001", Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "Boom").ToString(), Is.EqualTo("[SAM-OS-RUN-001] Error: Boom"));
            Assert.That(Create("SAM-OS-SCH-001", Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "Profile missing", spaceGuid, "Space").ToString(), Is.EqualTo("[SAM-OS-SCH-001] Warning: Profile missing (SAM Space " + spaceGuid + ")"));
            Assert.That(Create("SAM-OS-SCH-001", Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "Deferred", spaceGuid, "Space", "SAM_Space_Office_3f2a9c1e").ToString(), Is.EqualTo("[SAM-OS-SCH-001] Information: Deferred (SAM Space " + spaceGuid + ") → SAM_Space_Office_3f2a9c1e"));
            Assert.That(Create("C", Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "m", null, null, "OS Name").ToString(), Is.EqualTo("[C] Information: m → OS Name"));

            Core.OpenStudio.OpenStudioDiagnostic diagnostic = Create("SAM-OS-RUN-001", Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, "Boom");
            Assert.That(new Core.OpenStudio.OpenStudioDiagnosticRecord(diagnostic, diagnostic.ToDiagnostic()).Text, Is.EqualTo(diagnostic.ToString()));
            Assert.That(new Core.OpenStudio.OpenStudioDiagnosticRecord(null, null).Text, Is.EqualTo(string.Empty));
        }

        [Test]
        public void PublicApi_ExistingMembersAreUnchanged()
        {
            Type diagnosticType = typeof(Core.OpenStudio.OpenStudioDiagnostic);
            Assert.That(diagnosticType.GetConstructor(new[] { typeof(string), typeof(Core.OpenStudio.OpenStudioDiagnosticSeverity), typeof(string), typeof(Guid?), typeof(string), typeof(string) }), Is.Not.Null);
            foreach (string property in new[] { "Code", "Severity", "Message", "SamGuid", "SamObjectType", "OpenStudioObjectName" })
            {
                Assert.That(diagnosticType.GetProperty(property), Is.Not.Null, property);
                Assert.That(diagnosticType.GetProperty(property).CanWrite, Is.False, property + " stays read-only");
            }

            Assert.That(typeof(Core.OpenStudio.OpenStudioDiagnosticSeverity).GetEnumNames(), Is.EqualTo(new[] { "Information", "Warning", "Error" }));
            Assert.That((int)Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, Is.EqualTo(0));
            Assert.That((int)Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, Is.EqualTo(1));
            Assert.That((int)Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, Is.EqualTo(2));

            Assert.That(typeof(OpenStudioConversionResult).GetProperty("Diagnostics").PropertyType, Is.EqualTo(typeof(IReadOnlyList<Core.OpenStudio.OpenStudioDiagnostic>)));
            Assert.That(typeof(OpenStudioImportResult).GetProperty("Diagnostics").PropertyType, Is.EqualTo(typeof(IReadOnlyList<Core.OpenStudio.OpenStudioDiagnostic>)));
            Assert.That(typeof(OpenStudioConversionContext).GetProperty("Diagnostics").PropertyType, Is.EqualTo(typeof(IList<Core.OpenStudio.OpenStudioDiagnostic>)));
            Assert.That(typeof(OpenStudioImportContext).GetProperty("Diagnostics").PropertyType, Is.EqualTo(typeof(IList<Core.OpenStudio.OpenStudioDiagnostic>)));

            Assert.That(typeof(OpenStudioConversionContext).GetMethod("AddDiagnostic", new[] { typeof(string), typeof(Core.OpenStudio.OpenStudioDiagnosticSeverity), typeof(string), typeof(Core.SAMObject), typeof(string) }), Is.Not.Null);
            Assert.That(typeof(OpenStudioImportContext).GetMethod("AddDiagnostic", new[] { typeof(string), typeof(Core.OpenStudio.OpenStudioDiagnosticSeverity), typeof(string), typeof(global::OpenStudio.ModelObject), typeof(Core.SAMObject) }), Is.Not.Null);
            Assert.That(typeof(OpenStudioImportContext).GetMethod("AddDiagnostic", new[] { typeof(string), typeof(Core.OpenStudio.OpenStudioDiagnosticSeverity), typeof(string), typeof(string), typeof(Core.SAMObject) }), Is.Not.Null);

            Assert.That(typeof(Core.OpenStudio.OpenStudioDiagnosticCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Length, Is.EqualTo(17), "No forward code added or removed");
            Assert.That(typeof(Core.OpenStudio.OpenStudioImportDiagnosticCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Length, Is.EqualTo(31), "No import code added or removed");
        }

        [Test]
        public void Conversion_Diagnostics_AreUnchangedByTheAdapters_AndMatchTheStatistics()
        {
            Func<AnalyticalModel>[] models =
            {
                () => AnalyticalModelFixtures.SingleBox(),
                () => AnalyticalModelFixtures.SingleBox(withProfiles: false),
                () => AnalyticalModelFixtures.TwoAdjacentBoxes(),
                () => AnalyticalModelFixtures.DegeneratePanelBox(),
            };

            foreach (Func<AnalyticalModel> create in models)
            {
                using (OpenStudioConversionResult result = create().ToOpenStudio())
                {
                    List<string> legacy = result.Diagnostics.Select(x => x.ToString()).ToList();
                    bool isValid = result.IsValid;
                    int information = result.Statistics.InformationCount;
                    int warnings = result.Statistics.WarningCount;
                    int errors = result.Statistics.ErrorCount;

                    foreach (Core.DiagnosticDetail detail in new[] { Core.DiagnosticDetail.Summary, Core.DiagnosticDetail.Standard, Core.DiagnosticDetail.Detailed })
                    {
                        result.GetDiagnostics(detail);
                        result.Diagnostics.FormatDiagnostics(detail);
                    }

                    Assert.That(result.Diagnostics.Select(x => x.ToString()).ToList(), Is.EqualTo(legacy), "Reading through the adapters does not change the diagnostics");
                    Assert.That(result.IsValid, Is.EqualTo(isValid));
                    Assert.That(result.Statistics.InformationCount, Is.EqualTo(information));
                    Assert.That(result.Statistics.WarningCount, Is.EqualTo(warnings));
                    Assert.That(result.Statistics.ErrorCount, Is.EqualTo(errors));

                    Core.Diagnostics structured = result.GetDiagnostics();
                    Assert.That(structured.Count, Is.EqualTo(result.Diagnostics.Count));
                    Assert.That(structured.Count(x => x.Severity == Core.DiagnosticSeverity.Info), Is.EqualTo(information));
                    Assert.That(structured.Count(x => x.Severity == Core.DiagnosticSeverity.Warning), Is.EqualTo(warnings));
                    Assert.That(structured.Count(x => x.Severity == Core.DiagnosticSeverity.Error), Is.EqualTo(errors));
                    Assert.That(structured.HasErrors, Is.EqualTo(errors > 0));
                    Assert.That(structured.HasErrors, Is.EqualTo(!isValid), "IsValid and the structured Errors agree");
                    Assert.That(result.Diagnostics.FormatDiagnostics(), Is.EqualTo(legacy), "Detailed equals the legacy text");
                    Assert.That(structured.Select(x => x.Code).ToList(), Is.EqualTo(result.Diagnostics.Select(x => x.Code).ToList()));
                    Assert.That(structured.Select(x => x.Message).ToList(), Is.EqualTo(result.Diagnostics.Select(x => x.Message).ToList()));
                }
            }
        }

        [Test]
        public void Conversion_DegenerateModel_ErrorIsPreservedAtEveryLevel()
        {
            using (OpenStudioConversionResult result = AnalyticalModelFixtures.DegeneratePanelBox().ToOpenStudio())
            {
                Assert.That(result.IsValid, Is.False);
                Assert.That(result.Diagnostics.Any(x => x.Code == Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryInvalidBoundary && x.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error), Is.True);

                foreach (Core.DiagnosticDetail detail in new[] { Core.DiagnosticDetail.Summary, Core.DiagnosticDetail.Standard, Core.DiagnosticDetail.Detailed })
                {
                    Core.Diagnostics structured = result.GetDiagnostics(detail);
                    Assert.That(structured.HasErrors, Is.True, detail.ToString());
                    Assert.That(structured.Any(x => x.Code == "SAM-OS-GEO-001" && x.Severity == Core.DiagnosticSeverity.Error), Is.True, detail.ToString());
                    Assert.That(result.Diagnostics.FormatDiagnostics(detail).Any(x => x.StartsWith("[SAM-OS-GEO-001] Error:")), Is.True, detail.ToString());
                }
            }
        }

        [Test]
        public void ExportedModel_IsIdentical_WhetherOrNotTheAdaptersRun()
        {
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.TwoAdjacentBoxes();

            string withoutAdapters;
            using (OpenStudioConversionResult result = analyticalModel.ToOpenStudio())
            {
                withoutAdapters = NormalisedOsm(result.Model, "d1_model_plain");
            }

            string withAdapters;
            using (OpenStudioConversionResult result = analyticalModel.ToOpenStudio())
            {
                foreach (Core.DiagnosticDetail detail in new[] { Core.DiagnosticDetail.Summary, Core.DiagnosticDetail.Standard, Core.DiagnosticDetail.Detailed })
                {
                    result.GetDiagnostics(detail);
                    result.Diagnostics.CreateDiagnosticRecords().FilterDiagnosticRecords(detail);
                }

                withAdapters = NormalisedOsm(result.Model, "d1_model_adapters");
            }

            Assert.That(withoutAdapters, Does.Contain("OS:Space,"));
            Assert.That(withAdapters, Is.EqualTo(withoutAdapters), "The exported OSM is identical apart from OpenStudio's random handles");
        }

        /// <summary>Needs the OpenStudio CLI and EnergyPlus; skipped (not failed) when the CLI is absent.</summary>
        [Test]
        [Category("Simulation")]
        public void RealRun_DiagnosticsAndLoads_AreUnchangedByTheAdapters()
        {
            Assume.That(Core.OpenStudio.Query.OpenStudioCliPath(), Is.Not.Null, "OpenStudio CLI not installed");

            string epwPath = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "resources", "weather", "USA_MA_Boston-Logan.Intl.AP.725090_TMYx.2004-2018.epw"));
            Assert.That(File.Exists(epwPath), Is.True);

            AnalyticalModel analyticalModel = AnalyticalModelFixtures.SingleBox();

            OpenStudioConversionResult Run(string name)
            {
                string outputDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, name);
                if (Directory.Exists(outputDirectory))
                {
                    Directory.Delete(outputDirectory, true);
                }

                return analyticalModel.ToOpenStudio(epwPath, outputDirectory);
            }

            OpenStudioConversionResult first = Run("d1_real_run_1");
            List<string> legacy = first.Diagnostics.Select(x => x.ToString()).ToList();
            double heating = first.Loads.TotalHeating;
            double cooling = first.Loads.TotalCooling;
            Assert.That(first.RunResult.Success, Is.True);

            foreach (Core.DiagnosticDetail detail in new[] { Core.DiagnosticDetail.Summary, Core.DiagnosticDetail.Standard, Core.DiagnosticDetail.Detailed })
            {
                Core.Diagnostics structured = first.GetDiagnostics(detail);
                Assert.That(structured.Count(x => x.Severity != Core.DiagnosticSeverity.Info), Is.EqualTo(first.Statistics.WarningCount + first.Statistics.ErrorCount), "Every Warning and Error of the real run is kept at " + detail);
                Assert.That(structured.HasErrors, Is.EqualTo(!first.IsValid));
            }

            Assert.That(first.Diagnostics.FormatDiagnostics(), Is.EqualTo(legacy));
            Assert.That(first.Diagnostics.Select(x => x.ToString()).ToList(), Is.EqualTo(legacy));
            Assert.That(first.Loads.TotalHeating, Is.EqualTo(heating));
            Assert.That(first.Loads.TotalCooling, Is.EqualTo(cooling));

            // The same model run again after the adapters ran: same diagnostics text and the same loads.
            OpenStudioConversionResult second = Run("d1_real_run_2");
            Assert.That(second.Diagnostics.Select(x => x.ToString()).ToList(), Is.EqualTo(legacy));
            Assert.That(second.Loads.TotalHeating, Is.EqualTo(heating));
            Assert.That(second.Loads.TotalCooling, Is.EqualTo(cooling));
        }

        [Test]
        public void MissingAdjacencyCluster_IsAnErrorAndNothingIsConverted()
        {
            AnalyticalModel model = new AnalyticalModel("No cluster", "no adjacency cluster", null, null, null, AnalyticalModelFixtures.CreateMaterialLibrary(), AnalyticalModelFixtures.CreateProfileLibrary());

            using (OpenStudioConversionResult result = model.ToOpenStudio())
            {
                Assert.That(result.IsValid, Is.False);
                Core.Diagnostics diagnostics = result.GetDiagnostics(Core.DiagnosticDetail.Summary);
                Assert.That(diagnostics.HasErrors, Is.True);
                Assert.That(diagnostics.Any(x => x.Code == Core.OpenStudio.OpenStudioDiagnosticCodes.AdjacencyMissingSurface && x.Severity == Core.DiagnosticSeverity.Error), Is.True);
            }
        }

        [Test]
        public void ExportRefusal_ConversionErrorsPreventTheRun_AsAWarningAndKeepTheError()
        {
            string epwPath = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "resources", "weather", "USA_MA_Boston-Logan.Intl.AP.725090_TMYx.2004-2018.epw"));
            Assert.That(File.Exists(epwPath), Is.True);

            string outputDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "d1_refusal");
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, true);
            }

            using (OpenStudioConversionResult result = AnalyticalModelFixtures.DegeneratePanelBox().ToOpenStudio(epwPath, outputDirectory))
            {
                Assert.That(result.RunResult, Is.Null, "The CLI must not run when the conversion reported errors");

                Core.Diagnostics diagnostics = result.GetDiagnostics(Core.DiagnosticDetail.Summary);
                Assert.That(diagnostics.HasErrors, Is.True);
                Core.Diagnostic notExecuted = diagnostics.FirstOrDefault(x => x.Code == Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed);
                Assert.That(notExecuted, Is.Not.Null);
                Assert.That(notExecuted.Severity, Is.EqualTo(Core.DiagnosticSeverity.Warning), "The refusal is reported at the severity the producer chose");
                Assert.That(notExecuted.Message, Does.StartWith("Simulation not executed"));
            }
        }

        [Test]
        public void MissingOutputDirectory_IsAStructuredError_WithoutTheCli()
        {
            OpenStudioConversionContext context = new OpenStudioConversionContext(null, new global::OpenStudio.Model());

            using (OpenStudioConversionResult result = OpenStudioSimulationRunner.Run(context, null, null))
            {
                Assert.That(result.IsValid, Is.False);
                Core.Diagnostic diagnostic = result.GetDiagnostics(Core.DiagnosticDetail.Summary).Single();
                Assert.That(diagnostic.Severity, Is.EqualTo(Core.DiagnosticSeverity.Error));
                Assert.That(diagnostic.Code, Is.EqualTo("SAM-OS-RUN-001"));
                Assert.That(diagnostic.Message, Is.EqualTo("No output directory was provided for the simulation run"));
            }
        }

        [Test]
        public void StaticRun_MissingInput_IsAStructuredError_WithoutTheCli()
        {
            List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = new List<Core.OpenStudio.OpenStudioDiagnostic>();
            string missing = Path.Combine(TestContext.CurrentContext.WorkDirectory, "d1_does_not_exist.osm");

            Core.OpenStudio.OpenStudioRunResult runResult = OpenStudioSimulationRunner.Run(missing, null, TestContext.CurrentContext.WorkDirectory, null, out OpenStudioLoadSummary loads, diagnostics);

            Assert.That(runResult.Success, Is.False);
            Assert.That(loads, Is.Null);
            Core.Diagnostic diagnostic = diagnostics.ToDiagnostics(Core.DiagnosticDetail.Summary).Single();
            Assert.That(diagnostic.Severity, Is.EqualTo(Core.DiagnosticSeverity.Error));
            Assert.That(diagnostic.Code, Is.EqualTo("SAM-OS-RUN-001"));
            Assert.That(diagnostic.Message, Does.StartWith("Input file not found"));
            Assert.That(diagnostics.FormatDiagnostics(), Is.EqualTo(diagnostics.Select(x => x.ToString()).ToList()));
        }

        [Test]
        public void Import_MissingPath_IsAStructuredError()
        {
            string missing = Path.Combine(TestContext.CurrentContext.WorkDirectory, "d1_nothing_here.osm");

            OpenStudioImportResult result = Convert.ToSAM(missing);

            Assert.That(result.Successful, Is.False);
            Assert.That(result.IsValid, Is.False);

            Core.Diagnostics diagnostics = result.GetDiagnostics(Core.DiagnosticDetail.Summary);
            Assert.That(diagnostics.HasErrors, Is.True);
            Assert.That(diagnostics.Any(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.InputPathInvalid && x.Severity == Core.DiagnosticSeverity.Error), Is.True);
            Assert.That(result.Diagnostics.FormatDiagnostics(), Is.EqualTo(result.Diagnostics.Select(x => x.ToString()).ToList()));
            Assert.That(result.Statistics.ErrorCount, Is.EqualTo(diagnostics.Count(x => x.Severity == Core.DiagnosticSeverity.Error)));
        }

        [Test]
        public void Import_RoundTrip_DiagnosticsAreUnchangedByTheAdapters()
        {
            string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "d1_import_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(directory);
            try
            {
                string osmPath = Path.Combine(directory, "box.osm");
                using (OpenStudioConversionResult conversion = AnalyticalModelFixtures.SingleBox().ToOpenStudio())
                {
                    Assert.That(conversion.Model.save(global::OpenStudio.OpenStudioUtilitiesCore.toPath(osmPath), true), Is.True);
                }

                OpenStudioImportResult result = Convert.ToSAM(osmPath);
                List<string> legacy = result.Diagnostics.Select(x => x.ToString()).ToList();

                Core.Diagnostics structured = result.GetDiagnostics();

                Assert.That(result.Diagnostics.Select(x => x.ToString()).ToList(), Is.EqualTo(legacy));
                Assert.That(structured.Count, Is.EqualTo(result.Diagnostics.Count));
                Assert.That(structured.Count(x => x.Severity == Core.DiagnosticSeverity.Warning), Is.EqualTo(result.Statistics.WarningCount));
                Assert.That(structured.Count(x => x.Severity == Core.DiagnosticSeverity.Info), Is.EqualTo(result.Statistics.InformationCount));
                Assert.That(structured.HasErrors, Is.EqualTo(!result.IsValid && result.AnalyticalModel != null));
                Assert.That(result.Diagnostics.FormatDiagnostics(), Is.EqualTo(legacy));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }
    }
}
