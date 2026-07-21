// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SAM.Analytical.OpenStudio
{
    /// <summary>
    /// Reads an OpenStudio workflow file (OSW) and resolves the model it describes.
    /// <para>
    /// An OSW is a workflow description, not a building model: it names a seed OSM and a list of
    /// measures to apply to it. Parsing the JSON therefore tells you what the workflow *would*
    /// produce, never what it *does* produce — only running it does that. This class keeps those
    /// two cases explicit rather than letting a parsed OSW masquerade as an imported model.
    /// </para>
    /// </summary>
    public sealed class OpenStudioWorkflow
    {
        /// <summary>Absolute path of the OSW file.</summary>
        public string Path { get; }

        /// <summary>
        /// Directory paths are resolved against: the OSW's own <c>root</c> field when present,
        /// otherwise the directory containing the OSW.
        /// </summary>
        public string Root { get; }

        /// <summary>Raw <c>seed_file</c> value, as written in the OSW; null when absent.</summary>
        public string SeedFile { get; }

        /// <summary>Raw <c>weather_file</c> value; null when absent.</summary>
        public string WeatherFile { get; }

        /// <summary>Additional <c>file_paths</c> search directories, resolved to absolute paths.</summary>
        public IReadOnlyList<string> FilePaths { get; }

        /// <summary>Measure directory names of the workflow's steps, in order.</summary>
        public IReadOnlyList<string> StepMeasureNames { get; }

        /// <summary>The parsed OSW document; used to write an isolated copy without losing any field.</summary>
        internal JsonObject Document { get; }

        private OpenStudioWorkflow(string path, string root, string seedFile, string weatherFile, List<string> filePaths, List<string> stepMeasureNames, JsonObject document)
        {
            Path = path;
            Root = root;
            SeedFile = seedFile;
            WeatherFile = weatherFile;
            FilePaths = filePaths;
            StepMeasureNames = stepMeasureNames;
            Document = document;
        }

        /// <summary>
        /// Parses an OSW file. Returns null and sets <paramref name="failureReason"/> when the
        /// file cannot be read or is not a JSON object.
        /// </summary>
        /// <param name="path">OSW file path.</param>
        /// <param name="failureReason">Single-line description of the failure; null on success.</param>
        /// <returns>The parsed workflow, or null.</returns>
        public static OpenStudioWorkflow Parse(string path, out string failureReason)
        {
            failureReason = null;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                failureReason = string.Format("the file does not exist: {0}", path);
                return null;
            }

            JsonNode jsonNode;
            try
            {
                jsonNode = JsonNode.Parse(File.ReadAllText(path), null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            }
            catch (Exception exception)
            {
                failureReason = string.Format("{0}: {1}", exception.GetType().Name, exception.Message);
                return null;
            }

            JsonObject jsonObject = jsonNode as JsonObject;
            if (jsonObject == null)
            {
                failureReason = "the file does not contain a JSON object";
                return null;
            }

            string fullPath = System.IO.Path.GetFullPath(path);
            string directory = System.IO.Path.GetDirectoryName(fullPath);

            // "root" is relative to the OSW's own directory when relative — that is what the
            // OpenStudio CLI does, and getting it wrong silently breaks every relative seed.
            string root = directory;
            string rootValue = StringValue(jsonObject, "root");
            if (!string.IsNullOrWhiteSpace(rootValue))
            {
                root = System.IO.Path.IsPathRooted(rootValue) ? rootValue : System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, rootValue));
            }

            List<string> filePaths = new List<string>();
            JsonArray filePathsArray = jsonObject["file_paths"] as JsonArray;
            if (filePathsArray != null)
            {
                foreach (JsonNode node in filePathsArray)
                {
                    string value = node?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    filePaths.Add(System.IO.Path.IsPathRooted(value) ? value : System.IO.Path.GetFullPath(System.IO.Path.Combine(root, value)));
                }
            }

            List<string> stepMeasureNames = new List<string>();
            JsonArray stepsArray = jsonObject["steps"] as JsonArray;
            if (stepsArray != null)
            {
                foreach (JsonNode node in stepsArray)
                {
                    JsonObject step = node as JsonObject;
                    string measureDirName = StringValue(step, "measure_dir_name");
                    if (!string.IsNullOrWhiteSpace(measureDirName))
                    {
                        stepMeasureNames.Add(measureDirName);
                    }
                }
            }

            return new OpenStudioWorkflow(fullPath, root, StringValue(jsonObject, "seed_file"), StringValue(jsonObject, "weather_file"), filePaths, stepMeasureNames, jsonObject);
        }

        /// <summary>
        /// Resolves <see cref="SeedFile"/> to an existing OSM on disk.
        /// <para>
        /// The search follows the OpenStudio CLI's own order: the path as given (absolute or
        /// relative to the root), then <c>&lt;root&gt;/files</c> — where the CLI conventionally
        /// keeps seed models — then each <c>file_paths</c> entry and its own <c>files</c>
        /// subdirectory.
        /// </para>
        /// </summary>
        /// <param name="searchedPaths">Every location tried, for a diagnostic when nothing is found.</param>
        /// <returns>The resolved OSM path, or null.</returns>
        public string ResolveSeedPath(out List<string> searchedPaths)
        {
            searchedPaths = new List<string>();

            if (string.IsNullOrWhiteSpace(SeedFile))
            {
                return null;
            }

            if (System.IO.Path.IsPathRooted(SeedFile))
            {
                searchedPaths.Add(SeedFile);
                return File.Exists(SeedFile) ? SeedFile : null;
            }

            List<string> directories = new List<string> { Root, System.IO.Path.Combine(Root, "files") };
            foreach (string filePath in FilePaths)
            {
                directories.Add(filePath);
                directories.Add(System.IO.Path.Combine(filePath, "files"));
            }

            foreach (string directory in directories)
            {
                string candidate;
                try
                {
                    candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, SeedFile));
                }
                catch (Exception)
                {
                    continue;
                }

                if (searchedPaths.Contains(candidate))
                {
                    continue;
                }

                searchedPaths.Add(candidate);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// True when the workflow contains at least one step whose measure directory name marks
        /// it as an EnergyPlus measure by convention. Used only to decide whether to warn that
        /// such a measure's changes live in the generated IDF and cannot appear in the OSM; a
        /// heuristic, because measure type is declared in measure.xml, not in the OSW.
        /// </summary>
        public bool MayContainEnergyPlusMeasures
        {
            get { return StepMeasureNames.Count > 0; }
        }

        /// <summary>
        /// Writes a copy of this workflow into <paramref name="directory"/> with every path field
        /// made absolute and <c>run_directory</c> pointed at that directory.
        /// <para>
        /// The copy exists so concurrent imports of the same OSW cannot collide: the CLI writes
        /// its run folder next to the workflow file, so running the original in place would give
        /// two parallel imports one shared <c>run</c> directory. Only path fields are rewritten —
        /// <c>steps</c> and their arguments are copied verbatim, so the workflow that executes is
        /// the one the caller supplied.
        /// </para>
        /// </summary>
        /// <param name="directory">Isolated run directory; created when absent.</param>
        /// <param name="resolvedSeedPath">Absolute seed path to write, when one was resolved.</param>
        /// <returns>Path of the written OSW copy.</returns>
        public string WriteIsolatedCopy(string directory, string resolvedSeedPath)
        {
            Directory.CreateDirectory(directory);

            JsonObject copy = Document.DeepClone() as JsonObject;

            copy["root"] = Root.Replace('\\', '/');
            copy["run_directory"] = System.IO.Path.GetFullPath(directory).Replace('\\', '/');

            if (!string.IsNullOrWhiteSpace(resolvedSeedPath))
            {
                copy["seed_file"] = System.IO.Path.GetFullPath(resolvedSeedPath).Replace('\\', '/');
            }

            if (!string.IsNullOrWhiteSpace(WeatherFile) && !System.IO.Path.IsPathRooted(WeatherFile))
            {
                string weatherPath = ResolveRelative(WeatherFile);
                if (weatherPath != null)
                {
                    copy["weather_file"] = weatherPath.Replace('\\', '/');
                }
            }

            // measure_paths and file_paths must survive as absolute paths: the copy no longer
            // sits beside the original workflow, so anything relative would resolve elsewhere.
            RewritePathArray(copy, "measure_paths");
            RewritePathArray(copy, "file_paths");

            string result = System.IO.Path.Combine(directory, System.IO.Path.GetFileName(Path));
            File.WriteAllText(result, copy.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return result;
        }

        private void RewritePathArray(JsonObject jsonObject, string key)
        {
            JsonArray jsonArray = jsonObject[key] as JsonArray;
            if (jsonArray == null)
            {
                return;
            }

            JsonArray replacement = new JsonArray();
            foreach (JsonNode node in jsonArray)
            {
                string value = node?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                string resolved = System.IO.Path.IsPathRooted(value) ? System.IO.Path.GetFullPath(value) : ResolveRelative(value);
                replacement.Add(JsonValue.Create((resolved ?? value).Replace('\\', '/')));
            }

            jsonObject[key] = replacement;
        }

        private string ResolveRelative(string value)
        {
            try
            {
                return System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, value));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string StringValue(JsonObject jsonObject, string key)
        {
            if (jsonObject == null)
            {
                return null;
            }

            JsonNode jsonNode;
            if (!jsonObject.TryGetPropertyValue(key, out jsonNode) || jsonNode == null)
            {
                return null;
            }

            try
            {
                return jsonNode.GetValue<string>();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Locates the OSM a completed workflow produced — the model after all model measures
        /// have run, which is what an import must convert.
        /// <para>
        /// The order is deterministic and never trusts a single unverified filename: the
        /// <c>osm_path</c> recorded in the run's <c>out.osw</c>, then the CLI's conventional
        /// <c>run/in.osm</c>, then the newest OSM written under the run directory after the run
        /// started. The timestamp bound matters — a stale OSM left by an earlier run must not be
        /// mistaken for this run's output.
        /// </para>
        /// </summary>
        /// <param name="runDirectory">Directory the workflow ran in.</param>
        /// <param name="runStartedUtc">When the run started; older files are ignored.</param>
        /// <param name="how">How the OSM was located, for the diagnostic record.</param>
        /// <returns>The final OSM path, or null.</returns>
        public static string FindFinalOsm(string runDirectory, DateTime runStartedUtc, out string how)
        {
            how = null;

            if (string.IsNullOrWhiteSpace(runDirectory) || !Directory.Exists(runDirectory))
            {
                return null;
            }

            foreach (string outOswPath in new[] { System.IO.Path.Combine(runDirectory, "out.osw"), System.IO.Path.Combine(runDirectory, "run", "out.osw") })
            {
                string recorded = OsmPathFromOutOsw(outOswPath);
                if (recorded != null && File.Exists(recorded))
                {
                    how = string.Format("recorded as osm_path in {0}", outOswPath);
                    return recorded;
                }
            }

            string inOsm = System.IO.Path.Combine(runDirectory, "run", "in.osm");
            if (File.Exists(inOsm))
            {
                how = "the CLI's run/in.osm";
                return inOsm;
            }

            string newest = null;
            DateTime newestWriteUtc = DateTime.MinValue;
            try
            {
                foreach (string candidate in Directory.GetFiles(runDirectory, "*.osm", SearchOption.AllDirectories))
                {
                    DateTime writeUtc = File.GetLastWriteTimeUtc(candidate);

                    // One second of slack: file timestamps and the process clock are not the same
                    // clock, and a model written in the run's first moments is still this run's.
                    if (writeUtc.AddSeconds(1) < runStartedUtc || writeUtc <= newestWriteUtc)
                    {
                        continue;
                    }

                    newest = candidate;
                    newestWriteUtc = writeUtc;
                }
            }
            catch (Exception)
            {
                return null;
            }

            if (newest != null)
            {
                how = string.Format("the newest OSM written under the run directory after the run started ({0})", newest);
            }

            return newest;
        }

        /// <summary>Reads the <c>osm_path</c> the CLI recorded in an out.osw, or null.</summary>
        private static string OsmPathFromOutOsw(string outOswPath)
        {
            if (!File.Exists(outOswPath))
            {
                return null;
            }

            try
            {
                JsonObject jsonObject = JsonNode.Parse(File.ReadAllText(outOswPath)) as JsonObject;
                string value = StringValue(jsonObject, "osm_path");
                if (string.IsNullOrWhiteSpace(value))
                {
                    return null;
                }

                return System.IO.Path.IsPathRooted(value)
                    ? value
                    : System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(outOswPath), value));
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
