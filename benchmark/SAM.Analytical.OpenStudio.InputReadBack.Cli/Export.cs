// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical.OpenStudio.InputReadBack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.InputReadBack.Cli
{
    /// <summary>
    /// The export step (writer side). It converts the frozen SAM model with the production writer and the explicit EPW,
    /// saves the OSM without running the CLI, reloads the saved OSM and translates it to IDF with the OpenStudio SDK's
    /// ForwardTranslator (the step the CLI performs before EnergyPlus). Nothing is simulated. It records hashes and
    /// diagnostics; it never reads values back.
    /// </summary>
    internal static class Export
    {
        public const string ModelFile = "frozen-model.json";
        public const string EpwFile = "weather.epw";
        public const string OsmFile = "model.osm";
        public const string IdfFile = "in.idf";
        public const string ManifestFile = "export.json";

        public static int Run(string modelPath, string epwPath, string outputDirectory, string label)
        {
            if (!File.Exists(modelPath) || !File.Exists(epwPath))
            {
                throw new FileNotFoundException("The frozen model and the EPW must exist", File.Exists(modelPath) ? epwPath : modelPath);
            }

            if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
            {
                throw new IOException("The export directory must be new or empty: " + outputDirectory);
            }

            Directory.CreateDirectory(outputDirectory);

            // The run's inputs are byte copies of the frozen inputs, so the compare step can prove what was converted.
            string modelCopy = Path.Combine(outputDirectory, ModelFile);
            string epwCopy = Path.Combine(outputDirectory, EpwFile);
            File.Copy(modelPath, modelCopy);
            File.Copy(epwPath, epwCopy);

            ExportManifest manifest = new ExportManifest
            {
                Label = label,
                ModelSha256 = Hashing.Sha256(modelCopy),
                EpwSha256 = Hashing.Sha256(epwCopy),
                SourceModelPath = Path.GetFullPath(modelPath),
                SourceEpwPath = Path.GetFullPath(epwPath),
                ProcessId = Environment.ProcessId,
                StartedUtc = DateTime.UtcNow,
                OpenStudioSdkVersion = global::OpenStudio.OpenStudioUtilitiesCore.openStudioLongVersion(),
                WriterAssembly = typeof(Convert).Assembly.Location,
                WriterAssemblySha256 = Hashing.Sha256(typeof(Convert).Assembly.Location)
            };

            AnalyticalModel analyticalModel = Core.Convert.ToSAM<AnalyticalModel>(modelCopy)?.FirstOrDefault();
            if (analyticalModel == null)
            {
                throw new InvalidDataException("The frozen model could not be read: " + modelCopy);
            }

            string conversionDirectory = Path.Combine(outputDirectory, "conversion");
            Core.OpenStudio.OpenStudioRunOptions runOptions = new Core.OpenStudio.OpenStudioRunOptions { UseUniqueRunDirectory = false };
            using (OpenStudioConversionResult result = analyticalModel.ToOpenStudio(epwCopy, conversionDirectory, null, runOptions, run: false))
            {
                if (result?.OsmPath == null || !File.Exists(result.OsmPath))
                {
                    throw new InvalidOperationException("The writer saved no OSM");
                }

                manifest.ConversionValid = result.IsValid;
                foreach (Core.OpenStudio.OpenStudioDiagnostic diagnostic in result.Diagnostics)
                {
                    manifest.Diagnostics.Add(string.Format("{0} {1} {2}", diagnostic.Severity, diagnostic.Code, diagnostic.Message));
                }

                File.Copy(result.OsmPath, Path.Combine(outputDirectory, OsmFile));
            }

            // The CLI route: load the saved OSM (not the in-memory model) and forward-translate it.
            string osmPath = Path.Combine(outputDirectory, OsmFile);
            global::OpenStudio.OptionalModel loaded = global::OpenStudio.Model.load(global::OpenStudio.OpenStudioUtilitiesCore.toPath(osmPath));
            if (loaded == null || loaded.isNull())
            {
                throw new InvalidDataException("The saved OSM could not be loaded by the SDK");
            }

            global::OpenStudio.EnergyPlusForwardTranslator forwardTranslator = new global::OpenStudio.EnergyPlusForwardTranslator();
            global::OpenStudio.Workspace workspace = forwardTranslator.translateModel(loaded.get());
            foreach (global::OpenStudio.LogMessage message in forwardTranslator.errors())
            {
                manifest.TranslatorMessages.Add("Error " + message.logMessage());
            }

            foreach (global::OpenStudio.LogMessage message in forwardTranslator.warnings())
            {
                manifest.TranslatorMessages.Add("Warning " + message.logMessage());
            }

            string idfPath = Path.Combine(outputDirectory, IdfFile);
            if (!workspace.toIdfFile().save(global::OpenStudio.OpenStudioUtilitiesCore.toPath(idfPath), true))
            {
                throw new IOException("The IDF could not be saved");
            }

            manifest.OsmSha256 = Hashing.Sha256(osmPath);
            manifest.IdfSha256 = Hashing.Sha256(idfPath);
            manifest.FinishedUtc = DateTime.UtcNow;
            manifest.Save(Path.Combine(outputDirectory, ManifestFile));

            Console.WriteLine("export: {0} (valid {1}, {2} diagnostics, {3} translator messages)", outputDirectory, manifest.ConversionValid, manifest.Diagnostics.Count, manifest.TranslatorMessages.Count);
            return 0;
        }
    }
}
