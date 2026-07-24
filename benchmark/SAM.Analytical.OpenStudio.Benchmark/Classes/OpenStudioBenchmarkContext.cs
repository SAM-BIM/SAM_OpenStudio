// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Analytical.Benchmark;

namespace SAM.Analytical.OpenStudio.Benchmark
{
    /// <summary>
    /// Everything the <see cref="Modify.ToBenchmark(AnalyticalModel, OpenStudioBenchmarkContext)"/>
    /// mapping needs that cannot be derived from the source <see cref="AnalyticalModel"/> alone:
    /// the engine-neutral C5 result set (annual energies, coincident peaks, unmet hours) that
    /// carries the measurements, and the run provenance (both model hashes computed by the B1a
    /// helpers, weather identity/hash, engine/SDK versions, commits, timing and state).
    /// <para>
    /// The context is engine-artefact-free: it holds an <see cref="OpenStudioSimulationResultSet"/>
    /// (already reduced to SAM units — kWh, kW, W, hour-of-year) and pre-computed hashes, never an
    /// OpenStudio model, gbXML, SQLite handle or working directory. That keeps the mapping a pure
    /// function of SAM-neutral inputs and lets it be exercised with no OpenStudio install.
    /// </para>
    /// </summary>
    public sealed class OpenStudioBenchmarkContext
    {
        /// <summary>Portable model label (not a filesystem path).</summary>
        public string SourceModelName { get; set; }

        /// <summary>Source SAM model GUID in 32-character lowercase hexadecimal form; null only for a pre-model failure.</summary>
        public string SourceModelGuid { get; set; }

        /// <summary>SHA-256 of the exact input model-file bytes (<c>sha256:</c> + 64 hex); null only when a failure prevented hashing.</summary>
        public string SourceFileHash { get; set; }

        /// <summary>SHA-256 of the canonical neutral SAM model (<c>sha256:</c> + 64 hex); null only when a failure prevented canonicalization.</summary>
        public string CanonicalModelHash { get; set; }

        /// <summary>Canonicalization rules version; <see cref="BenchmarkCanonicalization.CurrentVersion"/> for a v1 producer.</summary>
        public string CanonicalizationVersion { get; set; }

        /// <summary>SAM source revision used for the run (7-64 lowercase hex).</summary>
        public string SamCommit { get; set; }

        /// <summary>Producer repository revision used for the run (7-64 lowercase hex).</summary>
        public string RunnerCommit { get; set; }

        /// <summary>Simulation engine name, e.g. <c>EnergyPlus</c>.</summary>
        public string EngineName { get; set; }

        /// <summary>EnergyPlus engine version; null only when unavailable and explained in <see cref="Warnings"/>/<see cref="Notes"/>.</summary>
        public string EngineVersion { get; set; }

        /// <summary>OpenStudio SDK version (translation/runtime), e.g. <c>3.10.0</c>.</summary>
        public string SdkVersion { get; set; }

        /// <summary>Portable weather identity (station/file identity, not an absolute path).</summary>
        public string WeatherIdentity { get; set; }

        /// <summary>SHA-256 of the exact weather-file bytes (<c>sha256:</c> + 64 hex).</summary>
        public string WeatherHash { get; set; }

        /// <summary>Design-day source used for the run.</summary>
        public DesignDaySource DesignDaySource { get; set; } = DesignDaySource.None;

        /// <summary>ISO 8601 UTC instant the run started/completed.</summary>
        public DateTimeOffset RunTimestampUtc { get; set; }

        /// <summary>Non-negative elapsed wall-clock seconds.</summary>
        public double DurationSeconds { get; set; }

        /// <summary>Run outcome.</summary>
        public RunState State { get; set; } = RunState.Success;

        /// <summary>Deterministically ordered warnings; no machine-specific paths.</summary>
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>Deterministically ordered material assumptions or limitations.</summary>
        public List<string> Notes { get; } = new List<string>();

        /// <summary>
        /// The engine-neutral result set (C5) supplying every measurement. Null for a failure
        /// document, in which case all metrics are emitted as unavailable.
        /// </summary>
        public OpenStudioSimulationResultSet ResultSet { get; set; }

        /// <summary>
        /// Optional per-space design-day (ZoneSizes) results supplying <c>designLoad</c> [W].
        /// Null/empty when no sizing periods ran — <c>designLoad</c> is then emitted as unavailable,
        /// which is the expected v1 headless-annual case. Matched to spaces by <c>Reference</c>
        /// (the SAM space GUID in "N" form) and load type.
        /// </summary>
        public IEnumerable<SpaceSimulationResult> SpaceDesignLoadResults { get; set; }
    }
}
