// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Core;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// Review P1-04: glazing optical properties must map SAM External* → EnergyPlus Front side
    /// (the side opposite the zone, i.e. outdoors for exterior windows) and SAM Internal* →
    /// EnergyPlus Back side (side closest to the zone) — EnergyPlus I/O Reference,
    /// "Materials for Glass Windows and Doors". Verified with deliberately asymmetric glass.
    /// </summary>
    [TestFixture]
    public class GlazingSideTests
    {
        [Test]
        public void AsymmetricGlazing_MapsExternalToFront_InternalToBack()
        {
            TransparentMaterial glass = new TransparentMaterial(new System.Guid("66666666-0000-0000-0000-000000000001"), "Coated Glass", "Coated Glass", "Asymmetric fixture glass", 1.0, 2500, 840);
            glass.SetValue(TransparentMaterialParameter.SolarTransmittance, 0.5);
            glass.SetValue(TransparentMaterialParameter.ExternalSolarReflectance, 0.31);
            glass.SetValue(TransparentMaterialParameter.InternalSolarReflectance, 0.11);
            glass.SetValue(TransparentMaterialParameter.LightTransmittance, 0.6);
            glass.SetValue(TransparentMaterialParameter.ExternalLightReflectance, 0.32);
            glass.SetValue(TransparentMaterialParameter.InternalLightReflectance, 0.12);
            glass.SetValue(TransparentMaterialParameter.ExternalEmissivity, 0.84);
            glass.SetValue(TransparentMaterialParameter.InternalEmissivity, 0.05);

            MaterialLibrary materialLibrary = AnalyticalModelFixtures.CreateMaterialLibrary();
            materialLibrary.Add(glass);

            ApertureConstruction glazing = new ApertureConstruction(new System.Guid("66666666-0000-0000-0000-000000000002"), "Asymmetric Glazing", ApertureType.Window, new List<ConstructionLayer>
            {
                new ConstructionLayer("Coated Glass", 0.006),
            });

            AdjacencyCluster adjacencyCluster = AnalyticalModelFixtures.SingleBox().AdjacencyCluster;
            Panel host = adjacencyCluster.GetPanels().First(p => p.PanelType == PanelType.WallExternal && (p.Apertures?.Count ?? 0) > 0);
            host.RemoveApertures();
            host.AddAperture(Analytical.Create.Aperture(glazing, new Geometry.Spatial.Face3D(new Geometry.Spatial.Polygon3D(new[]
            {
                new Geometry.Spatial.Point3D(1, 0, 0.8),
                new Geometry.Spatial.Point3D(3, 0, 0.8),
                new Geometry.Spatial.Point3D(3, 0, 2.2),
                new Geometry.Spatial.Point3D(1, 0, 2.2),
            }))));

            AnalyticalModel analyticalModel = new AnalyticalModel("Glazing Side Model", "Asymmetric glazing fixture", null, null, adjacencyCluster, materialLibrary, AnalyticalModelFixtures.CreateProfileLibrary());

            OpenStudioConversionResult result = analyticalModel.ToOpenStudio();
            Assert.That(result.IsValid, Is.True);

            global::OpenStudio.StandardGlazing standardGlazing = result.Model.getStandardGlazings().FirstOrDefault(g => g.nameString().Contains("Coated_Glass"));
            Assert.That(standardGlazing, Is.Not.Null, "The coated glass must be converted");

            Assert.That(standardGlazing.frontSideSolarReflectanceatNormalIncidence().get(), Is.EqualTo(0.31).Within(1e-9), "E+ Front = side opposite the zone = SAM External");
            Assert.That(standardGlazing.backSideSolarReflectanceatNormalIncidence().get(), Is.EqualTo(0.11).Within(1e-9), "E+ Back = side closest to the zone = SAM Internal");
            Assert.That(standardGlazing.frontSideVisibleReflectanceatNormalIncidence().get(), Is.EqualTo(0.32).Within(1e-9));
            Assert.That(standardGlazing.backSideVisibleReflectanceatNormalIncidence().get(), Is.EqualTo(0.12).Within(1e-9));
            Assert.That(standardGlazing.frontSideInfraredHemisphericalEmissivity(), Is.EqualTo(0.84).Within(1e-9));
            Assert.That(standardGlazing.backSideInfraredHemisphericalEmissivity(), Is.EqualTo(0.05).Within(1e-9), "Internal low-e coating belongs on the zone-facing (back) side");
        }
    }
}
