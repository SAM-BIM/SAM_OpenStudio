// SPDX-License-Identifier: LGPL-3.0-only

using SAM.Core;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Maps a SAM aperture to the OpenStudio SubSurface type. Mirrors SAM_LadybugTools
        /// semantics: windows with an opaque pane build-up become doors, doors with a transparent
        /// pane build-up become glass doors. SAM has no explicit operable-window flag, so windows
        /// map to "FixedWindow" (documented MVP assumption).
        /// </summary>
        /// <param name="aperture">SAM aperture.</param>
        /// <param name="materialLibrary">Material library used to resolve the pane material type; may be null.</param>
        /// <returns>"FixedWindow", "Door" or "GlassDoor"; null when the aperture is null.</returns>
        public static string SubSurfaceType(this Aperture aperture, MaterialLibrary materialLibrary)
        {
            if (aperture == null)
            {
                return null;
            }

            MaterialType materialType = MaterialType.Undefined;
            ApertureConstruction apertureConstruction = aperture.ApertureConstruction;
            if (apertureConstruction != null)
            {
                materialType = apertureConstruction.PaneConstructionLayers.MaterialType(materialLibrary);
            }

            if (aperture.ApertureType == ApertureType.Door)
            {
                return materialType == MaterialType.Transparent ? "GlassDoor" : "Door";
            }

            return materialType == MaterialType.Opaque ? "Door" : "FixedWindow";
        }
    }
}
