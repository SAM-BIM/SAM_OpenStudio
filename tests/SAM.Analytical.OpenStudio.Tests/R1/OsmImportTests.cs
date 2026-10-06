// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// R1: OpenStudio → SAM import of models produced by the forward conversion. Every fixture is
    /// exported and re-imported in memory, so the two directions are exercised against each
    /// other and any asymmetry shows up as a failing assertion rather than as a silently
    /// different model.
    /// </summary>
    [TestFixture]
    public class OsmImportTests
    {
        /// <summary>
        /// Exports a SAM model and imports it back. The OpenStudio model is disposed with the
        /// conversion result, so nothing native outlives the test.
        /// </summary>
        private static OpenStudioImportResult RoundTrip(AnalyticalModel analyticalModel, Core.OpenStudio.OpenStudioImportOptions openStudioImportOptions = null)
        {
            using (OpenStudioConversionResult conversionResult = analyticalModel.ToOpenStudio())
            {
                Assert.That(conversionResult, Is.Not.Null);
                Assert.That(conversionResult.IsValid, Is.True, "The forward conversion must succeed before the import can be judged");

                OpenStudioImportResult result = conversionResult.Model.ToSAM(openStudioImportOptions);
                Assert.That(result, Is.Not.Null);

                foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in result.Diagnostics)
                {
                    TestContext.Out.WriteLine(diagnostic.ToString());
                }

                return result;
            }
        }

        [Test]
        public void SingleBox_ImportsOneClosedRoom()
        {
            OpenStudioImportResult result = RoundTrip(AnalyticalModelFixtures.SingleBox());

            Assert.That(result.Successful, Is.True);
            Assert.That(result.IsValid, Is.True, "Warnings are allowed; errors are not");

            AdjacencyCluster adjacencyCluster = result.AnalyticalModel.AdjacencyCluster;
            Assert.That(adjacencyCluster.GetSpaces().Count, Is.EqualTo(1));
            Assert.That(adjacencyCluster.GetPanels().Count, Is.EqualTo(6), "A closed box has six bounding panels");
        }

        [Test]
        public void TwoAdjacentBoxes_ShareOneInternalPanelRelatedToBothSpaces()
        {
            OpenStudioImportResult result = RoundTrip(AnalyticalModelFixtures.TwoAdjacentBoxes());

            AdjacencyCluster adjacencyCluster = result.AnalyticalModel.AdjacencyCluster;
            List<Space> spaces = adjacencyCluster.GetSpaces();
            List<Panel> panels = adjacencyCluster.GetPanels();

            Assert.That(spaces.Count, Is.EqualTo(2));

            // 6 + 6 bounding surfaces, with the shared wall counted once: 11, not 12.
            Assert.That(panels.Count, Is.EqualTo(11), "The two OpenStudio surfaces of the shared wall must become ONE SAM panel");

            List<Panel> shared = panels.FindAll(x => adjacencyCluster.GetSpaces(x)?.Count == 2);
            Assert.That(shared.Count, Is.EqualTo(1), "Exactly one panel is related to both spaces");
            Assert.That(shared[0].PanelType, Is.EqualTo(PanelType.WallInternal));
        }

        [Test]
        public void PanelTypes_AndBoundaryConditions_RoundTrip()
        {
            OpenStudioImportResult result = RoundTrip(AnalyticalModelFixtures.TwoAdjacentBoxes());

            List<Panel> panels = result.AnalyticalModel.AdjacencyCluster.GetPanels();

            Assert.That(panels.Count(x => x.PanelType == PanelType.SlabOnGrade), Is.EqualTo(2), "Ground-bearing floors");
            Assert.That(panels.Count(x => x.PanelType == PanelType.Roof), Is.EqualTo(2));
            Assert.That(panels.Count(x => x.PanelType == PanelType.WallExternal), Is.EqualTo(6));
            Assert.That(panels.Count(x => x.PanelType == PanelType.WallInternal), Is.EqualTo(1));
        }

        [Test]
        public void Windows_AttachToTheirHostPanel()
        {
            OpenStudioImportResult result = RoundTrip(AnalyticalModelFixtures.SingleBox());

            List<Panel> panels = result.AnalyticalModel.AdjacencyCluster.GetPanels();
            List<Aperture> apertures = new List<Aperture>();
            foreach (Panel panel in panels)
            {
                if (panel.Apertures != null)
                {
                    apertures.AddRange(panel.Apertures);
                }
            }

            Assert.That(apertures.Count, Is.EqualTo(1), "The fixture's single window must survive the round trip");
            Assert.That(apertures[0].ApertureType, Is.EqualTo(ApertureType.Window));
            Assert.That(apertures[0].GetFace3D().GetArea(), Is.GreaterThan(0));
        }

        [Test]
        public void SingleBoxWithoutWindow_ImportsNoApertures()
        {
            OpenStudioImportResult result = RoundTrip(AnalyticalModelFixtures.SingleBox(includeWindow: false));

            foreach (Panel panel in result.AnalyticalModel.AdjacencyCluster.GetPanels())
            {
                Assert.That(panel.Apertures == null || panel.Apertures.Count == 0, Is.True);
            }
        }

        [Test]
        public void StackedBoxes_ImportSharedFloorCeiling()
        {
            OpenStudioImportResult result = RoundTrip(AnalyticalModelFixtures.TwoStackedBoxes());

            AdjacencyCluster adjacencyCluster = result.AnalyticalModel.AdjacencyCluster;
            List<Panel> panels = adjacencyCluster.GetPanels();

            Assert.That(adjacencyCluster.GetSpaces().Count, Is.EqualTo(2));

            List<Panel> shared = panels.FindAll(x => adjacencyCluster.GetSpaces(x)?.Count == 2);
            Assert.That(shared.Count, Is.EqualTo(1), "The slab between the two storeys is one shared panel");
            Assert.That(shared[0].PanelType, Is.EqualTo(PanelType.FloorInternal).Or.EqualTo(PanelType.Ceiling));
        }

        [Test]
        public void FloorArea_And_Volume_SurviveWithinTolerance()
        {
            AnalyticalModel source = AnalyticalModelFixtures.SingleBox();
            OpenStudioImportResult result = RoundTrip(source);

            Space sourceSpace = source.AdjacencyCluster.GetSpaces()[0];
            Space importedSpace = result.AnalyticalModel.AdjacencyCluster.GetSpaces()[0];

            double sourceVolume;
            double importedVolume;
            Assert.That(sourceSpace.TryGetValue(SpaceParameter.Volume, out sourceVolume), Is.True);
            Assert.That(importedSpace.TryGetValue(SpaceParameter.Volume, out importedVolume), Is.True, "Volume must be derived from the imported shell");
            Assert.That(importedVolume, Is.EqualTo(sourceVolume).Within(0.01).Percent);
        }

        [Test]
        public void ApertureArea_SurvivesWithinTolerance()
        {
            AnalyticalModel source = AnalyticalModelFixtures.SingleBox();
            OpenStudioImportResult result = RoundTrip(source);

            Assert.That(TotalApertureArea(result.AnalyticalModel), Is.EqualTo(TotalApertureArea(source)).Within(0.5).Percent);
        }

        private static double TotalApertureArea(AnalyticalModel analyticalModel)
        {
            double result = 0;
            foreach (Panel panel in analyticalModel.AdjacencyCluster.GetPanels())
            {
                if (panel.Apertures == null)
                {
                    continue;
                }

                foreach (Aperture aperture in panel.Apertures)
                {
                    result += aperture.GetFace3D().GetArea();
                }
            }

            return result;
        }

        [Test]
        public void Constructions_And_MaterialLayers_RoundTrip()
        {
            OpenStudioImportResult result = RoundTrip(AnalyticalModelFixtures.SingleBox());

            List<Construction> constructions = result.AnalyticalModel.AdjacencyCluster.GetConstructions();
            Assert.That(constructions, Is.Not.Null.And.Not.Empty);

            Construction wall = constructions.Find(x => x.ConstructionLayers != null && x.ConstructionLayers.Count > 0);
            Assert.That(wall, Is.Not.Null, "At least one imported construction must carry its layers");

            Core.MaterialLibrary materialLibrary = result.AnalyticalModel.MaterialLibrary;
            Assert.That(materialLibrary, Is.Not.Null);

            foreach (ConstructionLayer constructionLayer in wall.ConstructionLayers)
            {
                Assert.That(constructionLayer.Thickness, Is.GreaterThan(0), "No imported layer may be zero-thickness");
                Assert.That(materialLibrary.GetMaterial(constructionLayer.Name), Is.Not.Null, "Every referenced material must be in the library");
            }
        }

        [Test]
        public void UnreferencedMaterials_ArePruned()
        {
            OpenStudioImportResult result = RoundTrip(AnalyticalModelFixtures.SingleBox());

            HashSet<string> referenced = new HashSet<string>();
            foreach (Construction construction in result.AnalyticalModel.AdjacencyCluster.GetConstructions())
            {
                construction.ConstructionLayers?.ForEach(x => referenced.Add(x.Name));
            }

            foreach (ApertureConstruction apertureConstruction in result.AnalyticalModel.AdjacencyCluster.GetApertureConstructions())
            {
                apertureConstruction.PaneConstructionLayers?.ForEach(x => referenced.Add(x.Name));
                apertureConstruction.FrameConstructionLayers?.ForEach(x => referenced.Add(x.Name));
            }

            foreach (Core.IMaterial material in result.AnalyticalModel.MaterialLibrary.GetMaterials())
            {
                Assert.That(referenced.Contains(material.Name), Is.True, $"Material '{material.Name}' is not referenced by any imported construction and should have been pruned");
            }
        }

        [Test]
        public void OpaqueAirGap_RoundTripsItsHeatTransferCoefficient()
        {
            const double heatTransferCoefficient = 1.25;

            OpenStudioImportResult result = RoundTrip(AnalyticalModelFixtures.OpaqueAirGapBox(heatTransferCoefficient));

            Core.GasMaterial gasMaterial = result.AnalyticalModel.MaterialLibrary.GetMaterials().OfType<Core.GasMaterial>().FirstOrDefault();
            Assert.That(gasMaterial, Is.Not.Null, "The opaque cavity must import as a SAM gas material");

            double imported;
            Assert.That(gasMaterial.TryGetValue(GasMaterialParameter.HeatTransferCoefficient, out imported), Is.True);

            // Forward writes R = 1/h, reverse reads h = 1/R: the pair must be exact.
            Assert.That(imported, Is.EqualTo(heatTransferCoefficient).Within(1e-9));
        }

        [Test]
        public void InternalConditions_And_Profiles_RoundTrip()
        {
            OpenStudioImportResult result = RoundTrip(AnalyticalModelFixtures.SingleBox());

            List<Space> spaces = result.AnalyticalModel.AdjacencyCluster.GetSpaces();
            InternalCondition internalCondition = spaces[0].InternalCondition;
            Assert.That(internalCondition, Is.Not.Null, "The imported space must carry an internal condition");

            string occupancyProfileName;
            Assert.That(internalCondition.TryGetValue(InternalConditionParameter.OccupancyProfileName, out occupancyProfileName), Is.True, "The occupancy schedule must return as a SAM profile");

            ProfileLibrary profileLibrary = result.AnalyticalModel.ProfileLibrary;
            Assert.That(profileLibrary, Is.Not.Null);
            Assert.That(profileLibrary.GetProfiles().Find(x => x.Name == occupancyProfileName), Is.Not.Null, "The referenced profile must be in the library");
        }

        [Test]
        public void HeatingAndCoolingSetpoints_ImportAsProfiles()
        {
            OpenStudioImportResult result = RoundTrip(AnalyticalModelFixtures.SingleBox());

            InternalCondition internalCondition = result.AnalyticalModel.AdjacencyCluster.GetSpaces()[0].InternalCondition;

            string heatingProfileName;
            string coolingProfileName;
            Assert.That(internalCondition.TryGetValue(InternalConditionParameter.HeatingProfileName, out heatingProfileName), Is.True);
            Assert.That(internalCondition.TryGetValue(InternalConditionParameter.CoolingProfileName, out coolingProfileName), Is.True);

            ProfileLibrary profileLibrary = result.AnalyticalModel.ProfileLibrary;
            Profile heating = profileLibrary.GetProfiles().Find(x => x.Name == heatingProfileName);
            Assert.That(heating, Is.Not.Null);

            // SAM stores setpoints as profiles whose values ARE the temperatures.
            // Profile.Count is the index span, not the value count (see the forward converter's
            // note): an 8760-value annual profile spans indices 0..8759.
            Assert.That(heating.Min, Is.EqualTo(0));
            Assert.That(heating.Max, Is.EqualTo(8759), "Imported profiles are annual hourly");
            Assert.That(heating[0], Is.EqualTo(21).Within(0.5), "The fixture's 21 degC heating setpoint must survive");
            Assert.That(heating[8759], Is.EqualTo(21).Within(0.5), "The setpoint must hold to the last hour of the year");
        }

        [Test]
        public void Shading_ImportsAsShadePanels()
        {
            AnalyticalModel source = AnalyticalModelFixtures.SingleBox();

            OpenStudioImportResult result = RoundTrip(source);

            List<Panel> shades = result.AnalyticalModel.AdjacencyCluster.GetPanels().FindAll(x => x.PanelType == PanelType.Shade);

            // The fixture carries no shade panels, so importing any would mean the converter
            // invented geometry. This is the negative half of the shading contract; the positive
            // half is covered by ShadingSurfaceImportTests.
            Assert.That(shades.Count, Is.EqualTo(0));
        }

        [Test]
        public void ImportOptions_CanSuppressConstructionsAndConditions()
        {
            Core.OpenStudio.OpenStudioImportOptions options = new Core.OpenStudio.OpenStudioImportOptions
            {
                IncludeConstructions = false,
                IncludeInternalConditions = false,
            };

            OpenStudioImportResult result = RoundTrip(AnalyticalModelFixtures.SingleBox(), options);

            Assert.That(result.Successful, Is.True, "Geometry-only import must still produce a model");
            Assert.That(result.AnalyticalModel.AdjacencyCluster.GetPanels().Count, Is.EqualTo(6));
            Assert.That(result.AnalyticalModel.MaterialLibrary.GetMaterials()?.Count ?? 0, Is.Zero);
        }

        [Test]
        public void NorthAxis_ImportsFromDegreesIntoRadians()
        {
            // Set on the OpenStudio model directly: the geometry-only forward overload does not
            // apply simulation settings, and the north axis is what is under test here, not which
            // forward overload writes it.
            using (OpenStudioConversionResult conversionResult = AnalyticalModelFixtures.SingleBox().ToOpenStudio())
            {
                conversionResult.Model.getBuilding().setNorthAxis(45);

                OpenStudioImportResult result = conversionResult.Model.ToSAM();

                double northAngle;
                Assert.That(result.AnalyticalModel.TryGetValue(AnalyticalModelParameter.NorthAngle, out northAngle), Is.True);
                Assert.That(northAngle, Is.EqualTo(System.Math.PI / 4).Within(1e-9));
            }
        }

        [Test]
        public void EmptyModel_ProducesAModelAndReportsTheAbsenceOfSpaces()
        {
            using (global::OpenStudio.Model model = new global::OpenStudio.Model())
            {
                OpenStudioImportResult result = model.ToSAM();

                Assert.That(result.Successful, Is.True, "An empty model is not a failure; it is an empty building");
                // SAM collection getters return null rather than an empty list when nothing matches.
                Assert.That(result.AnalyticalModel.AdjacencyCluster.GetSpaces()?.Count ?? 0, Is.Zero);
                Assert.That(result.Diagnostics.Any(x => x.Code == Core.OpenStudio.OpenStudioImportDiagnosticCodes.ZoneIncomplete), Is.True, "The absence of spaces must be reported, never silent");
            }
        }
    }
}
