// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;

namespace SAM.Core.OpenStudio
{
    public static partial class Modify
    {
        /// <summary>
        /// Stamps the SAM identity of <paramref name="sAMObject"/> onto an OpenStudio object's
        /// <c>AdditionalProperties</c> using the namespaced keys in
        /// <see cref="OpenStudioIdentityKeys"/>. This is the only lossless identity channel
        /// through an OSM: the deterministic object name carries just the first 8 hex characters
        /// of the Guid, which cannot be inverted.
        /// <para>
        /// Purely additive — <c>OS:AdditionalProperties</c> is a separate object attached to the
        /// owner, so no existing field, object name or EnergyPlus input is affected.
        /// </para>
        /// </summary>
        /// <param name="modelObject">Target OpenStudio object; null is a no-op.</param>
        /// <param name="sAMObject">Source SAM object; null is a no-op.</param>
        /// <returns>True when the identity was written.</returns>
        public static bool SetSAMIdentity(this global::OpenStudio.ModelObject modelObject, SAMObject sAMObject)
        {
            if (modelObject == null || sAMObject == null)
            {
                return false;
            }

            return SetSAMIdentity(modelObject, sAMObject.Guid, sAMObject.GetType().Name, sAMObject.Name);
        }

        /// <summary>
        /// Stamps an explicit SAM identity onto an OpenStudio object's
        /// <c>AdditionalProperties</c>. See <see cref="SetSAMIdentity(global::OpenStudio.ModelObject, SAMObject)"/>.
        /// </summary>
        /// <param name="modelObject">Target OpenStudio object; null is a no-op.</param>
        /// <param name="guid">Full SAM Guid; <see cref="Guid.Empty"/> is a no-op.</param>
        /// <param name="type">SAM runtime type name; written when non-empty.</param>
        /// <param name="name">Original, unsanitised SAM name; written when non-empty.</param>
        /// <returns>True when the Guid feature was written.</returns>
        public static bool SetSAMIdentity(this global::OpenStudio.ModelObject modelObject, Guid guid, string type, string name)
        {
            if (modelObject == null || guid == Guid.Empty)
            {
                return false;
            }

            global::OpenStudio.AdditionalProperties additionalProperties = modelObject.additionalProperties();
            if (additionalProperties == null)
            {
                return false;
            }

            // A failed setFeature is never fatal to the conversion: identity is metadata, and an
            // OSM without it still simulates. It is reported by the caller when it matters.
            bool result = additionalProperties.setFeature(OpenStudioIdentityKeys.Guid, guid.ToString("D"));

            if (!string.IsNullOrWhiteSpace(type))
            {
                additionalProperties.setFeature(OpenStudioIdentityKeys.Type, type);
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                additionalProperties.setFeature(OpenStudioIdentityKeys.Name, name);
            }

            return result;
        }
    }
}
