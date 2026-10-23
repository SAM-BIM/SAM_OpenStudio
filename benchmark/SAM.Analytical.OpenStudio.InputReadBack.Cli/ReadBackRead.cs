// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical.InputEquivalence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.InputReadBack.Cli
{
    /// <summary>The read step: a separate process that reads model.osm and in.idf as text into native.json.</summary>
    internal static class ReadBackRead
    {
        public const string SnapshotFile = "native.json";

        public static int Run(string runDirectory)
        {
            string osm = Path.Combine(runDirectory, Export.OsmFile);
            string idf = Path.Combine(runDirectory, Export.IdfFile);
            NativeSnapshot snapshot = NativeReader.Read(osm, idf);
            snapshot.Save(Path.Combine(runDirectory, SnapshotFile));
            Console.WriteLine("read: {0} IDF objects, {1} OSM objects, {2} IDF schedules ({3} unreadable), {4} OSM schedules; pid {5}", snapshot.Idf.Count, snapshot.Osm.Count, snapshot.IdfSchedules.Count, snapshot.IdfSchedules.Count(x => x.Values == null), snapshot.OsmSchedules.Count, snapshot.ProcessId);
            return 0;
        }
    }

    /// <summary>The compare step: SAM intent from the frozen model against native.json, gated by the S08 core.</summary>
    internal static class ReadBackCompare
    {
        public static int Run(string runDirectory, string fixtureId, string ruleSetName, string outputDirectory)
        {
            GateRuleSet ruleSet;
            switch ((ruleSetName ?? "approved").ToLowerInvariant())
            {
                case "approved":
                    ruleSet = GateRuleSet.Approved;
                    break;
                case "provisional":
                    ruleSet = GateRuleSet.ProvisionalProposed;
                    break;
                case "interim":
                    ruleSet = GateRuleSet.Interim;
                    break;
                default:
                    Console.Error.WriteLine("unknown rule set: " + ruleSetName);
                    return ReadBackExit.Usage;
            }

            ExportManifest manifest = ExportManifest.Load(Path.Combine(runDirectory, Export.ManifestFile));
            NativeSnapshot snapshot = NativeSnapshot.Load(Path.Combine(runDirectory, ReadBackRead.SnapshotFile));
            string modelPath = Path.Combine(runDirectory, Export.ModelFile);
            string epwPath = Path.Combine(runDirectory, Export.EpwFile);

            List<string> problems = Infrastructure(manifest, snapshot, runDirectory, modelPath, epwPath);
            if (problems.Count > 0)
            {
                foreach (string problem in problems)
                {
                    Console.Error.WriteLine("infrastructure: " + problem);
                }

                return ReadBackExit.Infrastructure;
            }

            AnalyticalModel model = Core.Convert.ToSAM<AnalyticalModel>(modelPath)?.FirstOrDefault();
            OsComparison comparison = OsComparison.Run(model, snapshot, epwPath, fixtureId, ruleSet);
            Dictionary<string, string> inputs = new Dictionary<string, string>
            {
                ["label"] = manifest.Label,
                ["modelSha256"] = manifest.ModelSha256,
                ["epwSha256"] = manifest.EpwSha256,
                ["osmSha256"] = manifest.OsmSha256,
                ["idfSha256"] = manifest.IdfSha256,
                ["openStudioSdk"] = manifest.OpenStudioSdkVersion,
                ["writerSha256"] = manifest.WriterAssemblySha256,
                ["readerSha256"] = snapshot.ReaderAssemblySha256,
                ["exportPid"] = manifest.ProcessId.ToString(),
                ["readerPid"] = snapshot.ProcessId.ToString(),
                ["comparePid"] = Environment.ProcessId.ToString(),
                ["exportProcessStartedUtc"] = manifest.ProcessStartedUtc.ToString("O", CultureInfo.InvariantCulture),
                ["readerProcessStartedUtc"] = snapshot.ProcessStartedUtc.ToString("O", CultureInfo.InvariantCulture),
                ["compareProcessStartedUtc"] = ProcessIdentity.CurrentStartedUtc().ToString("O", CultureInfo.InvariantCulture),
                ["mutation"] = manifest.Mutation,
                ["writerDiagnostics"] = string.Join(" | ", manifest.Diagnostics.Where(x => !x.StartsWith("Information SAM-OS-MAT-002 Vapour", StringComparison.Ordinal))),
                ["translatorMessages"] = manifest.TranslatorMessages.Count.ToString()
            };

            OsReport report = OsReport.From(comparison, inputs);
            report.Save(outputDirectory ?? Path.Combine(runDirectory, "compare-" + ruleSetName));
            Console.Write(report.Text());
            return report.ExitCode;
        }

        /// <summary>
        /// The run is what it claims (as S08): the reader ran in its own process after the export finished; the OSM and IDF
        /// it read are the exported ones and were not changed while it read; the frozen model and EPW are the exported bytes.
        /// </summary>
        private static List<string> Infrastructure(ExportManifest manifest, NativeSnapshot snapshot, string runDirectory, string modelPath, string epwPath)
        {
            List<string> result = new List<string>();
            if (snapshot.Schema != "s09-native/2" || manifest.Schema != "s09-export/2")
            {
                result.Add("unknown export or snapshot schema");
            }

            // A process is its id and its creation time: Windows reuses the id of an exited process, so a fresh reader can
            // carry the export's id, and a fresh comparison the reader's (ProcessIdentity).
            if (!ProcessIdentity.IsRecorded(manifest.ProcessId, manifest.ProcessStartedUtc) || !ProcessIdentity.IsRecorded(snapshot.ProcessId, snapshot.ProcessStartedUtc))
            {
                result.Add("the export or reader process identity is not recorded: a fresh read is not proven");
            }
            else if (ProcessIdentity.Same(snapshot.ProcessId, snapshot.ProcessStartedUtc, manifest.ProcessId, manifest.ProcessStartedUtc) || snapshot.StartedUtc <= manifest.FinishedUtc)
            {
                result.Add("the reader did not run in a fresh process after the export");
            }

            if (ProcessIdentity.Same(snapshot.ProcessId, snapshot.ProcessStartedUtc, Environment.ProcessId, ProcessIdentity.CurrentStartedUtc()))
            {
                result.Add("the comparison runs in the reader's process");
            }

            if (snapshot.OsmSha256Before != manifest.OsmSha256 || snapshot.OsmSha256After != manifest.OsmSha256 || Hashing.Sha256(Path.Combine(runDirectory, Export.OsmFile)) != manifest.OsmSha256)
            {
                result.Add("the OSM read is not the exported OSM, or it changed");
            }

            if (snapshot.IdfSha256Before != manifest.IdfSha256 || snapshot.IdfSha256After != manifest.IdfSha256 || Hashing.Sha256(Path.Combine(runDirectory, Export.IdfFile)) != manifest.IdfSha256)
            {
                result.Add("the IDF read is not the exported IDF, or it changed");
            }

            if (Hashing.Sha256(modelPath) != manifest.ModelSha256 || Hashing.Sha256(epwPath) != manifest.EpwSha256)
            {
                result.Add("the frozen model or EPW is not the exported one");
            }

            return result;
        }
    }
}
