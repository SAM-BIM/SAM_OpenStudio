// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical.OpenStudio.InputReadBack;
using System.IO;

namespace SAM.Analytical.OpenStudio.ResultsReadBack.Cli
{
    /// <summary>The reference read: library code only (no production importer, writer or naming).</summary>
    internal static class ReadStep
    {
        public static int Run(string runDirectory, string modelPath, string fixtureId, TextWriter output, TextWriter error)
        {
            RunLayout layout = Program.Layout(runDirectory, error, out bool ok);
            if (!ok)
            {
                return ReadBackExit.Infrastructure;
            }

            AnalyticalModel model = Program.LoadModel(modelPath);
            ReferenceDocument reference = ResultReference.Build(model, modelPath, layout.Idf, layout.Sql, layout.Eso, fixtureId ?? Path.GetFileNameWithoutExtension(modelPath));
            S11Json.Save(reference, layout.Reference);
            output.WriteLine("read: " + layout.Reference + " (" + reference.Spaces.Count + " spaces, " + reference.GlobalBlocks.Count + " global block(s), " + reference.Completeness.Count + " series judged)");
            return ReadBackExit.Pass;
        }
    }
}
