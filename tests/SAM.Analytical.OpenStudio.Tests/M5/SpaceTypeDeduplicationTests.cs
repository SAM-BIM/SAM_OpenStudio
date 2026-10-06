// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Core;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// Review P1-03: SpaceType deduplication must distinguish internal conditions that share a
    /// name but differ in parameters or profile references (SAM clones conditions with new Guids
    /// per space, so identity is name + content — never name alone).
    /// </summary>
    [TestFixture]
    public class SpaceTypeDeduplicationTests
    {
        [Test]
        public void SameNamedConditions_WithDifferentContent_GetDistinctSpaceTypes()
        {
            AnalyticalModel analyticalModel = AnalyticalModelFixtures.TwoAdjacentBoxes();

            Space spaceB = analyticalModel.AdjacencyCluster.GetSpaces().First(s => s.Name == "Space B");
            InternalCondition altered = AnalyticalModelFixtures.CreateOfficeInternalCondition(); // still named "Office"
            altered.SetValue(InternalConditionParameter.LightingGainPerArea, 99.0);
            spaceB.InternalCondition = altered;

            OpenStudioConversionResult result = analyticalModel.ToOpenStudio();
            global::OpenStudio.Model model = result.Model;

            Assert.That(result.IsValid, Is.True);
            Assert.That(model.getSpaceTypes().Count, Is.EqualTo(2), "Same name but different gains must NOT share a SpaceType");

            foreach (global::OpenStudio.Space space in model.getSpaces())
            {
                Assert.That(space.spaceType != null && !space.spaceType.isNull(), $"{space.nameString()} must have a SpaceType");
                global::OpenStudio.SpaceType spaceType = space.spaceType.get();
                Assert.That(spaceType.lights().Count, Is.EqualTo(1), $"{spaceType.nameString()} must have lights");

                double wattsPerArea = spaceType.lights()[0].lightsDefinition().wattsperSpaceFloorArea().get();
                if (space.nameString().Contains("Space_A"))
                {
                    Assert.That(wattsPerArea, Is.EqualTo(8.0).Within(1e-9), "Space A keeps its own 8 W/m² condition");
                }
                else
                {
                    Assert.That(wattsPerArea, Is.EqualTo(99.0).Within(1e-9), "Space B must get its own 99 W/m² condition — never Space A's");
                }
            }
        }

        [Test]
        public void SameNamedConditions_WithDifferentProfiles_GetDistinctSpaceTypes()
        {
            double[] nightShift = new double[24];
            for (int i = 0; i < 6; i++)
            {
                nightShift[i] = 1;
            }

            ProfileLibrary profileLibrary = AnalyticalModelFixtures.CreateProfileLibrary();
            profileLibrary.Add(new Profile("Night Lighting", ProfileType.Lighting, nightShift));

            AdjacencyCluster adjacencyCluster = AnalyticalModelFixtures.TwoAdjacentBoxes().AdjacencyCluster;

            Space spaceB = adjacencyCluster.GetSpaces().First(s => s.Name == "Space B");
            InternalCondition altered = AnalyticalModelFixtures.CreateOfficeInternalCondition(); // still named "Office"
            altered.SetValue(InternalConditionParameter.LightingProfileName, "Night Lighting");
            spaceB.InternalCondition = altered;

            AnalyticalModel analyticalModel = new AnalyticalModel("Two Box Model", "Same-name IC profile-collision fixture", null, null, adjacencyCluster, AnalyticalModelFixtures.CreateMaterialLibrary(), profileLibrary);

            OpenStudioConversionResult result = analyticalModel.ToOpenStudio();
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Model.getSpaceTypes().Count, Is.EqualTo(2), "Same name but different profile references must NOT share a SpaceType");
        }

        [Test]
        public void IdenticalClones_StillShareOneSpaceType()
        {
            OpenStudioConversionResult result = AnalyticalModelFixtures.TwoAdjacentBoxes().ToOpenStudio();
            global::OpenStudio.Model model = result.Model;

            Assert.That(result.IsValid, Is.True);
            Assert.That(model.getSpaceTypes().Count, Is.EqualTo(1), "Clones of the same condition (new Guid per space) must still deduplicate");

            string name = model.getSpaceTypes()[0].nameString();
            Assert.That(name, Does.StartWith("SAM_InternalCondition_Office_"), "Name keeps readability plus a deterministic content hash");

            // Determinism: an identical model converted again produces the same SpaceType name.
            OpenStudioConversionResult repeated = AnalyticalModelFixtures.TwoAdjacentBoxes().ToOpenStudio();
            Assert.That(repeated.Model.getSpaceTypes()[0].nameString(), Is.EqualTo(name), "Content hash must be deterministic across conversions");
        }
    }
}
