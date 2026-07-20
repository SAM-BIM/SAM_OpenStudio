// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Core.OpenStudio
{
    /// <summary>Stages of one OpenStudio save → run → extract pipeline execution.</summary>
    public enum OpenStudioSimulationStage
    {
        /// <summary>Writing the OSM file.</summary>
        SavingOsm = 0,

        /// <summary>Writing the OSW workflow file.</summary>
        WritingOsw = 1,

        /// <summary>The OpenStudio CLI (EnergyPlus) is executing.</summary>
        RunningCli = 2,

        /// <summary>Reading and mapping the SQLite results.</summary>
        ReadingResults = 3,

        /// <summary>Pipeline finished (successfully, cancelled or failed).</summary>
        Complete = 4,
    }

    /// <summary>
    /// Progress report of one pipeline execution, delivered through
    /// <see cref="System.IProgress{T}"/>.
    /// </summary>
    public sealed class OpenStudioSimulationProgress
    {
        /// <summary>Current stage.</summary>
        public OpenStudioSimulationStage Stage { get; }

        /// <summary>Human-readable detail (paths, exit codes).</summary>
        public string Message { get; }

        /// <summary>Creates a progress report.</summary>
        public OpenStudioSimulationProgress(OpenStudioSimulationStage stage, string message)
        {
            Stage = stage;
            Message = message;
        }

        /// <summary>Formats the report.</summary>
        public override string ToString()
        {
            return string.Format("[{0}] {1}", Stage, Message);
        }
    }
}
