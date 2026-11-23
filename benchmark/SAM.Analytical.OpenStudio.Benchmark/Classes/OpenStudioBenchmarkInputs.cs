// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SAM.Analytical.OpenStudio.Benchmark
{
    /// <summary>
    /// The SAM result objects a B1a document is mapped from: the model result and the per-space results the established
    /// SAM mappings produced from the run's result set, the per-space design-load results, and whether the result set
    /// carried any zone key for each annual consumption. <see cref="Modify.BenchmarkInputs"/> builds them once and
    /// <see cref="Modify.ToBenchmark(AnalyticalModel, OpenStudioBenchmarkContext, OpenStudioBenchmarkInputs)"/> maps exactly
    /// these instances, so the objects written with <c>--sam-results</c> are the ones that produced the document (S11).
    /// </summary>
    public sealed class OpenStudioBenchmarkInputs
    {
        /// <summary>Schema of the <c>--sam-results</c> file.</summary>
        public const string Schema = "s11-sam-results/1";

        /// <summary>The model result (consumption, coincident peaks, floor area, volume); null for a failed run.</summary>
        public AnalyticalModelSimulationResult ModelResult { get; set; }

        /// <summary>Per-space, per-load-type annual results (peak load, its hour, unmet hours).</summary>
        public List<SpaceSimulationResult> SpaceResults { get; set; } = new List<SpaceSimulationResult>();

        /// <summary>Per-space design-day (ZoneSizes) results; empty when no sizing ran.</summary>
        public List<SpaceSimulationResult> DesignLoadResults { get; set; } = new List<SpaceSimulationResult>();

        /// <summary>
        /// True when the result set carried at least one zone key for annual heating. Without one the model's heating
        /// consumption is missing, never a measured 0 kWh (the result set's empty sum).
        /// </summary>
        public bool HeatingConsumptionKeyed { get; set; }

        /// <summary>As <see cref="HeatingConsumptionKeyed"/>, for annual cooling.</summary>
        public bool CoolingConsumptionKeyed { get; set; }

        /// <summary>The objects as SAM JSON inside a small envelope (the <c>--sam-results</c> file).</summary>
        public JsonObject ToJsonObject()
        {
            JsonArray spaceResults = new JsonArray();
            foreach (SpaceSimulationResult spaceResult in SpaceResults ?? new List<SpaceSimulationResult>())
            {
                spaceResults.Add(spaceResult?.ToJsonObject());
            }

            JsonArray designLoadResults = new JsonArray();
            foreach (SpaceSimulationResult designLoadResult in DesignLoadResults ?? new List<SpaceSimulationResult>())
            {
                designLoadResults.Add(designLoadResult?.ToJsonObject());
            }

            return new JsonObject
            {
                ["schema"] = Schema,
                ["heatingConsumptionKeyed"] = HeatingConsumptionKeyed,
                ["coolingConsumptionKeyed"] = CoolingConsumptionKeyed,
                ["modelResult"] = ModelResult?.ToJsonObject(),
                ["spaceResults"] = spaceResults,
                ["designLoadResults"] = designLoadResults,
            };
        }

        /// <summary>Reads an envelope written by <see cref="ToJsonObject"/>; throws <see cref="JsonException"/> for another schema.</summary>
        public static OpenStudioBenchmarkInputs FromJsonObject(JsonObject jsonObject)
        {
            if (jsonObject == null || (string)jsonObject["schema"] != Schema)
            {
                throw new JsonException("Not a " + Schema + " document.");
            }

            return new OpenStudioBenchmarkInputs
            {
                HeatingConsumptionKeyed = (bool)jsonObject["heatingConsumptionKeyed"],
                CoolingConsumptionKeyed = (bool)jsonObject["coolingConsumptionKeyed"],
                ModelResult = jsonObject["modelResult"] is JsonObject model ? Core.Create.IJSAMObject<AnalyticalModelSimulationResult>(model) : null,
                SpaceResults = Results(jsonObject["spaceResults"] as JsonArray),
                DesignLoadResults = Results(jsonObject["designLoadResults"] as JsonArray),
            };
        }

        /// <summary>Writes the envelope as indented UTF-8 (no BOM) with LF line ends.</summary>
        public void Write(string path)
        {
            string json = ToJsonObject().ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n";
            File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(json));
        }

        /// <summary>Reads a file written by <see cref="Write"/>.</summary>
        public static OpenStudioBenchmarkInputs Read(string path)
        {
            return FromJsonObject(JsonNode.Parse(File.ReadAllText(path)) as JsonObject);
        }

        private static List<SpaceSimulationResult> Results(JsonArray jsonArray)
        {
            return jsonArray == null
                ? new List<SpaceSimulationResult>()
                : jsonArray.OfType<JsonObject>().Select(x => Core.Create.IJSAMObject<SpaceSimulationResult>(x)).ToList();
        }
    }
}
