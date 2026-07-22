// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Analytical.OpenStudio
{
    public static partial class Modify
    {
        /// <summary>
        /// Records on an imported SAM object which OpenStudio object it came from: name, handle
        /// and IDD type (<see cref="OpenStudioSourceParameter"/>).
        /// <para>
        /// The handle matters more than the name. Names are unique only per object type in an
        /// OSM, and a measure may rewrite them; the handle is the stable identity, and it is the
        /// only way to explain later which of two identically named surfaces a diagnostic refers
        /// to.
        /// </para>
        /// </summary>
        /// <param name="parameterizedSAMObject">Imported SAM object; null is a no-op.</param>
        /// <param name="modelObject">OpenStudio source object; null is a no-op.</param>
        /// <returns>True when provenance was recorded.</returns>
        public static bool SetOpenStudioSource(this Core.ParameterizedSAMObject parameterizedSAMObject, global::OpenStudio.ModelObject modelObject)
        {
            if (parameterizedSAMObject == null || modelObject == null)
            {
                return false;
            }

            try
            {
                parameterizedSAMObject.SetValue(OpenStudioSourceParameter.SourceName, modelObject.nameString());
            }
            catch (System.Exception)
            {
                // an unnamed object still gets its handle and type recorded
            }

            try
            {
                global::OpenStudio.UUID uuid = modelObject.handle();
                if (uuid != null)
                {
                    // __str__ is the SWIG string conversion; UUID does not override ToString.
                    parameterizedSAMObject.SetValue(OpenStudioSourceParameter.SourceHandle, uuid.__str__());
                }
            }
            catch (System.Exception)
            {
                // best effort
            }

            try
            {
                global::OpenStudio.IddObjectType iddObjectType = modelObject.iddObjectType();
                if (iddObjectType != null)
                {
                    parameterizedSAMObject.SetValue(OpenStudioSourceParameter.SourceType, iddObjectType.valueName());
                }
            }
            catch (System.Exception)
            {
                // best effort
            }

            return true;
        }
    }
}
