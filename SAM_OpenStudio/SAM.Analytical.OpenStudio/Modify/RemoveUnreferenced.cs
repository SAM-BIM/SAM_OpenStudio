// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using SAM.Core;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Modify
    {
        /// <summary>
        /// Removes materials no construction in the imported model actually references.
        /// <para>
        /// An OpenStudio model routinely carries a full library of materials from measures,
        /// template construction sets and default construction sets, most of which no surface
        /// uses. Importing all of them would hand the user a SAM material library where the
        /// handful of materials in the building are indistinguishable from hundreds that are not.
        /// Same principle as the SAM_LadybugTools reverse converter.
        /// </para>
        /// </summary>
        /// <param name="materialLibrary">Library to prune; null is a no-op.</param>
        /// <param name="adjacencyCluster">Imported topology whose constructions define what is referenced.</param>
        public static void RemoveUnreferencedMaterials(MaterialLibrary materialLibrary, AdjacencyCluster adjacencyCluster)
        {
            if (materialLibrary == null || adjacencyCluster == null)
            {
                return;
            }

            HashSet<string> materialNames = new HashSet<string>();

            List<Construction> constructions = adjacencyCluster.GetConstructions();
            if (constructions != null)
            {
                foreach (Construction construction in constructions)
                {
                    AddLayerNames(materialNames, construction?.ConstructionLayers);
                }
            }

            List<ApertureConstruction> apertureConstructions = adjacencyCluster.GetApertureConstructions();
            if (apertureConstructions != null)
            {
                foreach (ApertureConstruction apertureConstruction in apertureConstructions)
                {
                    AddLayerNames(materialNames, apertureConstruction?.PaneConstructionLayers);
                    AddLayerNames(materialNames, apertureConstruction?.FrameConstructionLayers);
                }
            }

            List<IMaterial> materials = materialLibrary.GetMaterials();
            if (materials == null)
            {
                return;
            }

            foreach (IMaterial material in materials)
            {
                if (material == null || string.IsNullOrWhiteSpace(material.Name))
                {
                    continue;
                }

                if (!materialNames.Contains(material.Name))
                {
                    materialLibrary.Remove(material);
                }
            }
        }

        private static void AddLayerNames(HashSet<string> materialNames, IEnumerable<ConstructionLayer> constructionLayers)
        {
            if (constructionLayers == null)
            {
                return;
            }

            foreach (ConstructionLayer constructionLayer in constructionLayers)
            {
                if (constructionLayer != null && !string.IsNullOrWhiteSpace(constructionLayer.Name))
                {
                    materialNames.Add(constructionLayer.Name);
                }
            }
        }

        /// <summary>
        /// Removes profiles that no imported <see cref="InternalCondition"/> references.
        /// <para>
        /// Every schedule in the OpenStudio model is converted, including the ones attached to
        /// unused space types, HVAC objects that were not imported, and availability managers.
        /// Only those an assigned internal condition names — through a "… Profile Name"
        /// parameter — belong in the SAM profile library.
        /// </para>
        /// </summary>
        /// <param name="profileLibrary">Library to prune; null is a no-op.</param>
        /// <param name="adjacencyCluster">Imported topology whose spaces carry the internal conditions.</param>
        public static void RemoveUnreferencedProfiles(ProfileLibrary profileLibrary, AdjacencyCluster adjacencyCluster)
        {
            if (profileLibrary == null || adjacencyCluster == null)
            {
                return;
            }

            HashSet<string> profileNames = new HashSet<string>();

            List<Space> spaces = adjacencyCluster.GetSpaces();
            if (spaces != null)
            {
                foreach (Space space in spaces)
                {
                    InternalCondition internalCondition = space?.InternalCondition;
                    if (internalCondition == null)
                    {
                        continue;
                    }

                    List<ParameterSet> parameterSets = internalCondition.GetParameterSets();
                    if (parameterSets == null)
                    {
                        continue;
                    }

                    foreach (ParameterSet parameterSet in parameterSets)
                    {
                        if (parameterSet?.Names == null)
                        {
                            continue;
                        }

                        foreach (string parameterName in parameterSet.Names)
                        {
                            if (parameterName == null || !parameterName.EndsWith("Profile Name"))
                            {
                                continue;
                            }

                            string profileName = parameterSet.ToObject(parameterName) as string;
                            if (!string.IsNullOrWhiteSpace(profileName))
                            {
                                profileNames.Add(profileName);
                            }
                        }
                    }
                }
            }

            List<Profile> profiles = profileLibrary.GetProfiles();
            if (profiles == null)
            {
                return;
            }

            foreach (Profile profile in profiles)
            {
                if (profile == null || string.IsNullOrWhiteSpace(profile.Name))
                {
                    continue;
                }

                if (!profileNames.Contains(profile.Name))
                {
                    profileLibrary.Remove(profile);
                }
            }
        }
    }
}
