// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

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
        /// Without an explicit path, when more than one CLI is found, the first one (in that order) whose version equals the
        /// loaded OpenStudio SDK version (<see cref="OpenStudioVersion"/>) is returned, so that another OpenStudio earlier on
        /// PATH is not picked by accident; when none matches, the first one found is returned, as before.
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

            List<string> candidates = OpenStudioCliCandidates(executableName);
            if (candidates.Count < 2)
            {
                return candidates.Count == 0 ? null : candidates[0];
            }

            string sdkVersion;
            try
            {
                sdkVersion = OpenStudioVersion();
            }
            catch (Exception)
            {
                sdkVersion = null;
            }

            return OpenStudioCliPath(candidates, sdkVersion, OpenStudioCliVersion);
        }

        /// <summary>
        /// Chooses among discovered OpenStudio CLI candidates: the first whose version equals <paramref name="sdkVersion"/>
        /// (compared without +build metadata), otherwise the first candidate.
        /// </summary>
        /// <param name="candidates">Candidate openstudio.exe paths in discovery order.</param>
        /// <param name="sdkVersion">Loaded OpenStudio SDK version; when null or empty the first candidate is returned.</param>
        /// <param name="cliVersion">Returns a candidate's version (for example "3.10.0+86d7e215a1"), or null when unknown.</param>
        /// <returns>The selected candidate, or null when there is none.</returns>
        public static string OpenStudioCliPath(IEnumerable<string> candidates, string sdkVersion, Func<string, string> cliVersion)
        {
            if (candidates == null)
            {
                return null;
            }

            string sdkCoreVersion = CoreVersion(sdkVersion);
            string first = null;
            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                if (first == null)
                {
                    first = candidate;
                }

                if (sdkCoreVersion != null && cliVersion != null && string.Equals(CoreVersion(cliVersion(candidate)), sdkCoreVersion, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return first;
        }

        /// <summary>
        /// Version reported by an OpenStudio CLI (<c>openstudio.exe --version</c>, for example "3.10.0+86d7e215a1").
        /// The result is cached per executable path, size and write time.
        /// </summary>
        /// <param name="cliPath">Path to openstudio.exe.</param>
        /// <returns>The first non-empty line the CLI printed, or null when it could not be run or printed nothing.</returns>
        public static string OpenStudioCliVersion(string cliPath)
        {
            if (string.IsNullOrWhiteSpace(cliPath) || !File.Exists(cliPath))
            {
                return null;
            }

            string key;
            try
            {
                FileInfo fileInfo = new FileInfo(cliPath);
                key = string.Format("{0}|{1}|{2}", fileInfo.FullName.ToUpperInvariant(), fileInfo.Length, fileInfo.LastWriteTimeUtc.Ticks);
            }
            catch (Exception)
            {
                return null;
            }

            lock (openStudioCliVersions)
            {
                if (openStudioCliVersions.TryGetValue(key, out string cached))
                {
                    return cached;
                }
            }

            string result = null;
            try
            {
                System.Diagnostics.ProcessStartInfo processStartInfo = new System.Diagnostics.ProcessStartInfo(cliPath, "--version")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(processStartInfo))
                {
                    System.Threading.Tasks.Task<string> output = process.StandardOutput.ReadToEndAsync();
                    System.Threading.Tasks.Task<string> error = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(30000))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception)
                        {
                            // the CLI may already have exited
                        }

                        return null;
                    }

                    foreach (string line in output.Result.Split('\n'))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            result = line.Trim();
                            break;
                        }
                    }
                }
            }
            catch (Exception)
            {
                result = null;
            }

            lock (openStudioCliVersions)
            {
                openStudioCliVersions[key] = result;
            }

            return result;
        }

        private static readonly Dictionary<string, string> openStudioCliVersions = new Dictionary<string, string>();

        private static string CoreVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            string result = version.Trim();
            int index = result.IndexOf('+');
            if (index >= 0)
            {
                result = result.Substring(0, index).Trim();
            }

            return result.Length == 0 ? null : result;
        }

        private static List<string> OpenStudioCliCandidates(string executableName)
        {
            List<string> result = new List<string>();
            HashSet<string> fullPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string candidate)
            {
                if (!File.Exists(candidate))
                {
                    return;
                }

                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(candidate);
                }
                catch (Exception)
                {
                    return;
                }

                if (fullPaths.Add(fullPath))
                {
                    result.Add(candidate);
                }
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

                    Add(candidate);
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
                Add(Path.Combine(installationDirectory, "bin", executableName));
            }

            return result;
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
