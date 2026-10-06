// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Core;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// Review P1-06: SAM GasMaterial in an opaque Construction must become an OpenStudio AirGap
    /// (OS:Material:AirGap, R = 1/h from GasMaterialParameter.HeatTransferCoefficient [W/m²K]);
    /// in ApertureConstruction pane layers it must remain OpenStudio.Gas (OS:WindowMaterial:Gas).
    /// Reproduces the real Rhino failure (AR90UP 50 mm cavities → E+ "R Value below lowest
    /// allowed value" → fatal).
    /// </summary>
    [TestFixture]
    public class GasMaterialUsageTests
    {
        private static global::OpenStudio.Construction CavityWallForwardConstruction(global::OpenStudio.Model model)
        {
            foreach (global::OpenStudio.Construction construction in model.getConstructions())
            {
                if (construction.nameString().Contains("Fixture_Cavity_Wall_Forward"))
                {
                    return construction;
                }
            }

            return null;
        }

        [Test]
        public void OpaqueCavity_BecomesAirGap()
        {
            OpenStudioConversionResult result = AnalyticalModelFixtures.OpaqueAirGapBox().ToOpenStudio();
            Assert.That(result.IsValid, Is.True);

            global::OpenStudio.Construction construction = CavityWallForwardConstruction(result.Model);
            Assert.That(construction, Is.Not.Null);
            Assert.That(construction.layers().Count, Is.EqualTo(3));

            global::OpenStudio.Material cavity = construction.layers()[1];
            Assert.That(cavity.to_AirGap() != null && !cavity.to_AirGap().isNull(), "The opaque cavity layer must be an OpenStudio AirGap (OS:Material:AirGap), never a window gas");
            Assert.That(cavity.to_Gas() != null && cavity.to_Gas().isNull(), "OS:WindowMaterial:Gas must not appear in an opaque construction");
        }

        [Test]
        public void OpaqueCavity_Resistance_FromConductance_1_25()
        {
            OpenStudioConversionResult result = AnalyticalModelFixtures.OpaqueAirGapBox(1.25).ToOpenStudio();
            Assert.That(result.IsValid, Is.True);

            global::OpenStudio.Construction construction = CavityWallForwardConstruction(result.Model);
            global::OpenStudio.AirGap airGap = construction.layers()[1].to_AirGap().get();
            Assert.That(airGap.thermalResistance(), Is.EqualTo(0.8).Within(1e-9), "h = 1.25 W/m²K → R = 1/1.25 = 0.8 m²K/W");
        }

        [Test]
        public void OpaqueCavity_Resistance_FromConductance_1_95()
        {
            GasMaterial cavity195 = new GasMaterial(new System.Guid("33333333-0000-0000-0000-000000000007"), "Fixture Air Cavity");
            cavity195.SetValue(GasMaterialParameter.HeatTransferCoefficient, 1.95);
            cavity195.SetValue(GasMaterialParameter.DefaultGasType, "Air");

            OpenStudioConversionResult result = AnalyticalModelFixtures.OpaqueAirGapBox(1.95, cavity195).ToOpenStudio();
            Assert.That(result.IsValid, Is.True);

            global::OpenStudio.Construction construction = CavityWallForwardConstruction(result.Model);
            global::OpenStudio.AirGap airGap = construction.layers()[1].to_AirGap().get();
            Assert.That(airGap.thermalResistance(), Is.EqualTo(1.0 / 1.95).Within(1e-9), "h = 1.95 W/m²K → R = 1/1.95 ≈ 0.5128205 m²K/W");
        }

        [Test]
        public void WindowGas_RemainsGas_InAperturePane()
        {
            OpenStudioConversionResult result = AnalyticalModelFixtures.OpaqueAirGapBox().ToOpenStudio();
            Assert.That(result.IsValid, Is.True);

            global::OpenStudio.SubSurfaceVector subSurfaces = result.Model.getSubSurfaces();
            Assert.That(subSurfaces.Count, Is.EqualTo(1));

            global::OpenStudio.Construction paneConstruction = subSurfaces[0].construction().get().to_Construction().get();
            Assert.That(paneConstruction.layers().Count, Is.EqualTo(3), "Glass / Air / Glass");

            global::OpenStudio.Material paneGas = paneConstruction.layers()[1];
            Assert.That(paneGas.to_Gas() != null && !paneGas.to_Gas().isNull(), "The pane gas layer must remain OpenStudio.Gas (OS:WindowMaterial:Gas)");
            Assert.That(paneGas.to_Gas().get().gasType(), Is.EqualTo("Air"));
            Assert.That(paneGas.to_Gas().get().thickness(), Is.EqualTo(0.012).Within(1e-9));
        }

        [Test]
        public void SameGasMaterial_TwoContexts_SeparateObjects()
        {
            // The SAME SAM GasMaterial (same Guid) used in an opaque wall layer and in a pane
            // layer must produce two distinct OpenStudio objects (AirGap + Gas), never shared.
            GasMaterial shared = AnalyticalModelFixtures.CreateCavityGasMaterial(1.25);

            ApertureConstruction glazingWithSharedGas = new ApertureConstruction(new System.Guid("22222222-3333-3333-3333-333333333333"), "Shared Gas Glazing", ApertureType.Window, new List<ConstructionLayer>
            {
                new ConstructionLayer("Glass", 0.006),
                new ConstructionLayer(shared.Name, 0.05),
                new ConstructionLayer("Glass", 0.006),
            });

            AnalyticalModel analyticalModel = AnalyticalModelFixtures.OpaqueAirGapBox(1.25, null, false);
            AdjacencyCluster adjacencyCluster = analyticalModel.AdjacencyCluster;
            Panel host = adjacencyCluster.GetPanels().First(p => p.PanelType == PanelType.WallExternal);
            host.AddAperture(Analytical.Create.Aperture(glazingWithSharedGas, new Geometry.Spatial.Face3D(new Geometry.Spatial.Polygon3D(new[]
            {
                new Geometry.Spatial.Point3D(1, 0, 0.8),
                new Geometry.Spatial.Point3D(3, 0, 0.8),
                new Geometry.Spatial.Point3D(3, 0, 2.2),
                new Geometry.Spatial.Point3D(1, 0, 2.2),
            }))));

            OpenStudioConversionResult result = analyticalModel.ToOpenStudio();
            Assert.That(result.IsValid, Is.True);

            Assert.That(result.Model.getAirGaps().Count, Is.EqualTo(1), "Exactly one AirGap instance for the opaque usage");
            Assert.That(result.Model.getGass().Count, Is.EqualTo(1), "Exactly one Gas instance for the fenestration usage");

            global::OpenStudio.Construction wallConstruction = CavityWallForwardConstruction(result.Model);
            global::OpenStudio.Construction paneConstruction = result.Model.getSubSurfaces()[0].construction().get().to_Construction().get();

            Assert.That(wallConstruction.layers()[1].to_AirGap().isNull(), Is.False, "Wall references the AirGap object");
            Assert.That(paneConstruction.layers()[1].to_Gas().isNull(), Is.False, "Pane references the Gas object");
            Assert.That(wallConstruction.layers()[1].to_Gas().isNull(), Is.True, "Cache must never reuse a Gas as an AirGap");
            Assert.That(paneConstruction.layers()[1].to_AirGap().isNull(), Is.True, "Cache must never reuse an AirGap as a Gas");
        }

        [Test]
        public void MissingConductance_RaisesError_NeverSilent()
        {
            MaterialLibrary materialLibrary = AnalyticalModelFixtures.CreateMaterialLibrary();
            materialLibrary.Add(new GasMaterial(new System.Guid("33333333-0000-0000-0000-000000000008"), "Fixture Air Cavity")); // no Heat Transfer Coefficient

            AdjacencyCluster adjacencyCluster = AnalyticalModelFixtures.OpaqueAirGapBox(includeWindow: false).AdjacencyCluster;
            AnalyticalModel analyticalModel = new AnalyticalModel("Missing Conductance Model", "Opaque cavity without conductance", null, null, adjacencyCluster, materialLibrary, AnalyticalModelFixtures.CreateProfileLibrary());

            OpenStudioConversionResult result = analyticalModel.ToOpenStudio();

            Assert.That(result.IsValid, Is.False, "An opaque cavity without conductance must invalidate the conversion");
            Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OS-MAT-001" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error && d.Message.Contains("Heat Transfer Coefficient")), Is.True, "Structured SAM-OS-MAT-001 expected before any CLI execution");
            Assert.That(result.Model.getAirGaps().Count, Is.EqualTo(0), "No silent zero-resistance AirGap may be created");
        }

        [Test]
        public void ResistanceBelowEnergyPlusMinimum_RaisesError()
        {
            OpenStudioConversionResult result = AnalyticalModelFixtures.OpaqueAirGapBox(2000).ToOpenStudio();

            Assert.That(result.IsValid, Is.False, "h = 2000 W/m²K → R = 0.0005 m²K/W, below the EnergyPlus per-layer minimum 0.001 — must be rejected before simulation");
            Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OS-MAT-001" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error && d.Message.Contains("minimum")), Is.True);
        }

        [Test]
        public void GlazingLayer_InOpaqueConstruction_IsRejected()
        {
            Construction mixedConstruction = new Construction(new System.Guid("99999999-0000-0000-0000-000000000002"), "Mixed Family Wall", new List<ConstructionLayer>
            {
                new ConstructionLayer("Plasterboard", 0.0125),
                new ConstructionLayer("Glass", 0.006),
                new ConstructionLayer("Brick", 0.1),
            });

            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(mixedConstruction, includeWindow: false).ToOpenStudio();

            Assert.That(result.IsValid, Is.False, "A fenestration-family material in an opaque construction must be rejected, never simulated");
            Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OS-MAT-001" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error && d.Message.Contains("opaque construction")), Is.True);
            Assert.That(result.Model.getStandardGlazings().Count, Is.EqualTo(0), "No glazing object may be created for an opaque construction");
        }
    }
}
