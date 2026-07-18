// SPDX-License-Identifier: LGPL-3.0-only

using System;

namespace SAM.Core.OpenStudio
{
    /// <summary>
    /// Traceability record linking one SAM object (by Guid and type) to the OpenStudio object
    /// created from it (by deterministic name — never by OpenStudio handle, which changes between
    /// sessions). Immutable.
    /// </summary>
    public sealed class OpenStudioObjectReference
    {
        /// <summary>Guid of the source SAM object.</summary>
        public Guid SamGuid { get; }

        /// <summary>SAM type name of the source object (for example "Space" or "Panel").</summary>
        public string SamObjectType { get; }

        /// <summary>Deterministic name of the created OpenStudio object.</summary>
        public string OpenStudioObjectName { get; }

        /// <summary>Creates an immutable reference.</summary>
        /// <param name="samGuid">Guid of the source SAM object.</param>
        /// <param name="samObjectType">SAM type name of the source object.</param>
        /// <param name="openStudioObjectName">Deterministic name of the created OpenStudio object.</param>
        public OpenStudioObjectReference(Guid samGuid, string samObjectType, string openStudioObjectName)
        {
            SamGuid = samGuid;
            SamObjectType = samObjectType;
            OpenStudioObjectName = openStudioObjectName;
        }
    }
}
