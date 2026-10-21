// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace SAM.Analytical.OpenStudio.InputReadBack
{
    /// <summary>What the export step converted and wrote (export.json). Writer-side facts only: hashes, versions, diagnostics.</summary>
    public sealed class ExportManifest
    {
        public string Schema { get; set; } = "s09-export/1";
        public string Label { get; set; }

        /// <summary>Set only on a disposable negative copy: the targeted mutation applied to the exported files.</summary>
        public string Mutation { get; set; }
        public string SourceModelPath { get; set; }
        public string SourceEpwPath { get; set; }
        public string ModelSha256 { get; set; }
        public string EpwSha256 { get; set; }
        public string OsmSha256 { get; set; }
        public string IdfSha256 { get; set; }
        public string OpenStudioSdkVersion { get; set; }
        public string WriterAssembly { get; set; }
        public string WriterAssemblySha256 { get; set; }
        public int ProcessId { get; set; }
        public DateTime StartedUtc { get; set; }
        public DateTime FinishedUtc { get; set; }
        public bool ConversionValid { get; set; }
        public List<string> Diagnostics { get; set; } = new List<string>();
        public List<string> TranslatorMessages { get; set; } = new List<string>();

        public void Save(string path)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, Json.Options));
        }

        public static ExportManifest Load(string path)
        {
            return JsonSerializer.Deserialize<ExportManifest>(File.ReadAllText(path), Json.Options);
        }
    }

    public static class Hashing
    {
        public static string Sha256(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha256 = SHA256.Create())
            {
                return System.Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
            }
        }

        public static string Sha256(byte[] bytes)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return System.Convert.ToHexString(sha256.ComputeHash(bytes)).ToLowerInvariant();
            }
        }
    }

    internal static class Json
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
    }
}
