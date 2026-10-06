// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>M7: fixture regression suite — semantic assertions, failure policy, determinism, no source mutation (plan §12/§13).</summary>
    [TestFixture]
    public class RegressionTests
    {
        [Test]
        public void TwoLevels_AssignSpacesToStories_AndFlipSharedFloorTypes()
        {
            OpenStudioConversionResult result = AnalyticalModelFixtures.TwoStackedBoxes().ToOpenStudio();
            global::OpenStudio.Model model = result.Model;

            foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in result.Diagnostics)
            {
                TestContext.Out.WriteLine(diagnostic.ToString());
            }

            Assert.That(result.IsValid, Is.True);
            Assert.That(model.getSpaces().Count, Is.EqualTo(2));
            Assert.That(model.getBuildingStorys().Count, Is.GreaterThanOrEqualTo(2), "At least the 0 m and 3 m stories must exist");

            foreach (global::OpenStudio.Space space in model.getSpaces())
            {
                global::OpenStudio.OptionalBuildingStory story = space.buildingStory();
                Assert.That(story != null && !story.isNull(), $"{space.nameString()} must be assigned to a story");

                bool isLower = space.nameString().Contains("Lower");
                Assert.That(story.get().nameString(), Does.Contain(isLower ? "_0m" : "_3m"), $"{space.nameString()} story");
            }

            List<global::OpenStudio.Surface> internalSurfaces = new List<global::OpenStudio.Surface>();
            foreach (global::OpenStudio.Surface surface in model.getSurfaces())
            {
                if (surface.outsideBoundaryCondition() == "Surface")
                {
                    internalSurfaces.Add(surface);
                }
            }

            Assert.That(internalSurfaces.Count, Is.EqualTo(2), "Both sides of the shared internal floor");

            List<string> surfaceTypes = internalSurfaces.ConvertAll(s => s.surfaceType());
            Assert.That(surfaceTypes, Does.Contain("RoofCeiling"), "The lower space sees the shared panel as its ceiling");
            Assert.That(surfaceTypes, Does.Contain("Floor"), "The upper space sees the shared panel as its floor");
        }

        [Test]
        public void ConditionedUnconditionedPair_OnlyConditionedZoneGetsHvac()
        {
            OpenStudioConversionResult result = AnalyticalModelFixtures.TwoAdjacentBoxes(spaceBUnconditioned: true).ToOpenStudio();
            global::OpenStudio.Model model = result.Model;

            Assert.That(result.IsValid, Is.True);
            Assert.That(model.getThermalZones().Count, Is.EqualTo(2), "Unconditioned space keeps its zone and geometry");
            Assert.That(model.getThermostatSetpointDualSetpoints().Count, Is.EqualTo(1), "Only the conditioned zone gets a thermostat");
            Assert.That(model.getZoneHVACIdealLoadsAirSystems().Count, Is.EqualTo(1), "Only the conditioned zone gets Ideal Loads");
            Assert.That(model.getSpaceTypes().Count, Is.EqualTo(2), "The unconditioned condition still carries its gains via its own SpaceType");
        }

        [Test]
        public void IrregularPlanarRoom_IsCleaned_WithWarning_AreaPreserved()
        {
            OpenStudioConversionResult result = AnalyticalModelFixtures.IrregularPlanarBox().ToOpenStudio();

            Assert.That(result.IsValid, Is.True, "Cleaning is a warning, not an error");

            foreach (global::OpenStudio.Space space in result.Model.getSpaces())
            {
                Assert.That(space.floorArea, Is.EqualTo(20.0).Within(1e-6), "Cleaning must not change the floor area");
            }

            Assert.That(result.Model.getSurfaces().Count, Is.EqualTo(6));
        }

        [Test]
        public void DegeneratePanel_IsSkippedWithDiagnostic_NeverRepairedOrSilent()
        {
            OpenStudioConversionResult result = AnalyticalModelFixtures.DegeneratePanelBox().ToOpenStudio();

            foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in result.Diagnostics)
            {
                TestContext.Out.WriteLine(diagnostic.ToString());
            }

            // The degenerate sliver makes SAM's own shell computation (UpdateNormals →
            // Panel.Face3D) throw for the whole space. The converter must isolate that kernel
            // failure: space-level SAM-OS-GEO-001 error, per-panel no-surface warnings, no crash,
            // no repair — and the result is invalid.
            Assert.That(result.IsValid, Is.False, "A space whose shell cannot be computed must invalidate the conversion");
            Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OS-GEO-001" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error && d.Message.Contains("shell")), Is.True, "The kernel failure must be a diagnosed error, never a crash");
            Assert.That(result.Diagnostics.Count(d => d.Code == "SAM-OS-GEO-001" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning && d.Message.Contains("no surface")), Is.EqualTo(6), "Every unconverted panel must be reported — no silent drops");
            Assert.That(result.Model.getSurfaces().Count, Is.EqualTo(0), "No surfaces can be created when the space shell fails; nothing is repaired");
        }

        [Test]
        public void RepeatedConversion_DoesNotMutate_TheSourceModel()
        {
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.TwoAdjacentBoxes();
            AdjacencyCluster adjacencyCluster = analyticalModel.AdjacencyCluster;

            List<Panel> panelsBefore = adjacencyCluster.GetPanels();
            Dictionary<Guid, double[]> normalsBefore = panelsBefore.ToDictionary(p => p.Guid, p => new[] { p.Normal.X, p.Normal.Y, p.Normal.Z });
            Dictionary<Guid, int> apertureCountsBefore = panelsBefore.ToDictionary(p => p.Guid, p => p.Apertures?.Count ?? 0);
            List<Guid> spaceGuidsBefore = adjacencyCluster.GetSpaces().ConvertAll(s => s.Guid);

            analyticalModel.ToOpenStudio();
            analyticalModel.ToOpenStudio();

            AdjacencyCluster adjacencyClusterAfter = analyticalModel.AdjacencyCluster;
            List<Panel> panelsAfter = adjacencyClusterAfter.GetPanels();

            Assert.That(panelsAfter.Count, Is.EqualTo(panelsBefore.Count), "Panel count must be unchanged");
            Assert.That(adjacencyClusterAfter.GetSpaces().ConvertAll(s => s.Guid), Is.EquivalentTo(spaceGuidsBefore), "Space guids must be unchanged");

            foreach (Panel panel in panelsAfter)
            {
                double[] normalBefore = normalsBefore[panel.Guid];
                Assert.That(panel.Normal.X, Is.EqualTo(normalBefore[0]).Within(1e-12), $"{panel.Name} normal X mutated");
                Assert.That(panel.Normal.Y, Is.EqualTo(normalBefore[1]).Within(1e-12), $"{panel.Name} normal Y mutated");
                Assert.That(panel.Normal.Z, Is.EqualTo(normalBefore[2]).Within(1e-12), $"{panel.Name} normal Z mutated");
                Assert.That(panel.Apertures?.Count ?? 0, Is.EqualTo(apertureCountsBefore[panel.Guid]), $"{panel.Name} aperture count mutated");
            }
        }

        [Test]
        public void RepeatedConversion_ProducesDeterministicNames()
        {
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.TwoAdjacentBoxes();

            OpenStudioConversionResult first = analyticalModel.ToOpenStudio();
            OpenStudioConversionResult second = analyticalModel.ToOpenStudio();

            Assert.That(second.ObjectMap.Count, Is.EqualTo(first.ObjectMap.Count));
            foreach (KeyValuePair<Guid, string> keyValuePair in first.ObjectMap)
            {
                Assert.That(second.ObjectMap.ContainsKey(keyValuePair.Key), Is.True);
                Assert.That(second.ObjectMap[keyValuePair.Key], Is.EqualTo(keyValuePair.Value), "Names must be deterministic across conversions");
            }
        }

        [Test]
        public void RepeatedConversion_WithDisposal_DoesNotCrash()
        {
            for (int i = 0; i < 10; i++)
            {
                OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox().ToOpenStudio();
                Assert.That(result.IsValid, Is.True);
                Assert.That(result.Model.getSurfaces().Count, Is.EqualTo(6));
                result.Model.Dispose();
            }
        }

        [Test]
        [Category("Simulation")]
        public void TwoBoxes_EndToEnd_EnergyPlusRun_BothZonesProduceLoads()
        {
            string epwPath = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "resources", "weather", "USA_MA_Boston-Logan.Intl.AP.725090_TMYx.2004-2018.epw"));
            Assert.That(File.Exists(epwPath), Is.True);

            string outputDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "e2e_two_boxes");
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, true);
            }

            OpenStudioConversionResult result = AnalyticalModelFixtures.TwoAdjacentBoxes().ToOpenStudio(epwPath, outputDirectory);

            foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in result.Diagnostics)
            {
                TestContext.Out.WriteLine(diagnostic.ToString());
            }

            Assert.That(result.RunResult, Is.Not.Null);
            Assert.That(result.RunResult.Success, Is.True, "Two-zone model with matched internal surfaces must simulate cleanly");
            Assert.That(result.RunResult.FatalErrors, Is.Empty);
            Assert.That(result.RunResult.SevereErrors, Is.Empty);

            OpenStudioLoadSummary loads = result.Loads;
            Assert.That(loads, Is.Not.Null);
            Assert.That(loads.ZoneHeating.Count, Is.EqualTo(2), "Both conditioned zones must report results");
            Assert.That(loads.ZoneCooling.Count, Is.EqualTo(2));
            foreach (KeyValuePair<string, double> zone in loads.ZoneHeating)
            {
                Assert.That(zone.Value, Is.GreaterThan(0), $"{zone.Key} must need heating in Boston");
            }

            TestContext.Out.WriteLine(string.Format("Annual heating {0:0.0} kWh, cooling {1:0.0} kWh", loads.TotalHeating, loads.TotalCooling));
        }
    }
}
