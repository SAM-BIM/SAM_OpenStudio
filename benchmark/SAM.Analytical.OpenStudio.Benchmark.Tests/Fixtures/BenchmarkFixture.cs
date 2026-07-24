// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Analytical.Benchmark;
using SAM.Core;
using SAM.Geometry.Spatial;
using AnalyticalCreate = SAM.Analytical.Create;

namespace SAM.Analytical.OpenStudio.Benchmark.Tests
{
    /// <summary>
    /// Deterministic fixtures for the benchmark producer tests. A one-space
    /// <see cref="AnalyticalModel"/> (fixed GUID, area and volume) plus a synthetic engine-neutral
    /// <see cref="OpenStudioSimulationResultSet"/> keyed on that space's deterministic Ideal Loads
    /// name for the offline mapping; and a full simulatable one-zone <see cref="SingleBox"/> (the
    /// same box, GUID and name as the C5 fixture) for the live consumption-unit pin.
    /// </summary>
    public static class BenchmarkFixture
    {
        public static readonly Guid SpaceGuid = new Guid("cccccccc-0000-0000-0000-000000000001");

        public const string SpaceName = "Space Single";

        public const double FloorArea = 20.0;

        public const double SpaceVolume = 60.0;

        // Golden measurements for the offline result set (distinct, non-round, in-range).
        public const double AnnualHeatingKwh = 1234.5;

        public const double AnnualCoolingKwh = 678.25;

        public const double PeakHeatingKw = 3.6;

        public const double PeakCoolingKw = 2.4;

        public const int PeakHeatingHour = 205;

        public const int PeakCoolingHour = 4602;

        public const double UnmetHeatingHours = 3.0;

        public const double UnmetCoolingHours = 0.0;

        /// <summary>One conditioned office space with a fixed GUID, 20 m² floor area and 60 m³ volume (no geometry).</summary>
        public static AnalyticalModel SingleSpaceModel()
        {
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster();

            Space space = new Space(SpaceGuid, SpaceName, new Point3D(2.5, 2, 1.5));
            space.SetValue(SpaceParameter.Area, FloorArea);
            space.SetValue(SpaceParameter.Volume, SpaceVolume);
            adjacencyCluster.AddObject(space);

            return new AnalyticalModel(
                "Single Box Model",
                "Benchmark producer offline fixture",
                null,
                null,
                adjacencyCluster,
                new MaterialLibrary("Benchmark Material Library"),
                new ProfileLibrary("Benchmark Profile Library"));
        }

        /// <summary>The Ideal Loads report key EnergyPlus would emit for the fixture space.</summary>
        public static string IdealLoadsKey()
        {
            return Core.OpenStudio.Query.OpenStudioName("IdealLoads", SpaceName, SpaceGuid);
        }

        /// <summary>The golden result set: annual energies [kWh], coincident peaks [kW]/hour-of-year and unmet hours [h].</summary>
        public static OpenStudioSimulationResultSet ResultSet()
        {
            string key = IdealLoadsKey();

            Dictionary<string, double> heatingEnergy = new Dictionary<string, double> { { key, AnnualHeatingKwh } };
            Dictionary<string, double> coolingEnergy = new Dictionary<string, double> { { key, AnnualCoolingKwh } };
            Dictionary<string, double> peakHeatingLoad = new Dictionary<string, double> { { key, PeakHeatingKw } };
            Dictionary<string, double> peakCoolingLoad = new Dictionary<string, double> { { key, PeakCoolingKw } };
            Dictionary<string, int> peakHeatingHour = new Dictionary<string, int> { { key, PeakHeatingHour } };
            Dictionary<string, int> peakCoolingHour = new Dictionary<string, int> { { key, PeakCoolingHour } };
            Dictionary<string, double> unmetHeating = new Dictionary<string, double> { { key, UnmetHeatingHours } };
            Dictionary<string, double> unmetCooling = new Dictionary<string, double> { { key, UnmetCoolingHours } };
            Dictionary<string, double> empty = new Dictionary<string, double>();

            return new OpenStudioSimulationResultSet(
                heatingEnergy,
                coolingEnergy,
                peakHeatingLoad,
                peakCoolingLoad,
                peakHeatingHour,
                peakCoolingHour,
                PeakHeatingKw,
                PeakHeatingHour,
                PeakCoolingKw,
                PeakCoolingHour,
                unmetHeating,
                unmetCooling,
                empty,
                empty,
                empty,
                empty,
                empty,
                empty,
                empty,
                null,
                null,
                null,
                12.5,
                0,
                0,
                0);
        }

        /// <summary>A context with valid provenance, ready to feed a successful benchmark document.</summary>
        public static OpenStudioBenchmarkContext Context(AnalyticalModel model, OpenStudioSimulationResultSet resultSet)
        {
            return new OpenStudioBenchmarkContext
            {
                SourceModelName = model.Name,
                SourceModelGuid = model.Guid.ToString("N"),
                SourceFileHash = BenchmarkHash.ComputeSha256(new byte[] { 1, 2, 3, 4 }),
                CanonicalModelHash = BenchmarkCanonicalJson.ComputeSha256(model.ToJsonObject().ToJsonString()),
                CanonicalizationVersion = BenchmarkCanonicalization.CurrentVersion,
                SamCommit = "0123456789abcdef",
                RunnerCommit = "fedcba9876543210",
                EngineName = "EnergyPlus",
                EngineVersion = "24.2.0",
                SdkVersion = "3.10.0",
                WeatherIdentity = "USA_TEST_Weather",
                WeatherHash = BenchmarkHash.ComputeSha256(new byte[] { 5, 6, 7, 8 }),
                DesignDaySource = DesignDaySource.None,
                RunTimestampUtc = new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero),
                DurationSeconds = 12.5,
                State = RunState.Success,
                ResultSet = resultSet,
            };
        }

        // ---- Full simulatable fixture (mirrors the C5 SingleBox: same box, GUID and name) ----

        /// <summary>Opaque wall construction, layers inside → outside: Plasterboard 12.5 mm, Insulation 100 mm, Brick 100 mm.</summary>
        private static readonly Construction WallConstruction = new Construction(new Guid("11111111-1111-1111-1111-111111111111"), "Fixture Construction", new List<ConstructionLayer>
        {
            new ConstructionLayer("Plasterboard", 0.0125),
            new ConstructionLayer("Insulation", 0.1),
            new ConstructionLayer("Brick", 0.1),
        });

        /// <summary>Double-glazing aperture construction, panes inside → outside: Glass 6 mm, Air 12 mm, Glass 6 mm.</summary>
        private static readonly ApertureConstruction WindowConstruction = new ApertureConstruction(new Guid("22222222-2222-2222-2222-222222222222"), "Fixture Window", ApertureType.Window, new List<ConstructionLayer>
        {
            new ConstructionLayer("Glass", 0.006),
            new ConstructionLayer("Air", 0.012),
            new ConstructionLayer("Glass", 0.006),
        });

        /// <summary>
        /// One-zone box (x 0–5, y 0–4, z 0–3) with a south window — the same conditioned office box
        /// the C5 tests simulate. Reused here to pin the annual-consumption unit against the raw SQL.
        /// </summary>
        public static AnalyticalModel SingleBox()
        {
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster();

            Space space = new Space(SpaceGuid, SpaceName, P(2.5, 2, 1.5));
            space.SetValue(SpaceParameter.Area, FloorArea);
            space.SetValue(SpaceParameter.Volume, SpaceVolume);
            space.SetValue(SpaceParameter.OutsideSupplyAirFlow, 0.02);
            space.InternalCondition = CreateOfficeInternalCondition();
            adjacencyCluster.AddObject(space);

            List<Panel> panels = new List<Panel>
            {
                AnalyticalCreate.Panel(WallConstruction, PanelType.SlabOnGrade, F(P(0, 0, 0), P(5, 0, 0), P(5, 4, 0), P(0, 4, 0))),
                AnalyticalCreate.Panel(WallConstruction, PanelType.Roof, F(P(0, 0, 3), P(5, 0, 3), P(5, 4, 3), P(0, 4, 3))),
                AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(0, 0, 0), P(5, 0, 0), P(5, 0, 3), P(0, 0, 3))),
                AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(0, 0, 0), P(0, 4, 0), P(0, 4, 3), P(0, 0, 3))),
                AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(0, 4, 0), P(5, 4, 0), P(5, 4, 3), P(0, 4, 3))),
                AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(5, 0, 0), P(5, 4, 0), P(5, 4, 3), P(5, 0, 3))),
            };

            panels[2].AddAperture(AnalyticalCreate.Aperture(WindowConstruction, F(P(1, 0, 0.8), P(3, 0, 0.8), P(3, 0, 2.2), P(1, 0, 2.2))));

            foreach (Panel panel in panels)
            {
                adjacencyCluster.AddObject(panel);
                adjacencyCluster.AddRelation(space, panel);
            }

            return new AnalyticalModel("Single Box Model", "Benchmark live consumption-unit fixture", null, null, adjacencyCluster, CreateMaterialLibrary(), CreateProfileLibrary());
        }

        private static MaterialLibrary CreateMaterialLibrary()
        {
            OpaqueMaterial brick = new OpaqueMaterial(new Guid("33333333-0000-0000-0000-000000000001"), "Brick", "Brick", "Fixture brick", 0.84, 1700, 800);
            brick.SetValue(OpaqueMaterialParameter.ExternalEmissivity, 0.9);
            brick.SetValue(OpaqueMaterialParameter.ExternalSolarReflectance, 0.3);
            brick.SetValue(OpaqueMaterialParameter.ExternalLightReflectance, 0.3);

            OpaqueMaterial insulation = new OpaqueMaterial(new Guid("33333333-0000-0000-0000-000000000002"), "Insulation", "Insulation", "Fixture insulation", 0.035, 25, 1400);
            insulation.SetValue(OpaqueMaterialParameter.ExternalEmissivity, 0.9);
            insulation.SetValue(OpaqueMaterialParameter.ExternalSolarReflectance, 0.3);
            insulation.SetValue(OpaqueMaterialParameter.ExternalLightReflectance, 0.3);

            OpaqueMaterial plasterboard = new OpaqueMaterial(new Guid("33333333-0000-0000-0000-000000000003"), "Plasterboard", "Plasterboard", "Fixture plasterboard", 0.25, 900, 1000);
            plasterboard.SetValue(OpaqueMaterialParameter.ExternalEmissivity, 0.9);
            plasterboard.SetValue(OpaqueMaterialParameter.ExternalSolarReflectance, 0.5);
            plasterboard.SetValue(OpaqueMaterialParameter.ExternalLightReflectance, 0.5);

            TransparentMaterial glass = new TransparentMaterial(new Guid("33333333-0000-0000-0000-000000000004"), "Glass", "Glass", "Fixture glass", 1.0, 2500, 840);
            glass.SetValue(TransparentMaterialParameter.SolarTransmittance, 0.7);
            glass.SetValue(TransparentMaterialParameter.InternalSolarReflectance, 0.07);
            glass.SetValue(TransparentMaterialParameter.ExternalSolarReflectance, 0.07);
            glass.SetValue(TransparentMaterialParameter.LightTransmittance, 0.8);
            glass.SetValue(TransparentMaterialParameter.InternalLightReflectance, 0.07);
            glass.SetValue(TransparentMaterialParameter.ExternalLightReflectance, 0.07);
            glass.SetValue(TransparentMaterialParameter.InternalEmissivity, 0.84);
            glass.SetValue(TransparentMaterialParameter.ExternalEmissivity, 0.84);

            GasMaterial air = new GasMaterial(new Guid("33333333-0000-0000-0000-000000000005"), "Air");

            MaterialLibrary result = new MaterialLibrary("Fixture Material Library");
            result.Add(brick);
            result.Add(insulation);
            result.Add(plasterboard);
            result.Add(glass);
            result.Add(air);
            return result;
        }

        private static ProfileLibrary CreateProfileLibrary(double heatingSetpoint = 21, double coolingSetpoint = 25)
        {
            double[] officeHours = new double[24];
            for (int i = 8; i <= 17; i++)
            {
                officeHours[i] = 1;
            }

            double[] alwaysOn = new double[24];
            double[] heating = new double[24];
            double[] cooling = new double[24];
            for (int i = 0; i < 24; i++)
            {
                alwaysOn[i] = 1;
                heating[i] = heatingSetpoint;
                cooling[i] = coolingSetpoint;
            }

            ProfileLibrary result = new ProfileLibrary("Fixture Profile Library");
            result.Add(new Profile("Office Occupancy", ProfileType.Occupancy, officeHours));
            result.Add(new Profile("Office Lighting", ProfileType.Lighting, officeHours));
            result.Add(new Profile("Office Equipment", ProfileType.EquipmentSensible, alwaysOn));
            result.Add(new Profile("Office Infiltration", ProfileType.Infiltration, alwaysOn));
            result.Add(new Profile("Office Heating", ProfileType.Heating, heating));
            result.Add(new Profile("Office Cooling", ProfileType.Cooling, cooling));
            return result;
        }

        private static InternalCondition CreateOfficeInternalCondition()
        {
            InternalCondition result = new InternalCondition(new Guid("44444444-0000-0000-0000-000000000001"), "Office");
            result.SetValue(InternalConditionParameter.AreaPerPerson, 10.0);
            result.SetValue(InternalConditionParameter.OccupancySensibleGainPerPerson, 75.0);
            result.SetValue(InternalConditionParameter.OccupancyLatentGainPerPerson, 55.0);
            result.SetValue(InternalConditionParameter.LightingGainPerArea, 8.0);
            result.SetValue(InternalConditionParameter.EquipmentSensibleGainPerArea, 12.0);
            result.SetValue(InternalConditionParameter.InfiltrationAirChangesPerHour, 0.5);
            result.SetValue(InternalConditionParameter.OccupancyProfileName, "Office Occupancy");
            result.SetValue(InternalConditionParameter.LightingProfileName, "Office Lighting");
            result.SetValue(InternalConditionParameter.EquipmentSensibleProfileName, "Office Equipment");
            result.SetValue(InternalConditionParameter.InfiltrationProfileName, "Office Infiltration");
            result.SetValue(InternalConditionParameter.HeatingProfileName, "Office Heating");
            result.SetValue(InternalConditionParameter.CoolingProfileName, "Office Cooling");
            return result;
        }

        private static Point3D P(double x, double y, double z)
        {
            return new Point3D(x, y, z);
        }

        private static Face3D F(params Point3D[] point3Ds)
        {
            return new Face3D(new Polygon3D(point3Ds));
        }
    }
}
