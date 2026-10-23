// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Core.OpenStudio;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// SAM S07 §5.1 (amendment L1, T-LIGHT-SPLIT). SAM <c>LightingRadiantProportion</c> p is the total radiant share; the
    /// writer splits it by s = 0.2415 / 0.576 (Fraction Visible p·s, Fraction Radiant p·(1 − s), Return Air 0) and never
    /// reads the view coefficient, which is a comfort coefficient. The importer recovers p = Fraction Radiant + Fraction
    /// Visible and does not set the view coefficient. The expected values are the contract's vectors, not the writer's
    /// constants.
    /// </summary>
    [TestFixture]
    public class LightingSplitTests
    {
        private const string ASSUMPTION = "SAM-OS-IC-002";
        private const string IC_PARAM = "SAM-OS-IC-001";

        private static global::OpenStudio.LightsDefinition Lights(OpenStudioConversionResult result)
        {
            Assert.That(result.Model.getLightsDefinitions().Count, Is.EqualTo(1));
            return result.Model.getLightsDefinitions()[0];
        }

        private static OpenStudioConversionResult Convert(double? radiantProportion, double? viewCoefficient = null)
        {
            InternalCondition internalCondition = AnalyticalModelFixtures.CreateOfficeInternalCondition();
            if (radiantProportion.HasValue)
            {
                internalCondition.SetValue(InternalConditionParameter.LightingRadiantProportion, radiantProportion.Value);
            }

            if (viewCoefficient.HasValue)
            {
                internalCondition.SetValue(InternalConditionParameter.LightingViewCoefficient, viewCoefficient.Value);
            }

            return AnalyticalModelFixtures.SingleBox(internalConditionOverride: internalCondition).ToOpenStudio();
        }

        // S07 §5.1, "Expected vectors" (tolerance 0.001; the vectors are printed to 6 decimals).
        [TestCase(0.37, 0.155130, 0.214870)]
        [TestCase(0.34, 0.142552, 0.197448)]
        [TestCase(0.41, 0.171901, 0.238099)]
        public void AnAuthoredP_IsSplitByS_WithNoReturnAir(double p, double visible, double radiant)
        {
            using (OpenStudioConversionResult result = Convert(p))
            {
                global::OpenStudio.LightsDefinition lights = Lights(result);
                Assert.That(lights.fractionVisible(), Is.EqualTo(visible).Within(1e-6));
                Assert.That(lights.fractionRadiant(), Is.EqualTo(radiant).Within(1e-6));
                Assert.That(lights.returnAirFraction(), Is.EqualTo(0.0));
                Assert.That(lights.fractionRadiant() + lights.fractionVisible(), Is.EqualTo(p).Within(1e-12), "the total radiant share is preserved");
                Assert.That(1 - lights.returnAirFraction() - lights.fractionRadiant() - lights.fractionVisible(), Is.EqualTo(1 - p).Within(1e-12), "convective 1 − p, as Tas");

                Core.OpenStudio.OpenStudioDiagnostic note = result.Diagnostics.Single(x => x.Code == ASSUMPTION);
                Assert.That(note.Severity, Is.EqualTo(Core.OpenStudio.OpenStudioDiagnosticSeverity.Information));
                Assert.That(note.GetDiagnosticDetail(), Is.EqualTo(Core.DiagnosticDetail.Summary), "s is an assumption that changes results");
                Assert.That(note.Message, Does.StartWith("Lighting radiant proportion p = " + p.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " (authored)"));
                Assert.That(note.Message, Does.Contain("s = 0.2415 / 0.576").And.Contain("suspended (pendant) LED office lighting").And.Contain("Not applicable to recessed"));
                Assert.That(note.Message, Does.Not.Contain("p₀"), "an authored p is not the default");
            }
        }

        [Test]
        public void AnAuthoredZero_IsPreserved_AllConvective()
        {
            using (OpenStudioConversionResult result = Convert(0.0))
            {
                global::OpenStudio.LightsDefinition lights = Lights(result);
                Assert.That(lights.fractionRadiant(), Is.EqualTo(0.0));
                Assert.That(lights.fractionVisible(), Is.EqualTo(0.0));
                Assert.That(lights.returnAirFraction(), Is.EqualTo(0.0));
                Assert.That(result.Diagnostics.Single(x => x.Code == ASSUMPTION).Message, Does.StartWith("Lighting radiant proportion p = 0 (authored)"));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AnAbsentOrNaNP_TakesTheCommonDefault_WithItsCategoryAndOpenDecisionStated(bool nan)
        {
            using (OpenStudioConversionResult result = Convert(nan ? double.NaN : (double?)null))
            {
                global::OpenStudio.LightsDefinition lights = Lights(result);
                Assert.That(lights.fractionRadiant(), Is.EqualTo(0.3345).Within(1e-12));
                Assert.That(lights.fractionVisible(), Is.EqualTo(0.2415).Within(1e-12));
                Assert.That(lights.returnAirFraction(), Is.EqualTo(0.0));

                Core.OpenStudio.OpenStudioDiagnostic note = result.Diagnostics.Single(x => x.Code == ASSUMPTION);
                Assert.That(note.Severity, Is.EqualTo(Core.OpenStudio.OpenStudioDiagnosticSeverity.Information));
                Assert.That(note.GetDiagnosticDetail(), Is.EqualTo(Core.DiagnosticDetail.Summary), "a fallback that can change results is never hidden");
                Assert.That(note.Message, Does.StartWith("Lighting radiant proportion is not stated; the S07 §5.1 common default p₀ = 0.576 was applied"));
                Assert.That(note.Message, Does.Contain("OD-L1").And.Contain("not a regulatory, NCM, TM59 or dwelling value").And.Contain("suspended (pendant) LED office lighting"));
            }
        }

        [TestCase(0.37)]
        [TestCase(0.0)]
        [TestCase(null)]
        public void TheComfortCoefficient_NeverChangesTheLightsFractions(double? p)
        {
            double radiant, visible, returnAir;
            using (OpenStudioConversionResult without = Convert(p))
            {
                radiant = Lights(without).fractionRadiant();
                visible = Lights(without).fractionVisible();
                returnAir = Lights(without).returnAirFraction();
                Assert.That(without.Diagnostics.Any(x => x.Message != null && x.Message.StartsWith("Lighting view coefficient")), Is.False);
            }

            foreach (double coefficient in new[] { 0.49, 0.25, 0.0, 1.0 })
            {
                using (OpenStudioConversionResult with = Convert(p, coefficient))
                {
                    Assert.That(Lights(with).fractionRadiant(), Is.EqualTo(radiant), "C = " + coefficient);
                    Assert.That(Lights(with).fractionVisible(), Is.EqualTo(visible), "C = " + coefficient);
                    Assert.That(Lights(with).returnAirFraction(), Is.EqualTo(returnAir), "C = " + coefficient);

                    Core.OpenStudio.OpenStudioDiagnostic note = with.Diagnostics.Single(x => x.Code == IC_PARAM && x.Message.StartsWith("Lighting view coefficient"));
                    Assert.That(note.Severity, Is.EqualTo(Core.OpenStudio.OpenStudioDiagnosticSeverity.Information));
                    Assert.That(note.Message, Does.Contain("not an energy fraction"));
                }
            }
        }

        // --- import -------------------------------------------------------------------------------------------------------

        private static InternalCondition Import(double returnAir, double radiant, double visible, out List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics)
        {
            using (global::OpenStudio.Model model = new global::OpenStudio.Model())
            {
                global::OpenStudio.Space space = new global::OpenStudio.Space(model);
                space.setName("Room");
                global::OpenStudio.ThermalZone thermalZone = new global::OpenStudio.ThermalZone(model);
                space.setThermalZone(thermalZone);
                global::OpenStudio.Point3dVector vertices = new global::OpenStudio.Point3dVector();
                foreach (double[] vertex in new[] { new[] { 0.0, 0.0, 0.0 }, new[] { 4.0, 0.0, 0.0 }, new[] { 4.0, 0.0, 2.7 }, new[] { 0.0, 0.0, 2.7 } })
                {
                    vertices.Add(new global::OpenStudio.Point3d(vertex[0], vertex[1], vertex[2]));
                }

                global::OpenStudio.Surface wall = new global::OpenStudio.Surface(vertices, model);
                wall.setSpace(space);

                global::OpenStudio.SpaceType spaceType = new global::OpenStudio.SpaceType(model);
                spaceType.setName("Office");
                space.setSpaceType(spaceType);

                global::OpenStudio.LightsDefinition lightsDefinition = new global::OpenStudio.LightsDefinition(model);
                lightsDefinition.setWattsperSpaceFloorArea(8);
                Assert.That(lightsDefinition.setReturnAirFraction(returnAir), Is.True);
                Assert.That(lightsDefinition.setFractionRadiant(radiant), Is.True);
                Assert.That(lightsDefinition.setFractionVisible(visible), Is.True);
                global::OpenStudio.Lights lights = new global::OpenStudio.Lights(lightsDefinition);
                lights.setSpaceType(spaceType);

                OpenStudioImportResult result = model.ToSAM();
                diagnostics = result.Diagnostics.ToList();
                return result.AnalyticalModel.AdjacencyCluster.GetSpaces()[0].InternalCondition;
            }
        }

        private static IEnumerable<Core.OpenStudio.OpenStudioDiagnostic> LightingNotes(IEnumerable<Core.OpenStudio.OpenStudioDiagnostic> diagnostics)
        {
            return diagnostics.Where(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied && x.Message.StartsWith("The Lights"));
        }

        [Test]
        public void Import_RecoversPFromRadiantPlusVisible_AndNeverSetsTheComfortCoefficient()
        {
            InternalCondition internalCondition = Import(0, 0.3345, 0.2415, out List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics);
            Assert.That(internalCondition.TryGetValue(InternalConditionParameter.LightingRadiantProportion, out double p), Is.True);
            Assert.That(p, Is.EqualTo(0.576).Within(1e-12));
            Assert.That(internalCondition.TryGetValue(InternalConditionParameter.LightingViewCoefficient, out double _), Is.False, "the view coefficient is not an energy fraction");
            Assert.That(LightingNotes(diagnostics), Is.Empty, "the split is s and there is no return air");
        }

        [Test]
        public void Import_AZeroSplit_IsAnAuthoredZero()
        {
            InternalCondition internalCondition = Import(0, 0, 0, out List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics);
            Assert.That(internalCondition.TryGetValue(InternalConditionParameter.LightingRadiantProportion, out double p), Is.True);
            Assert.That(p, Is.EqualTo(0.0));
            Assert.That(internalCondition.TryGetValue(InternalConditionParameter.LightingViewCoefficient, out double _), Is.False);
            Assert.That(LightingNotes(diagnostics), Is.Empty);
        }

        [Test]
        public void Import_AnotherShortWaveShare_IsReported()
        {
            InternalCondition internalCondition = Import(0, 0.3, 0.2, out List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics);
            Assert.That(internalCondition.GetValue<double>(InternalConditionParameter.LightingRadiantProportion), Is.EqualTo(0.5).Within(1e-12));
            Core.OpenStudio.OpenStudioDiagnostic note = LightingNotes(diagnostics).Single();
            Assert.That(note.Severity, Is.EqualTo(Core.OpenStudio.OpenStudioDiagnosticSeverity.Information));
            Assert.That(note.GetDiagnosticDetail(), Is.EqualTo(Core.DiagnosticDetail.Summary));
            Assert.That(note.Message, Does.StartWith("The Lights short-wave share of the radiant output"));
        }

        [Test]
        public void Import_ANonzeroReturnAirShare_IsWarnedAndBecomesConvective()
        {
            InternalCondition internalCondition = Import(0.1, 0.3345, 0.2415, out List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics);
            Assert.That(internalCondition.GetValue<double>(InternalConditionParameter.LightingRadiantProportion), Is.EqualTo(0.576).Within(1e-12), "p is the radiant share only");
            Core.OpenStudio.OpenStudioDiagnostic warning = LightingNotes(diagnostics).Single();
            Assert.That(warning.Severity, Is.EqualTo(Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning));
            Assert.That(warning.Message, Does.StartWith("The Lights Return Air Fraction 0.1 has no SAM allocation"));
            Assert.That(internalCondition.TryGetValue(InternalConditionParameter.LightingViewCoefficient, out double _), Is.False);
        }

        [Test]
        public void Import_FractionsSummingAboveOne_AreNotImported()
        {
            InternalCondition internalCondition = Import(0.3, 0.5, 0.4, out List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics);
            Assert.That(internalCondition.TryGetValue(InternalConditionParameter.LightingRadiantProportion, out double _), Is.False);
            Assert.That(LightingNotes(diagnostics).Single().Severity, Is.EqualTo(Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning));
        }

        [TestCase(0.37)]
        [TestCase(0.0)]
        public void RoundTrip_PreservesTheAuthoredP_WithoutAComfortCoefficient(double p)
        {
            using (OpenStudioConversionResult conversion = Convert(p, 0.49))
            {
                OpenStudioImportResult result = conversion.Model.ToSAM();
                InternalCondition internalCondition = result.AnalyticalModel.AdjacencyCluster.GetSpaces()[0].InternalCondition;
                Assert.That(internalCondition.GetValue<double>(InternalConditionParameter.LightingRadiantProportion), Is.EqualTo(p).Within(1e-12));
                Assert.That(internalCondition.TryGetValue(InternalConditionParameter.LightingViewCoefficient, out double _), Is.False);
                Assert.That(LightingNotes(result.Diagnostics), Is.Empty);
            }
        }
    }
}
