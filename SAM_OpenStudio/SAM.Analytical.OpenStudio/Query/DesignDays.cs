// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using System.IO;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Loads the SizingPeriod:DesignDay objects of a DDY file through the EnergyPlus
        /// reverse translator (deterministic import — the primary design-day path).
        /// Source: https://github.com/NREL/OpenStudio-resources/blob/538b3c7df3f6ed4513f354cff7d96f7c556c267e/model/simulationtests/lib/baseline_model.rb#L612:L655
        /// </summary>
        /// <param name="path_DDY">DDY file path.</param>
        /// <returns>The design days (owned by an internal temporary model; clone them into a target model), or null when the file cannot be read.</returns>
        public static List<global::OpenStudio.DesignDay> DesignDays(string path_DDY)
        {
            if (string.IsNullOrWhiteSpace(path_DDY) || !File.Exists(path_DDY))
            {
                return null;
            }

            global::OpenStudio.Path openStudioPath = global::OpenStudio.OpenStudioUtilitiesCore.toPath(path_DDY);
            if (openStudioPath == null)
            {
                return null;
            }

            global::OpenStudio.OptionalIdfFile optionalIdfFile = global::OpenStudio.IdfFile.load(openStudioPath, new global::OpenStudio.IddFileType("EnergyPlus"));
            if (optionalIdfFile == null || optionalIdfFile.isNull())
            {
                return null;
            }

            global::OpenStudio.Workspace workspace = new global::OpenStudio.Workspace(optionalIdfFile.get());

            global::OpenStudio.EnergyPlusReverseTranslator energyPlusReverseTranslator = new global::OpenStudio.EnergyPlusReverseTranslator();
            global::OpenStudio.Model model = energyPlusReverseTranslator.translateWorkspace(workspace);

            List<global::OpenStudio.DesignDay> result = new List<global::OpenStudio.DesignDay>();
            foreach (global::OpenStudio.DesignDay designDay in model.getDesignDays())
            {
                result.Add(designDay);
            }

            return result;
        }
    }
}
