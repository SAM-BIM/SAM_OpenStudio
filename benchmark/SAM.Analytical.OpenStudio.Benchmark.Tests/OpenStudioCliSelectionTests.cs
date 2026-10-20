// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace SAM.Analytical.OpenStudio.Benchmark.Tests
{
    /// <summary>
    /// Roadmap S06: the OpenStudio CLI that runs a simulation is chosen deterministically. Among the discovered CLIs
    /// (PATH, direct installations, ladybug_tools, in that order) the first whose version equals the in-process SDK version
    /// wins, so another OpenStudio earlier on PATH (ladybug's 3.11 on the S05 laptop) is not picked by accident; with no
    /// match the first one found is kept, as before. Fully offline: candidate versions are supplied by the test.
    /// </summary>
    [TestFixture]
    public class OpenStudioCliSelectionTests
    {
        private const string Ladybug = @"C:\Program Files\ladybug_tools\openstudio\bin\openstudio.exe";
        private const string Direct = @"C:\openstudio-3.10.0\bin\openstudio.exe";
        private const string Other = @"D:\tools\OpenStudio-3.10.0\bin\openstudio.exe";

        private static readonly Dictionary<string, string> Versions = new Dictionary<string, string>
        {
            { Ladybug, "3.11.0+241b8abb4d" },
            { Direct, "3.10.0+86d7e215a1" },
            { Other, "3.10.0+86d7e215a1" },
        };

        private static string Version(string path)
        {
            return Versions.TryGetValue(path, out string version) ? version : null;
        }

        [Test]
        public void SdkMatchingCli_IsPreferred_OverAnEarlierDifferentVersionOnPath()
        {
            string selected = Core.OpenStudio.Query.OpenStudioCliPath(new[] { Ladybug, Direct }, "3.10.0", Version);
            Assert.That(selected, Is.EqualTo(Direct));
        }

        [Test]
        public void FirstMatchingCli_Wins_WhenSeveralMatch()
        {
            Assert.That(Core.OpenStudio.Query.OpenStudioCliPath(new[] { Ladybug, Other, Direct }, "3.10.0", Version), Is.EqualTo(Other));
            Assert.That(Core.OpenStudio.Query.OpenStudioCliPath(new[] { Ladybug, Direct, Other }, "3.10.0", Version), Is.EqualTo(Direct));
        }

        [Test]
        public void NoMatchingCli_KeepsTheFirstCandidate()
        {
            string selected = Core.OpenStudio.Query.OpenStudioCliPath(new[] { Ladybug, Direct }, "3.9.0", Version);
            Assert.That(selected, Is.EqualTo(Ladybug));
        }

        [Test]
        public void UnknownSdkVersion_KeepsTheFirstCandidate()
        {
            Assert.That(Core.OpenStudio.Query.OpenStudioCliPath(new[] { Ladybug, Direct }, null, Version), Is.EqualTo(Ladybug));
            Assert.That(Core.OpenStudio.Query.OpenStudioCliPath(new[] { Ladybug, Direct }, " ", Version), Is.EqualTo(Ladybug));
        }

        [Test]
        public void BuildMetadata_IsIgnored_OnBothSides()
        {
            Assert.That(Core.OpenStudio.Query.OpenStudioCliPath(new[] { Ladybug, Direct }, "3.10.0+86d7e215a1", Version), Is.EqualTo(Direct));
            Assert.That(Core.OpenStudio.Query.OpenStudioCliPath(new[] { Ladybug, Direct }, "3.11.0", Version), Is.EqualTo(Ladybug));
        }

        [Test]
        public void CliWithUnknownVersion_IsNotAMatch()
        {
            string unknown = @"E:\broken\openstudio.exe";
            Assert.That(Core.OpenStudio.Query.OpenStudioCliPath(new[] { unknown, Direct }, "3.10.0", Version), Is.EqualTo(Direct));
            Assert.That(Core.OpenStudio.Query.OpenStudioCliPath(new[] { unknown }, "3.10.0", Version), Is.EqualTo(unknown));
        }

        [Test]
        public void NoCandidates_ReturnsNull_AndBlankEntriesAreSkipped()
        {
            Assert.That(Core.OpenStudio.Query.OpenStudioCliPath(new string[0], "3.10.0", Version), Is.Null);
            Assert.That(Core.OpenStudio.Query.OpenStudioCliPath(null, "3.10.0", Version), Is.Null);
            Assert.That(Core.OpenStudio.Query.OpenStudioCliPath(new[] { "", null, Ladybug }, "3.9.0", Version), Is.EqualTo(Ladybug));
        }

        [Test]
        public void CliVersion_OfAMissingExecutable_IsNull()
        {
            string missing = Path.Combine(TestContext.CurrentContext.WorkDirectory, "no-such-folder", "openstudio.exe");
            Assert.That(Core.OpenStudio.Query.OpenStudioCliVersion(missing), Is.Null);
            Assert.That(Core.OpenStudio.Query.OpenStudioCliVersion(null), Is.Null);
        }

        [Test]
        public void ExplicitPath_IsUnchanged_AndNeverVersionChecked()
        {
            string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "s06-explicit-cli", "bin");
            Directory.CreateDirectory(directory);
            string executable = Path.Combine(directory, "openstudio.exe");
            File.WriteAllText(executable, "not an executable");

            Assert.That(Core.OpenStudio.Query.OpenStudioCliPath(executable), Is.EqualTo(executable));
            Assert.That(Core.OpenStudio.Query.OpenStudioCliPath(Path.GetDirectoryName(directory)), Is.EqualTo(executable));
        }
    }
}
