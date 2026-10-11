// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    [TestFixture]
    public class ComplianceInputTests
    {
        [Test]
        public void PresentDesignInputs_AreReported_WithSourceIdentity_AndIdealLoadsRetained()
        {
            AnalyticalModel model = AnalyticalModelFixtures.SingleBox();
            Assert.That(model.SetValue(AnalyticalModelParameter.OverheatingScenarios, new Core.SAMCollection<OverheatingScenario>(new OverheatingScenario())), Is.True);
            Assert.That(model.SetValue(AnalyticalModelParameter.PartOEquipmentSelection, new PartOEquipmentSelection()), Is.True);
            Assert.That(model.SetValue(AnalyticalModelParameter.PartOProjectTestVentilationUnit, new PartOProjectTestVentilationUnit()), Is.True);
            Assert.That(model.SetValue(AnalyticalModelParameter.PartODwellingStrategies, new PartODwellingStrategySet()), Is.True);
            Assert.That(model.SetValue(AnalyticalModelParameter.PartOManualEquipmentSelection, new PartOManualEquipmentSelection()), Is.True);
            AdjacencyCluster cluster = model.AdjacencyCluster;
            Space space = cluster.GetSpaces().Single();
            Assert.That(space.SetValue(SpaceParameter.PartFLocalExtractMethod, "CookerHood"), Is.True);
            cluster.AddObject(space);
            Panel panel = cluster.GetPanels().First(p => p.Apertures != null && p.Apertures.Count > 0);
            Aperture aperture = panel.Apertures.First();
            Assert.That(aperture.SetValue(ApertureParameter.PartFDoorTransferData, new PartFDoorTransferData()), Is.True);
            panel.AddAperture(aperture);
            cluster.AddObject(panel);
            model = new AnalyticalModel(model, cluster);

            OpenStudioConversionResult result = model.ToOpenStudio();
            var diagnostics = result.Diagnostics.Where(d => d.Code == "SAM-OS-SET-002").ToList();
            Assert.That(result.IsValid, Is.True);
            Assert.That(diagnostics.Count, Is.EqualTo(7));
            Assert.That(diagnostics.All(d => d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning), Is.True);
            Assert.That(diagnostics.Count(d => d.SamGuid == model.Guid), Is.EqualTo(5));
            Assert.That(diagnostics.Single(d => d.Message.Contains(".PartFLocalExtractMethod is present")).SamGuid, Is.EqualTo(space.Guid));
            Assert.That(diagnostics.Single(d => d.Message.Contains(".PartFDoorTransferData is present")).SamGuid, Is.EqualTo(aperture.Guid));
            foreach (string parameter in new[] { "OverheatingScenarios", "PartOEquipmentSelection", "PartOProjectTestVentilationUnit", "PartODwellingStrategies", "PartOManualEquipmentSelection", "PartFLocalExtractMethod", "PartFDoorTransferData" })
            {
                Assert.That(diagnostics.Count(d => d.Message.Contains("." + parameter + " is present")), Is.EqualTo(1), parameter);
            }
            Assert.That(result.Model.getThermalZones().Count, Is.EqualTo(1));
            Assert.That(result.Model.getZoneHVACIdealLoadsAirSystems().Count, Is.EqualTo(1));
        }

        [Test]
        public void AbsentDesignInputs_DoNotProduceComplianceWarnings()
        {
            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox().ToOpenStudio();
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OS-SET-002"), Is.False);
        }
    }
}
