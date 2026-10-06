// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using AnalyticalCreate = SAM.Analytical.Create;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// Review P1-01: every coverage-manifest row that declares a structured diagnostic must
    /// actually emit it when the source data is present — false completeness (a manifest row
    /// whose diagnostic no converter ever raises) is the exact failure mode these tests pin
    /// down. One test per emission site; the clean-fixture silence is enforced by
    /// <see cref="CompletenessTests.CleanFixture_DropsNothing"/>.
    /// </summary>
    [TestFixture]
    public class DeclaredDiagnosticsTests
    {
        private static Geometry.Spatial.Point3D P(double x, double y, double z)
        {
            return new Geometry.Spatial.Point3D(x, y, z);
        }

        private static Geometry.Spatial.Face3D F(params Geometry.Spatial.Point3D[] point3Ds)
        {
            return new Geometry.Spatial.Face3D(new Geometry.Spatial.Polygon3D(point3Ds));
        }

        /// <summary>
        /// Single-zone box assembled inline (the shared fixture has no material-library or
        /// panel-mutation override): 6 panels of <paramref name="construction"/>, office
        /// internal condition, optional mutation of the south wall before it enters the cluster.
        /// </summary>
        private static AnalyticalModel SingleBoxModel(Core.MaterialLibrary materialLibrary, Construction construction = null, Action<Panel> mutateSouthWall = null)
        {
            construction = construction ?? AnalyticalModelFixtures.WallConstruction;

            AdjacencyCluster adjacencyCluster = new AdjacencyCluster();

            Space space = new Space(new Guid("cccccccc-0000-0000-0000-0000000000dd"), "Space Diagnostic", P(2.5, 2, 1.5));
            space.SetValue(SpaceParameter.Area, 20.0);
            space.SetValue(SpaceParameter.Volume, 60.0);
            space.InternalCondition = AnalyticalModelFixtures.CreateOfficeInternalCondition();
            adjacencyCluster.AddObject(space);

            Panel southWall = AnalyticalCreate.Panel(construction, PanelType.WallExternal, F(P(0, 0, 0), P(5, 0, 0), P(5, 0, 3), P(0, 0, 3)));
            mutateSouthWall?.Invoke(southWall);

            List<Panel> panels = new List<Panel>
            {
                AnalyticalCreate.Panel(construction, PanelType.SlabOnGrade, F(P(0, 0, 0), P(5, 0, 0), P(5, 4, 0), P(0, 4, 0))),
                AnalyticalCreate.Panel(construction, PanelType.Roof, F(P(0, 0, 3), P(5, 0, 3), P(5, 4, 3), P(0, 4, 3))),
                southWall,
                AnalyticalCreate.Panel(construction, PanelType.WallExternal, F(P(0, 0, 0), P(0, 4, 0), P(0, 4, 3), P(0, 0, 3))),
                AnalyticalCreate.Panel(construction, PanelType.WallExternal, F(P(0, 4, 0), P(5, 4, 0), P(5, 4, 3), P(0, 4, 3))),
                AnalyticalCreate.Panel(construction, PanelType.WallExternal, F(P(5, 0, 0), P(5, 4, 0), P(5, 4, 3), P(5, 0, 3))),
            };

            foreach (Panel panel in panels)
            {
                adjacencyCluster.AddObject(panel);
                adjacencyCluster.AddRelation(space, panel);
            }

            return new AnalyticalModel("Declared Diagnostics Model", "P1-01 regression fixture", null, null, adjacencyCluster, materialLibrary ?? AnalyticalModelFixtures.CreateMaterialLibrary(), AnalyticalModelFixtures.CreateProfileLibrary());
        }

        [Test]
        public void InternalCondition_ViewCoefficientsAndControlFunction_RaiseDeclaredDiagnostics()
        {
            // Manifest: OccupancyViewCoefficient / EquipmentViewCoefficient /
            // LightingControlFunction — Unsupported, "SAM-OS-IC-001 info". SAM's own
            // Create.InternalCondition writes view coefficients on every NCM-derived condition.
            InternalCondition internalCondition = AnalyticalModelFixtures.CreateOfficeInternalCondition();
            internalCondition.SetValue(InternalConditionParameter.OccupancyViewCoefficient, 0.227);
            internalCondition.SetValue(InternalConditionParameter.EquipmentViewCoefficient, 0.372);
            internalCondition.SetValue(InternalConditionParameter.LightingControlFunction, "PHOTOCELL 300");

            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(internalConditionOverride: internalCondition).ToOpenStudio();

            Assert.That(result.IsValid, Is.True, "Unsupported-with-diagnostic data does not invalidate the conversion");
            Assert.That(result.Diagnostics.Count(d => d.Code == "SAM-OS-IC-001" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Information && d.Message.Contains("Occupancy view coefficient")), Is.EqualTo(1), "Declared occupancy view-coefficient diagnostic");
            Assert.That(result.Diagnostics.Count(d => d.Code == "SAM-OS-IC-001" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Information && d.Message.Contains("Equipment view coefficient")), Is.EqualTo(1), "Declared equipment view-coefficient diagnostic");
            Assert.That(result.Diagnostics.Count(d => d.Code == "SAM-OS-IC-001" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Information && d.Message.Contains("Lighting control function")), Is.EqualTo(1), "Declared lighting-control-function diagnostic");
        }

        [Test]
        public void InternalCondition_EmitterAndExhaustData_RaiseDeferredHvacDiagnostics()
        {
            // Manifest: the four emitter and four exhaust parameters — Deferred,
            // "SAM-OS-HVAC-001 info" (Ideal Loads is convective; exhaust needs fan equipment).
            InternalCondition internalCondition = AnalyticalModelFixtures.CreateOfficeInternalCondition();
            internalCondition.SetValue(InternalConditionParameter.HeatingEmitterRadiantProportion, 0.2);
            internalCondition.SetValue(InternalConditionParameter.HeatingEmitterCoefficient, 0.7);
            internalCondition.SetValue(InternalConditionParameter.CoolingEmitterRadiantProportion, 0.1);
            internalCondition.SetValue(InternalConditionParameter.CoolingEmitterCoefficient, 0.9);
            internalCondition.SetValue(InternalConditionParameter.ExhaustAirFlowPerPerson, 0.008);
            internalCondition.SetValue(InternalConditionParameter.ExhaustAirChangesPerHour, 1.5);
            internalCondition.SetValue(InternalConditionParameter.ExhaustAirFlowPerArea, 0.001);
            internalCondition.SetValue(InternalConditionParameter.ExhaustAirFlow, 0.05);

            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(internalConditionOverride: internalCondition).ToOpenStudio();

            Assert.That(result.IsValid, Is.True);

            List<Core.OpenStudio.OpenStudioDiagnostic> hvacDiagnostics = result.Diagnostics.Where(d => d.Code == "SAM-OS-HVAC-001" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Information).ToList();
            Core.OpenStudio.OpenStudioDiagnostic emitterDiagnostic = hvacDiagnostics.FirstOrDefault(d => d.Message.Contains("Emitter"));
            Core.OpenStudio.OpenStudioDiagnostic exhaustDiagnostic = hvacDiagnostics.FirstOrDefault(d => d.Message.Contains("Exhaust"));

            Assert.That(emitterDiagnostic, Is.Not.Null, "Declared emitter deferral diagnostic (SAM-OS-HVAC-001 info)");
            Assert.That(emitterDiagnostic.Message, Does.Contain("HeatingEmitterRadiantProportion").And.Contain("HeatingEmitterCoefficient").And.Contain("CoolingEmitterRadiantProportion").And.Contain("CoolingEmitterCoefficient"), "The deferral names every present emitter parameter");

            Assert.That(exhaustDiagnostic, Is.Not.Null, "Declared exhaust deferral diagnostic (SAM-OS-HVAC-001 info)");
            Assert.That(exhaustDiagnostic.Message, Does.Contain("ExhaustAirFlowPerPerson").And.Contain("ExhaustAirChangesPerHour").And.Contain("ExhaustAirFlowPerArea").And.Contain("ExhaustAirFlow"), "The deferral names every present exhaust parameter");
        }

        [Test]
        public void ApertureConstruction_InternalShadow_RaisesDeclaredDiagnostic()
        {
            // Manifest: ApertureConstructionParameter.IsInternalShadow — Unsupported,
            // "SAM-OS-CON-002 info".
            ApertureConstruction apertureConstruction = new ApertureConstruction(new Guid("22222222-9999-9999-9999-999999999999"), "Shadow Window", ApertureType.Window);
            apertureConstruction.SetValue(ApertureConstructionParameter.ThermalTransmittance, 2.8);
            apertureConstruction.SetValue(ApertureConstructionParameter.TotalSolarEnergyTransmittance, 0.6);
            apertureConstruction.SetValue(ApertureConstructionParameter.LightTransmittance, 0.7);
            apertureConstruction.SetValue(ApertureConstructionParameter.IsInternalShadow, true);

            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(apertureConstructionOverride: apertureConstruction).ToOpenStudio();

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Diagnostics.Count(d => d.Code == "SAM-OS-CON-002" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Information && d.Message.Contains("Internal-shadow")), Is.EqualTo(1), "Declared aperture-construction internal-shadow diagnostic");
        }

        [Test]
        public void Construction_InternalShadow_RaisesDeclaredDiagnosticOncePerConstruction()
        {
            // Manifest: ConstructionParameter.IsInternalShadow — Unsupported, "SAM-OS-CON-002
            // info". Six panels share the construction: the diagnostic fires once, not per panel.
            Construction shadowWall = new Construction(new Guid("11111111-9999-9999-9999-999999999999"), "Shadow Wall", new List<ConstructionLayer>
            {
                new ConstructionLayer("Plasterboard", 0.0125),
                new ConstructionLayer("Insulation", 0.1),
                new ConstructionLayer("Brick", 0.1),
            });
            shadowWall.SetValue(ConstructionParameter.IsInternalShadow, true);

            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(wallConstruction: shadowWall).ToOpenStudio();

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Diagnostics.Count(d => d.Code == "SAM-OS-CON-002" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Information && d.Message.Contains("Internal-shadow")), Is.EqualTo(1), "One diagnostic per construction, not one per panel");
        }

        [Test]
        public void Panel_FeatureShade_RaisesDeclaredDiagnostic()
        {
            // Manifest: PanelParameter.FeatureShade — Unsupported, "SAM-OS-CON-002 info".
            Panel shadedPanel = null;
            AnalyticalModel analyticalModel = SingleBoxModel(null, mutateSouthWall: panel =>
            {
                panel.SetValue(PanelParameter.FeatureShade, new FeatureShade("External louvre"));
                shadedPanel = panel;
            });

            OpenStudioConversionResult result = analyticalModel.ToOpenStudio();

            Assert.That(result.IsValid, Is.True);
            List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = result.Diagnostics.Where(d => d.Code == "SAM-OS-CON-002" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Information && d.Message.Contains("feature shade")).ToList();
            Assert.That(diagnostics.Count, Is.EqualTo(1), "Declared panel feature-shade diagnostic, once per panel");
            Assert.That(diagnostics[0].SamGuid, Is.EqualTo(shadedPanel.Guid), "The diagnostic carries the panel Guid");
        }

        [Test]
        public void Material_VapourDiffusionFactor_RaisesDeclaredDiagnosticOncePerMaterial()
        {
            // Manifest: AnalyticalMaterialParameter.VapourDiffusionFactor — Unsupported,
            // "SAM-OS-MAT-002 info". Brick appears in every panel and both construction
            // directions: the diagnostic still fires exactly once for the material.
            Core.MaterialLibrary materialLibrary = AnalyticalModelFixtures.CreateMaterialLibrary();
            OpaqueMaterial brick = materialLibrary.GetMaterial("Brick") as OpaqueMaterial;
            brick.SetValue(MaterialParameter.VapourDiffusionFactor, 10.0);
            materialLibrary.Add(brick);

            OpenStudioConversionResult result = SingleBoxModel(materialLibrary).ToOpenStudio();

            Assert.That(result.IsValid, Is.True);
            List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = result.Diagnostics.Where(d => d.Code == "SAM-OS-MAT-002" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Information && d.Message.Contains("Vapour diffusion factor")).ToList();
            Assert.That(diagnostics.Count, Is.EqualTo(1), "Declared vapour-diffusion-factor diagnostic, once per material");
            Assert.That(diagnostics[0].SamGuid, Is.EqualTo(brick.Guid), "The diagnostic carries the material Guid");
        }

        [Test]
        public void OpaqueMaterial_DivergentInternalOptics_RaisesDeclaredDiagnostic()
        {
            // Manifest: OpaqueMaterialParameter.InternalEmissivity (and the Internal
            // reflectances) — Approximated, "SAM-OS-MAT-002 info when Internal differs from
            // External; EnergyPlus opaque materials are single-sided".
            Core.MaterialLibrary materialLibrary = AnalyticalModelFixtures.CreateMaterialLibrary();
            OpaqueMaterial brick = materialLibrary.GetMaterial("Brick") as OpaqueMaterial;
            brick.SetValue(OpaqueMaterialParameter.InternalEmissivity, 0.4);        // external 0.9 → divergent
            brick.SetValue(OpaqueMaterialParameter.InternalSolarReflectance, 0.3);  // external 0.3 → identical
            materialLibrary.Add(brick);

            OpenStudioConversionResult result = SingleBoxModel(materialLibrary).ToOpenStudio();

            Assert.That(result.IsValid, Is.True);
            List<Core.OpenStudio.OpenStudioDiagnostic> diagnostics = result.Diagnostics.Where(d => d.Code == "SAM-OS-MAT-002" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Information && d.Message.Contains("Internal-side")).ToList();
            Assert.That(diagnostics.Count, Is.EqualTo(1), "Declared single-sided approximation diagnostic, once per material");
            Assert.That(diagnostics[0].Message, Does.Contain("InternalEmissivity"), "The divergent parameter is named");
            Assert.That(diagnostics[0].Message, Does.Not.Contain("InternalSolarReflectance"), "A matching internal value is not a divergence");
        }

        [Test]
        public void OpaqueMaterial_MatchingInternalOptics_StaysSilent()
        {
            // The single-sided approximation is exact when the internal values equal the
            // external ones — no diagnostic may fire.
            Core.MaterialLibrary materialLibrary = AnalyticalModelFixtures.CreateMaterialLibrary();
            OpaqueMaterial brick = materialLibrary.GetMaterial("Brick") as OpaqueMaterial;
            brick.SetValue(OpaqueMaterialParameter.InternalEmissivity, 0.9);
            brick.SetValue(OpaqueMaterialParameter.InternalSolarReflectance, 0.3);
            brick.SetValue(OpaqueMaterialParameter.InternalLightReflectance, 0.3);
            materialLibrary.Add(brick);

            OpenStudioConversionResult result = SingleBoxModel(materialLibrary).ToOpenStudio();

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OS-MAT-002"), Is.False, "Matching internal/external optics are the exact case — silent");
        }
    }
}
