// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using System.Linq;

namespace SAM.Analytical.Grasshopper.OpenStudio
{
    /// <summary>
    /// Shared helpers for the "Go to Directory" context-menu item (same behaviour as
    /// SAMAnalytical.Paths in the SAM repository): the path is read from the volatile data
    /// of an input parameter and opened in Windows Explorer.
    /// </summary>
    internal static class MenuHelper
    {
        internal static string GetVolatileString(GH_Component component, string name)
        {
            int index = component.Params.IndexOfInputParam(name);
            if (index == -1)
            {
                return null;
            }

            object @object = component.Params.Input[index].VolatileData.AllData(true)?.OfType<object>()?.FirstOrDefault();
            if (@object is IGH_Goo)
            {
                return (@object as dynamic).Value?.ToString();
            }

            return null;
        }

        internal static void GoToDirectory(string directory, bool create)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            if (create)
            {
                Core.Create.Directory(directory);
            }

            if (!System.IO.Directory.Exists(directory))
            {
                return;
            }

            Core.Query.StartProcess(directory);
        }

        internal static void GoToFileDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            {
                return;
            }

            GoToDirectory(System.IO.Path.GetDirectoryName(path), false);
        }
    }
}
