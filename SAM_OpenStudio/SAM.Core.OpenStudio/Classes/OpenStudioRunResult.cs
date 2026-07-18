// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;

namespace SAM.Core.OpenStudio
{
    /// <summary>
    /// Outcome of one OpenStudio CLI simulation run. Immutable. Zone load results are not part of
    /// this class to keep SAM.Core.OpenStudio free of analytical types — they are carried by the
    /// analytical-level conversion result (see SAM.Analytical.OpenStudio).
    /// </summary>
    public sealed class OpenStudioRunResult
    {
        /// <summary>True when the CLI exited with code 0 and EnergyPlus reported no fatal errors.</summary>
        public bool Success { get; }

        /// <summary>Exit code of the OpenStudio CLI process (-1 when the process timed out).</summary>
        public int ExitCode { get; }

        /// <summary>Path of the OSM that was run.</summary>
        public string OsmPath { get; }

        /// <summary>Path of the OSW workflow file that was run.</summary>
        public string OswPath { get; }

        /// <summary>Path of the EnergyPlus SQLite results file, when produced.</summary>
        public string SqlPath { get; }

        /// <summary>Path of the EnergyPlus error file (eplusout.err), when produced.</summary>
        public string ErrorFilePath { get; }

        /// <summary>Severe error lines parsed from eplusout.err.</summary>
        public IReadOnlyList<string> SevereErrors { get; }

        /// <summary>Fatal error lines parsed from eplusout.err.</summary>
        public IReadOnlyList<string> FatalErrors { get; }

        /// <summary>Creates an immutable run result.</summary>
        /// <param name="success">True when the run completed without fatal errors.</param>
        /// <param name="exitCode">CLI process exit code (-1 for timeout).</param>
        /// <param name="osmPath">Path of the OSM that was run.</param>
        /// <param name="oswPath">Path of the OSW workflow file.</param>
        /// <param name="sqlPath">Path of the SQLite results file, when produced.</param>
        /// <param name="errorFilePath">Path of eplusout.err, when produced.</param>
        /// <param name="severeErrors">Severe error lines parsed from eplusout.err.</param>
        /// <param name="fatalErrors">Fatal error lines parsed from eplusout.err.</param>
        public OpenStudioRunResult(bool success, int exitCode, string osmPath, string oswPath, string sqlPath, string errorFilePath, IEnumerable<string> severeErrors, IEnumerable<string> fatalErrors)
        {
            Success = success;
            ExitCode = exitCode;
            OsmPath = osmPath;
            OswPath = oswPath;
            SqlPath = sqlPath;
            ErrorFilePath = errorFilePath;
            SevereErrors = severeErrors == null ? new List<string>() : new List<string>(severeErrors);
            FatalErrors = fatalErrors == null ? new List<string>() : new List<string>(fatalErrors);
        }
    }
}
