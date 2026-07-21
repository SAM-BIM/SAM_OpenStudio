// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// C8: SQLite results extraction and its deployment contract (SAM-OS-RUN-001).
    ///
    /// Regression cover for a live failure where every Grasshopper run reported "Ideal Loads
    /// results could not be extracted" even though EnergyPlus had completed successfully and
    /// eplusout.sql contained 8760 rows per Ideal Loads variable. The cause was purely
    /// deployment: this is a class library, so the SDK does not copy PackageReference runtime
    /// assets to the output folder, and neither System.Data.SQLite.dll nor SQLite.Interop.dll
    /// ever reached %APPDATA%\SAM. The first touch of a SQLite type threw, and a blanket catch
    /// turned that into a message that read like missing simulation data.
    ///
    /// Note that the extraction tests here pass even on a broken deployment: the test host
    /// resolves NuGet assets through its own .deps.json, so they cannot see the fault that
    /// Rhino sees. <see cref="BuildOutput_ContainsSQLiteAssets_InLoadableLayout"/> is the test
    /// that actually guards the shipped layout.
    /// </summary>
    [TestFixture]
    public class SQLiteDeploymentTests
    {
        private static string RepositoryRoot()
        {
            // <repo>\tests\SAM.Analytical.OpenStudio.Tests\bin\<platform>\<config>\<tfm>
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", ".."));
        }

        private static string CreateSql(string name, IEnumerable<KeyValuePair<string, double>> rows, string variableName)
        {
            string result = Path.Combine(TestContext.CurrentContext.WorkDirectory, name);
            if (File.Exists(result))
            {
                File.Delete(result);
            }

            using (System.Data.SQLite.SQLiteConnection connection = new System.Data.SQLite.SQLiteConnection("Data Source=" + result))
            {
                connection.Open();
                using (System.Data.SQLite.SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "CREATE TABLE ReportDataDictionary (ReportDataDictionaryIndex INTEGER PRIMARY KEY, Name TEXT, KeyValue TEXT)";
                    command.ExecuteNonQuery();
                    command.CommandText = "CREATE TABLE ReportData (ReportDataIndex INTEGER PRIMARY KEY, ReportDataDictionaryIndex INTEGER, Value REAL)";
                    command.ExecuteNonQuery();

                    int index = 0;
                    foreach (KeyValuePair<string, double> row in rows)
                    {
                        index++;
                        command.Parameters.Clear();
                        command.CommandText = "INSERT INTO ReportDataDictionary VALUES (" + index + ", @name, @key)";
                        command.Parameters.AddWithValue("@name", variableName);
                        command.Parameters.AddWithValue("@key", row.Key);
                        command.ExecuteNonQuery();

                        command.Parameters.Clear();
                        command.CommandText = "INSERT INTO ReportData VALUES (" + index + ", " + index + ", @value)";
                        command.Parameters.AddWithValue("@value", row.Value);
                        command.ExecuteNonQuery();
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// The deployment contract. Both SQLite assets must be in the build output, and the
        /// native one must keep its runtimes\win-x64\native subdirectory: that is where both
        /// System.Data.SQLite's own loader and SAM's AssemblyResolver look for it. A flat
        /// managed DLL with no interop is exactly the state that produced SAM-OS-RUN-001.
        /// </summary>
        [Test]
        public void BuildOutput_ContainsSQLiteAssets_InLoadableLayout()
        {
            string buildDirectory = Path.Combine(RepositoryRoot(), "build");
            Assert.That(Directory.Exists(buildDirectory), Is.True, $"Build output directory not found: {buildDirectory}");

            string managedPath = Path.Combine(buildDirectory, "System.Data.SQLite.dll");
            Assert.That(File.Exists(managedPath), Is.True, $"System.Data.SQLite.dll must be deployed beside the plugin, else extraction throws FileNotFoundException in Rhino: {managedPath}");

            string nativePath = Path.Combine(buildDirectory, "runtimes", "win-x64", "native", "SQLite.Interop.dll");
            Assert.That(File.Exists(nativePath), Is.True, $"The win-x64 SQLite.Interop.dll must be deployed in its runtimes layout, else extraction throws DllNotFoundException in Rhino: {nativePath}");

            Assert.That(new FileInfo(managedPath).Length, Is.GreaterThan(0));
            Assert.That(new FileInfo(nativePath).Length, Is.GreaterThan(0));

            // A flat or x86 interop beside the managed assembly would be probed ahead of the
            // x64 one and silently break a 64-bit Rhino.
            Assert.That(File.Exists(Path.Combine(buildDirectory, "SQLite.Interop.dll")), Is.False, "A flat SQLite.Interop.dll must not shadow the win-x64 asset");
            Assert.That(Directory.Exists(Path.Combine(buildDirectory, "runtimes", "win-x86", "native")), Is.False, "The win-x86 interop must not be deployed into a 64-bit-only plugin");
        }

        [Test]
        public void ExtractLoads_KnownSqlFile_ReturnsSummary()
        {
            // Same file backs both variables, so heating and cooling both resolve.
            string sqlPath = CreateSql("c8_known.sql", new[] { new KeyValuePair<string, double>("SAM_IDEALLOADS_CELL_1", 3600000.0) }, "Zone Ideal Loads Supply Air Total Heating Energy");

            OpenStudioLoadSummary loadSummary = OpenStudioSimulationRunner.ExtractLoads(sqlPath, out string failureDetail);

            Assert.That(failureDetail, Is.Null);
            Assert.That(loadSummary, Is.Not.Null);
            Assert.That(loadSummary.TotalHeating, Is.EqualTo(1.0).Within(1e-9), "3 600 000 J = 1 kWh");
            Assert.That(loadSummary.ZoneHeating.Count, Is.EqualTo(1));
            Assert.That(loadSummary.ZoneCooling.Count, Is.EqualTo(0), "No cooling variable in this file — absent, never zero-filled");
            Assert.That(loadSummary.IsFinite, Is.True);
        }

        [Test]
        public void ExtractLoads_MissingSqlFile_ReturnsNull_WithPathInDetail()
        {
            string sqlPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "c8_does_not_exist.sql");
            if (File.Exists(sqlPath))
            {
                File.Delete(sqlPath);
            }

            OpenStudioLoadSummary loadSummary = OpenStudioSimulationRunner.ExtractLoads(sqlPath, out string failureDetail);

            Assert.That(loadSummary, Is.Null);
            Assert.That(failureDetail, Is.Not.Null.And.Contains(sqlPath), "The detail must name the file that could not be read");
            Assert.That(failureDetail, Does.Contain("exists: False"));
        }

        [Test]
        public void ExtractLoads_NoIdealLoadsVariables_ReturnsEmptySummary_NotNull()
        {
            // A readable file with an unrelated variable: zero conditioned zones is a legitimate
            // model, so this must not be reported as an extraction failure.
            string sqlPath = CreateSql("c8_no_ideal_loads.sql", new[] { new KeyValuePair<string, double>("ZONE A", 3600000.0) }, "Zone Mean Air Temperature");

            OpenStudioLoadSummary loadSummary = OpenStudioSimulationRunner.ExtractLoads(sqlPath, out string failureDetail);

            Assert.That(failureDetail, Is.Null);
            Assert.That(loadSummary, Is.Not.Null, "Missing variables are not a read failure");
            Assert.That(loadSummary.ZoneHeating.Count, Is.EqualTo(0));
            Assert.That(loadSummary.ZoneCooling.Count, Is.EqualTo(0));
            Assert.That(loadSummary.TotalHeating, Is.EqualTo(0));
        }

        [Test]
        public void ExtractLoads_CorruptSqlFile_ReturnsNull_WithExceptionDetail()
        {
            string sqlPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "c8_corrupt.sql");
            File.WriteAllText(sqlPath, "this is not a SQLite database");

            OpenStudioLoadSummary loadSummary = OpenStudioSimulationRunner.ExtractLoads(sqlPath, out string failureDetail);

            Assert.That(loadSummary, Is.Null);
            Assert.That(failureDetail, Is.Not.Null);
            Assert.That(failureDetail, Does.Contain("Exception"), "The exception type must survive into the diagnostic");
            Assert.That(failureDetail, Does.Contain(sqlPath));
        }

        /// <summary>
        /// The detail attached to SAM-OS-RUN-001 must distinguish the deployment faults from
        /// each other and from a real query failure — that is the whole point of the change.
        /// </summary>
        [Test]
        public void DescribeExtractionFailure_DistinguishesDeploymentFaults()
        {
            string sqlPath = @"C:\runs\eplusout.sql";

            string missingManaged = OpenStudioSimulationRunner.DescribeExtractionFailure(
                new FileNotFoundException("Could not load file or assembly 'System.Data.SQLite'."), sqlPath);
            Assert.That(missingManaged, Does.Contain("System.IO.FileNotFoundException"));
            Assert.That(missingManaged, Does.Contain("System.Data.SQLite"));

            string missingNative = OpenStudioSimulationRunner.DescribeExtractionFailure(
                new DllNotFoundException("Unable to load DLL 'SQLite.Interop.dll'."), sqlPath);
            Assert.That(missingNative, Does.Contain("System.DllNotFoundException"));
            Assert.That(missingNative, Does.Contain("SQLite.Interop.dll"));

            string wrongArchitecture = OpenStudioSimulationRunner.DescribeExtractionFailure(
                new BadImageFormatException("is not a valid Win32 application."), sqlPath);
            Assert.That(wrongArchitecture, Does.Contain("System.BadImageFormatException"));

            // Every variant carries the environment facts needed to act on it remotely.
            foreach (string detail in new[] { missingManaged, missingNative, wrongArchitecture })
            {
                Assert.That(detail, Does.Contain(sqlPath));
                Assert.That(detail, Does.Contain("System.Data.SQLite:"), "Must report where (or whether) the provider loaded");
                Assert.That(detail, Does.Contain("plugin:"));
                Assert.That(detail, Does.Contain("base:"));
                Assert.That(detail, Does.Contain(IntPtr.Size * 8 + "-bit"));
            }
        }

        [Test]
        public void DescribeExtractionFailure_PreservesInnerExceptionChain()
        {
            // A provider initialization failure arrives as TypeInitializationException wrapping
            // the real cause; the inner chain is the only thing that identifies it.
            Exception exception = new TypeInitializationException(
                "System.Data.SQLite.UnsafeNativeMethods",
                new DllNotFoundException("Unable to load DLL 'SQLite.Interop.dll'."));

            string detail = OpenStudioSimulationRunner.DescribeExtractionFailure(exception, @"C:\runs\eplusout.sql");

            Assert.That(detail, Does.Contain("System.TypeInitializationException"));
            Assert.That(detail, Does.Contain("->"), "The inner chain must be rendered, not dropped");
            Assert.That(detail, Does.Contain("System.DllNotFoundException"));
            Assert.That(detail, Does.Contain("SQLite.Interop.dll"));
        }
    }
}
