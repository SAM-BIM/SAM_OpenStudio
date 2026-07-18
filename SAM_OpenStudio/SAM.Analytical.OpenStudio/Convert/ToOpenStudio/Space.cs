// SPDX-License-Identifier: LGPL-3.0-only

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Converts a SAM Space to an OpenStudio Space with its own ThermalZone (one zone per SAM
        /// space, plan §5). The space is registered in the context; a space already converted is
        /// returned from the cache instead of being duplicated.
        /// </summary>
        /// <param name="space">SAM space.</param>
        /// <param name="openStudioConversionContext">Conversion context.</param>
        /// <returns>The OpenStudio space, or null.</returns>
        public static global::OpenStudio.Space ToOpenStudio(this Space space, OpenStudioConversionContext openStudioConversionContext)
        {
            if (space == null || openStudioConversionContext == null)
            {
                return null;
            }

            if (openStudioConversionContext.TryGetModelObject(space.Guid, out global::OpenStudio.Space existing))
            {
                return existing;
            }

            global::OpenStudio.Space result = new global::OpenStudio.Space(openStudioConversionContext.Target);
            result.setName(Core.OpenStudio.Query.OpenStudioName(space));

            global::OpenStudio.ThermalZone thermalZone = new global::OpenStudio.ThermalZone(openStudioConversionContext.Target);
            thermalZone.setName(Core.OpenStudio.Query.OpenStudioName("ThermalZone", space.Name, space.Guid));
            result.setThermalZone(thermalZone);

            openStudioConversionContext.RegisterModelObject(space, result);
            return result;
        }
    }
}
