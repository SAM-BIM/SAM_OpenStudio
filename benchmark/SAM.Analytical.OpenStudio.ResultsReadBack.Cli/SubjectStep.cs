// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical.Benchmark;
using SAM.Analytical.OpenStudio.Benchmark;
using SAM.Analytical.OpenStudio.InputReadBack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace SAM.Analytical.OpenStudio.ResultsReadBack.Cli
{
    /// <summary>
    /// The subject capture: the ONLY step that runs the code under test. It (1) re-maps the captured producer objects
    /// (sam.json) with the producer's own ToBenchmark and requires byte-identical B1a model and spaces sections (closure),
    /// (2) records the production report keys of every SAM space, and (3) imports the same SQL into a fresh copy of the
    /// model with the SAM_UI/Grasshopper path Modify.AddResults.
    /// </summary>
    internal static class SubjectStep
    {
        public static int Run(string runDirectory, string modelPath, TextWriter output, TextWriter error)
        {
            RunLayout layout = Program.Layout(runDirectory, error, out bool ok);
            if (!ok)
            {
                return ReadBackExit.Infrastructure;
            }

            AnalyticalModel model = Program.LoadModel(modelPath);
            SubjectDocument subject = Capture(model, modelPath, layout);
            S11Json.Save(subject, layout.Subject);
            output.WriteLine("subject: " + layout.Subject + " (closure " + (subject.ClosureIdentical ? "identical" : "DIFFERS") + ", " + subject.SamI.Count + " SAM-I space results)");
            return ReadBackExit.Pass;
        }

        public static SubjectDocument Capture(AnalyticalModel model, string modelPath, RunLayout layout)
        {
            SubjectDocument result = new SubjectDocument
            {
                ProcessId = Environment.ProcessId,
                ProcessStartedUtc = ProcessIdentity.CurrentStartedUtc(),
                StartedUtc = DateTime.UtcNow,
                SubjectAssemblySha256 = Sha256.Of(typeof(SubjectStep).Assembly.Location),
                ProducerAssemblySha256 = Sha256.Of(typeof(OpenStudioBenchmarkInputs).Assembly.Location),
                ImporterAssemblySha256 = Sha256.Of(typeof(Analytical.OpenStudio.Modify).Assembly.Location),
                ModelSha256 = Sha256.Of(modelPath),
                B1aSha256 = Sha256.Of(layout.B1a),
                SamResultsSha256 = Sha256.Of(layout.SamResults),
                SqlSha256Before = Sha256.Of(layout.Sql),
            };

            Closure(model, layout, result);

            foreach (Space space in model.AdjacencyCluster?.GetSpaces() ?? new List<Space>())
            {
                result.ProductionKeys.Add(new ProductionKey
                {
                    SpaceGuid = space.Guid,
                    SpaceName = space.Name,
                    IdealLoads = Core.OpenStudio.Query.OpenStudioName("IdealLoads", space.Name, space.Guid),
                    ThermalZone = Core.OpenStudio.Query.OpenStudioName("ThermalZone", space.Name, space.Guid),
                });
            }

            Import(model, layout.Sql, result);
            result.SqlSha256After = Sha256.Of(layout.Sql);
            result.FinishedUtc = DateTime.UtcNow;
            return result;
        }

        /// <summary>
        /// The captured objects, mapped again by the producer with a context rebuilt from the produced document's own
        /// provenance, must reproduce its model and spaces sections byte for byte.
        /// </summary>
        private static void Closure(AnalyticalModel model, RunLayout layout, SubjectDocument result)
        {
            try
            {
                string producedJson = File.ReadAllText(layout.B1a);
                BenchmarkDocument produced = BenchmarkSerializer.Deserialize(producedJson);
                OpenStudioBenchmarkInputs inputs = OpenStudioBenchmarkInputs.Read(layout.SamResults);
                BenchmarkDocument regenerated = model.ToBenchmark(Context(produced.Provenance), inputs);
                JsonObject producedNode = JsonNode.Parse(producedJson) as JsonObject;
                JsonObject regeneratedNode = JsonNode.Parse(BenchmarkSerializer.Serialize(regenerated)) as JsonObject;
                string producedModel = producedNode?["model"]?.ToJsonString();
                string regeneratedModel = regeneratedNode?["model"]?.ToJsonString();
                string producedSpaces = producedNode?["spaces"]?.ToJsonString();
                string regeneratedSpaces = regeneratedNode?["spaces"]?.ToJsonString();
                result.ClosureModelSha256Produced = Hash(producedModel);
                result.ClosureModelSha256Regenerated = Hash(regeneratedModel);
                result.ClosureSpacesSha256Produced = Hash(producedSpaces);
                result.ClosureSpacesSha256Regenerated = Hash(regeneratedSpaces);
                bool model_ = producedModel != null && producedModel == regeneratedModel;
                bool spaces = producedSpaces != null && producedSpaces == regeneratedSpaces;
                result.ClosureIdentical = model_ && spaces;
                result.ClosureDetail = result.ClosureIdentical
                    ? "the captured objects re-map to byte-identical B1a model and spaces sections"
                    : "the captured objects re-map to a different B1a: model section " + (model_ ? "identical" : "DIFFERS") + ", spaces section " + (spaces ? "identical" : "DIFFERS");
            }
            catch (Exception exception) when (exception is IOException || exception is System.Text.Json.JsonException || exception is InvalidDataException || exception is InvalidCastException)
            {
                result.ClosureIdentical = false;
                result.ClosureDetail = "closure could not be evaluated: " + exception.Message;
            }
        }

        /// <summary>A producer context carrying the produced document's provenance (the mapping reads only provenance from it).</summary>
        internal static OpenStudioBenchmarkContext Context(BenchmarkProvenance provenance)
        {
            OpenStudioBenchmarkContext context = new OpenStudioBenchmarkContext
            {
                SourceModelName = provenance.SourceModelName,
                SourceModelGuid = provenance.SourceModelGuid,
                SourceFileHash = provenance.SourceFileHash,
                CanonicalModelHash = provenance.CanonicalModelHash,
                CanonicalizationVersion = provenance.CanonicalizationVersion,
                SamCommit = provenance.SamCommit,
                RunnerCommit = provenance.RunnerCommit,
                EngineName = provenance.Engine?.Name,
                EngineVersion = provenance.Engine?.Version,
                SdkVersion = provenance.Engine?.SdkVersion,
                WeatherIdentity = provenance.Weather?.Identity,
                WeatherHash = provenance.Weather?.Hash,
                DesignDaySource = provenance.DesignDaySource,
                RunTimestampUtc = provenance.RunTimestampUtc,
                DurationSeconds = provenance.DurationSeconds ?? 0,
                State = provenance.State,
            };

            context.Warnings.AddRange(provenance.Warnings ?? new List<string>());
            context.Notes.AddRange(provenance.Notes ?? new List<string>());
            return context;
        }

        /// <summary>SAM-I: the production import of the SQL into a fresh copy of the model, with its diagnostics and relations.</summary>
        private static void Import(AnalyticalModel model, string sqlPath, SubjectDocument result)
        {
            AdjacencyCluster cluster = new AdjacencyCluster(model.AdjacencyCluster);
            List<Core.Result> results = Analytical.OpenStudio.Modify.AddResults(cluster, sqlPath, out List<string> diagnostics) ?? new List<Core.Result>();
            result.SamIDiagnostics.AddRange(diagnostics ?? new List<string>());
            List<Space> spaces = cluster.GetSpaces() ?? new List<Space>();
            foreach (SpaceSimulationResult spaceResult in results.OfType<SpaceSimulationResult>())
            {
                Space attached = spaces.FirstOrDefault(x => cluster.GetResults<SpaceSimulationResult>(x)?.Any(r => r.Guid == spaceResult.Guid) == true);
                result.SamI.Add(SamResults.Describe(spaceResult, attached?.Guid));
            }
        }

        private static string Hash(string text) => text == null ? null : Sha256.OfBytes(System.Text.Encoding.UTF8.GetBytes(text));
    }
}
