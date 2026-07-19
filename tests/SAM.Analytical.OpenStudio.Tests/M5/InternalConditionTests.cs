// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>M5: internal conditions, space loads and annual schedule conversion (plan §11).</summary>
    [TestFixture]
    public class InternalConditionTests
    {
        private OpenStudioConversionResult result;
        private global::OpenStudio.Model model;

        [OneTimeSetUp]
        public void Convert()
        {
            result = AnalyticalModelFixtures.TwoAdjacentBoxes().ToOpenStudio();
            model = result.Model;
        }

        [Test]
        public void SpaceType_IsDeduplicated_AcrossSpacesSharingTheInternalCondition()
        {
            foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in result.Diagnostics)
            {
                TestContext.Out.WriteLine(diagnostic.ToString());
            }

            Assert.That(result.IsValid, Is.True);
            Assert.That(model.getSpaceTypes().Count, Is.EqualTo(1), "Two spaces share one InternalCondition — exactly one SpaceType");

            foreach (global::OpenStudio.Space space in model.getSpaces())
            {
                global::OpenStudio.OptionalSpaceType spaceType = space.spaceType;
                Assert.That(spaceType != null && !spaceType.isNull(), $"{space.nameString()} must have a SpaceType");
                Assert.That(spaceType.get().nameString(), Does.StartWith("SAM_InternalCondition_Office_"), "Name keeps readability plus the deterministic content hash");
            }
        }

        [Test]
        public void LoadDensities_ArePreserved()
        {
            Assert.That(model.getPeopleDefinitions().Count, Is.EqualTo(1));
            global::OpenStudio.PeopleDefinition peopleDefinition = model.getPeopleDefinitions()[0];

            global::OpenStudio.OptionalDouble peoplePerArea = peopleDefinition.peopleperSpaceFloorArea();
            Assert.That(peoplePerArea != null && !peoplePerArea.isNull());
            Assert.That(peoplePerArea.get(), Is.EqualTo(0.1).Within(1e-9), "AreaPerPerson 10 m²/person → 0.1 people/m²");
            Assert.That(peopleDefinition.fractionRadiant(), Is.EqualTo(0.3).Within(1e-9), "Documented LadybugTools-parity default");
            Assert.That(peopleDefinition.sensibleHeatFraction().get(), Is.EqualTo(75.0 / 130.0).Within(1e-9), "75 W sensible of 130 W total");

            Assert.That(model.getLightsDefinitions().Count, Is.EqualTo(1));
            Assert.That(model.getLightsDefinitions()[0].wattsperSpaceFloorArea().get(), Is.EqualTo(8.0).Within(1e-9));

            Assert.That(model.getElectricEquipmentDefinitions().Count, Is.EqualTo(1));
            Assert.That(model.getElectricEquipmentDefinitions()[0].wattsperSpaceFloorArea().get(), Is.EqualTo(12.0).Within(1e-9));
        }

        [Test]
        public void Infiltration_UsesFlowPerExteriorAreaBasis()
        {
            Assert.That(model.getSpaceInfiltrationDesignFlowRates().Count, Is.EqualTo(1));

            global::OpenStudio.OptionalDouble flow = model.getSpaceInfiltrationDesignFlowRates()[0].flowperExteriorSurfaceArea();
            Assert.That(flow != null && !flow.isNull(), "Basis must be FlowPerExteriorSurfaceArea");

            double expected = 0.5 * 60.0 / 3600.0 / 62.0;
            Assert.That(flow.get(), Is.EqualTo(expected).Within(1e-9), "0.5 ACH × 60 m³ ÷ 3600 ÷ 62 m² sun-exposed area");
        }

        [Test]
        public void OutdoorAir_ComesFromExplicitSamParameter()
        {
            Assert.That(model.getDesignSpecificationOutdoorAirs().Count, Is.EqualTo(2), "One per space (SpaceParameter.OutsideSupplyAirFlow is space-level)");
            Assert.That(model.getDesignSpecificationOutdoorAirs()[0].outdoorAirFlowRate(), Is.EqualTo(0.02).Within(1e-9));
        }

        [Test]
        public void OccupancySchedule_HasCorrectAnnualValues()
        {
            global::OpenStudio.ScheduleFixedInterval occupancySchedule = null;
            foreach (global::OpenStudio.ScheduleFixedInterval schedule in model.getScheduleFixedIntervals())
            {
                if (schedule.nameString().Contains("Office_Occupancy"))
                {
                    occupancySchedule = schedule;
                    break;
                }
            }

            Assert.That(occupancySchedule, Is.Not.Null, "Occupancy schedule must exist");

            global::OpenStudio.TimeSeries timeSeries = occupancySchedule.timeSeries();
            global::OpenStudio.Vector values = timeSeries.values();

            Assert.That(values.size(), Is.EqualTo(8760), "One value per hour of a non-leap year");

            Assert.That(values.__getitem__(3), Is.EqualTo(0), "1 Jan 03:00 — unoccupied");
            Assert.That(values.__getitem__(10), Is.EqualTo(1), "1 Jan 10:00 — occupied");
            Assert.That(values.__getitem__(364 * 24 + 3), Is.EqualTo(0), "31 Dec 03:00 — unoccupied");
            Assert.That(values.__getitem__(364 * 24 + 10), Is.EqualTo(1), "31 Dec 10:00 — occupied");
        }

        [Test]
        public void MissingProfile_RaisesError_NeverAlwaysOn()
        {
            OpenStudioConversionResult badResult = AnalyticalModelFixtures.SingleBox(withProfiles: false).ToOpenStudio();

            Assert.That(badResult.IsValid, Is.False, "Gains without profiles must invalidate the conversion");
            Assert.That(badResult.Diagnostics.Any(d => d.Code == "SAM-OS-SCH-001" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error), Is.True);
            Assert.That(badResult.Model.getPeoples().Count, Is.EqualTo(0), "No people load may be created without its schedule");
        }
    }
}
