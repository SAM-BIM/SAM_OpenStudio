// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.IO;

namespace SAM.Core.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Locates the OpenStudio CLI executable (openstudio.exe) used to run simulations.
        /// Discovery order:
        /// 1. explicit path (either the executable itself or an installation directory; a bin subdirectory is honoured);
        /// 2. directories on the PATH environment variable;
        /// 3. conventional direct installations (system drive root openstudio-* and %ProgramFiles%\OpenStudio*), newest first;
        /// 4. Ladybug Tools bundled installation (%ProgramFiles%\ladybug_tools\openstudio).
        /// </summary>
        /// <param name="path">Optional explicit executable or installation directory path.</param>
        /// <returns>Full path to openstudio.exe or null when not found.</returns>
        public static string OpenStudioCliPath(string path = null)
        {
            const string executableName = "openstudio.exe";

            if (!string.IsNullOrWhiteSpace(path))
            {
                if (File.Exists(path))
                {
                    return string.Equals(Path.GetFileName(path), executableName, StringComparison.OrdinalIgnoreCase) ? path : null;
                }

                if (Directory.Exists(path))
                {
                    string candidate = Path.Combine(path, executableName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }

                    candidate = Path.Combine(path, "bin", executableName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }

                return null;
            }

            string pathVariable = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrWhiteSpace(pathVariable))
            {
                foreach (string directory in pathVariable.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrWhiteSpace(directory))
                    {
                        continue;
                    }

                    string candidate;
                    try
                    {
                        candidate = Path.Combine(directory.Trim(), executableName);
                    }
                    catch (ArgumentException)
                    {
                        continue;
                    }

                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            List<string> installationDirectories = new List<string>();

            string systemRoot;
            try
            {
                systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System));
            }
            catch (Exception)
            {
                systemRoot = null;
            }

            if (!string.IsNullOrWhiteSpace(systemRoot) && Directory.Exists(systemRoot))
            {
                installationDirectories.AddRange(InstallationDirectories(systemRoot, "openstudio*"));
            }

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles) && Directory.Exists(programFiles))
            {
                installationDirectories.AddRange(InstallationDirectories(programFiles, "OpenStudio*"));
                installationDirectories.Add(Path.Combine(programFiles, "ladybug_tools", "openstudio"));
            }

            foreach (string installationDirectory in installationDirectories)
            {
                string candidate = Path.Combine(installationDirectory, "bin", executableName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static IEnumerable<string> InstallationDirectories(string directory, string searchPattern)
        {
            List<string> result = new List<string>();
            try
            {
                result.AddRange(Directory.GetDirectories(directory, searchPattern));
            }
            catch (Exception)
            {
                return result;
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            result.Reverse();
            return result;
        }
    }
}
