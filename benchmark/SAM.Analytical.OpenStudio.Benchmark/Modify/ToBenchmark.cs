// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Linq;
using SAM.Analytical.Benchmark;

namespace SAM.Analytical.OpenStudio.Benchmark
{
    public static partial class Modify
    {
        /// <summary>
        /// Maps a simulated SAM <see cref="AnalyticalModel"/> and its run context to a B1a
        /// benchmark document (schema v1, route <c>Native-OpenStudio</c>). Measurements are read
        /// from the engine-neutral C5 result set through the established SAM mappings
        /// (<see cref="Convert.ToSAM(OpenStudioSimulationResultSet, AnalyticalModel)"/> for the
        /// model, <see cref="Convert.ToSAM_SpaceSimulationResults(OpenStudioSimulationResultSet, AnalyticalModel)"/>
        /// for spaces) so this producer never re-extracts or re-converts anything.
        /// </summary>
        /// <remarks>
        /// Units are emitted with explicit tokens and no re-scaling: annual consumption is stored
        /// as kWh exactly as the one J→kWh authority (<c>Core.OpenStudio.Query.JoulesToKilowattHours</c>)
        /// produced it, model peaks as kW, space peaks/design loads as W, hours as hour-of-year in
        /// 0..8759. A measurement that is present, finite and in range is <c>available</c>; anything
        /// missing or out of range is <c>available:false</c> with a null value. A measured zero
        /// stays a value of 0. Peak load and its hour are coupled: a peak with no valid hour-of-year
        /// is emitted as unavailable rather than a misleading zero.
        /// <para>
        /// S11 rules: an all-zero peak (load exactly 0) is a measured load of 0 with NO hour — every
        /// hour of the year ties, so no hour is the peak hour (the SAM result's hour 0 is the earliest
        /// of that full-year tie and is not published). An annual consumption whose result set carried
        /// no zone key is unavailable, not 0 kWh. A successful document carries the
        /// <see cref="TotalBasisNote"/> (F3): cooling and heating are the EnergyPlus Ideal Loads
        /// supply-air Total energies, cooling including latent.
        /// </para>
        /// </remarks>
        /// <param name="analyticalModel">The neutral source model (provides GUIDs, names, areas and volumes).</param>
        /// <param name="openStudioBenchmarkContext">Run context: result set, hashes and provenance.</param>
        /// <returns>The benchmark document, or null when either argument is null.</returns>
        public static BenchmarkDocument ToBenchmark(this AnalyticalModel analyticalModel, OpenStudioBenchmarkContext openStudioBenchmarkContext)
        {
            return ToBenchmark(analyticalModel, openStudioBenchmarkContext, BenchmarkInputs(analyticalModel, openStudioBenchmarkContext));
        }

        /// <summary>
        /// The F3 basis label on every successful OpenStudio B1a document. The values are not changed:
        /// the route always reported the Ideal Loads Total energies; this states it.
        /// </summary>
        public const string TotalBasisNote = "Cooling metrics are EnergyPlus Ideal Loads Supply Air Total cooling (sensible + latent); heating is Total heating.";

        /// <summary>
        /// The SAM result objects the document is mapped from, built once from the context's result set
        /// through the established SAM mappings. Null when either argument is null.
        /// </summary>
        public static OpenStudioBenchmarkInputs BenchmarkInputs(this AnalyticalModel analyticalModel, OpenStudioBenchmarkContext openStudioBenchmarkContext)
        {
            if (analyticalModel == null || openStudioBenchmarkContext == null)
            {
                return null;
            }

            OpenStudioSimulationResultSet resultSet = openStudioBenchmarkContext.ResultSet;
            return new OpenStudioBenchmarkInputs
            {
                ModelResult = resultSet?.ToSAM(analyticalModel),
                SpaceResults = resultSet?.ToSAM_SpaceSimulationResults(analyticalModel) ?? new List<SpaceSimulationResult>(),
                DesignLoadResults = openStudioBenchmarkContext.SpaceDesignLoadResults?.ToList() ?? new List<SpaceSimulationResult>(),
                HeatingConsumptionKeyed = resultSet?.AnnualHeatingEnergy.Count > 0,
                CoolingConsumptionKeyed = resultSet?.AnnualCoolingEnergy.Count > 0,
            };
        }

        /// <summary>
        /// Maps exactly the given <paramref name="openStudioBenchmarkInputs"/> instances (S11: the objects
        /// that produce the document are the ones captured beside it). The context supplies provenance only.
        /// </summary>
        /// <returns>The benchmark document, or null when any argument is null.</returns>
        public static BenchmarkDocument ToBenchmark(this AnalyticalModel analyticalModel, OpenStudioBenchmarkContext openStudioBenchmarkContext, OpenStudioBenchmarkInputs openStudioBenchmarkInputs)
        {
            if (analyticalModel == null || openStudioBenchmarkContext == null || openStudioBenchmarkInputs == null)
            {
                return null;
            }

            AnalyticalModelSimulationResult modelResult = openStudioBenchmarkInputs.ModelResult;
            List<SpaceSimulationResult> spaceResults = openStudioBenchmarkInputs.SpaceResults ?? new List<SpaceSimulationResult>();

            return new BenchmarkDocument
            {
                SchemaVersion = BenchmarkSchema.CurrentVersion,
                Provenance = Provenance(openStudioBenchmarkContext, modelResult, spaceResults),
                Model = Model(modelResult, openStudioBenchmarkInputs),
                Spaces = Spaces(analyticalModel, spaceResults, openStudioBenchmarkInputs.DesignLoadResults),
            };
        }

        private static BenchmarkProvenance Provenance(OpenStudioBenchmarkContext context, AnalyticalModelSimulationResult modelResult, IEnumerable<SpaceSimulationResult> spaceResults)
        {
            List<string> notes = new List<string>(context.Notes ?? new List<string>());
            if (context.State == RunState.Success && !notes.Contains(TotalBasisNote))
            {
                notes.Add(TotalBasisNote);
                notes.Sort(StringComparer.Ordinal);
            }

            return new BenchmarkProvenance
            {
                SourceModelName = context.SourceModelName,
                SourceModelGuid = context.SourceModelGuid,
                SourceFileHash = context.SourceFileHash,
                CanonicalModelHash = context.CanonicalModelHash,
                CanonicalizationVersion = context.CanonicalizationVersion,
                SamCommit = context.SamCommit,
                RunnerCommit = context.RunnerCommit,
                Engine = new BenchmarkEngine
                {
                    Kind = EngineKind.OpenStudio,
                    Name = context.EngineName,
                    Version = context.EngineVersion,
                    SdkVersion = context.SdkVersion,
                },
                Route = BenchmarkRoute.NativeOpenStudio,
                Weather = new BenchmarkWeather { Identity = context.WeatherIdentity, Hash = context.WeatherHash },
                DesignDaySource = context.DesignDaySource,
                RunTimestampUtc = context.RunTimestampUtc,
                DurationSeconds = context.DurationSeconds,
                State = context.State,
                ResultSources = ResultSources(modelResult, spaceResults),
                Warnings = new List<string>(context.Warnings ?? new List<string>()),
                Notes = notes,
            };
        }

        /// <summary>Distinct, non-empty SAM <c>Query.Source()</c> values retained from the attached results.</summary>
        private static List<string> ResultSources(AnalyticalModelSimulationResult modelResult, IEnumerable<SpaceSimulationResult> spaceResults)
        {
            HashSet<string> sources = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(modelResult?.Source))
            {
                sources.Add(modelResult.Source);
            }

            if (spaceResults != null)
            {
                foreach (SpaceSimulationResult spaceResult in spaceResults)
                {
                    if (!string.IsNullOrWhiteSpace(spaceResult?.Source))
                    {
                        sources.Add(spaceResult.Source);
                    }
                }
            }

            return sources.ToList();
        }

        private static BenchmarkModelResult Model(AnalyticalModelSimulationResult modelResult, OpenStudioBenchmarkInputs inputs)
        {
            (MetricValue heatingLoad, MetricValue heatingHour) = Peak(
                Double(modelResult, AnalyticalModelSimulationResultParameter.PeakHeatingLoad),
                Integer(modelResult, AnalyticalModelSimulationResultParameter.PeakHeatingHour),
                MetricUnit.Kilowatt);

            (MetricValue coolingLoad, MetricValue coolingHour) = Peak(
                Double(modelResult, AnalyticalModelSimulationResultParameter.PeakCoolingLoad),
                Integer(modelResult, AnalyticalModelSimulationResultParameter.PeakCoolingHour),
                MetricUnit.Kilowatt);

            return new BenchmarkModelResult
            {
                // No zone key: the result set's empty sum is not a measured 0 kWh.
                ConsumptionHeating = inputs.HeatingConsumptionKeyed
                    ? NonNegative(Double(modelResult, AnalyticalModelSimulationResultParameter.ConsumptionHeating), MetricUnit.KilowattHour)
                    : MetricValue.Unavailable(MetricUnit.KilowattHour),
                ConsumptionCooling = inputs.CoolingConsumptionKeyed
                    ? NonNegative(Double(modelResult, AnalyticalModelSimulationResultParameter.ConsumptionCooling), MetricUnit.KilowattHour)
                    : MetricValue.Unavailable(MetricUnit.KilowattHour),
                PeakHeatingLoad = heatingLoad,
                PeakHeatingHour = heatingHour,
                PeakCoolingLoad = coolingLoad,
                PeakCoolingHour = coolingHour,
                FloorArea = NonNegative(Double(modelResult, AnalyticalModelSimulationResultParameter.FloorArea), MetricUnit.SquareMetre),
                Volume = NonNegative(Double(modelResult, AnalyticalModelSimulationResultParameter.Volume), MetricUnit.CubicMetre),
            };
        }

        private static List<BenchmarkSpaceResult> Spaces(AnalyticalModel analyticalModel, List<SpaceSimulationResult> spaceResults, IEnumerable<SpaceSimulationResult> designLoadResults)
        {
            List<BenchmarkSpaceResult> result = new List<BenchmarkSpaceResult>();

            List<Space> spaces = analyticalModel.AdjacencyCluster?.GetSpaces();
            if (spaces == null)
            {
                return result;
            }

            List<SpaceSimulationResult> designLoads = designLoadResults?.ToList() ?? new List<SpaceSimulationResult>();

            foreach (Space space in spaces)
            {
                if (space == null)
                {
                    continue;
                }

                string reference = space.Guid.ToString("N");

                result.Add(new BenchmarkSpaceResult
                {
                    Guid = reference,
                    Name = space.Name,
                    Area = NonNegative(Double(space, SpaceParameter.Area), MetricUnit.SquareMetre),
                    Volume = NonNegative(Double(space, SpaceParameter.Volume), MetricUnit.CubicMetre),
                    Heating = Condition(reference, LoadType.Heating, spaceResults, designLoads),
                    Cooling = Condition(reference, LoadType.Cooling, spaceResults, designLoads),
                });
            }

            return result;
        }

        private static BenchmarkConditionResult Condition(string reference, LoadType loadType, List<SpaceSimulationResult> spaceResults, List<SpaceSimulationResult> designLoads)
        {
            SpaceSimulationResult annual = spaceResults.FirstOrDefault(x => Matches(x, reference, loadType) && Double(x, Analytical.SpaceSimulationResultParameter.Load).HasValue);
            SpaceSimulationResult design = designLoads.FirstOrDefault(x => Matches(x, reference, loadType) && Double(x, Analytical.SpaceSimulationResultParameter.DesignLoad).HasValue);

            (MetricValue peakLoad, MetricValue peakHour) = Peak(
                Double(annual, Analytical.SpaceSimulationResultParameter.Load),
                Integer(annual, Analytical.SpaceSimulationResultParameter.LoadIndex),
                MetricUnit.Watt);

            return new BenchmarkConditionResult
            {
                DesignLoad = NonNegative(Double(design, Analytical.SpaceSimulationResultParameter.DesignLoad), MetricUnit.Watt),
                PeakLoad = peakLoad,
                PeakHour = peakHour,
                UnmetHours = NonNegative(Double(annual, Analytical.SpaceSimulationResultParameter.UnmetHours), MetricUnit.Hour),
            };
        }

        private static bool Matches(SpaceSimulationResult spaceSimulationResult, string reference, LoadType loadType)
        {
            return spaceSimulationResult != null
                && string.Equals(spaceSimulationResult.Reference, reference, StringComparison.OrdinalIgnoreCase)
                && spaceSimulationResult.TryGetValue(Analytical.SpaceSimulationResultParameter.LoadType, out string value)
                && string.Equals(value, loadType.ToString(), StringComparison.Ordinal);
        }

        private static MetricValue NonNegative(double? value, MetricUnit unit)
        {
            if (value.HasValue && IsFinite(value.Value) && value.Value >= 0)
            {
                return MetricValue.AvailableValue(value.Value, unit);
            }

            return MetricValue.Unavailable(unit);
        }

        /// <summary>
        /// A peak load and its hour-of-year, coupled: both are available only when the load is a
        /// finite non-negative number and the hour is an integer in 0..8759; otherwise both are
        /// unavailable (a peak without a valid hour is missing, never a measured zero). A load of
        /// exactly 0 is available with its hour unavailable: an all-zero series ties at every hour,
        /// so it has no peak hour (S11 zero rule, symmetric with S10).
        /// </summary>
        private static (MetricValue load, MetricValue hour) Peak(double? loadValue, int? hourValue, MetricUnit loadUnit)
        {
            bool hourValid = hourValue.HasValue && hourValue.Value >= 0 && hourValue.Value <= 8759;
            bool loadValid = loadValue.HasValue && IsFinite(loadValue.Value) && loadValue.Value >= 0;
            if (hourValid && loadValid && loadValue.Value == 0)
            {
                return (MetricValue.AvailableValue(0, loadUnit), MetricValue.Unavailable(MetricUnit.HourOfYear));
            }

            if (hourValid && loadValid)
            {
                return (MetricValue.AvailableValue(loadValue.Value, loadUnit), MetricValue.AvailableValue(hourValue.Value, MetricUnit.HourOfYear));
            }

            return (MetricValue.Unavailable(loadUnit), MetricValue.Unavailable(MetricUnit.HourOfYear));
        }

        private static double? Double(Core.SAMObject sAMObject, Enum parameter)
        {
            if (sAMObject != null && sAMObject.TryGetValue(parameter, out double value) && IsFinite(value))
            {
                return value;
            }

            return null;
        }

        private static int? Integer(Core.SAMObject sAMObject, Enum parameter)
        {
            if (sAMObject != null && sAMObject.TryGetValue(parameter, out int value))
            {
                return value;
            }

            // Integer parameters are sometimes stored as doubles (e.g. a peak hour read back as a
            // double); accept a whole-number double as the hour-of-year.
            double? asDouble = Double(sAMObject, parameter);
            if (asDouble.HasValue && asDouble.Value == Math.Truncate(asDouble.Value))
            {
                return (int)asDouble.Value;
            }

            return null;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
