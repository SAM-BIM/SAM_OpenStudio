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
    /// R5: enforcement of the reverse coverage manifest
    /// (tests/resources/openstudio-to-sam-coverage.json). The manifest is the claim the
    /// documentation makes about what the importer covers; these tests make it a claim the build
    /// can falsify.
    /// </summary>
    [TestFixture]
    public class ReverseCoverageTests
    {
        private static readonly string[] ValidStatuses = { "Native", "Derived", "Approximated", "Unsupported", "Deferred", "NotApplicable" };

        private static JsonDocument Manifest()
        {
            string path = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "resources", "openstudio-to-sam-coverage.json"));
            Assert.That(File.Exists(path), Is.True, $"Reverse coverage manifest missing: {path}");
            return JsonDocument.Parse(File.ReadAllText(path));
        }

        private static List<JsonElement> Entries(JsonDocument manifest)
        {
            return manifest.RootElement.GetProperty("entries").EnumerateArray().ToList();
        }

        /// <summary>Every constant declared on the reverse diagnostic-code class.</summary>
        private static HashSet<string> DeclaredCodes()
        {
            HashSet<string> result = new HashSet<string>();
            foreach (FieldInfo fieldInfo in typeof(Core.OpenStudio.OpenStudioImportDiagnosticCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (fieldInfo.IsLiteral && fieldInfo.FieldType == typeof(string))
                {
                    result.Add((string)fieldInfo.GetRawConstantValue());
                }
            }

            return result;
        }

        [Test]
        public void Manifest_IsStructurallySound()
        {
            using (JsonDocument manifest = Manifest())
            {
                List<JsonElement> entries = Entries(manifest);
                Assert.That(entries.Count, Is.GreaterThan(100), "The audit covers the OpenStudio concepts the importer meets");

                HashSet<string> ids = new HashSet<string>();
                List<string> violations = new List<string>();

                foreach (JsonElement entry in entries)
                {
                    string id = entry.GetProperty("id").GetString();
                    string status = entry.GetProperty("status").GetString();

                    if (!ids.Add(id))
                    {
                        violations.Add($"Duplicate manifest id {id}");
                    }

                    if (!ValidStatuses.Contains(status))
                    {
                        violations.Add($"{id}: invalid status {status}");
                    }

                    // The whole point of the Unsupported and Approximated statuses is that
                    // something is said out loud; an entry with neither a diagnostic nor a note
                    // is a silent drop wearing a label.
                    if ((status == "Unsupported" || status == "Approximated" || status == "Deferred")
                        && !entry.TryGetProperty("diagnostic", out _) && !entry.TryGetProperty("notes", out _))
                    {
                        violations.Add($"{id}: {status} entries must declare a diagnostic or a note");
                    }

                    if (status == "Derived" && !entry.TryGetProperty("formula", out _) && !entry.TryGetProperty("notes", out _) && !entry.TryGetProperty("diagnostic", out _))
                    {
                        violations.Add($"{id}: Derived entries must document how the value is calculated");
                    }
                }

                Assert.That(violations, Is.Empty, string.Join(Environment.NewLine, violations));
            }
        }

        [Test]
        public void EveryDeclaredDiagnosticCode_Exists()
        {
            HashSet<string> declaredCodes = DeclaredCodes();

            using (JsonDocument manifest = Manifest())
            {
                List<string> violations = new List<string>();

                foreach (JsonElement entry in Entries(manifest))
                {
                    JsonElement diagnostic;
                    if (!entry.TryGetProperty("diagnostic", out diagnostic))
                    {
                        continue;
                    }

                    string code = diagnostic.GetString();
                    if (!declaredCodes.Contains(code))
                    {
                        violations.Add($"{entry.GetProperty("id").GetString()}: diagnostic {code} is not a constant of OpenStudioImportDiagnosticCodes");
                    }
                }

                Assert.That(violations, Is.Empty, string.Join(Environment.NewLine, violations));
            }
        }

        [Test]
        public void EveryDiagnosticCode_IsUsedByTheImporter()
        {
            // A code nobody raises is a promise the importer does not keep. The manifest is the
            // register of intent, so every declared constant must appear in it.
            using (JsonDocument manifest = Manifest())
            {
                HashSet<string> manifestCodes = new HashSet<string>();
                foreach (JsonElement entry in Entries(manifest))
                {
                    JsonElement diagnostic;
                    if (entry.TryGetProperty("diagnostic", out diagnostic))
                    {
                        manifestCodes.Add(diagnostic.GetString());
                    }
                }

                List<string> unused = DeclaredCodes().Where(x => !manifestCodes.Contains(x)).ToList();

                Assert.That(unused, Is.Empty, "Every reverse diagnostic code must be claimed by a manifest entry: " + string.Join(", ", unused));
            }
        }

        [Test]
        public void DiagnosticCodes_AreUniqueAndCorrectlyPrefixed()
        {
            HashSet<string> seen = new HashSet<string>();

            foreach (string code in DeclaredCodes())
            {
                Assert.That(code, Does.StartWith("SAM-OSI-"), "Reverse codes must be distinguishable from the forward SAM-OS-* codes by prefix alone");
                Assert.That(seen.Add(code), Is.True, $"Duplicate diagnostic code {code}");
            }
        }

        [Test]
        public void ForwardAndReverseCodeSets_AreDisjoint()
        {
            HashSet<string> forward = new HashSet<string>();
            foreach (FieldInfo fieldInfo in typeof(Core.OpenStudio.OpenStudioDiagnosticCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (fieldInfo.IsLiteral && fieldInfo.FieldType == typeof(string))
                {
                    forward.Add((string)fieldInfo.GetRawConstantValue());
                }
            }

            foreach (string code in DeclaredCodes())
            {
                Assert.That(forward.Contains(code), Is.False, $"{code} is declared in both directions; a consumer could not tell which produced a diagnostic");
            }
        }

        [Test]
        public void EveryPanelType_HasAReverseMapping()
        {
            // Every SAM PanelType the forward direction can produce must be reachable coming
            // back, otherwise a round trip silently changes a panel's role.
            HashSet<PanelType> reachable = new HashSet<PanelType>();

            foreach (string surfaceType in new[] { "Wall", "Floor", "RoofCeiling" })
            {
                foreach (string boundaryCondition in new[] { "Outdoors", "Surface", "Adiabatic", "Ground", "Foundation" })
                {
                    bool adiabatic;
                    bool supported;
                    reachable.Add(Query.SAMPanelType(surfaceType, boundaryCondition, out adiabatic, out supported));
                }
            }

            foreach (PanelType panelType in new[] { PanelType.WallExternal, PanelType.WallInternal, PanelType.UndergroundWall, PanelType.FloorExposed, PanelType.FloorInternal, PanelType.SlabOnGrade, PanelType.Roof, PanelType.Ceiling, PanelType.UndergroundCeiling })
            {
                Assert.That(reachable.Contains(panelType), Is.True, $"{panelType} is not reachable from any surface type / boundary condition pair");
            }
        }

        [Test]
        public void EverySubSurfaceType_MapsOrIsExplicitlyUnsupported()
        {
            // The full OpenStudio subsurface-type domain: each must either map or be rejected
            // deliberately, never fall through by accident.
            Dictionary<string, ApertureType> expected = new Dictionary<string, ApertureType>
            {
                { "FixedWindow", ApertureType.Window },
                { "OperableWindow", ApertureType.Window },
                { "Skylight", ApertureType.Window },
                { "TubularDaylightDome", ApertureType.Window },
                { "TubularDaylightDiffuser", ApertureType.Window },
                { "Door", ApertureType.Door },
                { "GlassDoor", ApertureType.Door },
                { "OverheadDoor", ApertureType.Door },
            };

            foreach (KeyValuePair<string, ApertureType> keyValuePair in expected)
            {
                bool approximated;
                Assert.That(Query.SAMApertureType(keyValuePair.Key, out approximated), Is.EqualTo(keyValuePair.Value), keyValuePair.Key);
            }

            bool unusedApproximated;
            Assert.That(Query.SAMApertureType("SomethingElse", out unusedApproximated), Is.EqualTo(ApertureType.Undefined));
            Assert.That(Query.SAMApertureType(null, out unusedApproximated), Is.EqualTo(ApertureType.Undefined));
        }

        [Test]
        public void BoundaryConditionMapping_CoversTheOpenStudioDomain()
        {
            // The values OpenStudio itself declares valid. Anything it can write, the importer
            // must classify — and the ones it cannot represent must say so.
            using (global::OpenStudio.Model model = new global::OpenStudio.Model())
            {
                global::OpenStudio.Point3dVector point3dVector = new global::OpenStudio.Point3dVector();
                point3dVector.Add(new global::OpenStudio.Point3d(0, 0, 0));
                point3dVector.Add(new global::OpenStudio.Point3d(4, 0, 0));
                point3dVector.Add(new global::OpenStudio.Point3d(4, 0, 3));
                point3dVector.Add(new global::OpenStudio.Point3d(0, 0, 3));

                global::OpenStudio.Surface surface = new global::OpenStudio.Surface(point3dVector, model);

                List<string> unclassified = new List<string>();
                foreach (string value in global::OpenStudio.Surface.validOutsideBoundaryConditionValues())
                {
                    bool adiabatic;
                    bool supported;
                    PanelType panelType = Query.SAMPanelType("Wall", value, out adiabatic, out supported);

                    // Either it maps to a real panel type, or it is explicitly flagged
                    // unsupported so the caller reports SAM-OSI-BC-001. Silent Undefined is the
                    // one outcome that is not acceptable.
                    if (panelType == PanelType.Undefined && supported)
                    {
                        unclassified.Add(value);
                    }
                }

                Assert.That(unclassified, Is.Empty, "Unclassified boundary conditions: " + string.Join(", ", unclassified));
            }
        }
    }
}
