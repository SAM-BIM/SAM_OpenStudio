// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Core.OpenStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// D1: the Information detail table, pinned against every Information call site of the converters.
    /// Principle: a line that records an approximation, fallback, assumption, dropped input or data source that can
    /// change simulation results is visible at Summary. Only the few known-inert lines are lowered.
    /// </summary>
    [TestFixture]
    public class DiagnosticDetailTableTests
    {
        private const Core.DiagnosticDetail S = Core.DiagnosticDetail.Summary;
        private const Core.DiagnosticDetail T = Core.DiagnosticDetail.Standard;
        private const Core.DiagnosticDetail D = Core.DiagnosticDetail.Detailed;

        private const string RUN = Core.OpenStudio.OpenStudioDiagnosticCodes.RunCliFailed;
        private const string WEATHER = Core.OpenStudio.OpenStudioDiagnosticCodes.WeatherDataIssue;
        private const string SETTINGS = Core.OpenStudio.OpenStudioDiagnosticCodes.SimulationSettingsUnsupported;
        private const string CON_LAYER = Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionMissingLayer;
        private const string CON_PARAM = Core.OpenStudio.OpenStudioDiagnosticCodes.ConstructionUnsupportedParameter;
        private const string MAT_PARAM = Core.OpenStudio.OpenStudioDiagnosticCodes.MaterialUnsupportedParameter;
        private const string IC_PARAM = Core.OpenStudio.OpenStudioDiagnosticCodes.InternalConditionUnsupportedParameter;
        private const string HVAC = Core.OpenStudio.OpenStudioDiagnosticCodes.HvacMissingSetpoints;
        private const string SCHEDULE = Core.OpenStudio.OpenStudioDiagnosticCodes.ScheduleMissingProfile;
        private const string NON_CONVEX = Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryNonConvexCasting;
        private const string ADJ = Core.OpenStudio.OpenStudioDiagnosticCodes.AdjacencyMissingSurface;
        private const string GEO_CLEAN = Core.OpenStudio.OpenStudioDiagnosticCodes.GeometryVerticesCleaned;

        /// <summary>
        /// One row per Information call site (the start of its message, as written in the converters) with the level it
        /// must have. If a producer is reworded or a new Information site is added, this table is where the decision is made.
        /// </summary>
        private static readonly (string Code, string Message, Core.DiagnosticDetail Expected, string Why)[] informationSites =
        {
            // Run, workflow and basis of the run.
            (SETTINGS, "Measure 'Foo' applied as workflow step 1 (arguments at their measure defaults)", S, "a measure changes the model that is simulated"),
            (SETTINGS, "2 additional IDF string(s) will be injected through the generated EnergyPlus measure 'x' (advanced use", S, "verbatim EnergyPlus objects change the run"),
            (RUN, "Site coordinates taken from the SAM model Location (lat 51, lon 0, elevation 10), overriding the EPW", S, "site override changes weather and solar geometry"),
            (WEATHER, "Design-day source: explicit DDY (a.ddy); embedded AnalyticalModel design days are not merged", S, "design days drive sizing"),
            (WEATHER, "Design-day source: AnalyticalModel heating/cooling design days (2 imported)", S, "design days drive sizing"),
            (WEATHER, "No design-day source supplied; sizing-period runs stay disabled unless", S, "sizing is not run"),
            (WEATHER, "Imported 2 design day(s) from a.ddy", S, "design days drive sizing"),
            (WEATHER, "Annual weather source: explicit EPW (a.epw)", S, "weather basis"),
            (WEATHER, "Site coordinates taken from the AnalyticalModel WeatherData (lat 1, lon 2); an EPW path is still required", S, "site basis"),
            (WEATHER, "Ground temperatures taken from the SAM model WeatherData (depth 0.5 m set, 12 monthly values)", S, "ground boundary condition source"),
            (WEATHER, "Ground temperatures taken from the EPW GROUND TEMPERATURES header (depth 0.5 m set, 12 monthly values)", S, "ground boundary condition source"),

            // Approximations and fallbacks.
            (CON_LAYER, "Aperture construction has no pane layers; SimpleGlazingSystem fallback built from the performance parameters (approximation)", S, "glazing U, SHGC and VT are approximated"),
            (SCHEDULE, "Profile has 8760 values in a leap-year run; 31 December is repeated for day 366", S, "schedule is approximated"),
            (IC_PARAM, "Air panel converted with Air Exchange Method 'None': the zones share one radiant/solar enclosure but exchange no air.", S, "inter-zone air exchange assumption"),
            (IC_PARAM, "Air panel converted with Air Exchange Method 'SimpleMixing' at 2 ACH (caller-supplied assumption", S, "inter-zone air exchange assumption"),
            (MAT_PARAM, "Internal-side optical properties (InternalEmissivity) differ from the external-side values", S, "single-sided optics approximation"),
            (MAT_PARAM, "Material is flagged as a blind but SAM carries no slat geometry", S, "solar control is not modelled"),

            // Dropped inputs that can change loads.
            (CON_PARAM, "Opening properties (openable fraction, discharge coefficient) are not converted", S, "natural ventilation is not modelled"),
            (CON_PARAM, "Aperture feature shade is not converted", S, "shading is not modelled"),
            (CON_PARAM, "Panel feature shade is not converted", S, "shading is not modelled"),
            (CON_PARAM, "Pane additional heat transfer (TAS % U-adjustment) is not converted", S, "glazing U-value"),
            (CON_PARAM, "Frame additional heat transfer (TAS % U-adjustment) is not converted", S, "frame U-value"),
            (CON_PARAM, "Internal-shadow flag (TAS) on the aperture construction is not converted", S, "not shown to be inert"),
            (CON_PARAM, "Internal-shadow flag (TAS) on the construction is not converted", S, "not shown to be inert"),
            (IC_PARAM, "Ventilation function (TAS expression/factor/setback) is not converted", S, "ventilation"),
            (IC_PARAM, "Lighting control function (TAS control expression) is not converted", S, "lighting gains"),
            (HVAC, "Emitter characteristics (HeatingEmitterCoefficient) are not converted", S, "emitters are deferred"),
            (HVAC, "Exhaust air flows (ExhaustAirFlow) are not converted", S, "exhaust ventilation is deferred"),

            // Known-inert lines: lowered.
            (IC_PARAM, "Occupancy view coefficient is not converted", D, "OS:People has no visible-fraction field; daylighting only"),
            (IC_PARAM, "Equipment view coefficient is not converted", D, "OS:ElectricEquipment has no visible-fraction field; daylighting only"),
            (MAT_PARAM, "Vapour diffusion factor (10) is not converted", D, "only matters for HAMT / EMPD, which is not used"),
            (CON_PARAM, "Frame data on an opaque door is not converted (frames apply to glazed subsurfaces); frameless conversion", T, "frames do not apply to opaque doors"),

            // Advisories.
            (NON_CONVEX, "Non-convex surface casts shadows: PixelCounting (the resolved shading calculation method) has no concavity limitation", T, "advisory, no effect under PixelCounting"),
            (ADJ, "No floor-group panels found; building stories were derived from space minimum elevations", T, "stories do not change the loads"),
            (GEO_CLEAN, "Removed 2 duplicate vertices", D, "per-object clean-up"),

            // Import.
            (Core.OpenStudio.OpenStudioImportDiagnosticCodes.OsmVersionTranslationFailed, "The OSM was written by OpenStudio 3.9.0 and translated on load", S, "translation note"),
            (Core.OpenStudio.OpenStudioImportDiagnosticCodes.OswEnergyPlusMeasureNotRepresentable, "The workflow has 3 step(s). Any EnergyPlus measure among them modifies only the generated IDF", S, "changes cannot appear"),
            (Core.OpenStudio.OpenStudioImportDiagnosticCodes.OswWorkflowExecuted, "The workflow was executed; the imported model is the final post-model-measure OSM", S, "workflow outcome"),
            (Core.OpenStudio.OpenStudioImportDiagnosticCodes.OswNoSimulationResults, "The workflow completed (exit code 0, no fatal errors) and produced the model", S, "workflow outcome"),
            (Core.OpenStudio.OpenStudioImportDiagnosticCodes.SimulationSettingUnsupported, "Simulation settings (RunPeriod) have no SAM AnalyticalModel equivalent", S, "settings not imported"),
            (Core.OpenStudio.OpenStudioImportDiagnosticCodes.LoadUnsupported, "The People activity level is zero or varies through the year", S, "gain not imported"),
            (Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied, "Subsurface type 'TubularDaylightDome' was imported as a SAM aperture", S, "approximation"),
            (Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied, "Construction 'X' has opaque (non-glazing) layers but is used by a subsurface", S, "approximation"),
            (Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied, "Electric equipment 'E' is 20% latent; the gain was split", S, "approximation"),
            (Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied, "Massless material (R = 0.1 m²K/W) imported as a SAM material", S, "approximation"),
            (Core.OpenStudio.OpenStudioImportDiagnosticCodes.ApproximationApplied, "Shading group 'G' declares a shaded surface", S, "approximation"),
            (Core.OpenStudio.OpenStudioImportDiagnosticCodes.WeatherLimitation, "The model's Site still carries the default (0, 0, 0) coordinates", S, "no Location"),
            (Core.OpenStudio.OpenStudioImportDiagnosticCodes.WeatherLimitation, "Site terrain 'City' and time zone 0 have no SAM Location equivalent", S, "not imported"),
            (Core.OpenStudio.OpenStudioImportDiagnosticCodes.WeatherLimitation, "The model references the weather file 'a.epw' (x, y) but it is missing", S, "no WeatherData"),
            (Core.OpenStudio.OpenStudioImportDiagnosticCodes.WeatherLimitation, "Hourly weather was loaded from the referenced EPW 'a.epw' (1 year(s))", S, "weather basis"),
            (Core.OpenStudio.OpenStudioImportDiagnosticCodes.ZoneMultiSpace, "ThermalZone 'Z' contains 2 spaces; each becomes its own SAM space", T, "structure note"),
            (Core.OpenStudio.OpenStudioImportDiagnosticCodes.GeometryVerticesCleaned, "Removed 1 collinear vertex", D, "per-object clean-up"),
        };

        [Test]
        public void EveryInformationCallSite_HasThePinnedLevel()
        {
            foreach ((string code, string message, Core.DiagnosticDetail expected, string why) in informationSites)
            {
                Core.OpenStudio.OpenStudioDiagnostic diagnostic = new Core.OpenStudio.OpenStudioDiagnostic(code, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, message);
                Assert.That(diagnostic.GetDiagnosticDetail(), Is.EqualTo(expected), code + " '" + message + "' (" + why + ")");
                Assert.That(diagnostic.ToDiagnostic().Detail, Is.EqualTo(expected));
            }
        }

        [Test]
        public void ResultAffectingInformation_IsVisibleAtSummary()
        {
            // Everything in the table that is not a documented inert line, advisory or clean-up is Summary.
            IEnumerable<(string Code, string Message, Core.DiagnosticDetail Expected, string Why)> notSummary = informationSites.Where(x => x.Expected != S);
            Assert.That(notSummary.Count(), Is.EqualTo(9), "Only the four inert lines, the three forward advisories/clean-ups and the two import notes are below Summary; adding to this list is a deliberate decision");

            foreach (var site in informationSites.Where(x => x.Expected == S))
            {
                List<string> summary = new[] { new Core.OpenStudio.OpenStudioDiagnostic(site.Code, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, site.Message) }.FormatDiagnostics(Core.DiagnosticDetail.Summary);
                Assert.That(summary, Has.Count.EqualTo(1), site.Message);
            }
        }

        [Test]
        public void ReworderOrUnknownMessageUnderAResultAffectingCode_StaysAtSummary()
        {
            // The inert list matches message prefixes; anything else under these codes must stay visible, so a rewording can only add lines.
            foreach (string code in new[] { MAT_PARAM, IC_PARAM, CON_PARAM })
            {
                Assert.That(new Core.OpenStudio.OpenStudioDiagnostic(code, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "Something else is not converted").GetDiagnosticDetail(), Is.EqualTo(S), code);
                Assert.That(new Core.OpenStudio.OpenStudioDiagnostic(code, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, null).GetDiagnosticDetail(), Is.EqualTo(S), code + " with a null message");
                Assert.That(new Core.OpenStudio.OpenStudioDiagnostic(code, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "vapour diffusion factor (lower case)").GetDiagnosticDetail(), Is.EqualTo(S), code + " prefix match is exact");
            }

            // The inert prefix only applies to its own code.
            Assert.That(new Core.OpenStudio.OpenStudioDiagnostic(CON_PARAM, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "Vapour diffusion factor (10) is not converted").GetDiagnosticDetail(), Is.EqualTo(S));
        }

        [Test]
        public void SeverityAndOrder_AreIndependentOfTheDetailTable()
        {
            // Original severity, code, message and order are untouched whatever level an Information line has.
            List<Core.OpenStudio.OpenStudioDiagnostic> source = informationSites
                .SelectMany(x => new[]
                {
                    new Core.OpenStudio.OpenStudioDiagnostic(x.Code, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, x.Message),
                    new Core.OpenStudio.OpenStudioDiagnostic(x.Code, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, x.Message),
                    new Core.OpenStudio.OpenStudioDiagnostic(x.Code, Core.OpenStudio.OpenStudioDiagnosticSeverity.Error, x.Message),
                }).ToList();

            foreach (Core.DiagnosticDetail detail in new[] { S, T, D })
            {
                List<Core.OpenStudio.OpenStudioDiagnosticRecord> kept = source.CreateDiagnosticRecords().FilterDiagnosticRecords(detail);

                Assert.That(kept.Where(x => x.Source.Severity != Core.OpenStudio.OpenStudioDiagnosticSeverity.Information).Select(x => x.Source), Is.EqualTo(source.Where(x => x.Severity != Core.OpenStudio.OpenStudioDiagnosticSeverity.Information)), "Every Warning and Error, same instances, same order, at " + detail);
                Assert.That(kept.Select(x => x.Source), Is.EqualTo(kept.Select(x => x.Source).OrderBy(x => source.IndexOf(x))), "Order is the production order at " + detail);
            }
        }

        // --- the same lines from the real converters -------------------------------------------------------------------

        private static List<Core.OpenStudio.OpenStudioDiagnostic> Information(OpenStudioConversionResult result)
        {
            return result.Diagnostics.Where(x => x.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Information).ToList();
        }

        [Test]
        public void RealConverters_ViewCoefficientsAreDetailed_LightingControlIsSummary()
        {
            InternalCondition internalCondition = AnalyticalModelFixtures.CreateOfficeInternalCondition();
            internalCondition.SetValue(InternalConditionParameter.OccupancyViewCoefficient, 0.227);
            internalCondition.SetValue(InternalConditionParameter.EquipmentViewCoefficient, 0.372);
            internalCondition.SetValue(InternalConditionParameter.LightingControlFunction, "PHOTOCELL 300");

            using (OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(internalConditionOverride: internalCondition).ToOpenStudio())
            {
                List<Core.OpenStudio.OpenStudioDiagnostic> information = Information(result).Where(x => x.Code == IC_PARAM).ToList();

                Assert.That(information.Single(x => x.Message.Contains("Occupancy view coefficient")).GetDiagnosticDetail(), Is.EqualTo(D));
                Assert.That(information.Single(x => x.Message.Contains("Equipment view coefficient")).GetDiagnosticDetail(), Is.EqualTo(D));
                Assert.That(information.Single(x => x.Message.Contains("Lighting control function")).GetDiagnosticDetail(), Is.EqualTo(S));
            }
        }

        [Test]
        public void RealConverters_EmitterAndExhaustDeferralsAreSummary()
        {
            InternalCondition internalCondition = AnalyticalModelFixtures.CreateOfficeInternalCondition();
            internalCondition.SetValue(InternalConditionParameter.HeatingEmitterCoefficient, 0.7);
            internalCondition.SetValue(InternalConditionParameter.ExhaustAirFlow, 0.05);

            using (OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(internalConditionOverride: internalCondition).ToOpenStudio())
            {
                // S09: the fixture's condition selects no Ventilation profile, so the same code also reports that its
                // outdoor-air requirement is kept but not delivered; only the two deferrals are counted here.
                List<Core.OpenStudio.OpenStudioDiagnostic> information = Information(result).Where(x => x.Code == HVAC && x.Message.Contains("deferred")).ToList();
                Assert.That(information.Count, Is.EqualTo(2));
                Assert.That(information.All(x => x.GetDiagnosticDetail() == S), Is.True);
                Assert.That(Information(result).Where(x => x.Code == HVAC).All(x => x.GetDiagnosticDetail() == S), Is.True);
            }
        }

        [Test]
        public void RealConverters_VapourDiffusionIsDetailed_DivergentOpticsIsSummary()
        {
            Core.MaterialLibrary materialLibrary = AnalyticalModelFixtures.CreateMaterialLibrary();
            Core.OpaqueMaterial brick = materialLibrary.GetMaterial("Brick") as Core.OpaqueMaterial;
            brick.SetValue(MaterialParameter.VapourDiffusionFactor, 10.0);
            brick.SetValue(OpaqueMaterialParameter.InternalEmissivity, 0.4);
            materialLibrary.Add(brick);

            using (OpenStudioConversionResult result = DeclaredDiagnosticsTests.SingleBoxModel(materialLibrary).ToOpenStudio())
            {
                List<Core.OpenStudio.OpenStudioDiagnostic> information = Information(result).Where(x => x.Code == MAT_PARAM).ToList();

                Assert.That(information.Single(x => x.Message.StartsWith("Vapour diffusion factor")).GetDiagnosticDetail(), Is.EqualTo(D));
                Assert.That(information.Single(x => x.Message.StartsWith("Internal-side optical properties")).GetDiagnosticDetail(), Is.EqualTo(S));
            }
        }

        [Test]
        public void RealConverters_WeatherAndDesignDayBasis_IsSummary_WithoutTheCli()
        {
            string epwPath = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "resources", "weather", "USA_MA_Boston-Logan.Intl.AP.725090_TMYx.2004-2018.epw"));
            string outputDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "d1_detail_weather");
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, true);
            }

            using (OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox().ToOpenStudio(epwPath, outputDirectory, null, null, false))
            {
                List<Core.OpenStudio.OpenStudioDiagnostic> information = Information(result).Where(x => x.Code == WEATHER || x.Code == RUN).ToList();

                Assert.That(information.Any(x => x.Message.StartsWith("Annual weather source")), Is.True, "The weather basis is reported");
                Assert.That(information.All(x => x.GetDiagnosticDetail() == S), Is.True, string.Join(" | ", information.Select(x => x.Message)));
                Assert.That(result.GetDiagnostics(Core.DiagnosticDetail.Summary).Count(x => x.Code == WEATHER), Is.EqualTo(information.Count(x => x.Code == WEATHER)));
            }
        }

        [Test]
        public void RealConverters_EveryInformationLineBelowSummaryIsAKnownInertOrAdvisoryLine()
        {
            // Across a corpus of real conversions: an Information line is below Summary only when it is one of the pinned lines.
            InternalCondition internalCondition = AnalyticalModelFixtures.CreateOfficeInternalCondition();
            internalCondition.SetValue(InternalConditionParameter.OccupancyViewCoefficient, 0.227);
            internalCondition.SetValue(InternalConditionParameter.EquipmentViewCoefficient, 0.372);
            internalCondition.SetValue(InternalConditionParameter.HeatingEmitterCoefficient, 0.7);

            Core.MaterialLibrary materialLibrary = AnalyticalModelFixtures.CreateMaterialLibrary();
            Core.OpaqueMaterial brick = materialLibrary.GetMaterial("Brick") as Core.OpaqueMaterial;
            brick.SetValue(MaterialParameter.VapourDiffusionFactor, 10.0);
            materialLibrary.Add(brick);

            List<Func<AnalyticalModel>> models = new List<Func<AnalyticalModel>>
            {
                () => AnalyticalModelFixtures.SingleBox(internalConditionOverride: internalCondition),
                () => DeclaredDiagnosticsTests.SingleBoxModel(materialLibrary),
                () => AnalyticalModelFixtures.TwoAdjacentBoxes(),
                () => AnalyticalModelFixtures.TwoStackedBoxes(),
                () => AnalyticalModelFixtures.LShapedBox(),
                () => AnalyticalModelFixtures.OpaqueAirGapBox(),
            };

            int checkedLines = 0;
            foreach (Func<AnalyticalModel> create in models)
            {
                using (OpenStudioConversionResult result = create().ToOpenStudio())
                {
                    foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in Information(result))
                    {
                        checkedLines++;
                        Core.DiagnosticDetail detail = diagnostic.GetDiagnosticDetail();
                        if (detail != S)
                        {
                            Assert.That(informationSites.Any(x => x.Code == diagnostic.Code && x.Expected == detail && diagnostic.Message.StartsWith(x.Message.Substring(0, Math.Min(18, x.Message.Length)))), Is.True, "Unpinned lowered line: " + diagnostic);
                        }
                    }
                }
            }

            Assert.That(checkedLines, Is.GreaterThan(0), "The corpus produced Information lines");
        }
    }
}
