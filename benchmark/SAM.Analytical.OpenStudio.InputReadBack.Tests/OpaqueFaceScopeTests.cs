// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Analytical.InputEquivalence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EQ = SAM.Analytical.InputEquivalence.Query;

namespace SAM.Analytical.OpenStudio.InputReadBack.Tests
{
    /// <summary>
    /// S07 §5.2 (amendment U1, SAM#218 b06dbe8c): row-3a opaque optics on faces EnergyPlus 25.1 does not use are
    /// OUT-OF-ENGINE-SCOPE, informational and not gated, but only when the face's role is established independently and
    /// both values are valid and comparable. Every used face, every thermal layer property and every invalid, unreadable
    /// or undeterminable row stays gated, and an informational row never hides a defect elsewhere. Each case runs the CLI
    /// (export, fresh-process read and compare) and checks its report and exit code.
    /// </summary>
    public partial class ReadBackTests
    {
        private static readonly string[] OpticalProperties = { "externalSolarReflectance", "internalSolarReflectance", "externalLightReflectance", "internalLightReflectance", "externalEmissivity", "internalEmissivity" };

        /// <summary>
        /// Every row-3a optical key on a face EnergyPlus does not use, with its scope, derived here from the frozen fixture
        /// alone (GetSpaces() order, adjacency, the adiabatic flag and the panel type), never from the writer or from S09.
        /// SAM lists a construction inside → outside from its first space; EnergyPlus lists it outside first.
        /// </summary>
        private static Dictionary<string, string> UnusedFaces(AnalyticalModel model)
        {
            AdjacencyCluster cluster = model.AdjacencyCluster;
            List<Space> spaces = cluster.GetSpaces();
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Panel panel in cluster.GetPanels())
            {
                List<Space> related = cluster.GetRelatedObjects<Space>(panel).OrderBy(x => spaces.FindIndex(s => s.Guid == x.Guid)).ToList();
                bool adiabatic = panel.TryGetValue(PanelParameter.Adiabatic, out bool flag) && flag;
                bool ground = panel.PanelType == PanelType.SlabOnGrade || panel.PanelType == PanelType.UndergroundSlab || panel.PanelType == PanelType.UndergroundWall || panel.PanelType == PanelType.UndergroundCeiling;
                bool interzone = related.Count == 2;
                int count = panel.Construction.ConstructionLayers.Count;
                for (int i = 0; i < related.Count; i++)
                {
                    string side = "panel:" + panel.Guid.ToString("D") + "@space:" + related[i].Guid.ToString("D");
                    for (int k = 1; k <= count; k++)
                    {
                        foreach (string face in new[] { "internal", "external" })
                        {
                            bool towardsZone = (face == "internal") == (i == 0);
                            bool zoneFace = k == count && towardsZone;
                            bool outerFace = k == 1 && !towardsZone;
                            if (zoneFace || (outerFace && !interzone && !adiabatic && !ground))
                            {
                                continue;
                            }

                            string scope = !outerFace ? "buried layer face" : interzone ? "interzone surface outer face" : adiabatic ? "adiabatic surface outer face" : "ground surface outer face";
                            foreach (string optic in Optics)
                            {
                                result.Add(side + "#layer" + k + "." + face + optic, scope);
                            }
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>A SAM opaque material's authored face value, from the frozen model.</summary>
        private static double SamFace(AnalyticalModel model, string materialName, string property)
        {
            Core.OpaqueMaterial material = model.MaterialLibrary.GetMaterials().OfType<Core.OpaqueMaterial>().Single(x => x.Name == materialName);
            OpaqueMaterialParameter parameter = (OpaqueMaterialParameter)Enum.Parse(typeof(OpaqueMaterialParameter), char.ToUpperInvariant(property[0]) + property.Substring(1));
            Assert.That(material.TryGetValue(parameter, out double value), Is.True, materialName + " " + property);
            return value;
        }

        private static string OtherFace(string property)
        {
            return property.StartsWith("internal", StringComparison.Ordinal) ? "external" + property.Substring("internal".Length) : "internal" + property.Substring("external".Length);
        }

        private static string SideKey(Panel panel, Space space) => "panel:" + panel.Guid.ToString("D") + "@space:" + space.Guid.ToString("D");

        private static AnalyticalModel WithMaterial(string fixtureId, string materialName, Action<Core.OpaqueMaterial> change)
        {
            AnalyticalModel model = TestSupport.Load(fixtureId);
            Core.MaterialLibrary materialLibrary = model.MaterialLibrary;
            Core.OpaqueMaterial material = materialLibrary.GetMaterials().OfType<Core.OpaqueMaterial>().Single(x => x.Name == materialName);
            change(material);
            materialLibrary.Add(material);
            return new AnalyticalModel(model, model.AdjacencyCluster, materialLibrary, model.ProfileLibrary);
        }

        private static AnalyticalModel WithAdiabaticPanel(string fixtureId, Func<AdjacencyCluster, Panel> select, out Panel selected)
        {
            AnalyticalModel model = TestSupport.Load(fixtureId);
            AdjacencyCluster cluster = model.AdjacencyCluster;
            Panel panel = Analytical.Create.Panel(select(cluster));
            Assert.That(panel.SetValue(PanelParameter.Adiabatic, true), Is.True);
            Assert.That(cluster.AddObject(panel), Is.True);
            selected = panel;
            return new AnalyticalModel(model, cluster);
        }

        private static string Text(string run) => File.ReadAllText(Path.Combine(run, "compare-approved", "report.txt"));

        // ---- both fixtures ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Every unused-face row, and only those, is OUT-OF-ENGINE-SCOPE, with its authored value, its native value, the
        /// difference, side, layer, scope and the amendment. The rows that differ are exactly the single-sided difference
        /// of revisions 8-11 (the SAM face differs from its other face and the native holds that other face): 69 / 138,
        /// still visible. Equal-valued unused rows qualify too, so the total is larger and is not forced to 69 / 138.
        /// </summary>
        [TestCase(TestSupport.Box, 69)]
        [TestCase(TestSupport.Two, 138)]
        public void EveryUnusedFaceRow_IsOutOfEngineScope_WithBothValuesAndItsReason(string fixtureId, int historicalDiffering)
        {
            OsReport report = fixtureId == TestSupport.Box ? box : two;
            string run = fixtureId == TestSupport.Box ? boxRun : twoRun;
            AnalyticalModel model = TestSupport.Load(fixtureId);
            Dictionary<string, string> unused = UnusedFaces(model);
            Dictionary<string, OutOfEngineScopeRow> rows = report.OutOfEngineScope.Rows.ToDictionary(x => x.Key, StringComparer.Ordinal);
            Assert.That(rows.Keys.OrderBy(x => x, StringComparer.Ordinal), Is.EqualTo(unused.Keys.OrderBy(x => x, StringComparer.Ordinal)), "exactly the independently derived unused faces");
            Assert.That(report.OutOfEngineScope.Count, Is.EqualTo(unused.Count));
            Assert.That(report.OutOfEngineScope.Amendment, Is.EqualTo(OsComparison.OpaqueFaceScope));
            Assert.That(report.OutOfEngineScope.CountByScope, Is.EquivalentTo(unused.GroupBy(x => x.Value).ToDictionary(x => x.Key, x => x.Count())));
            Assert.That(report.OutOfEngineScope.CountByScope.Keys, Has.Member(fixtureId == TestSupport.Two ? "interzone surface outer face" : "ground surface outer face"));

            ToleranceRule tolerance = QuantityCatalog.Get("3a").Tolerance;
            Dictionary<string, QuantityResult> results = LayerResults(run, fixtureId);
            List<string> singleSided = new List<string>();
            foreach (OutOfEngineScopeRow row in rows.Values)
            {
                Assert.That(row.Quantity, Is.EqualTo("3a"));
                Assert.That(row.Amendment, Is.EqualTo(OsComparison.OpaqueFaceScope));
                Assert.That(row.Key, Is.EqualTo(row.Side + "#layer" + row.Layer + "." + row.Property));
                Assert.That(row.Property, Does.StartWith(row.SamFace));
                Assert.That(row.ScopeReason, Does.StartWith(unused[row.Key]), row.Key);
                Assert.That(row.SamValue, Is.EqualTo(SamFace(model, row.SamMaterial, row.Property)), row.Key + ": the authored value, as the frozen model states it");
                Assert.That(row.Delta, Is.EqualTo(Math.Abs(row.NativeValue - row.SamValue)), row.Key);
                Assert.That(row.Differs, Is.EqualTo(!EQ.Compare(row.SamValue, row.NativeValue, tolerance).IsWithin), row.Key);
                Assert.That(row.NativeObject, Does.Contain(row.SamMaterial), row.Key);

                QuantityResult result = results[row.Key];
                Assert.That(result.GatesFixture, Is.False, row.Key + ": informational");
                Assert.That(result.Input.Native.Value, Is.EqualTo(row.NativeValue), row.Key);
                Assert.That(result.Reason, Does.StartWith("OUT-OF-ENGINE-SCOPE (" + OsComparison.OpaqueFaceScope + ")"), row.Key);

                double other = SamFace(model, row.SamMaterial, OtherFace(row.Property));
                if (!EQ.Compare(other, row.SamValue, tolerance).IsWithin && EQ.Compare(other, row.NativeValue, tolerance).IsWithin)
                {
                    singleSided.Add(row.Key);
                }
            }

            Assert.That(rows.Values.Where(x => x.Differs).Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal), Is.EqualTo(singleSided.OrderBy(x => x, StringComparer.Ordinal)), "every differing row is the single-sided difference");
            Assert.That(report.OutOfEngineScope.Differing, Is.EqualTo(historicalDiffering), "the rows MAPPING-DIFFERENCE named in revisions 8-11 stay visible");
            Assert.That(report.OutOfEngineScope.Equal, Is.EqualTo(report.OutOfEngineScope.Count - historicalDiffering));
            Assert.That(report.OutOfEngineScope.Equal, Is.GreaterThan(0), "equal-valued unused faces are valid and comparable, so they are in scope too");
            Assert.That(report.NonPass.Select(x => x.Key).Intersect(rows.Keys), Is.Empty);

            string text = Text(run);
            Assert.That(text, Does.Contain("; gated non-PASS 0; out-of-engine-scope " + report.OutOfEngineScope.Count + " (informational, not gated)"));
            Assert.That(text, Does.Contain("OUT-OF-ENGINE-SCOPE (informational, not gated; " + OsComparison.OpaqueFaceScope + "): " + report.OutOfEngineScope.Count + " row(s), " + historicalDiffering + " differing, " + report.OutOfEngineScope.Equal + " equal"));
            Assert.That(text, Does.Contain(OsComparison.OutOfEngineScopeOutcome + " " + report.OutOfEngineScope.Count));
            Assert.That(text, Does.Not.Contain("NOT PASS"));
        }

        // ---- used faces stay gated -----------------------------------------------------------------------------------------

        /// <summary>
        /// The outdoor face: the outermost layer's absorptance changed natively. Exactly the outdoor-face rows of the sides
        /// with that layer outermost FAIL; the same object's buried face stays OUT-OF-ENGINE-SCOPE with the new value
        /// (U1 does not require it to equal the other face), and it cannot mask the failure.
        /// </summary>
        [Test]
        public void AWrongOutdoorFaceValue_Fails_WhileTheSameLayersBuriedFaceStaysInformational()
        {
            // An outdoor side: its outermost layer's outer face is used, so only the inner face of that layer is in scope.
            HashSet<string> keys = new HashSet<string>(box.OutOfEngineScope.Rows.Select(x => x.Key), StringComparer.Ordinal);
            bool Outdoor(OutOfEngineScopeRow x) => x.Layer == 1 && x.Property == "internalSolarReflectance" && !keys.Contains(x.Side + "#layer1.externalSolarReflectance");
            OutOfEngineScopeRow buried = box.OutOfEngineScope.Rows.First(Outdoor);
            List<string> sides = box.OutOfEngineScope.Rows.Where(x => Outdoor(x) && x.NativeObject == buried.NativeObject).Select(x => x.Side).ToList();
            OsReport report = MutatedIdf(boxRun, TestSupport.Box, "u1-outdoor-face", (idf, model) => Cli.Negative.SetField(idf, "Material", buried.NativeObject, "Solar Absorptance", "0.5"));

            Assert.That(report.ExitCode, Is.EqualTo(ReadBackExit.Fail), report.Status);
            List<string> failed = report.NonPass.Where(x => x.Status == "Fail").Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.That(failed, Is.EqualTo(sides.Select(x => x + "#layer1.externalSolarReflectance").OrderBy(x => x, StringComparer.Ordinal)), "exactly the outdoor faces of that layer");
            foreach (string side in sides)
            {
                OutOfEngineScopeRow row = report.OutOfEngineScope.Rows.Single(x => x.Key == side + "#layer1.internalSolarReflectance");
                Assert.That(row.NativeValue, Is.EqualTo(0.5));
                Assert.That(row.Differs, Is.True);
            }

            Assert.That(report.OutOfEngineScope.Count, Is.EqualTo(box.OutOfEngineScope.Count), "no row moves in or out of scope");
        }

        /// <summary>
        /// Interzone, both directions: the object that is one side's zone face is the other side's outer face. Changing it
        /// FAILs the zone face (and every other zone face that object holds); the partner side's outer-face row stays
        /// OUT-OF-ENGINE-SCOPE ("interzone surface outer face") with the new native value.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void AnInterzoneZoneFaceError_Fails_WhileThePartnersOuterFaceStaysInformational(bool firstSpaceZoneFace)
        {
            AnalyticalModel model = TestSupport.Load(TestSupport.Two);
            AdjacencyCluster cluster = model.AdjacencyCluster;
            List<Space> spaces = cluster.GetSpaces();
            Panel partition = cluster.GetPanels().Single(x => cluster.GetRelatedObjects<Space>(x).Count == 2);
            List<Space> related = cluster.GetRelatedObjects<Space>(partition).OrderBy(x => spaces.FindIndex(s => s.Guid == x.Guid)).ToList();
            int count = partition.Construction.ConstructionLayers.Count;
            string first = SideKey(partition, related[0]), second = SideKey(partition, related[1]);
            string zoneKey = firstSpaceZoneFace ? first + "#layer" + count + ".internalSolarReflectance" : second + "#layer" + count + ".externalSolarReflectance";
            string outerKey = firstSpaceZoneFace ? second + "#layer1.internalSolarReflectance" : first + "#layer1.externalSolarReflectance";
            OutOfEngineScopeRow outer = two.OutOfEngineScope.Rows.Single(x => x.Key == outerKey);
            Assert.That(outer.ScopeReason, Does.StartWith("interzone surface outer face"));

            OsReport report = MutatedIdf(twoRun, TestSupport.Two, "u1-interzone-" + (firstSpaceZoneFace ? "first" : "second"), (idf, m) => Cli.Negative.SetField(idf, "Material", outer.NativeObject, "Solar Absorptance", "0.5"));
            Assert.That(report.ExitCode, Is.EqualTo(ReadBackExit.Fail), report.Status);
            List<ReportItem> failed = report.NonPass.Where(x => x.Status == "Fail").ToList();
            List<string> zoneFaces = ZoneFaceKeys(TestSupport.Two);
            Assert.That(failed.Select(x => x.Key), Has.Member(zoneKey));
            Assert.That(failed.All(x => zoneFaces.Contains(x.Key) && x.Key.EndsWith("SolarReflectance", StringComparison.Ordinal)), Is.True, string.Join(" | ", failed.Select(x => x.Key)));

            OutOfEngineScopeRow after = report.OutOfEngineScope.Rows.Single(x => x.Key == outerKey);
            Assert.That(after.NativeValue, Is.EqualTo(0.5));
            Assert.That(report.NonPass.Select(x => x.Key), Has.No.Member(outerKey));
            Assert.That(report.OutOfEngineScope.Count, Is.EqualTo(two.OutOfEngineScope.Count));
        }

        /// <summary>A wrong thickness on a buried layer FAILs its thickness rows (and the derived U); its faces stay informational and mask nothing.</summary>
        [Test]
        public void AWrongBuriedLayerThickness_Fails_AndTheInformationalRowsMaskNothing()
        {
            OutOfEngineScopeRow buried = box.OutOfEngineScope.Rows.First(x => x.Layer > 1 && x.Layer < x.LayerCount);
            List<string> thickness = box.OutOfEngineScope.Rows.Where(x => x.NativeObject == buried.NativeObject).Select(x => x.Side + "#layer" + x.Layer + ".thickness").Distinct().ToList();
            OsReport report = MutatedIdf(boxRun, TestSupport.Box, "u1-buried-thickness", (idf, model) =>
            {
                IdfObject material = IdfText.Parse(idf).Single(x => x.Type == "Material" && x.Name == buried.NativeObject);
                double value = double.Parse(material.Value(2), System.Globalization.CultureInfo.InvariantCulture) * 1.1;
                return Cli.Negative.SetField(idf, "Material", buried.NativeObject, "Thickness", value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            });

            Assert.That(report.ExitCode, Is.EqualTo(ReadBackExit.Fail), report.Status);
            List<ReportItem> failed = report.NonPass.Where(x => x.Status == "Fail").ToList();
            Assert.That(failed.Where(x => x.Quantity == "3a").Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal), Is.EqualTo(thickness.OrderBy(x => x, StringComparer.Ordinal)));
            Assert.That(failed.All(x => (x.Quantity == "3a" && x.Key.EndsWith(".thickness", StringComparison.Ordinal)) || x.Quantity == "3b"), Is.True, string.Join(" | ", failed.Select(x => x.Key)));
            Assert.That(report.OutOfEngineScope.Count, Is.EqualTo(box.OutOfEngineScope.Count));
            Assert.That(report.NonPass.Select(x => x.Key).Intersect(report.OutOfEngineScope.Rows.Select(x => x.Key)), Is.Empty);
        }

        /// <summary>An informational row coexists with a genuine blocker elsewhere: the blocker decides, both stay visible, and the counts are unchanged.</summary>
        [TestCase("neg-layer-conductivity", TestSupport.Two)]
        [TestCase("neg-lighting-return-air", TestSupport.Box)]
        public void AnInformationalRow_NeverMasksAGatedDefect(string caseId, string fixture)
        {
            OsReport original = fixture == TestSupport.Box ? box : two;
            string run = TestSupport.NewDirectory("u1-" + caseId + "-" + fixture);
            Assert.That(TestSupport.Cli(out string output, "negative", "--run", fixture == TestSupport.Box ? boxRun : twoRun, "--case", caseId, "--out", run), Is.EqualTo(0), output);
            OsReport report = TestSupport.ReadAndCompare(run, fixture);
            Assert.That(report.ExitCode, Is.EqualTo(ReadBackExit.Fail), report.Status);
            Assert.That(report.GatedNonPass, Is.EqualTo(report.NonPass.Count).And.GreaterThan(0));
            Assert.That(report.OutOfEngineScope.Count, Is.EqualTo(original.OutOfEngineScope.Count));
            Assert.That(report.OutOfEngineScope.Differing, Is.EqualTo(original.OutOfEngineScope.Differing));
            Assert.That(report.CountByOutcome[OsComparison.OutOfEngineScopeOutcome], Is.EqualTo(original.OutOfEngineScope.Count));
            Assert.That(report.NonPass.Select(x => x.Key).Intersect(report.OutOfEngineScope.Rows.Select(x => x.Key)), Is.Empty);
            string text = Text(run);
            Assert.That(text, Does.Contain("NOT PASS ").And.Contain("OUT-OF-ENGINE-SCOPE (informational, not gated;"));
        }

        // ---- authored zero and equal values --------------------------------------------------------------------------------

        /// <summary>
        /// An authored zero is a value (S-ZERO), not absence. On the box floor's buried EPS layer: internal emissivity 0
        /// against the native external value (differing), and both light reflectances 0 against a native 0 (equal). All
        /// three are OUT-OF-ENGINE-SCOPE and the fixture still PASSes.
        /// </summary>
        [Test]
        public void AnAuthoredZeroOnAnUnusedFace_IsAValue_AndOutOfEngineScope()
        {
            AnalyticalModel model = WithMaterial(TestSupport.Box, "FIX_EPS", material =>
            {
                material.SetValue(OpaqueMaterialParameter.InternalEmissivity, 0.0);
                material.SetValue(OpaqueMaterialParameter.InternalLightReflectance, 0.0);
                material.SetValue(OpaqueMaterialParameter.ExternalLightReflectance, 0.0);
            });

            OsReport report = TestSupport.Pipeline(TestSupport.Write(model, "u1-zero"), TestSupport.Box, "u1-zero", out string run);
            Assert.That(report.NonPass, Is.Empty, string.Join(" | ", report.NonPass.Select(x => x.Key + " " + x.Reason)));
            Assert.That(report.ExitCode, Is.EqualTo(ReadBackExit.Pass), report.Status);

            Dictionary<string, OutOfEngineScopeRow> eps = report.OutOfEngineScope.Rows.Where(x => x.SamMaterial == "FIX_EPS").ToDictionary(x => x.Property);
            Assert.That(eps.Count, Is.EqualTo(6), "both faces of a buried layer");
            Assert.That(eps["internalEmissivity"].SamValue, Is.EqualTo(0.0));
            Assert.That(eps["internalEmissivity"].Differs, Is.True);
            foreach (string property in new[] { "internalLightReflectance", "externalLightReflectance" })
            {
                Assert.That(eps[property].SamValue, Is.EqualTo(0.0), property);
                Assert.That(eps[property].NativeValue, Is.EqualTo(0.0), property);
                Assert.That(eps[property].Differs, Is.False, property);
                Assert.That(eps[property].NumericVerdict, Is.EqualTo("Identical"), property);
            }

            Dictionary<string, QuantityResult> results = LayerResults(run, TestSupport.Box);
            Assert.That(results[eps["internalEmissivity"].Key].Input.Expected.State, Is.EqualTo(SamInputState.Zero));
            Assert.That(report.OutOfEngineScope.Count, Is.EqualTo(box.OutOfEngineScope.Count));
        }

        // ---- not valid and comparable: never excluded --------------------------------------------------------------------

        /// <summary>A native value on an unused face that is NaN, blank (absent), unreadable or outside [0, 1] keeps its gated result and is never informational.</summary>
        [TestCase("NaN", "ExportDefect", "Fail", ReadBackExit.Fail, "not a read value")]
        [TestCase("", "ExportDefect", "Fail", ReadBackExit.Fail, "not a read value")]
        [TestCase("abc", "UnverifiedUnreadable", "BlockedTechnical", ReadBackExit.BlockedTechnical, "not a read value")]
        [TestCase("1.25", "ExportDefect", "Fail", ReadBackExit.Fail, "is outside [0, 1]")]
        public void AnInvalidNativeValueOnAnUnusedFace_IsNeverExcluded(string value, string outcome, string status, int exit, string fragment)
        {
            List<OutOfEngineScopeRow> eps = box.OutOfEngineScope.Rows.Where(x => x.SamMaterial == "FIX_EPS" && x.Property.EndsWith("SolarReflectance", StringComparison.Ordinal)).ToList();
            Assert.That(eps.Count, Is.EqualTo(2), "the buried EPS layer's two solar faces");
            OsReport report = MutatedIdf(boxRun, TestSupport.Box, "u1-native-" + (value.Length == 0 ? "blank" : value), (idf, model) => Cli.Negative.SetField(idf, "Material", eps[0].NativeObject, "Solar Absorptance", value));

            Assert.That(report.ExitCode, Is.EqualTo(exit), report.Status);
            Assert.That(report.NonPass.Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal), Is.EqualTo(eps.Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal)), "exactly the two rows, gated");
            Assert.That(report.NonPass.All(x => x.Outcome == outcome && x.Status == status && x.Reason.Contains(fragment) && x.Reason.Contains("Not OUT-OF-ENGINE-SCOPE")), Is.True, string.Join(" | ", report.NonPass.Select(x => x.Outcome + " " + x.Status + " " + x.Reason)));
            Assert.That(report.OutOfEngineScope.Rows.Select(x => x.Key).Intersect(eps.Select(x => x.Key)), Is.Empty);
            Assert.That(report.OutOfEngineScope.Count, Is.EqualTo(box.OutOfEngineScope.Count - 2));
            Assert.That(report.Coverage, Is.EqualTo("Complete"));
            Assert.That(report.Required, Is.EqualTo(box.Required));
        }

        /// <summary>A SAM value on an unused face that is absent or NaN (S-UNSPEC) or outside [0, 1] (S-INVALID) keeps its gated result and is never informational.</summary>
        [TestCase("absent", "ExportDefect", "Fail", ReadBackExit.Fail)]
        [TestCase("nan", "ExportDefect", "Fail", ReadBackExit.Fail)]
        [TestCase("out-of-domain", "InputInvalid", "BlockedTechnical", ReadBackExit.BlockedTechnical)]
        public void AnInvalidSamValueOnAnUnusedFace_IsNeverExcluded(string what, string outcome, string status, int exit)
        {
            string key = box.OutOfEngineScope.Rows.Single(x => x.SamMaterial == "FIX_EPS" && x.Property == "internalSolarReflectance").Key;
            AnalyticalModel model = WithMaterial(TestSupport.Box, "FIX_EPS", material =>
            {
                switch (what)
                {
                    case "absent":
                        material.RemoveValue(OpaqueMaterialParameter.InternalSolarReflectance);
                        break;
                    case "nan":
                        material.SetValue(OpaqueMaterialParameter.InternalSolarReflectance, double.NaN);
                        break;
                    default:
                        material.SetValue(OpaqueMaterialParameter.InternalSolarReflectance, 1.4);
                        break;
                }
            });

            OsReport report = TestSupport.Pipeline(TestSupport.Write(model, "u1-sam-" + what), TestSupport.Box, "u1-sam-" + what, out string run);
            Assert.That(report.ExitCode, Is.EqualTo(exit), report.Status);
            ReportItem item = report.NonPass.Single();
            Assert.That(item.Key, Is.EqualTo(key));
            Assert.That(item.Outcome, Is.EqualTo(outcome), item.Reason);
            Assert.That(item.Status, Is.EqualTo(status), item.Reason);
            Assert.That(item.Reason, Does.Contain("Not OUT-OF-ENGINE-SCOPE"));
            Assert.That(report.OutOfEngineScope.Rows.Select(x => x.Key), Has.No.Member(key));
            Assert.That(report.OutOfEngineScope.Count, Is.EqualTo(box.OutOfEngineScope.Count - 1));
        }

        // ---- face roles ----------------------------------------------------------------------------------------------------

        /// <summary>
        /// The control: a box wall made adiabatic in SAM. Its outermost layer's outer face is now unused ("adiabatic surface
        /// outer face"); the role is established from the SAM flag and the native Adiabatic boundary, and the fixture passes.
        /// </summary>
        [Test]
        public void AnAdiabaticWall_HasItsOuterFaceOutOfEngineScope()
        {
            AnalyticalModel model = WithAdiabaticPanel(TestSupport.Box, cluster => cluster.GetPanels().First(x => x.PanelType == PanelType.WallExternal && (x.Apertures == null || x.Apertures.Count == 0)), out Panel wall);
            OsReport report = TestSupport.Pipeline(TestSupport.Write(model, "u1-adiabatic"), TestSupport.Box, "u1-adiabatic", out string run);
            Assert.That(report.Unsupported, Is.Empty, string.Join(" | ", report.Unsupported));
            Assert.That(report.Consistency, Is.Empty, string.Join(" | ", report.Consistency));
            Assert.That(report.NonPass, Is.Empty, string.Join(" | ", report.NonPass.Select(x => x.Key + " " + x.Reason)));
            Assert.That(report.ExitCode, Is.EqualTo(ReadBackExit.Pass), report.Status);
            string side = SideKey(wall, model.AdjacencyCluster.GetRelatedObjects<Space>(wall).Single());
            List<OutOfEngineScopeRow> outer = report.OutOfEngineScope.Rows.Where(x => x.Side == side && x.ScopeReason == "adiabatic surface outer face").ToList();
            Assert.That(outer.Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal), Is.EqualTo(UnusedFaces(model).Where(x => x.Value == "adiabatic surface outer face").Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal)));
            Assert.That(outer.Count, Is.EqualTo(3));
            Assert.That(report.OutOfEngineScope.Count, Is.EqualTo(box.OutOfEngineScope.Count + 3));
        }

        /// <summary>
        /// A face role that cannot be established is never excluded: an unsupported native ground form, an adiabatic SAM
        /// panel written as Ground, an interzone partner that is not this side's mirror or does not name it back, and a SAM
        /// panel that bounds two spaces and is adiabatic. Each is named as unsupported (BLOCKED technical at least), none of
        /// the side's rows is OUT-OF-ENGINE-SCOPE, and the fixture never passes.
        /// </summary>
        [TestCase("ground-preprocessor")]
        [TestCase("adiabatic-written-as-ground")]
        [TestCase("interzone-not-mirrored")]
        [TestCase("interzone-partner-missing")]
        [TestCase("interzone-adiabatic")]
        public void AFaceRoleThatCannotBeEstablished_IsNeverExcluded(string what)
        {
            OsReport report;
            List<string> sides;
            string fragment;
            switch (what)
            {
                case "ground-preprocessor":
                {
                    AnalyticalModel model = TestSupport.Load(TestSupport.Box);
                    Panel floor = model.AdjacencyCluster.GetPanels().Single(x => x.PanelType == PanelType.SlabOnGrade);
                    sides = new List<string> { SideKey(floor, model.AdjacencyCluster.GetRelatedObjects<Space>(floor).Single()) };
                    report = MutatedIdf(boxRun, TestSupport.Box, "u1-ground-preprocessor", (idf, m) =>
                    {
                        IdfObject surface = IdfText.Parse(idf).Single(x => x.Type == "BuildingSurface:Detailed" && string.Equals(x.Value(5), "Ground", StringComparison.OrdinalIgnoreCase));
                        return Cli.Negative.SetField(idf, "BuildingSurface:Detailed", surface.Name, "Outside Boundary Condition", "GroundFCfactorMethod");
                    });
                    fragment = "the native outside boundary 'GroundFCfactorMethod' is not the SAM boundary 'Ground'";
                    Assert.That(report.Consistency, Is.Not.Empty, "the boundary itself also disagrees with SAM");
                    break;
                }

                case "adiabatic-written-as-ground":
                {
                    AnalyticalModel model = WithAdiabaticPanel(TestSupport.Box, cluster => cluster.GetPanels().First(x => x.PanelType == PanelType.WallExternal && (x.Apertures == null || x.Apertures.Count == 0)), out Panel wall);
                    TestSupport.Pipeline(TestSupport.Write(model, "u1-adiabatic-source"), TestSupport.Box, "u1-adiabatic-source", out string source);
                    sides = new List<string> { SideKey(wall, model.AdjacencyCluster.GetRelatedObjects<Space>(wall).Single()) };
                    report = MutatedIdf(source, TestSupport.Box, "u1-adiabatic-as-ground", (idf, m) =>
                    {
                        IdfObject surface = IdfText.Parse(idf).Single(x => x.Type == "BuildingSurface:Detailed" && string.Equals(x.Value(5), "Adiabatic", StringComparison.OrdinalIgnoreCase));
                        return Cli.Negative.SetField(idf, "BuildingSurface:Detailed", surface.Name, "Outside Boundary Condition", "Ground");
                    });
                    fragment = "the native outside boundary 'Ground' is not the SAM boundary 'Adiabatic'";
                    break;
                }

                case "interzone-not-mirrored":
                case "interzone-partner-missing":
                {
                    AnalyticalModel model = TestSupport.Load(TestSupport.Two);
                    Panel partition = model.AdjacencyCluster.GetPanels().Single(x => model.AdjacencyCluster.GetRelatedObjects<Space>(x).Count == 2);
                    sides = model.AdjacencyCluster.GetRelatedObjects<Space>(partition).Select(x => SideKey(partition, x)).ToList();
                    if (what == "interzone-not-mirrored")
                    {
                        report = MutatedIdf(twoRun, TestSupport.Two, "u1-interzone-not-mirrored", (idf, m) =>
                        {
                            IdfObject reverse = IdfText.Parse(idf).Single(x => x.Type == "Construction" && x.Name.Contains("PARTITION_Reverse"));
                            idf = Cli.Negative.SetField(idf, "Construction", reverse.Name, "Layer 2", reverse.Value(3));
                            return Cli.Negative.SetField(idf, "Construction", reverse.Name, "Layer 3", reverse.Value(2));
                        });
                        fragment = "the interzone partner's construction is not this side's construction reversed";
                    }
                    else
                    {
                        report = MutatedIdf(twoRun, TestSupport.Two, "u1-interzone-partner-missing", (idf, m) =>
                        {
                            IdfObject surface = IdfText.Parse(idf).First(x => x.Type == "BuildingSurface:Detailed" && string.Equals(x.Value(5), "Surface", StringComparison.OrdinalIgnoreCase));
                            return Cli.Negative.SetField(idf, "BuildingSurface:Detailed", surface.Name, "Outside Boundary Condition Object", "S09_NoSuchSurface");
                        });
                        fragment = "the interzone partner";
                    }

                    break;
                }

                default:
                {
                    AnalyticalModel model = WithAdiabaticPanel(TestSupport.Two, cluster => cluster.GetPanels().Single(x => cluster.GetRelatedObjects<Space>(x).Count == 2), out Panel partition);
                    sides = model.AdjacencyCluster.GetRelatedObjects<Space>(partition).Select(x => SideKey(partition, x)).ToList();
                    report = TestSupport.Pipeline(TestSupport.Write(model, "u1-interzone-adiabatic"), TestSupport.Two, "u1-interzone-adiabatic", out string _);
                    fragment = "contradictory SAM boundary: the panel bounds two spaces and is adiabatic";
                    break;
                }
            }

            Assert.That(report.ExitCode, Is.AnyOf(ReadBackExit.Fail, ReadBackExit.BlockedTechnical), what + ": " + report.Status);
            Assert.That(report.Status, Is.Not.EqualTo(GateStatus.Pass.ToString()));
            foreach (string side in sides)
            {
                Assert.That(report.Unsupported.Count(x => x.StartsWith("optics role: " + side + ": ", StringComparison.Ordinal) && x.Contains(fragment) && x.EndsWith("no optical face of this side is OUT-OF-ENGINE-SCOPE (" + OsComparison.OpaqueFaceScope + ")", StringComparison.Ordinal)), Is.EqualTo(1), side + ": " + string.Join(" | ", report.Unsupported));
                Assert.That(report.OutOfEngineScope.Rows.Where(x => x.Side == side), Is.Empty, side);
            }

            Assert.That(report.NonPass.Where(x => sides.Any(s => x.Key.StartsWith(s + "#", StringComparison.Ordinal)) && OpticalProperties.Any(p => x.Key.EndsWith("." + p, StringComparison.Ordinal))).All(x => x.Reason.Contains("Not OUT-OF-ENGINE-SCOPE") || x.Status == "Fail"), Is.True);
            Assert.That(report.Coverage, Is.EqualTo("Complete"));
        }
    }
}
