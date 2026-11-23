// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace SAM.Analytical.OpenStudio.ResultsReadBack
{
    /// <summary>One SAM SpaceSimulationResult as the subject saw it (parameter values, identity, relation).</summary>
    public sealed class SubjectResult
    {
        public Guid ResultGuid { get; set; }
        public string Name { get; set; }
        public string Reference { get; set; }
        public string Source { get; set; }
        public string LoadType { get; set; }
        public double? Load { get; set; }
        public int? LoadIndex { get; set; }
        public double? UnmetHours { get; set; }
        public double? DesignLoad { get; set; }

        /// <summary>The SAM space the result is related to in the cluster; null when unattached.</summary>
        public Guid? AttachedTo { get; set; }

        public bool IsDesignLoad => DesignLoad.HasValue;
        public bool IsAnnual => !DesignLoad.HasValue && Load.HasValue;
    }

    /// <summary>The report keys the production naming gives one SAM space (cross-checked against the native identity).</summary>
    public sealed class ProductionKey
    {
        public Guid SpaceGuid { get; set; }
        public string SpaceName { get; set; }
        public string IdealLoads { get; set; }
        public string ThermalZone { get; set; }
    }

    /// <summary>
    /// subject.json: what the code under test produced, captured by the CLI's <c>subject</c> step (the only step that runs
    /// production code): the closure of the captured producer objects onto the produced B1a, the production report keys,
    /// and the SAM_UI/Grasshopper import (Modify.AddResults) of the same SQL on a fresh copy of the model.
    /// </summary>
    public sealed class SubjectDocument
    {
        public const string CurrentSchema = "s11-subject/1";

        public string Schema { get; set; } = CurrentSchema;
        public int ProcessId { get; set; }
        public DateTime ProcessStartedUtc { get; set; }
        public DateTime StartedUtc { get; set; }
        public DateTime FinishedUtc { get; set; }
        public string SubjectAssemblySha256 { get; set; }
        public string ProducerAssemblySha256 { get; set; }
        public string ImporterAssemblySha256 { get; set; }
        public string ModelSha256 { get; set; }
        public string B1aSha256 { get; set; }
        public string SamResultsSha256 { get; set; }
        public string SqlSha256Before { get; set; }
        public string SqlSha256After { get; set; }

        /// <summary>True when the captured objects re-mapped offline give byte-identical B1a model and spaces sections.</summary>
        public bool ClosureIdentical { get; set; }
        public string ClosureDetail { get; set; }
        public string ClosureModelSha256Produced { get; set; }
        public string ClosureModelSha256Regenerated { get; set; }
        public string ClosureSpacesSha256Produced { get; set; }
        public string ClosureSpacesSha256Regenerated { get; set; }

        public List<ProductionKey> ProductionKeys { get; set; } = new List<ProductionKey>();
        public List<SubjectResult> SamI { get; set; } = new List<SubjectResult>();
        public List<string> SamIDiagnostics { get; set; } = new List<string>();

        /// <summary>Set on a negative copy whose subject was mutated rather than captured.</summary>
        public string Mutation { get; set; }
    }

    /// <summary>
    /// The captured producer objects (SAM-P) read straight from the <c>--sam-results</c> file with the SAM JSON
    /// deserializer, independently of the subject step: the compare judges the very objects the closure tied to the B1a.
    /// </summary>
    public sealed class SamResults
    {
        public const string Schema = "s11-sam-results/1";

        public AnalyticalModelSimulationResult ModelResult { get; set; }
        public List<SpaceSimulationResult> SpaceResults { get; set; } = new List<SpaceSimulationResult>();
        public List<SpaceSimulationResult> DesignLoadResults { get; set; } = new List<SpaceSimulationResult>();
        public bool HeatingConsumptionKeyed { get; set; }
        public bool CoolingConsumptionKeyed { get; set; }

        public static SamResults Read(string path)
        {
            JsonObject jsonObject = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (jsonObject == null || (string)jsonObject["schema"] != Schema)
            {
                throw new InvalidDataException(path + " is not a " + Schema + " document");
            }

            return new SamResults
            {
                HeatingConsumptionKeyed = (bool)jsonObject["heatingConsumptionKeyed"],
                CoolingConsumptionKeyed = (bool)jsonObject["coolingConsumptionKeyed"],
                ModelResult = jsonObject["modelResult"] is JsonObject model ? Core.Create.IJSAMObject<AnalyticalModelSimulationResult>(model) : null,
                SpaceResults = Results(jsonObject["spaceResults"] as JsonArray),
                DesignLoadResults = Results(jsonObject["designLoadResults"] as JsonArray),
            };
        }

        private static List<SpaceSimulationResult> Results(JsonArray jsonArray)
        {
            return jsonArray == null
                ? new List<SpaceSimulationResult>()
                : jsonArray.OfType<JsonObject>().Select(x => Core.Create.IJSAMObject<SpaceSimulationResult>(x)).ToList();
        }

        /// <summary>A SAM result's parameter values in the subject's neutral form.</summary>
        public static SubjectResult Describe(SpaceSimulationResult result, Guid? attachedTo)
        {
            return new SubjectResult
            {
                ResultGuid = result.Guid,
                Name = result.Name,
                Reference = result.Reference,
                Source = result.Source,
                LoadType = result.TryGetValue(global::SAM.Analytical.SpaceSimulationResultParameter.LoadType, out string loadType) ? loadType : null,
                Load = Double(result, global::SAM.Analytical.SpaceSimulationResultParameter.Load),
                LoadIndex = Integer(result, global::SAM.Analytical.SpaceSimulationResultParameter.LoadIndex),
                UnmetHours = Double(result, global::SAM.Analytical.SpaceSimulationResultParameter.UnmetHours),
                DesignLoad = Double(result, global::SAM.Analytical.SpaceSimulationResultParameter.DesignLoad),
                AttachedTo = attachedTo,
            };
        }

        public static double? Double(Core.SAMObject sAMObject, Enum parameter)
        {
            return sAMObject != null && sAMObject.TryGetValue(parameter, out double value) ? value : (double?)null;
        }

        /// <summary>An integer parameter, accepting a whole-number double (JSON round trips may store it so).</summary>
        public static int? Integer(Core.SAMObject sAMObject, Enum parameter)
        {
            if (sAMObject == null)
            {
                return null;
            }

            if (sAMObject.TryGetValue(parameter, out int value))
            {
                return value;
            }

            double? asDouble = Double(sAMObject, parameter);
            return asDouble.HasValue && asDouble.Value == Math.Truncate(asDouble.Value) ? (int)asDouble.Value : (int?)null;
        }
    }
}
