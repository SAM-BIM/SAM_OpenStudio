// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SAM.Analytical.OpenStudio.ResultsReadBack.Tests
{
    /// <summary>
    /// S11 reference independence (ported from SAM_Tas S10SourceScanTests): no file of the reference library (native
    /// readers, reference, compare, report, classes) and not its project file may reach the code under test: the
    /// OpenStudio runner, the importer and producer conversions, the production naming and unit helpers, AddResults, the
    /// SAM.Analytical.OpenStudio / SAM.Core.OpenStudio namespaces or the OpenStudio SDK. Only the CLI's subject step calls
    /// production code.
    /// </summary>
    [TestFixture]
    public class S11SourceScanTests
    {
        public static readonly string[] Forbidden =
        {
            "OpenStudioSimulationRunner", "Convert.ToSAM", "ToSAM_", "OpenStudioName", "ConvertUnit", "JoulesToKilowattHours",
            "JoulesPerIntervalToWatts", "AddResults", "Core.OpenStudio", "OpenStudioSimulationResultSet", "OpenStudioBenchmarkInputs",
            "ToBenchmark", "BenchmarkInputs", "using OpenStudio", "OpenStudio.Model", "SqlFile",
        };

        /// <summary>The production namespace, but not this library's own or the S09 reader's.</summary>
        private static readonly Regex ProductionNamespace = new Regex(@"\bSAM\.Analytical\.OpenStudio(?!\.(ResultsReadBack|InputReadBack)\b)(\.|;|\s)", RegexOptions.CultureInvariant);

        private static string LibraryDirectory()
        {
            DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "benchmark", "SAM.Analytical.OpenStudio.ResultsReadBack");
                if (Directory.Exists(candidate)) return candidate;
                candidate = Path.Combine(directory.FullName, "SAM.Analytical.OpenStudio.ResultsReadBack");
                if (File.Exists(Path.Combine(candidate, "SAM.Analytical.OpenStudio.ResultsReadBack.csproj"))) return candidate;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("SAM.Analytical.OpenStudio.ResultsReadBack not found above " + TestContext.CurrentContext.TestDirectory);
        }

        /// <summary>Every forbidden token found in the code lines of <paramref name="source"/> (comment lines are not code).</summary>
        public static List<string> Scan(string source)
        {
            List<string> result = new List<string>();
            string[] lines = source.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("//", StringComparison.Ordinal)) continue;
                foreach (string token in Forbidden.Where(x => line.Contains(x)))
                {
                    result.Add("line " + (i + 1) + ": " + token);
                }

                if (ProductionNamespace.IsMatch(line))
                {
                    result.Add("line " + (i + 1) + ": SAM.Analytical.OpenStudio namespace");
                }
            }

            return result;
        }

        private static IEnumerable<string> ReferenceFiles()
        {
            return Directory.GetFiles(LibraryDirectory(), "*.cs", SearchOption.AllDirectories).Where(x => !x.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !x.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));
        }

        [Test]
        public void ReferenceFiles_CallNothingOfTheCodeUnderTest()
        {
            List<string> files = ReferenceFiles().ToList();
            Assert.That(files.Select(Path.GetFileName), Is.SupersetOf(new[] { "SqlResultRead.cs", "EsoResultRead.cs", "RunCalendar.cs", "ResultReference.cs", "ResultCompare.cs", "ResultReport.cs" }));
            foreach (string file in files)
            {
                Assert.That(Scan(File.ReadAllText(file)), Is.Empty, file);
            }
        }

        [Test]
        public void ReferenceProject_ReferencesNoProductionProjectAndNoSdk()
        {
            System.Xml.Linq.XDocument project = System.Xml.Linq.XDocument.Load(Path.Combine(LibraryDirectory(), "SAM.Analytical.OpenStudio.ResultsReadBack.csproj"));
            List<string> includes = project.Descendants().Where(x => x.Name.LocalName == "ProjectReference" || x.Name.LocalName == "PackageReference" || x.Name.LocalName == "Reference").Select(x => (string)x.Attribute("Include")).ToList();
            Assert.That(includes, Is.EquivalentTo(new[]
            {
                "System.Data.SQLite.Core",
                @"..\SAM.Analytical.OpenStudio.InputReadBack\SAM.Analytical.OpenStudio.InputReadBack.csproj",
                "SAM.Analytical.Benchmark", "SAM.Core", "SAM.Geometry", "SAM.Analytical", "SAM.Architectural", "SAM.Weather",
            }), "the reference library references only SQLite, the S09 reader and SAM core/schema assemblies");
        }

        [Test]
        public void ReferenceAssembly_LoadsNoProductionAssembly()
        {
            string[] references = typeof(ResultReference).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
            Assert.That(references, Has.None.EqualTo("SAM.Analytical.OpenStudio"));
            Assert.That(references, Has.None.EqualTo("SAM.Core.OpenStudio"));
            Assert.That(references, Has.None.EqualTo("OpenStudio"));
            Assert.That(references, Has.None.EqualTo("benchmark-openstudio"));
        }

        [Test]
        public void Scanner_FindsAPlantedForbiddenCall()
        {
            string path = ReferenceFiles().Single(x => Path.GetFileName(x) == "ResultReference.cs");
            string planted = File.ReadAllText(path).Replace("public static ReferenceDocument Build(", "public static object Cheat(string p) => SAM.Analytical.OpenStudio.Modify.AddResults(null, p, out _);\n        public static ReferenceDocument Build(");
            List<string> found = Scan(planted);
            Assert.That(found, Has.Some.Contains("AddResults"));
            Assert.That(found, Has.Some.Contains("namespace"));
            Assert.That(Scan("string key = Core.OpenStudio.Query.OpenStudioName(\"IdealLoads\", name, guid);"), Has.Count.EqualTo(2));
            Assert.That(Scan("// a comment may name OpenStudioSimulationRunner"), Is.Empty);
        }
    }
}
