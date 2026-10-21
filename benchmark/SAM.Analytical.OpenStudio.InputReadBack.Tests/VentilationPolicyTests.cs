// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OpenStudio.InputReadBack.Tests
{
    /// <summary>
    /// The writer side of the S09 owner ventilation policy (S07 D-F2), on the signed-off fixtures and on single changes of
    /// them: no selected Ventilation profile delivers no outdoor air and keeps the requirement (V0/V1); a valid selection
    /// is the delivery schedule (V2); a selection that does not resolve is an invalid input; the per-space
    /// OutsideSupplyAirFlow override and the Ideal Loads controls follow the same rule.
    /// </summary>
    [TestFixture]
    public class VentilationPolicyTests
    {
        private const string NotDelivered = Convert.VentilationNotDeliveredScheduleName;

        private static OpenStudioConversionResult Convert_(AnalyticalModel model)
        {
            return model.ToOpenStudio();
        }

        private static global::OpenStudio.DesignSpecificationOutdoorAir SpaceTypeDsoa(OpenStudioConversionResult result, Guid space)
        {
            global::OpenStudio.Space openStudioSpace = result.Model.getSpaces().Single(x => NamedFor(x.nameString(), space));
            return openStudioSpace.spaceType.get().designSpecificationOutdoorAir().get();
        }

        private static global::OpenStudio.ZoneHVACIdealLoadsAirSystem IdealLoads(OpenStudioConversionResult result, Guid space)
        {
            return result.Model.getZoneHVACIdealLoadsAirSystems().Single(x => NamedFor(x.nameString(), space));
        }

        // The fixture space GUIDs share their first 8 hex digits, so the deterministic name is told apart by the SAM name.
        private static bool NamedFor(string openStudioName, Guid space)
        {
            string token = space == TestSupport.BoxSpace ? "FixBox" : space == TestSupport.SpaceA ? "FixA" : "FixB";
            return openStudioName.Contains("_" + token + "_", StringComparison.Ordinal);
        }

        private static string Schedule(global::OpenStudio.DesignSpecificationOutdoorAir dsoa)
        {
            global::OpenStudio.OptionalSchedule schedule = dsoa.outdoorAirFlowRateFractionSchedule();
            return schedule == null || schedule.isNull() ? null : schedule.get().nameString();
        }

        private static double[] Values(OpenStudioConversionResult result, string scheduleName)
        {
            global::OpenStudio.ScheduleFixedInterval schedule = result.Model.getScheduleFixedIntervalByName(scheduleName).get();
            global::OpenStudio.Vector values = schedule.timeSeries().values();
            return Enumerable.Range(0, (int)values.size()).Select(i => values.__getitem__((uint)i)).ToArray();
        }

        [Test]
        public void V1_TheTwoZoneFixture_KeepsEachRequirement_AndDeliversNothing()
        {
            using (OpenStudioConversionResult result = Convert_(TestSupport.Load(TestSupport.Two)))
            {
                Assert.That(result.IsValid, Is.True);
                global::OpenStudio.DesignSpecificationOutdoorAir a = SpaceTypeDsoa(result, TestSupport.SpaceA);
                global::OpenStudio.DesignSpecificationOutdoorAir b = SpaceTypeDsoa(result, TestSupport.SpaceB);
                Assert.That(a.outdoorAirFlowperPerson(), Is.EqualTo(0.008), "requirement kept");
                Assert.That(a.outdoorAirFlowperFloorArea(), Is.EqualTo(0.0006), "requirement kept");
                Assert.That(b.outdoorAirFlowAirChangesperHour(), Is.EqualTo(0.75), "requirement kept");
                Assert.That(Schedule(a), Is.EqualTo(NotDelivered));
                Assert.That(Schedule(b), Is.EqualTo(NotDelivered));
                double[] values = Values(result, NotDelivered);
                Assert.That(values.Length, Is.EqualTo(8760));
                Assert.That(values.All(x => x == 0), Is.True, "no outdoor air in any hour");
                Assert.That(result.Diagnostics.Count(x => x.Message.Contains("no outdoor air is delivered (S07 D-F2 V1)")), Is.EqualTo(2));
            }
        }

        [Test]
        public void V2_TheBoxFixture_DeliversThroughItsSelectedProfile()
        {
            using (OpenStudioConversionResult result = Convert_(TestSupport.Load(TestSupport.Box)))
            {
                Assert.That(result.IsValid, Is.True);
                global::OpenStudio.DesignSpecificationOutdoorAir dsoa = SpaceTypeDsoa(result, TestSupport.BoxSpace);
                Assert.That(dsoa.outdoorAirFlowperPerson(), Is.EqualTo(0.01));
                Assert.That(Schedule(dsoa), Does.Contain("Ventilation"));
                Assert.That(Values(result, Schedule(dsoa)).Any(x => x > 0), Is.True);
            }
        }

        [Test]
        public void V2ChangedBackToV1_TheSameSpace_DeliversNothing_AndKeepsItsRequirement()
        {
            AnalyticalModel model = TestSupport.Load(TestSupport.Box);
            using (OpenStudioConversionResult v2 = Convert_(model))
            {
                Assert.That(Schedule(SpaceTypeDsoa(v2, TestSupport.BoxSpace)), Does.Contain("Ventilation"), "V2 first");
            }

            TestSupport.ChangeCondition(model, TestSupport.BoxSpace, ic => ic.RemoveValue(InternalConditionParameter.VentilationProfileName));
            using (OpenStudioConversionResult v1 = Convert_(model))
            {
                Assert.That(v1.IsValid, Is.True);
                global::OpenStudio.DesignSpecificationOutdoorAir dsoa = SpaceTypeDsoa(v1, TestSupport.BoxSpace);
                Assert.That(dsoa.outdoorAirFlowperPerson(), Is.EqualTo(0.01), "the requirement is kept");
                Assert.That(Schedule(dsoa), Is.EqualTo(NotDelivered), "no stale V2 delivery");
                Assert.That(Values(v1, NotDelivered).All(x => x == 0), Is.True);
                Assert.That(IdealLoads(v1, TestSupport.BoxSpace).designSpecificationOutdoorAirObject().get().nameString(), Is.EqualTo(dsoa.nameString()));
            }
        }

        private static readonly object[] Invalid =
        {
            new object[] { "missing", (Func<InternalCondition, string>)(ic => "s09-missing-ventilation-profile") },
            new object[] { "wrong-type", (Func<InternalCondition, string>)(ic => ic.GetProfileName(ProfileType.Occupancy)) }
        };

        [TestCaseSource(nameof(Invalid))]
        public void ASelectionThatDoesNotResolve_IsAnInvalidInput_AndNeverDeliversUnscheduled(string what, Func<InternalCondition, string> name)
        {
            AnalyticalModel model = TestSupport.Load(TestSupport.Box);
            TestSupport.ChangeCondition(model, TestSupport.BoxSpace, ic => ic.SetProfileName(ProfileType.Ventilation, name(ic)));
            using (OpenStudioConversionResult result = Convert_(model))
            {
                Assert.That(result.IsValid, Is.False, what + ": an invalid input is an error");
                Assert.That(result.Diagnostics.Any(x => x.Code == "SAM-OS-SCH-001" && x.Message.Contains("not found as a Ventilation profile")), Is.True, what);
                global::OpenStudio.DesignSpecificationOutdoorAir dsoa = SpaceTypeDsoa(result, TestSupport.BoxSpace);
                Assert.That(Schedule(dsoa), Is.EqualTo(NotDelivered), what + ": never unscheduled");
                Assert.That(Values(result, NotDelivered).All(x => x == 0), Is.True);
            }
        }

        [Test]
        public void TheSpaceOverride_InV2_IsSumOfItsFlowOnly_ScheduledByTheSelectedProfile_AndLinkedToIdealLoads()
        {
            AnalyticalModel model = TestSupport.Load(TestSupport.Box);
            TestSupport.ChangeSpace(model, TestSupport.BoxSpace, space => space.SetValue(SpaceParameter.OutsideSupplyAirFlow, 0.05));
            using (OpenStudioConversionResult result = Convert_(model))
            {
                Assert.That(result.IsValid, Is.True);
                global::OpenStudio.Space openStudioSpace = result.Model.getSpaces().Single();
                Assert.That(openStudioSpace.isDesignSpecificationOutdoorAirDefaulted(), Is.False, "the override is set on the space");
                global::OpenStudio.DesignSpecificationOutdoorAir own = openStudioSpace.designSpecificationOutdoorAir().get();
                Assert.That(own.outdoorAirMethod(), Is.EqualTo("Sum"));
                Assert.That(own.outdoorAirFlowRate(), Is.EqualTo(0.05));
                Assert.That(own.outdoorAirFlowperPerson(), Is.EqualTo(0), "no OpenStudio Flow/Person default");
                Assert.That(own.outdoorAirFlowperFloorArea(), Is.EqualTo(0));
                Assert.That(own.outdoorAirFlowAirChangesperHour(), Is.EqualTo(0));
                Assert.That(Schedule(own), Is.EqualTo(Schedule(SpaceTypeDsoa(result, TestSupport.BoxSpace))), "the same selected profile as the SpaceType delivery");
                Assert.That(IdealLoads(result, TestSupport.BoxSpace).designSpecificationOutdoorAirObject().get().nameString(), Is.EqualTo(own.nameString()), "Ideal Loads delivers the effective (override) DSOA");
            }
        }

        [Test]
        public void TheSpaceOverride_InV1_DeliversNothing()
        {
            AnalyticalModel model = TestSupport.Load(TestSupport.Two);
            TestSupport.ChangeSpace(model, TestSupport.SpaceA, space => space.SetValue(SpaceParameter.OutsideSupplyAirFlow, 0.04));
            using (OpenStudioConversionResult result = Convert_(model))
            {
                global::OpenStudio.Space a = result.Model.getSpaces().Single(x => NamedFor(x.nameString(), TestSupport.SpaceA));
                global::OpenStudio.DesignSpecificationOutdoorAir own = a.designSpecificationOutdoorAir().get();
                Assert.That(own.outdoorAirFlowRate(), Is.EqualTo(0.04));
                Assert.That(Schedule(own), Is.EqualTo(NotDelivered));
            }
        }

        [Test]
        public void IdealLoads_StatesItsOutdoorAirControls_AndLinksTheInheritedSpaceTypeDsoa()
        {
            using (OpenStudioConversionResult result = Convert_(TestSupport.Load(TestSupport.Two)))
            {
                foreach (Guid space in new[] { TestSupport.SpaceA, TestSupport.SpaceB })
                {
                    global::OpenStudio.ZoneHVACIdealLoadsAirSystem idealLoads = IdealLoads(result, space);
                    Assert.That(idealLoads.demandControlledVentilationType(), Is.EqualTo("None"));
                    Assert.That(idealLoads.outdoorAirEconomizerType(), Is.EqualTo("NoEconomizer"));
                    Assert.That(idealLoads.heatRecoveryType(), Is.EqualTo("None"));
                    Assert.That(idealLoads.designSpecificationOutdoorAirObject().get().nameString(), Is.EqualTo(SpaceTypeDsoa(result, space).nameString()), "the inherited SpaceType DSOA");
                }
            }
        }

        [Test]
        public void V0_NoRequirement_WritesNoDsoa_AndIdealLoadsDeliversNoOutdoorAir()
        {
            AnalyticalModel model = TestSupport.Load(TestSupport.Two);
            TestSupport.ChangeCondition(model, TestSupport.SpaceB, ic => ic.RemoveValue(InternalConditionParameter.SupplyAirChangesPerHour));
            using (OpenStudioConversionResult result = Convert_(model))
            {
                global::OpenStudio.Space b = result.Model.getSpaces().Single(x => NamedFor(x.nameString(), TestSupport.SpaceB));
                Assert.That(b.designSpecificationOutdoorAir().isNull(), Is.True);
                Assert.That(IdealLoads(result, TestSupport.SpaceB).designSpecificationOutdoorAirObject().isNull(), Is.True);
            }
        }
    }
}
