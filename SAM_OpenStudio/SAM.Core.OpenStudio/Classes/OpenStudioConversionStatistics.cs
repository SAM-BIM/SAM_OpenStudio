// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Core.OpenStudio
{
    /// <summary>
    /// Translation statistics for one SAM → OpenStudio conversion: how many source objects were
    /// considered, how many OpenStudio objects were created, how many source objects or loads
    /// were explicitly skipped (always with a diagnostic), how many unsupported parameters were
    /// met, and the diagnostic counts by severity. Mutable during conversion; the result takes
    /// an immutable snapshot via <see cref="Snapshot"/>.
    /// </summary>
    public sealed class OpenStudioConversionStatistics
    {
        /// <summary>Source SAM objects considered for conversion (spaces, panels, apertures).</summary>
        public int SourceObjects { get; set; }

        /// <summary>SAM objects successfully registered with an OpenStudio counterpart.</summary>
        public int CreatedObjects { get; set; }

        /// <summary>Source objects or loads explicitly skipped (each skip carries a diagnostic).</summary>
        public int SkippedObjects { get; set; }

        /// <summary>Parameters/data with no OpenStudio representation (each carries a SAM-OS-IC-001 diagnostic).</summary>
        public int UnsupportedObjects { get; set; }

        /// <summary>Diagnostics raised with Information severity.</summary>
        public int InformationCount { get; set; }

        /// <summary>Diagnostics raised with Warning severity.</summary>
        public int WarningCount { get; set; }

        /// <summary>Diagnostics raised with Error severity.</summary>
        public int ErrorCount { get; set; }

        /// <summary>Creates an empty statistics instance.</summary>
        public OpenStudioConversionStatistics()
        {
        }

        private OpenStudioConversionStatistics(OpenStudioConversionStatistics other)
        {
            SourceObjects = other.SourceObjects;
            CreatedObjects = other.CreatedObjects;
            SkippedObjects = other.SkippedObjects;
            UnsupportedObjects = other.UnsupportedObjects;
            InformationCount = other.InformationCount;
            WarningCount = other.WarningCount;
            ErrorCount = other.ErrorCount;
        }

        /// <summary>Immutable copy of the current counters.</summary>
        public OpenStudioConversionStatistics Snapshot()
        {
            return new OpenStudioConversionStatistics(this);
        }
    }
}
