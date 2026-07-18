// SPDX-License-Identifier: LGPL-3.0-only

namespace SAM.Core.OpenStudio
{
    /// <summary>
    /// Options controlling execution of the OpenStudio CLI for a generated model.
    /// </summary>
    public sealed class OpenStudioRunOptions
    {
        /// <summary>
        /// Explicit path to openstudio.exe (or its installation directory). When null the CLI is
        /// discovered via <see cref="Query.OpenStudioCliPath(string)"/> (PATH, direct
        /// installations, Ladybug Tools bundle).
        /// </summary>
        public string CliPath { get; set; }

        /// <summary>
        /// Isolated directory in which the simulation runs and output files are written.
        /// When null a directory next to the OSW file is used.
        /// </summary>
        public string RunDirectory { get; set; }

        /// <summary>EPW weather file applied to the run. Required for simulation.</summary>
        public string EpwPath { get; set; }

        /// <summary>Optional DDY file; when present design days are imported.</summary>
        public string DdyPath { get; set; }

        /// <summary>Maximum wall-clock time for the CLI process in seconds. Default 3600.</summary>
        public int TimeoutSeconds { get; set; } = 3600;
    }
}
