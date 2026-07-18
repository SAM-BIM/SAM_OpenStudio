// SPDX-License-Identifier: LGPL-3.0-only

using System;

namespace SAM.Core.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Builds the deterministic OpenStudio object name for a SAM object:
        /// SAM_&lt;ObjectType&gt;_&lt;SanitizedName&gt;_&lt;GuidFirst8&gt;
        /// (for example SAM_Space_Office_04_72a6f932). Cross-references and tests must use these
        /// names, never OpenStudio-generated handles.
        /// </summary>
        /// <param name="objectType">SAM type name (for example "Space").</param>
        /// <param name="name">SAM object name; empty or null names are omitted from the result.</param>
        /// <param name="guid">SAM object Guid; its first 8 hex digits suffix the name.</param>
        /// <returns>Deterministic OpenStudio object name.</returns>
        public static string OpenStudioName(string objectType, string name, Guid guid)
        {
            string sanitizedObjectType = SanitizeName(objectType);
            if (string.IsNullOrWhiteSpace(sanitizedObjectType))
            {
                sanitizedObjectType = "Object";
            }

            string sanitizedName = SanitizeName(name);

            string result = "SAM_" + sanitizedObjectType;
            if (!string.IsNullOrWhiteSpace(sanitizedName))
            {
                result += "_" + sanitizedName;
            }

            return result + "_" + guid.ToString("N").Substring(0, 8);
        }

        /// <summary>
        /// Builds the deterministic OpenStudio object name for a SAM object from its runtime type,
        /// name and Guid. See <see cref="OpenStudioName(string, string, Guid)"/>.
        /// </summary>
        /// <param name="sAMObject">SAM object; null returns null.</param>
        /// <returns>Deterministic OpenStudio object name, or null.</returns>
        public static string OpenStudioName(this SAMObject sAMObject)
        {
            if (sAMObject == null)
            {
                return null;
            }

            return OpenStudioName(sAMObject.GetType().Name, sAMObject.Name, sAMObject.Guid);
        }
    }
}
