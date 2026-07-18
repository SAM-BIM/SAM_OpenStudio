// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// M6: thermostats, Ideal Loads and the first end-to-end EnergyPlus simulation with the
    /// strengthened result gate (plan §13 M6): exit 0, no fatal/severe errors, annual heating ≥ 0
    /// AND cooling ≥ 0 AND at least one &gt; 0, zone-level result count == conditioned-zone count.
    /// </summary>
    [TestFixture]
    public class SimulationTests
    {
        private static string WeatherPath(string extension)
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "resources", "weather", "USA_MA_Boston-Logan.Intl.AP.725090_TMYx.2004-2018" + extension));
        }

        [Test]
        public void ConditionedZones_GetThermostatAndIdealLoads()
        {
            OpenStudioConversionResult result = AnalyticalModelFixtures.TwoAdjacentBoxes().ToOpenStudio();
            global::OpenStudio.Model model = result.Model;

            Assert.That(result.IsValid, Is.True);
            Assert.That(model.getZoneHVACIdealLoadsAirSystems().Count, Is.EqualTo(2), "One Ideal Loads system per conditioned zone");
            Assert.That(model.getThermostatSetpointDualSetpoints().Count, Is.EqualTo(2), "One dual-setpoint thermostat per conditioned zone");

            foreach (global::OpenStudio.ThermalZone thermalZone in model.getThermalZones())
            {
                global::OpenStudio.OptionalThermostatSetpointDualSetpoint thermostat = thermalZone.thermostatSetpointDualSetpoint();
                Assert.That(thermostat != null && !thermostat.isNull(), $"{thermalZone.nameString()} must have a thermostat");
            }
        }

        [Test]
        public void IsConditioned_FollowsSamNameConvention()
        {
            Space conditioned = new Space("Office Space");
            conditioned.InternalCondition = new InternalCondition("Office");
            Assert.That(conditioned.IsConditioned(), Is.True);

            Space unconditioned = new Space("Store");
            unconditioned.InternalCondition = new InternalCondition("Office Unconditioned");
            Assert.That(unconditioned.IsConditioned(), Is.False);

            Space external = new Space("Outside");
            external.InternalCondition = new InternalCondition("External Zone");
            Assert.That(external.IsConditioned(), Is.False);

            Assert.That(new Space("Bare").IsConditioned(), Is.False, "No InternalCondition means unconditioned");
        }

        [Test]
        public void HeatingAboveCooling_RaisesError_NoThermostat()
        {
            ProfileLibrary invertedSetpoints = AnalyticalModelFixtures.CreateProfileLibrary(heatingSetpoint: 27, coolingSetpoint: 21);

            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(profileLibraryOverride: invertedSetpoints).ToOpenStudio();

            Assert.That(result.IsValid, Is.False, "Heating above cooling must invalidate the conversion");
            Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OS-HVAC-001" && d.Message.Contains("exceeds")), Is.True);
            Assert.That(result.Model.getThermostatSetpointDualSetpoints().Count, Is.EqualTo(0));
            Assert.That(result.Model.getZoneHVACIdealLoadsAirSystems().Count, Is.EqualTo(0), "No Ideal Loads without a valid thermostat");
        }

        [Test]
        [Category("Simulation")]
        public void SingleBox_EndToEnd_EnergyPlusRun_MeetsResultGate()
        {
            string epwPath = WeatherPath(".epw");
            Assert.That(File.Exists(epwPath), Is.True, $"Pinned weather fixture missing: {epwPath}");

            string outputDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "e2e_single_box");
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, true);
            }

            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox().ToOpenStudio(epwPath, outputDirectory);

            foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in result.Diagnostics)
            {
                TestContext.Out.WriteLine(diagnostic.ToString());
            }

            Assert.That(result.OsmPath, Is.Not.Null.And.Not.Empty);
            Assert.That(File.Exists(result.OsmPath), Is.True, "OSM must be saved");
            Assert.That(File.Exists(result.OswPath), Is.True, "OSW must be generated");

            Core.OpenStudio.OpenStudioRunResult runResult = result.RunResult;
            Assert.That(runResult, Is.Not.Null, "A run result must be produced");
            Assert.That(runResult.ExitCode, Is.EqualTo(0), "OpenStudio CLI must exit 0");
            Assert.That(runResult.FatalErrors, Is.Empty, "EnergyPlus must report no fatal errors");
            Assert.That(runResult.SevereErrors, Is.Empty, "EnergyPlus must report no severe errors");
            Assert.That(runResult.SqlPath, Is.Not.Null, "SQLite results must exist");
            Assert.That(runResult.Success, Is.True);

            OpenStudioLoadSummary loads = result.Loads;
            Assert.That(loads, Is.Not.Null, "Loads must be extracted");
            Assert.That(loads.IsFinite, Is.True, "Loads must be finite and non-negative");
            Assert.That(loads.TotalHeating, Is.GreaterThanOrEqualTo(0));
            Assert.That(loads.TotalCooling, Is.GreaterThanOrEqualTo(0));
            Assert.That(loads.TotalHeating + loads.TotalCooling, Is.GreaterThan(0), "Zeros-only results fail the gate (Boston climate guarantees load)");
            Assert.That(loads.ZoneHeating.Count, Is.EqualTo(1), "Zone-level result count must equal the conditioned-zone count");
            Assert.That(loads.ZoneCooling.Count, Is.EqualTo(1));

            TestContext.Out.WriteLine(string.Format("Annual heating {0:0.0} kWh, cooling {1:0.0} kWh", loads.TotalHeating, loads.TotalCooling));
        }
    }
}
