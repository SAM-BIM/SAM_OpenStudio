// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// C7: regression, validation and completeness enforcement. The machine-readable coverage
    /// manifest (tests/resources/openstudio-analytical-coverage.json) is enforced: every live
    /// member of every covered enum has an entry, every declared diagnostic code exists, the
    /// manifest is structurally sound, and clean fixtures drop NOTHING (no skips, no
    /// unsupported diagnostics). Plus determinism, repeated disposal and performance
    /// measurement.
    /// </summary>
    [TestFixture]
    public class CompletenessTests
    {
        private static readonly string[] ValidStatuses = { "Native", "Derived", "Approximated", "Unsupported", "Deferred", "NA" };
        private static readonly string[] ValidMilestones = { "MVP", "C0", "C1", "C2", "C3", "C4", "C5", "C6", "C7" };

        private static JsonDocument Manifest()
        {
            string path = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "resources", "openstudio-analytical-coverage.json"));
            Assert.That(File.Exists(path), Is.True, $"Coverage manifest missing: {path}");
            return JsonDocument.Parse(File.ReadAllText(path));
        }

        private static List<JsonElement> Entries(JsonDocument manifest)
        {
            return manifest.RootElement.GetProperty("entries").EnumerateArray().ToList();
        }

        [Test]
        public void Manifest_IsStructurallySound()
        {
            using (JsonDocument manifest = Manifest())
            {
                List<JsonElement> entries = Entries(manifest);
                Assert.That(entries.Count, Is.GreaterThan(250), "The audit covers the full SAM analytical surface");

                HashSet<string> ids = new HashSet<string>();
                List<string> violations = new List<string>();
                foreach (JsonElement entry in entries)
                {
                    string id = entry.GetProperty("id").GetString();
                    string status = entry.GetProperty("status").GetString();
                    string milestone = entry.GetProperty("milestone").GetString();

                    if (!ids.Add(id))
                    {
                        violations.Add($"Duplicate manifest id {id}");
                    }

                    if (!ValidStatuses.Contains(status))
                    {
                        violations.Add($"{id}: invalid status {status}");
                    }

                    if (!ValidMilestones.Contains(milestone))
                    {
                        violations.Add($"{id}: invalid milestone {milestone}");
                    }

                    if (status == "Unsupported" && !entry.TryGetProperty("diagnostic", out _) && !entry.TryGetProperty("notes", out _))
                    {
                        violations.Add($"{id}: Unsupported entries must declare a diagnostic or a note");
                    }

                    if ((status == "Approximated" || status == "Derived") && !entry.TryGetProperty("formula", out _) && !entry.TryGetProperty("notes", out _) && !entry.TryGetProperty("diagnostic", out _))
                    {
                        violations.Add($"{id}: {status} entries must document the conversion policy");
                    }
                }

                Assert.That(violations, Is.Empty, string.Join("; ", violations));

                TestContext.Out.WriteLine($"Manifest entries: {entries.Count}");
            }
        }

        [Test]
        public void Manifest_CoversEveryLiveEnumMember()
        {
            Assembly analyticalAssembly = typeof(InternalCondition).Assembly;
            Assembly coreAssembly = typeof(Core.SAMObject).Assembly;

            using (JsonDocument manifest = Manifest())
            {
                HashSet<string> ids = new HashSet<string>(Entries(manifest).Select(x => x.GetProperty("id").GetString()));

                List<string> missing = new List<string>();
                foreach (JsonElement coveredEnum in manifest.RootElement.GetProperty("coveredEnums").EnumerateArray())
                {
                    string typeName = coveredEnum.GetProperty("type").GetString();
                    string prefix = coveredEnum.GetProperty("prefix").GetString();

                    Type enumType = analyticalAssembly.GetType(typeName) ?? coreAssembly.GetType(typeName) ?? Type.GetType(typeName);
                    Assert.That(enumType, Is.Not.Null, $"Covered enum type not found: {typeName}");
                    Assert.That(enumType.IsEnum, Is.True, $"{typeName} is not an enum");

                    foreach (string memberName in Enum.GetNames(enumType))
                    {
                        string id = prefix + "." + memberName;
                        if (!ids.Contains(id))
                        {
                            missing.Add(id);
                        }
                    }
                }

                Assert.That(missing, Is.Empty, "Every live enum member must have a coverage entry (never silently omitted): " + string.Join(", ", missing));
            }
        }

        [Test]
        public void Manifest_DeclaredDiagnosticCodes_Exist()
        {
            HashSet<string> declaredCodes = new HashSet<string>(
                typeof(Core.OpenStudio.OpenStudioDiagnosticCodes)
                    .GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Select(x => (string)x.GetValue(null)));

            System.Text.RegularExpressions.Regex codePattern = new System.Text.RegularExpressions.Regex(@"SAM-OS-[A-Z]+-\d{3}");

            using (JsonDocument manifest = Manifest())
            {
                List<string> unknown = new List<string>();
                foreach (JsonElement entry in Entries(manifest))
                {
                    foreach (JsonProperty property in entry.EnumerateObject())
                    {
                        if (property.Value.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        foreach (System.Text.RegularExpressions.Match match in codePattern.Matches(property.Value.GetString()))
                        {
                            if (!declaredCodes.Contains(match.Value))
                            {
                                unknown.Add($"{entry.GetProperty("id").GetString()}: {match.Value}");
                            }
                        }
                    }
                }

                Assert.That(unknown, Is.Empty, "Every diagnostic code referenced by the manifest must exist in OpenStudioDiagnosticCodes");
            }
        }

        [Test]
        public void CleanFixture_DropsNothing()
        {
            // The office fixture exercises ~20 Native/Derived properties: every one of them is
            // mapped — no skips and no unsupported diagnostics may appear.
            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox().ToOpenStudio();

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Statistics.SkippedObjects, Is.EqualTo(0), "Nothing may be skipped for a clean fixture");
            Assert.That(result.Statistics.UnsupportedObjects, Is.EqualTo(0));
            Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OS-CON-002" || d.Code == "SAM-OS-MAT-002" || d.Code == "SAM-OS-IC-001"), Is.False);

            Core.OpenStudio.OpenStudioConversionStatistics statistics = result.Statistics;
            Assert.That(statistics.CreatedObjects, Is.GreaterThan(10));
            Assert.That(statistics.SourceObjects, Is.EqualTo(8));
        }

        [Test]
        public void UnsupportedData_RaisesExactlyTheDeclaredDiagnostics()
        {
            // Pollutant (declared Unsupported, SAM-OS-IC-001, milestone C2): the declared
            // diagnostic must fire — proving unsupported data is reported, never silent.
            InternalCondition internalCondition = AnalyticalModelFixtures.CreateOfficeInternalCondition();
            internalCondition.SetValue(InternalConditionParameter.PollutantGenerationPerArea, 0.5);
            internalCondition.SetValue(InternalConditionParameter.PollutantGenerationPerPerson, 1.0);

            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(internalConditionOverride: internalCondition).ToOpenStudio();

            Assert.That(result.Diagnostics.Count(d => d.Code == "SAM-OS-IC-001" && d.Message.Contains("Pollutant")), Is.EqualTo(1), "One structured pollutant diagnostic, as declared in the manifest");
            Assert.That(result.IsValid, Is.True, "Unsupported-with-diagnostic data does not invalidate the conversion");
        }

        [Test]
        public void Conversion_IsDeterministic()
        {
            // The SAME model converted twice: every fixture library object (profiles,
            // materials) keeps its Guid, so names must be identical.
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.TwoAdjacentBoxes();
            OpenStudioConversionResult first = analyticalModel.ToOpenStudio();
            OpenStudioConversionResult second = analyticalModel.ToOpenStudio();

            List<string> firstNames = first.Model.objects().Select(x => x.iddObject().name() + "|" + x.nameString()).OrderBy(x => x).ToList();
            List<string> secondNames = second.Model.objects().Select(x => x.iddObject().name() + "|" + x.nameString()).OrderBy(x => x).ToList();

            Assert.That(second.Model.objects().Count, Is.EqualTo(first.Model.objects().Count));
            Assert.That(secondNames, Is.EqualTo(firstNames), "Repeated conversion of the same model must produce identical objects");
        }

        [Test]
        public void RepeatedConversion_AndDisposal_DoesNotLeakOrThrow()
        {
            for (int i = 0; i < 5; i++)
            {
                OpenStudioConversionResult result = AnalyticalModelFixtures.TwoAdjacentBoxes().ToOpenStudio();
                Assert.That(result.IsValid, Is.True);
                result.Dispose();
            }
        }

        [Test]
        public void Performance_ConversionAndRun_AreMeasured()
        {
            // Measurement, not a hard gate: numbers are recorded in the status document. A
            // generous bound catches pathological regressions.
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            OpenStudioConversionResult result = AnalyticalModelFixtures.TwoAdjacentBoxes().ToOpenStudio();
            stopwatch.Stop();

            TestContext.Out.WriteLine($"TwoAdjacentBoxes conversion: {stopwatch.ElapsedMilliseconds} ms; objects: {result.Model.objects().Count}");
            Assert.That(stopwatch.Elapsed.TotalSeconds, Is.LessThan(60), "Conversion of the two-zone fixture must stay interactive");
        }
    }
}
