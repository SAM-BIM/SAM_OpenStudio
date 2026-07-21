// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnalyticalCreate = SAM.Analytical.Create;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// C1: boundary-condition regression tests — the Adiabatic panel flag must override the
    /// PanelType mapping (LadybugTools New/BoundaryCondition.cs parity; coverage manifest
    /// PanelParameter.Adiabatic / BoundaryType.Adiabatic) and an Air panel must convert to a
    /// shared OS:Construction:AirBoundary on both paired sides (coverage manifest PanelType.Air).
    /// </summary>
    [TestFixture]
    public class BoundaryConditionTests
    {
        private static List<global::OpenStudio.Surface> Surfaces(global::OpenStudio.Model model)
        {
            List<global::OpenStudio.Surface> surfaces = new List<global::OpenStudio.Surface>();
            foreach (global::OpenStudio.Surface surface in model.getSurfaces())
            {
                surfaces.Add(surface);
            }

            return surfaces;
        }

        private static Geometry.Spatial.Face3D F(params Geometry.Spatial.Point3D[] point3Ds)
        {
            return new Geometry.Spatial.Face3D(new Geometry.Spatial.Polygon3D(point3Ds));
        }

        /// <summary>The pinned production model carrying an Air partition and two adiabatic slabs (tests/resources/models).</summary>
        private static AnalyticalModel AdiabaticAirPanelModel()
        {
            string path = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "resources", "models", "rawModel-WindowsWeatherDataDDYAdiabaticAirPanel.sam"));
            Assert.That(File.Exists(path), Is.True, "Pinned model fixture missing: " + path);

            AnalyticalModel result = Core.Convert.ToSAM<AnalyticalModel>(path)?.FirstOrDefault();
            Assert.That(result, Is.Not.Null, "The pinned model must load");
            return result;
        }

        private static global::OpenStudio.ConstructionAirBoundary AirBoundary(global::OpenStudio.Model model)
        {
            foreach (global::OpenStudio.ConstructionAirBoundary constructionAirBoundary in model.getConstructionAirBoundarys())
            {
                return constructionAirBoundary;
            }

            return null;
        }

        [Test]
        public void AdiabaticFlag_OverridesPanelTypeMapping()
        {
            Panel floor = AnalyticalCreate.Panel(AnalyticalModelFixtures.WallConstruction, PanelType.SlabOnGrade, F(new Geometry.Spatial.Point3D(0, 0, 0), new Geometry.Spatial.Point3D(5, 0, 0), new Geometry.Spatial.Point3D(5, 4, 0), new Geometry.Spatial.Point3D(0, 4, 0)));

            Assert.That(floor.PanelType.OutsideBoundaryCondition(), Is.EqualTo("Ground"), "The PanelType mapping is unchanged: SlabOnGrade stays Ground");
            Assert.That(floor.OutsideBoundaryCondition(), Is.EqualTo("Ground"), "Without the flag the panel follows its PanelType");

            floor.SetValue(PanelParameter.Adiabatic, true);
            Assert.That(floor.OutsideBoundaryCondition(), Is.EqualTo("Adiabatic"), "The Adiabatic flag must win over the SlabOnGrade Ground mapping");

            floor.SetValue(PanelParameter.Adiabatic, false);
            Assert.That(floor.OutsideBoundaryCondition(), Is.EqualTo("Ground"), "An explicit false flag falls back to the PanelType mapping");
        }

        [Test]
        public void AirPanel_IsNeverAdiabatic()
        {
            Panel airPanel = AnalyticalCreate.Panel(null, PanelType.Air, F(new Geometry.Spatial.Point3D(5, 0, 0), new Geometry.Spatial.Point3D(5, 4, 0), new Geometry.Spatial.Point3D(5, 4, 3), new Geometry.Spatial.Point3D(5, 0, 3)));

            Assert.That(airPanel.Adiabatic(), Is.False, "Air panels are never adiabatic, even without a construction");
            Assert.That(airPanel.OutsideBoundaryCondition(), Is.EqualTo("Surface"), "An Air panel stays an internal (paired) boundary");
        }

        [Test]
        public void AdiabaticFloors_ConvertToAdiabaticSurfaces()
        {
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.TwoAdjacentBoxes();
            AdjacencyCluster adjacencyCluster = analyticalModel.AdjacencyCluster;
            foreach (Panel panel in adjacencyCluster.GetPanels())
            {
                if (panel.PanelType != PanelType.SlabOnGrade)
                {
                    continue;
                }

                panel.SetValue(PanelParameter.Adiabatic, true);
                adjacencyCluster.AddObject(panel);
            }

            OpenStudioConversionResult result = new AnalyticalModel(analyticalModel, adjacencyCluster).ToOpenStudio();
            Assert.That(result.IsValid, Is.True, "Conversion must be valid (warnings allowed)");

            List<global::OpenStudio.Surface> surfaces = Surfaces(result.Model);
            Assert.That(surfaces.Count(s => s.outsideBoundaryCondition() == "Ground"), Is.EqualTo(0), "No surface may stay ground-coupled once its panel is flagged adiabatic");

            List<global::OpenStudio.Surface> adiabaticSurfaces = surfaces.FindAll(s => s.outsideBoundaryCondition() == "Adiabatic");
            Assert.That(adiabaticSurfaces.Count, Is.EqualTo(2), "Both flagged floors convert to Adiabatic surfaces");
            foreach (global::OpenStudio.Surface surface in adiabaticSurfaces)
            {
                Assert.That(surface.surfaceType(), Is.EqualTo("Floor"), "The surface type is unaffected by the boundary condition");
                Assert.That(surface.sunExposure(), Is.EqualTo("NoSun"));
                Assert.That(surface.windExposure(), Is.EqualTo("NoWind"));
            }

            Assert.That(surfaces.Count(s => s.outsideBoundaryCondition() == "Outdoors"), Is.EqualTo(8), "Roofs and external walls are untouched");
            Assert.That(surfaces.Count(s => s.outsideBoundaryCondition() == "Surface"), Is.EqualTo(2), "The shared internal wall is untouched");
        }

        [Test]
        public void AdiabaticInternalPanel_DropsThePairing_BothSidesAdiabatic()
        {
            // The flag changes more than the boundary string on an internal panel: "Adiabatic"
            // no longer takes the "Surface" branch, so the two engine surfaces are deliberately
            // NOT paired with setAdjacentSurface. That is the correct EnergyPlus model of an
            // adiabatic partition — no heat crosses it — and it must stay a clean conversion.
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.TwoAdjacentBoxes();
            AdjacencyCluster adjacencyCluster = analyticalModel.AdjacencyCluster;
            Panel sharedWall = adjacencyCluster.GetPanels().Single(x => x.PanelType == PanelType.WallInternal);
            sharedWall.SetValue(PanelParameter.Adiabatic, true);
            adjacencyCluster.AddObject(sharedWall);

            OpenStudioConversionResult result = new AnalyticalModel(analyticalModel, adjacencyCluster).ToOpenStudio();
            Assert.That(result.IsValid, Is.True, "Conversion must be valid (warnings allowed)");

            List<global::OpenStudio.Surface> surfaces = Surfaces(result.Model);
            List<global::OpenStudio.Surface> adiabaticSurfaces = surfaces.FindAll(s => s.outsideBoundaryCondition() == "Adiabatic");
            Assert.That(adiabaticSurfaces.Count, Is.EqualTo(2), "Both sides of the flagged partition are adiabatic");
            Assert.That(surfaces.Count(s => s.outsideBoundaryCondition() == "Surface"), Is.EqualTo(0), "No surface stays paired once the partition is adiabatic");

            foreach (global::OpenStudio.Surface surface in adiabaticSurfaces)
            {
                global::OpenStudio.OptionalSurface adjacent = surface.adjacentSurface();
                Assert.That(adjacent == null || adjacent.isNull(), Is.True, string.Format("{0} must not keep an adjacent surface", surface.nameString()));
            }

            // Skipping the pairing is intentional, not a failure to pair — the "single space" and
            // "OpenStudio rejected the pairing" warnings belong to the Surface branch only.
            Assert.That(result.Diagnostics.Any(d => d.Message.Contains("bounds a single space") || d.Message.Contains("rejected the adjacent-surface pairing")), Is.False, "An adiabatic partition is not a pairing failure: " + string.Join(" | ", result.Diagnostics.Select(d => d.Message)));
        }

        [Test]
        public void AirPanel_ConvertsToSharedAirBoundaryConstruction()
        {
            OpenStudioConversionResult result = AnalyticalModelFixtures.TwoAdjacentBoxes(sharedWallIsAir: true).ToOpenStudio();

            foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in result.Diagnostics)
            {
                TestContext.Out.WriteLine(diagnostic.ToString());
            }

            Assert.That(result.IsValid, Is.True, "Conversion must be valid (warnings allowed)");

            List<global::OpenStudio.Surface> internalSurfaces = Surfaces(result.Model).FindAll(s => s.outsideBoundaryCondition() == "Surface");
            Assert.That(internalSurfaces.Count, Is.EqualTo(2), "Both sides of the Air panel remain an explicitly paired internal boundary");

            foreach (global::OpenStudio.Surface surface in internalSurfaces)
            {
                global::OpenStudio.OptionalConstructionBase constructionBase = surface.construction();
                Assert.That(constructionBase != null && !constructionBase.isNull(), string.Format("Surface {0} must carry the air-boundary construction", surface.nameString()));
                Assert.That(constructionBase.get().nameString(), Is.EqualTo("SAM_Construction_AirBoundary"));
            }

            global::OpenStudio.OptionalSurface adjacent = internalSurfaces[0].adjacentSurface();
            Assert.That(adjacent != null && !adjacent.isNull(), "The Air panel sides must stay paired");
            Assert.That(adjacent.get().nameString(), Is.EqualTo(internalSurfaces[1].nameString()));
        }

        [Test]
        public void ProductionModel_AdiabaticSlabsAndAirPartition_Convert()
        {
            // The real Rhino model both features came from: two cells joined by an Air partition,
            // both ground slabs flagged adiabatic. Synthetic fixtures can drift from what the
            // Grasshopper definition actually produces; this one cannot.
            OpenStudioConversionResult result = AdiabaticAirPanelModel().ToOpenStudio();
            Assert.That(result.IsValid, Is.True, "Conversion must be valid: " + string.Join(" | ", result.Diagnostics.Where(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error).Select(d => d.Message)));

            List<global::OpenStudio.Surface> surfaces = Surfaces(result.Model);
            Assert.That(result.Model.getThermalZones().Count, Is.EqualTo(2), "Cell 1 and Cell 2");

            // Both flagged slabs are adiabatic, and nothing is left ground-coupled.
            Assert.That(surfaces.Count(s => s.outsideBoundaryCondition() == "Adiabatic"), Is.EqualTo(2), "Both Adiabatic-flagged slabs: " + string.Join(", ", surfaces.Select(s => s.surfaceType() + "=" + s.outsideBoundaryCondition())));
            Assert.That(surfaces.Count(s => s.outsideBoundaryCondition() == "Ground"), Is.EqualTo(0), "No slab stays ground-coupled");
            foreach (global::OpenStudio.Surface surface in surfaces.FindAll(s => s.outsideBoundaryCondition() == "Adiabatic"))
            {
                Assert.That(surface.surfaceType(), Is.EqualTo("Floor"));
            }

            // The Air partition pairs and carries the air-boundary construction on both sides.
            List<global::OpenStudio.Surface> pairedSurfaces = surfaces.FindAll(s => s.outsideBoundaryCondition() == "Surface");
            Assert.That(pairedSurfaces.Count, Is.EqualTo(2), "The Air partition is one panel seen from both cells");
            foreach (global::OpenStudio.Surface surface in pairedSurfaces)
            {
                Assert.That(surface.construction().get().nameString(), Is.EqualTo("SAM_Construction_AirBoundary"));
            }

            Assert.That(result.Model.getSubSurfaces().Count, Is.EqualTo(10), "The model's windows still convert");
        }

        [Test]
        public void AirBoundary_AirExchangeMethod_DefaultsToNone_AndIsDiagnosed()
        {
            OpenStudioConversionResult result = AdiabaticAirPanelModel().ToOpenStudio();

            global::OpenStudio.ConstructionAirBoundary constructionAirBoundary = AirBoundary(result.Model);
            Assert.That(constructionAirBoundary, Is.Not.Null);
            Assert.That(constructionAirBoundary.airExchangeMethod(), Is.EqualTo("None"), "Without an explicit rate no air moves between the zones — SAM carries no airflow data");
            Assert.That(result.Diagnostics.Any(d => d.Message.Contains("Air Exchange Method 'None'") && d.Message.Contains("exchange no air")), Is.True, "The uncoupled boundary is a named modelling decision, not silence: " + string.Join(" | ", result.Diagnostics.Select(d => d.Message)));
        }

        [Test]
        public void AirBoundary_AirChangesPerHour_EnablesSimpleMixing()
        {
            Core.OpenStudio.OpenStudioConversionOptions options = new Core.OpenStudio.OpenStudioConversionOptions { AirBoundaryAirChangesPerHour = 0.5 };
            OpenStudioConversionResult result = AdiabaticAirPanelModel().ToOpenStudio(options);

            global::OpenStudio.ConstructionAirBoundary constructionAirBoundary = AirBoundary(result.Model);
            Assert.That(constructionAirBoundary, Is.Not.Null);
            Assert.That(constructionAirBoundary.airExchangeMethod(), Is.EqualTo("SimpleMixing"));
            Assert.That(constructionAirBoundary.simpleMixingAirChangesPerHour(), Is.EqualTo(0.5).Within(1e-9));
            Assert.That(result.Diagnostics.Any(d => d.Message.Contains("SimpleMixing") && d.Message.Contains("0.5") && d.Message.Contains("caller-supplied assumption")), Is.True, "The assumed rate is named: " + string.Join(" | ", result.Diagnostics.Select(d => d.Message)));

            // The surfaces still pair — mixing is a property of the construction, not the boundary.
            Assert.That(Surfaces(result.Model).Count(s => s.outsideBoundaryCondition() == "Surface"), Is.EqualTo(2));
        }

        [Test]
        public void AirBoundary_ZeroAirChangesPerHour_IsAnExplicitChoice_NotOff()
        {
            // 0 is a valid rate the caller asked for (mixing modelled, schedule always on, no
            // flow) and must not be silently reinterpreted as "no option supplied".
            Core.OpenStudio.OpenStudioConversionOptions options = new Core.OpenStudio.OpenStudioConversionOptions { AirBoundaryAirChangesPerHour = 0 };
            OpenStudioConversionResult result = AdiabaticAirPanelModel().ToOpenStudio(options);

            global::OpenStudio.ConstructionAirBoundary constructionAirBoundary = AirBoundary(result.Model);
            Assert.That(constructionAirBoundary.airExchangeMethod(), Is.EqualTo("SimpleMixing"));
            Assert.That(constructionAirBoundary.simpleMixingAirChangesPerHour(), Is.EqualTo(0.0).Within(1e-9));
        }

        [Test]
        [Category("Simulation")]
        public void AirBoundary_SimpleMixing_ReachesEnergyPlus()
        {
            // Setting the option is only half the claim — EnergyPlus has to accept it. This runs
            // the real model through the CLI and reads the generated IDF back.
            Core.OpenStudio.OpenStudioConversionOptions options = new Core.OpenStudio.OpenStudioConversionOptions { AirBoundaryAirChangesPerHour = 0.5 };
            string outputDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "c1_airmixing_" + System.Guid.NewGuid().ToString("N").Substring(0, 8));

            OpenStudioConversionResult result = AdiabaticAirPanelModel().ToOpenStudio(null, outputDirectory, options);

            Assert.That(result.RunResult?.Success, Is.True, "EnergyPlus must accept the mixing air boundary: " + string.Join(" | ", result.Diagnostics.Where(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error).Select(d => d.Message)));
            Assert.That(result.RunResult.SevereErrors, Is.Empty, "No severe errors: " + string.Join(" | ", result.RunResult.SevereErrors));

            string idfPath = Directory.GetFiles(outputDirectory, "in.idf", SearchOption.AllDirectories).FirstOrDefault();
            Assert.That(idfPath, Is.Not.Null, "The run produced an IDF");

            string idf = File.ReadAllText(idfPath);
            int index = idf.IndexOf("Construction:AirBoundary", System.StringComparison.Ordinal);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "The IDF carries the air boundary");

            string block = idf.Substring(index, System.Math.Min(400, idf.Length - index));
            Assert.That(block, Does.Contain("SimpleMixing"), "The air exchange method reaches EnergyPlus: " + block);
            Assert.That(block, Does.Contain("0.5"), "The air change rate reaches EnergyPlus: " + block);
        }

        [Test]
        public void AirBoundary_InvalidAirChangesPerHour_KeepsNone_WithWarning()
        {
            Core.OpenStudio.OpenStudioConversionOptions options = new Core.OpenStudio.OpenStudioConversionOptions { AirBoundaryAirChangesPerHour = -1 };
            OpenStudioConversionResult result = AdiabaticAirPanelModel().ToOpenStudio(options);

            global::OpenStudio.ConstructionAirBoundary constructionAirBoundary = AirBoundary(result.Model);
            Assert.That(constructionAirBoundary.airExchangeMethod(), Is.EqualTo("None"), "A negative rate is rejected, never clamped");
            Assert.That(result.Diagnostics.Any(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning && d.Message.Contains("not a valid rate")), Is.True, "The rejected value is reported: " + string.Join(" | ", result.Diagnostics.Select(d => d.Message)));
        }
    }
}
