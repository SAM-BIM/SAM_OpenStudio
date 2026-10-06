// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;

namespace SAM.Core.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Reads the full SAM Guid previously stamped on an OpenStudio object by
        /// <see cref="Modify.SetSAMIdentity(global::OpenStudio.ModelObject, SAMObject)"/>.
        /// <para>
        /// Only a syntactically valid, non-empty Guid is accepted. A third-party OSM carries no
        /// such feature and returns false — that is the normal case, not an error.
        /// </para>
        /// </summary>
        /// <param name="modelObject">OpenStudio object; null returns false.</param>
        /// <param name="guid">The restored SAM Guid when present and valid.</param>
        /// <returns>True when a valid full SAM Guid was found.</returns>
        public static bool TryGetSAMGuid(global::OpenStudio.ModelObject modelObject, out Guid guid)
        {
            guid = Guid.Empty;

            string value;
            if (!TryGetSAMFeature(modelObject, OpenStudioIdentityKeys.Guid, out value))
            {
                return false;
            }

            return Guid.TryParse(value, out guid) && guid != Guid.Empty;
        }

        /// <summary>
        /// Reads the original (unsanitised) SAM name stamped on an OpenStudio object. Falls back
        /// to nothing — the caller decides whether to use the OpenStudio object name instead.
        /// </summary>
        /// <param name="modelObject">OpenStudio object; null returns false.</param>
        /// <param name="name">The restored SAM name when present and non-empty.</param>
        public static bool TryGetSAMName(global::OpenStudio.ModelObject modelObject, out string name)
        {
            return TryGetSAMFeature(modelObject, OpenStudioIdentityKeys.Name, out name);
        }

        /// <summary>
        /// Reads the SAM runtime type name stamped on an OpenStudio object. Used to reject
        /// identity metadata that belongs to a different SAM object kind rather than restoring a
        /// Guid onto the wrong type.
        /// </summary>
        /// <param name="modelObject">OpenStudio object; null returns false.</param>
        /// <param name="type">The restored SAM type name when present and non-empty.</param>
        public static bool TryGetSAMType(global::OpenStudio.ModelObject modelObject, out string type)
        {
            return TryGetSAMFeature(modelObject, OpenStudioIdentityKeys.Type, out type);
        }

        /// <summary>
        /// Reads a namespaced SAM feature from an OpenStudio object's AdditionalProperties.
        /// Returns false when the object has no AdditionalProperties at all — checked with
        /// <c>hasAdditionalProperties()</c> first, so reading identity never materialises an
        /// empty AdditionalProperties object on a third-party model.
        /// </summary>
        /// <param name="modelObject">OpenStudio object; null returns false.</param>
        /// <param name="key">Feature name, see <see cref="OpenStudioIdentityKeys"/>.</param>
        /// <param name="value">The feature value when present and non-empty.</param>
        public static bool TryGetSAMFeature(global::OpenStudio.ModelObject modelObject, string key, out string value)
        {
            value = null;

            if (modelObject == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (!modelObject.hasAdditionalProperties())
            {
                return false;
            }

            global::OpenStudio.AdditionalProperties additionalProperties = modelObject.additionalProperties();
            if (additionalProperties == null || !additionalProperties.hasFeature(key))
            {
                return false;
            }

            global::OpenStudio.OptionalString optionalString = additionalProperties.getFeatureAsString(key);
            if (optionalString == null || optionalString.isNull())
            {
                return false;
            }

            value = optionalString.get();
            return !string.IsNullOrWhiteSpace(value);
        }
    }
}
