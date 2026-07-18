// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// M0 toolchain smoke tests: prove the OpenStudio 3.10.0 C# bindings load and work
    /// under net8.0 x64 outside Rhino, and that the installed OpenStudio CLI can open a
    /// generated OSM (SDK and CLI versions aligned per the implementation plan, section 11.1).
    /// </summary>
    [TestFixture]
    public class OpenStudioSmokeTests
    {
        [Test]
        public void OpenStudio_CreateSaveReload_RoundTrips()
        {
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "SAM_OpenStudio_Smoke.osm");
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            CreateSmokeModel(path, "SAM_Space_Smoke_00000001", "SAM_ThermalZone_Smoke_00000001");
            Assert.That(File.Exists(path), Is.True, "OSM file was not written");

            global::OpenStudio.Model reloaded = Core.OpenStudio.Create.Model(path);
            Assert.That(reloaded, Is.Not.Null, "SAM.Core.OpenStudio.Create.Model failed to reload the OSM");

            List<string> spaceNames = new List<string>();
            foreach (global::OpenStudio.Space space in reloaded.getSpaces())
            {
                spaceNames.Add(space.nameString());
            }

            Assert.That(spaceNames.Count, Is.EqualTo(1), "Reloaded model does not contain exactly one Space");
            Assert.That(spaceNames[0], Is.EqualTo("SAM_Space_Smoke_00000001"), "Space name was not preserved");

            int thermalZoneCount = 0;
            foreach (global::OpenStudio.ThermalZone thermalZone in reloaded.getThermalZones())
            {
                thermalZoneCount++;
            }

            Assert.That(thermalZoneCount, Is.EqualTo(1), "Reloaded model does not contain exactly one ThermalZone");
        }

        [Test]
        public void OpenStudioCli_Discovered_And_OpensGeneratedOsm()
        {
            string cliPath = Core.OpenStudio.Query.OpenStudioCliPath();
            Assert.That(cliPath, Is.Not.Null.And.Not.Empty, "OpenStudio CLI not found (searched PATH, direct installations and the ladybug_tools bundle)");
            TestContext.Out.WriteLine($"OpenStudio CLI: {cliPath}");

            string versionOutput = RunCli(cliPath, new[] { "openstudio_version" }, out int versionExitCode);
            Assert.That(versionExitCode, Is.EqualTo(0), $"openstudio_version failed: {versionOutput}");
            TestContext.Out.WriteLine($"OpenStudio CLI version: {versionOutput.Trim()}");

            string osmPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "SAM_OpenStudio_CliSmoke.osm");
            if (File.Exists(osmPath))
            {
                File.Delete(osmPath);
            }

            CreateSmokeModel(osmPath, "SAM_Space_CliSmoke_00000001", "SAM_ThermalZone_CliSmoke_00000001");

            string rubyScriptPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "SAM_OpenStudio_CliSmoke.rb");
            string rubyScript = string.Join("\n", new[]
            {
                "require 'openstudio'",
                $"model = OpenStudio::OSVersion::VersionTranslator.new.loadModel(OpenStudio::Path.new('{osmPath.Replace('\\', '/')}'))",
                "raise 'load failed' if model.empty?",
                "raise 'unexpected space count' unless model.get.getSpaces.size == 1",
                "puts 'CLI_SMOKE_OK'"
            });
            File.WriteAllText(rubyScriptPath, rubyScript);

            string output = RunCli(cliPath, new[] { "execute_ruby_script", rubyScriptPath }, out int exitCode);
            TestContext.Out.WriteLine(output);
            Assert.That(exitCode, Is.EqualTo(0), $"OpenStudio CLI could not open the generated OSM: {output}");
            Assert.That(output, Does.Contain("CLI_SMOKE_OK"), "CLI ruby round-trip did not report success");
        }

        private static void CreateSmokeModel(string path, string spaceName, string thermalZoneName)
        {
            global::OpenStudio.Model model = new global::OpenStudio.Model();

            global::OpenStudio.Space space = new global::OpenStudio.Space(model);
            space.setName(spaceName);

            global::OpenStudio.ThermalZone thermalZone = new global::OpenStudio.ThermalZone(model);
            thermalZone.setName(thermalZoneName);

            Assert.That(space.setThermalZone(thermalZone), Is.True, "Space could not be assigned to ThermalZone");

            bool saved = model.save(global::OpenStudio.OpenStudioUtilitiesCore.toPath(path), true);
            Assert.That(saved, Is.True, "Model.save returned false");
        }

        private static string RunCli(string cliPath, IEnumerable<string> arguments, out int exitCode)
        {
            ProcessStartInfo processStartInfo = new ProcessStartInfo
            {
                FileName = cliPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            foreach (string argument in arguments)
            {
                processStartInfo.ArgumentList.Add(argument);
            }

            using (Process process = Process.Start(processStartInfo))
            {
                string standardOutput = process.StandardOutput.ReadToEnd();
                string standardError = process.StandardError.ReadToEnd();
                bool exited = process.WaitForExit(120000);
                if (!exited)
                {
                    process.Kill();
                    exitCode = -1;
                    return "TIMEOUT: " + standardOutput + standardError;
                }

                exitCode = process.ExitCode;
                return standardOutput + standardError;
            }
        }
    }
}
