// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;

namespace SAM.Core.OpenStudio
{
    /// <summary>
    /// Guid-keyed registry of <see cref="OpenStudioObjectReference"/> entries created during a
    /// conversion. Prevents duplicate conversion of the same SAM object: an existing entry is
    /// never overwritten.
    /// </summary>
    public sealed class OpenStudioObjectMap
    {
        private readonly Dictionary<Guid, OpenStudioObjectReference> dictionary = new Dictionary<Guid, OpenStudioObjectReference>();

        /// <summary>Number of registered references.</summary>
        public int Count
        {
            get
            {
                return dictionary.Count;
            }
        }

        /// <summary>All registered references.</summary>
        public IEnumerable<OpenStudioObjectReference> References
        {
            get
            {
                return dictionary.Values;
            }
        }

        /// <summary>True when a reference for the given SAM Guid is registered.</summary>
        /// <param name="samGuid">Guid of the SAM object.</param>
        public bool Contains(Guid samGuid)
        {
            return dictionary.ContainsKey(samGuid);
        }

        /// <summary>
        /// Registers a reference. Returns false (and changes nothing) when the reference is null,
        /// its name is empty, or a reference for the same SAM Guid already exists.
        /// </summary>
        /// <param name="openStudioObjectReference">Reference to register.</param>
        public bool TryAdd(OpenStudioObjectReference openStudioObjectReference)
        {
            if (openStudioObjectReference == null || string.IsNullOrWhiteSpace(openStudioObjectReference.OpenStudioObjectName))
            {
                return false;
            }

            if (dictionary.ContainsKey(openStudioObjectReference.SamGuid))
            {
                return false;
            }

            dictionary[openStudioObjectReference.SamGuid] = openStudioObjectReference;
            return true;
        }

        /// <summary>Gets the registered reference for a SAM Guid.</summary>
        /// <param name="samGuid">Guid of the SAM object.</param>
        /// <param name="openStudioObjectReference">The registered reference, when present.</param>
        public bool TryGetReference(Guid samGuid, out OpenStudioObjectReference openStudioObjectReference)
        {
            return dictionary.TryGetValue(samGuid, out openStudioObjectReference);
        }

        /// <summary>Name of the OpenStudio object created for the SAM Guid, or null.</summary>
        /// <param name="samGuid">Guid of the SAM object.</param>
        public string OpenStudioObjectName(Guid samGuid)
        {
            OpenStudioObjectReference openStudioObjectReference;
            return dictionary.TryGetValue(samGuid, out openStudioObjectReference) ? openStudioObjectReference.OpenStudioObjectName : null;
        }

        /// <summary>Snapshot of the map as SAM Guid → OpenStudio object name.</summary>
        public IReadOnlyDictionary<Guid, string> ToNameDictionary()
        {
            Dictionary<Guid, string> result = new Dictionary<Guid, string>();
            foreach (KeyValuePair<Guid, OpenStudioObjectReference> keyValuePair in dictionary)
            {
                result[keyValuePair.Key] = keyValuePair.Value.OpenStudioObjectName;
            }

            return result;
        }
    }
}
