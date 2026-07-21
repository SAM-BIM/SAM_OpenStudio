// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// C2: internal conditions, schedules and conditioning modes — dedicated latent equipment
    /// instances, native ACH infiltration, SpaceType outdoor air with ventilation schedule,
    /// single-mode thermostats, ZoneControlHumidistat with Percent schedules and Ideal Loads
    /// humidity-control wiring, pollutant/ventilation-function diagnostics, and an end-to-end
    /// EnergyPlus gate exercising latent gains + humidistat.
    /// </summary>
    [TestFixture]
    public class InternalConditionCompletenessTests
    {
        private static string WeatherPath(string extension)
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "resources", "weather", "USA_MA_Boston-Logan.Intl.AP.725090_TMYx.2004-2018" + extension));
        }

        private static double[] Constant(double value)
        {
            double[] result = new double[24];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = value;
            }

            return result;
        }

        private static ProfileLibrary ExtendedProfileLibrary()
        {
            ProfileLibrary result = AnalyticalModelFixtures.CreateProfileLibrary();
            result.Add(new Profile("Office Equipment Latent", ProfileType.EquipmentLatent, Constant(0.5)));
            result.Add(new Profile("Office Humidification", ProfileType.Humidification, Constant(40)));
            result.Add(new Profile("Office Dehumidification", ProfileType.Dehumidification, Constant(60)));
            result.Add(new Profile("Office Ventilation", ProfileType.Ventilation, Constant(1)));
            return result;
        }

        private static InternalCondition OfficeWithExtras()
        {
            InternalCondition result = AnalyticalModelFixtures.CreateOfficeInternalCondition();
            result.SetValue(InternalConditionParameter.EquipmentLatentGainPerArea, 5.0);
            result.SetValue(InternalConditionParameter.EquipmentLatentProfileName, "Office Equipment Latent");
            return result;
        }

        [Test]
        public void LatentEquipment_CreatesDedicatedLatentInstance()
        {
            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(internalConditionOverride: OfficeWithExtras(), profileLibraryOverride: ExtendedProfileLibrary()).ToOpenStudio();

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Model.getElectricEquipmentDefinitions().Count, Is.EqualTo(2), "Sensible + dedicated latent equipment instances");

            global::OpenStudio.ElectricEquipmentDefinition latent = result.Model.getElectricEquipmentDefinitions().Single(x => x.nameString().Contains("Latent"));
            Assert.That(latent.fractionLatent(), Is.EqualTo(1.0).Within(1e-9));
            Assert.That(latent.fractionRadiant(), Is.EqualTo(0.0).Within(1e-9), "Fractions must sum to 1 — a fully latent instance carries no radiant part");
            Assert.That(latent.wattsperSpaceFloorArea().get(), Is.EqualTo(5.0).Within(1e-9));

            global::OpenStudio.ElectricEquipmentDefinition sensible = result.Model.getElectricEquipmentDefinitions().Single(x => !x.nameString().Contains("Latent"));
            Assert.That(sensible.fractionLatent(), Is.EqualTo(0.0).Within(1e-9));

            Assert.That(result.Model.getElectricEquipments().Count, Is.EqualTo(2));
            string sensibleScheduleName = result.Model.getElectricEquipments().Single(x => !x.nameString().Contains("Latent")).schedule().get().nameString();
            string latentScheduleName = result.Model.getElectricEquipments().Single(x => x.nameString().Contains("Latent")).schedule().get().nameString();
            Assert.That(latentScheduleName, Is.Not.EqualTo(sensibleScheduleName), "Asymmetric sensible/latent profiles keep independent schedules");
        }

        [Test]
        public void Thermostat_HeatingOnly_CreatesSingleModeThermostat()
        {
            InternalCondition internalCondition = AnalyticalModelFixtures.CreateOfficeInternalCondition();
            InternalCondition heatingOnly = new InternalCondition("Office Heating Only");
            heatingOnly.SetValue(InternalConditionParameter.HeatingProfileName, "Office Heating");

            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(internalConditionOverride: heatingOnly).ToOpenStudio();

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Model.getThermostatSetpointDualSetpoints().Count, Is.EqualTo(1));

            global::OpenStudio.ThermostatSetpointDualSetpoint thermostat = result.Model.getThermostatSetpointDualSetpoints()[0];
            Assert.That(thermostat.heatingSetpointTemperatureSchedule().isNull(), Is.False, "Heating schedule must be set");
            Assert.That(thermostat.coolingSetpointTemperatureSchedule().isNull(), Is.True, "Cooling schedule must stay empty — no invented setpoints");
            Assert.That(result.Model.getZoneHVACIdealLoadsAirSystems().Count, Is.EqualTo(1));
        }

        [Test]
        public void Thermostat_CoolingOnly_CreatesSingleModeThermostat()
        {
            InternalCondition coolingOnly = new InternalCondition("Office Cooling Only");
            coolingOnly.SetValue(InternalConditionParameter.CoolingProfileName, "Office Cooling");

            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(internalConditionOverride: coolingOnly).ToOpenStudio();

            Assert.That(result.IsValid, Is.True);
            global::OpenStudio.ThermostatSetpointDualSetpoint thermostat = result.Model.getThermostatSetpointDualSetpoints()[0];
            Assert.That(thermostat.coolingSetpointTemperatureSchedule().isNull(), Is.False);
            Assert.That(thermostat.heatingSetpointTemperatureSchedule().isNull(), Is.True);
        }

        [Test]
        public void Thermostat_MissingBothSetpoints_RaisesError()
        {
            InternalCondition bare = new InternalCondition("Office Bare");

            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(internalConditionOverride: bare).ToOpenStudio();

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OS-HVAC-001" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error), Is.True);
            Assert.That(result.Model.getThermostatSetpointDualSetpoints().Count, Is.EqualTo(0));
            Assert.That(result.Model.getZoneHVACIdealLoadsAirSystems().Count, Is.EqualTo(0));
        }

        [Test]
        public void Humidistat_HumidificationOnly_WiresIdealLoads()
        {
            InternalCondition internalCondition = AnalyticalModelFixtures.CreateOfficeInternalCondition();
            internalCondition.SetValue(InternalConditionParameter.HumidificationProfileName, "Office Humidification");

            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(internalConditionOverride: internalCondition, profileLibraryOverride: ExtendedProfileLibrary()).ToOpenStudio();

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Model.getZoneControlHumidistats().Count, Is.EqualTo(1));

            global::OpenStudio.ZoneControlHumidistat humidistat = result.Model.getZoneControlHumidistats()[0];
            Assert.That(humidistat.humidifyingRelativeHumiditySetpointSchedule().isNull(), Is.False);
            Assert.That(humidistat.dehumidifyingRelativeHumiditySetpointSchedule().isNull(), Is.True);

            global::OpenStudio.ZoneHVACIdealLoadsAirSystem idealLoads = result.Model.getZoneHVACIdealLoadsAirSystems()[0];
            Assert.That(idealLoads.humidificationControlType(), Is.EqualTo("Humidistat"));
            Assert.That(idealLoads.dehumidificationControlType(), Is.Not.EqualTo("Humidistat"), "Dehumidification stays at the documented EnergyPlus default without a dehumidification profile");

            global::OpenStudio.Schedule schedule = humidistat.humidifyingRelativeHumiditySetpointSchedule().get();
            global::OpenStudio.OptionalScheduleTypeLimits limits = schedule.scheduleTypeLimits();
            Assert.That(limits.isNull(), Is.False);
            Assert.That(limits.get().upperLimitValue().get(), Is.EqualTo(100).Within(1e-9), "Percent type limits");
        }

        [Test]
        public void Humidistat_DehumidificationOnly_WiresIdealLoads()
        {
            InternalCondition internalCondition = AnalyticalModelFixtures.CreateOfficeInternalCondition();
            internalCondition.SetValue(InternalConditionParameter.DehumidificationProfileName, "Office Dehumidification");

            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(internalConditionOverride: internalCondition, profileLibraryOverride: ExtendedProfileLibrary()).ToOpenStudio();

            Assert.That(result.IsValid, Is.True);

            global::OpenStudio.ZoneControlHumidistat humidistat = result.Model.getZoneControlHumidistats()[0];
            Assert.That(humidistat.dehumidifyingRelativeHumiditySetpointSchedule().isNull(), Is.False);
            Assert.That(humidistat.humidifyingRelativeHumiditySetpointSchedule().isNull(), Is.True);

            global::OpenStudio.ZoneHVACIdealLoadsAirSystem idealLoads = result.Model.getZoneHVACIdealLoadsAirSystems()[0];
            Assert.That(idealLoads.dehumidificationControlType(), Is.EqualTo("Humidistat"));
            Assert.That(idealLoads.humidificationControlType(), Is.EqualTo("None"), "Humidification stays off without a humidification profile");
        }

        [Test]
        public void Humidistat_NamedButUnresolvedProfile_RaisesError()
        {
            InternalCondition internalCondition = AnalyticalModelFixtures.CreateOfficeInternalCondition();
            internalCondition.SetValue(InternalConditionParameter.HumidificationProfileName, "Missing Humidification Profile");

            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(internalConditionOverride: internalCondition, profileLibraryOverride: ExtendedProfileLibrary()).ToOpenStudio();

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OS-SCH-001" && d.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error && d.Message.Contains("Missing Humidification Profile")), Is.True);
            Assert.That(result.Model.getZoneControlHumidistats().Count, Is.EqualTo(0));
        }

        [Test]
        public void Ventilation_CreatesSpaceTypeOutdoorAir_WithSchedule()
        {
            InternalCondition internalCondition = AnalyticalModelFixtures.CreateOfficeInternalCondition();
            internalCondition.SetValue(InternalConditionParameter.SupplyAirFlowPerArea, 0.001);
            internalCondition.SetValue(InternalConditionParameter.SupplyAirFlowPerPerson, 0.005);
            internalCondition.SetValue(InternalConditionParameter.VentilationProfileName, "Office Ventilation");

            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(internalConditionOverride: internalCondition, profileLibraryOverride: ExtendedProfileLibrary()).ToOpenStudio();

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Model.getDesignSpecificationOutdoorAirs().Count, Is.EqualTo(2), "SpaceType DSOA plus the per-space override (SpaceParameter.OutsideSupplyAirFlow)");

            global::OpenStudio.DesignSpecificationOutdoorAir spaceTypeOutdoorAir = result.Model.getDesignSpecificationOutdoorAirs().Single(x => x.nameString().Contains("InternalCondition"));
            Assert.That(spaceTypeOutdoorAir.outdoorAirMethod(), Is.EqualTo("Sum"));
            Assert.That(spaceTypeOutdoorAir.outdoorAirFlowperFloorArea(), Is.EqualTo(0.001).Within(1e-9));
            Assert.That(spaceTypeOutdoorAir.outdoorAirFlowperPerson(), Is.EqualTo(0.005).Within(1e-9));
            Assert.That(spaceTypeOutdoorAir.outdoorAirFlowRateFractionSchedule().isNull(), Is.False, "Ventilation profile mapped to the outdoor-air schedule");
        }

        [Test]
        public void Pollutant_RaisesUnsupportedDiagnostic()
        {
            InternalCondition internalCondition = AnalyticalModelFixtures.CreateOfficeInternalCondition();
            internalCondition.SetValue(InternalConditionParameter.PollutantGenerationPerArea, 0.5);

            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(internalConditionOverride: internalCondition).ToOpenStudio();

            Assert.That(result.Diagnostics.Any(d => d.Code == "SAM-OS-IC-001" && d.Message.Contains("Pollutant")), Is.True, "Pollutant data must be reported, never silently dropped");
            Assert.That(result.Statistics.UnsupportedObjects, Is.GreaterThan(0));
        }

        [Test]
        public void RadiantProportions_ComeFromSamParameters()
        {
            InternalCondition internalCondition = AnalyticalModelFixtures.CreateOfficeInternalCondition();
            internalCondition.SetValue(InternalConditionParameter.OccupancyRadiantProportion, 0.5);
            internalCondition.SetValue(InternalConditionParameter.LightingRadiantProportion, 0.2);
            internalCondition.SetValue(InternalConditionParameter.EquipmentRadiantProportion, 0.1);

            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(internalConditionOverride: internalCondition).ToOpenStudio();

            Assert.That(result.Model.getPeopleDefinitions()[0].fractionRadiant(), Is.EqualTo(0.5).Within(1e-9));
            Assert.That(result.Model.getLightsDefinitions()[0].fractionRadiant(), Is.EqualTo(0.2).Within(1e-9));
            Assert.That(result.Model.getElectricEquipmentDefinitions()[0].fractionRadiant(), Is.EqualTo(0.1).Within(1e-9));
        }

        [Test]
        [Category("Simulation")]
        public void LatentAndHumidistat_EndToEnd_EnergyPlusRun()
        {
            string epwPath = WeatherPath(".epw");
            Assert.That(File.Exists(epwPath), Is.True, $"Pinned weather fixture missing: {epwPath}");

            string outputDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "c2_latent_humidistat");
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, true);
            }

            InternalCondition internalCondition = OfficeWithExtras();
            internalCondition.SetValue(InternalConditionParameter.HumidificationProfileName, "Office Humidification");
            internalCondition.SetValue(InternalConditionParameter.DehumidificationProfileName, "Office Dehumidification");

            OpenStudioConversionResult result = AnalyticalModelFixtures.SingleBox(internalConditionOverride: internalCondition, profileLibraryOverride: ExtendedProfileLibrary()).ToOpenStudio(epwPath, outputDirectory);

            foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in result.Diagnostics)
            {
                TestContext.Out.WriteLine(diagnostic.ToString());
            }

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.RunResult, Is.Not.Null);
            Assert.That(result.RunResult.ExitCode, Is.EqualTo(0));
            Assert.That(result.RunResult.FatalErrors, Is.Empty);
            Assert.That(result.RunResult.SevereErrors, Is.Empty);
            Assert.That(result.RunResult.Success, Is.True);
            Assert.That(result.Loads, Is.Not.Null);
            Assert.That(result.Loads.TotalHeating + result.Loads.TotalCooling, Is.GreaterThan(0));

            using (System.Data.SQLite.SQLiteConnection connection = new System.Data.SQLite.SQLiteConnection("Data Source=" + result.RunResult.SqlPath + ";Read Only=True"))
            {
                connection.Open();
                using (System.Data.SQLite.SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT MIN(rd.Value), MAX(rd.Value) FROM ReportData rd JOIN ReportDataDictionary rdd ON rd.ReportDataDictionaryIndex = rdd.ReportDataDictionaryIndex WHERE rdd.Name = 'Zone Air Relative Humidity'";
                    using (System.Data.SQLite.SQLiteDataReader reader = command.ExecuteReader())
                    {
                        Assert.That(reader.Read(), Is.True, "Zone Air Relative Humidity output must be present");
                        double min = reader.GetDouble(0);
                        double max = reader.GetDouble(1);
                        TestContext.Out.WriteLine($"Zone Air Relative Humidity range: {min:0.0}–{max:0.0} %");
                        Assert.That(min, Is.GreaterThanOrEqualTo(0).And.LessThan(100));
                        Assert.That(max, Is.GreaterThan(0).And.LessThanOrEqualTo(100), "Humidity output must be a sane percentage");
                    }
                }
            }
        }
    }
}
