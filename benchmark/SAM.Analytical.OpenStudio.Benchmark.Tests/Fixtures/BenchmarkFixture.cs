// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Analytical.Benchmark;
using SAM.Core;
using SAM.Geometry.Spatial;

namespace SAM.Analytical.OpenStudio.Benchmark.Tests
{
    /// <summary>
    /// Deterministic offline fixtures for the benchmark producer tests. A one-space
    /// <see cref="AnalyticalModel"/> (fixed GUID, area and volume) and a synthetic engine-neutral
    /// <see cref="OpenStudioSimulationResultSet"/> keyed on that space's deterministic Ideal Loads
    /// name — the same C5 result-set type and <c>ToSAM</c> mappings the live route feeds through,
    /// built here directly so the mapping can be exercised with no OpenStudio/EnergyPlus install.
    /// </summary>
    public static class BenchmarkFixture
    {
        public static readonly Guid SpaceGuid = new Guid("cccccccc-0000-0000-0000-000000000001");

        public const string SpaceName = "Space Single";

        public const double FloorArea = 20.0;

        public const double SpaceVolume = 60.0;

        // Golden measurements (chosen distinct, non-round, in-range).
        public const double AnnualHeatingKwh = 1234.5;

        public const double AnnualCoolingKwh = 678.25;

        public const double PeakHeatingKw = 3.6;

        public const double PeakCoolingKw = 2.4;

        public const int PeakHeatingHour = 205;

        public const int PeakCoolingHour = 4602;

        public const double UnmetHeatingHours = 3.0;

        public const double UnmetCoolingHours = 0.0;

        /// <summary>One conditioned office space with a fixed GUID, 20 m² floor area and 60 m³ volume.</summary>
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
    }
}
