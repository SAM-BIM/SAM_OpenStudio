// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Analytical.Benchmark;
using SAM.Analytical.OpenStudio.Benchmark;
using SAM.Analytical.OpenStudio.ResultsReadBack;
using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.InputReadBack.Tests
{
    /// <summary>
    /// S11 negatives other than completeness and F3 (plan section 7): each must FAIL or BLOCK by its intended mechanism,
    /// and the zero rule, ties and design loads are checked positively as well.
    /// </summary>
    [TestFixture]
    public class S11NegativeTests
    {
        private static string Two() => S11Support.Two();

        private static SyntheticRun Synthetic() => SyntheticRun.For(Two(), "two");

        private static void AssertFails(S11Case run, params string[] rows)
        {
            Assert.That(run.Compare.Verdict, Is.EqualTo("FAIL"), run.Summary());
            foreach (string row in rows)
            {
                Assert.That(run.Row(row).Status, Is.EqualTo("FAIL"), row + ": " + run.Row(row).Reason);
            }
        }

        private static void AssertBlocks(S11Case run, string reason)
        {
            Assert.That(run.Compare.Verdict, Is.EqualTo("BLOCKED"), run.Summary());
            Assert.That(run.Compare.Failed, Is.Zero, run.Summary());
            Assert.That(string.Join(" ", run.Compare.Rows.Select(x => x.Reason)), Does.Contain(reason));
        }

        // ---- zero rule and ties ----------------------------------------------------------------------------------------

        private static SyntheticRun ZeroCoolingInSpace1()
        {
            SyntheticRun synthetic = Synthetic();
            synthetic.EditSeries(true, true, s => s.ForEach(p => p.Value = 0), k => k == SyntheticRun.Key(S11Names.TotalCooling, synthetic.Zones[1].IdealLoads) || k == SyntheticRun.Key(S11Names.SensibleCooling, synthetic.Zones[1].IdealLoads));
            return synthetic;
        }

        [Test]
        public void AllZeroCooling_B1aLoadZeroHourUnavailable_SamHourZeroAccepted_Passes()
        {
            S11Case run = S11Support.Evaluate(ZeroCoolingInSpace1(), Two(), "zero-rule");

            Assert.That(run.Compare.Verdict, Is.EqualTo("PASS"), run.Summary());
            Assert.That(run.Reference.Space(run.Synthetic.Zones[1].SpaceGuid).Quantities[S11Names.TotalCooling].AllZero, Is.True);
            Assert.That(run.Row("b1a|" + run.Space(1) + "|Cooling|peakHour").Reason, Does.Contain("zero rule"));
            Assert.That(run.Row("samP|" + run.Space(1) + "|Cooling|peakHour").Subject, Is.EqualTo(0), "SAM's own earliest-of-a-full-year-tie hour 0");
        }

        [Test]
        public void ZeroReplay_B1aPublishesHourZeroForAnAllZeroYear_Fails()
        {
            S11Case run = S11Support.Evaluate(ZeroCoolingInSpace1(), Two(), "zero-replay", afterProduce: c => S11Support.EditB1a(c, d =>
            {
                d.Spaces.Single(x => x.Guid == c.Synthetic.Zones[1].SpaceGuid.ToString("N")).Cooling.PeakHour = MetricValue.AvailableValue(0, MetricUnit.HourOfYear);
            }));

            AssertFails(run, "b1a|" + run.Space(1) + "|Cooling|peakHour");
        }

        [Test]
        public void MissingZoneReportedAsZero_Blocks()
        {
            SyntheticRun synthetic = Synthetic();
            foreach (string key in synthetic.Sql.Keys.Where(x => x.EndsWith("|" + synthetic.Zones[1].IdealLoads, StringComparison.Ordinal)).ToList())
            {
                synthetic.Sql.Remove(key);
                synthetic.Eso.Remove(key);
            }

            S11Case run = S11Support.Evaluate(synthetic, Two(), "missing-zone-zero", afterProduce: c => S11Support.EditB1a(c, d =>
            {
                BenchmarkSpaceResult space = d.Spaces.Single(x => x.Guid == c.Synthetic.Zones[1].SpaceGuid.ToString("N"));
                space.Cooling.PeakLoad = MetricValue.AvailableValue(0, MetricUnit.Watt);
            }));

            Assert.That(run.Compare.Verdict, Is.Not.EqualTo("PASS"));
            Assert.That(run.Row("b1a|" + run.Space(1) + "|Cooling|peakLoad").Status, Is.EqualTo("BLOCKED"), "a missing zone is unavailable in the reference, never 0");
        }

        [Test]
        public void Tie_EarliestHourRequired_LaterTieHourFails()
        {
            SyntheticRun synthetic = Synthetic();
            string total = SyntheticRun.Key(S11Names.TotalCooling, synthetic.Zones[0].IdealLoads);
            double peak = synthetic.Sql[total].Max(x => x.Value);
            synthetic.EditSeries(true, true, s => s.Single(p => p.Month == 9 && p.Day == 1 && p.Hour == 12).Value = peak, k => k == total);
            S11Case control = S11Support.Evaluate(synthetic, Two(), "tie-control");
            Assert.That(control.Reference.Space(synthetic.Zones[0].SpaceGuid).Quantities[S11Names.TotalCooling].TieCount, Is.EqualTo(2));
            Assert.That(control.Compare.Verdict, Is.EqualTo("PASS"), control.Summary());

            int later = control.Reference.Space(synthetic.Zones[0].SpaceGuid).Quantities[S11Names.TotalCooling].PeakHour;
            int septemberFirst = (31 + 28 + 31 + 30 + 31 + 30 + 31 + 31) * 24 + 11;
            Assert.That(later, Is.LessThan(septemberFirst), "the earliest tie is the August peak");
            S11Case run = S11Support.Evaluate(synthetic, Two(), "tie-later", mutateInputs: inputs =>
            {
                foreach (SpaceSimulationResult result in S11Support.Annual(inputs, synthetic.Zones[0].SpaceGuid, LoadKind.Cooling))
                {
                    result.SetValue(Analytical.SpaceSimulationResultParameter.LoadIndex, septemberFirst);
                }
            });

            AssertFails(run, "samP|" + run.Space(0) + "|Cooling|peakHour", "b1a|" + run.Space(0) + "|Cooling|peakHour");
        }

        // ---- value slips ---------------------------------------------------------------------------------------------

        [TestCase("hourPlusOne")]
        [TestCase("hourMinusOne")]
        [TestCase("heatingCoolingSwapped")]
        [TestCase("times1000")]
        [TestCase("dividedBy3600")]
        [TestCase("consumptionOff1e-6")]
        public void ValueSlip_Fails(string slip)
        {
            Guid space = Synthetic().Zones[0].SpaceGuid;
            S11Case run = S11Support.Evaluate(Synthetic(), Two(), "slip-" + slip, mutateInputs: inputs =>
            {
                switch (slip)
                {
                    case "hourPlusOne":
                    case "hourMinusOne":
                        foreach (SpaceSimulationResult result in S11Support.Annual(inputs, space, LoadKind.Cooling))
                        {
                            result.TryGetValue(Analytical.SpaceSimulationResultParameter.LoadIndex, out int hour);
                            result.SetValue(Analytical.SpaceSimulationResultParameter.LoadIndex, hour + (slip == "hourPlusOne" ? 1 : -1));
                        }

                        break;
                    case "heatingCoolingSwapped":
                        foreach (SpaceSimulationResult result in inputs.SpaceResults.Where(x => x.Reference == space.ToString("N")))
                        {
                            result.TryGetValue(Analytical.SpaceSimulationResultParameter.LoadType, out string loadType);
                            result.SetValue(Analytical.SpaceSimulationResultParameter.LoadType, loadType == "Heating" ? "Cooling" : "Heating");
                        }

                        break;
                    case "times1000":
                        foreach (SpaceSimulationResult result in S11Support.Annual(inputs, space, LoadKind.Cooling))
                        {
                            result.TryGetValue(Analytical.SpaceSimulationResultParameter.Load, out double load);
                            result.SetValue(Analytical.SpaceSimulationResultParameter.Load, load * 1000);
                        }

                        break;
                    case "dividedBy3600":
                        inputs.ModelResult.TryGetValue(AnalyticalModelSimulationResultParameter.ConsumptionCooling, out double kwh);
                        inputs.ModelResult.SetValue(AnalyticalModelSimulationResultParameter.ConsumptionCooling, kwh / 3600);
                        break;
                    case "consumptionOff1e-6":
                        inputs.ModelResult.TryGetValue(AnalyticalModelSimulationResultParameter.ConsumptionCooling, out double value);
                        inputs.ModelResult.SetValue(AnalyticalModelSimulationResultParameter.ConsumptionCooling, value * (1 + 1e-6));
                        break;
                }
            });

            string target = slip.StartsWith("hour", StringComparison.Ordinal) ? "b1a|" + run.Space(0) + "|Cooling|peakHour"
                : slip == "times1000" || slip == "heatingCoolingSwapped" ? "b1a|" + run.Space(0) + "|Cooling|peakLoad"
                : "b1a|model|Cooling|consumption";
            AssertFails(run, target, target.Replace("b1a|", "samP|"));
        }

        // ---- channels, keys, identity ----------------------------------------------------------------------------------

        [Test]
        public void SqlAndEsoDisagreeInOneHour_Blocks()
        {
            SyntheticRun synthetic = Synthetic();
            synthetic.EditSeries(false, true, s => s.Single(p => p.Month == 8 && p.Day == 1 && p.Hour == 12).Value += 1, k => k == SyntheticRun.Key(S11Names.TotalCooling, synthetic.Zones[0].IdealLoads));
            AssertBlocks(S11Support.Evaluate(synthetic, Two(), "c4-disagree"), "S11-C4");
        }

        [Test]
        public void UnknownReportKey_Blocks()
        {
            SyntheticRun synthetic = Synthetic();
            string ghost = SyntheticRun.Key(S11Names.TotalCooling, "SAM_IDEALLOADS_GHOST_0badf00d");
            synthetic.Sql[ghost] = synthetic.Sql[SyntheticRun.Key(S11Names.TotalCooling, synthetic.Zones[0].IdealLoads)].Select(x => x.Copy()).ToList();
            synthetic.Eso[ghost] = synthetic.Sql[ghost].Select(x => x.Copy()).ToList();
            AssertBlocks(S11Support.Evaluate(synthetic, Two(), "c5-unknown-key"), "S11-C5");
        }

        [Test]
        public void NativeKeyIsNotTheProductionKey_Fails()
        {
            S11Case run = S11Support.Evaluate(Synthetic(), Two(), "key-mismatch", beforeCompare: c => c.Subject.ProductionKeys[0].IdealLoads = "SAM_IdealLoads_Other_00000000");
            AssertFails(run, "identity|space:" + run.Subject.ProductionKeys[0].SpaceGuid.ToString("N") + "|-|idealLoadsKey");
        }

        [Test]
        public void ZoneWithTwoIdealLoadsSystems_Blocks()
        {
            SyntheticRun synthetic = Synthetic();
            int at =synthetic.Idf.IndexOf("ZoneHVAC:EquipmentList,", StringComparison.Ordinal);
            Assert.That(at, Is.GreaterThanOrEqualTo(0));
            int end = synthetic.Idf.IndexOf(';', at);
            string block = synthetic.Idf.Substring(at, end - at);
            synthetic.Idf = synthetic.Idf.Substring(0, at) + block + ",\n  ZoneHVAC:IdealLoadsAirSystem, !- Zone Equipment Object Type 2\n  " + synthetic.Zones[1].IdealLoads + ", !- Zone Equipment Name 2\n  2, !- Zone Equipment Cooling Sequence 2\n  2" + synthetic.Idf.Substring(end);

            S11Case run = S11Support.Evaluate(synthetic, Two(), "two-ideal-loads");
            Assert.That(run.Compare.Verdict, Is.Not.EqualTo("PASS"), run.Summary());
            Assert.That(run.Reference.Spaces.Any(x => x.IdentityProblem != null && (x.IdentityProblem.Contains("Ideal Loads") || x.IdentityProblem.Contains("bijective"))), Is.True, string.Join("; ", run.Reference.Spaces.Select(x => x.IdentityProblem)));
        }

        [Test]
        public void NonOpenStudioSource_Fails()
        {
            S11Case run = S11Support.Evaluate(Synthetic(), Two(), "source", beforeCompare: c => c.Subject.SamI.First(x => x.IsAnnual).Source = "SAM.Analytical.Tas");
            Assert.That(run.Compare.Verdict, Is.EqualTo("FAIL"), run.Summary());
            Assert.That(run.Compare.Rows.Any(x => x.Layer == "samI" && x.Quantity == "result" && x.Status == "FAIL" && x.Reason.Contains("source")), Is.True);
        }

        [Test]
        public void MissingResult_Fails()
        {
            Guid space = Synthetic().Zones[1].SpaceGuid;
            S11Case run = S11Support.Evaluate(Synthetic(), Two(), "missing-result", mutateInputs: inputs => inputs.SpaceResults.RemoveAll(x => x.Reference == space.ToString("N") && x.TryGetValue(Analytical.SpaceSimulationResultParameter.LoadType, out string t) && t == "Heating"));
            AssertFails(run, "samP|" + run.Space(1) + "|Heating|result", "b1a|" + run.Space(1) + "|Heating|peakLoad");
        }

        [Test]
        public void CapturedObjectsAlteredAfterTheB1a_ClosureFails_AndSamPRowsBlock()
        {
            S11Case run = S11Support.Evaluate(Synthetic(), Two(), "closure", afterProduce: c =>
            {
                OpenStudioBenchmarkInputs inputs = OpenStudioBenchmarkInputs.Read(Path.Combine(c.Run, RunLayout.SamResultsFile));
                inputs.SpaceResults[0].TryGetValue(Analytical.SpaceSimulationResultParameter.Load, out double load);
                inputs.SpaceResults[0].SetValue(Analytical.SpaceSimulationResultParameter.Load, load + 1);
                inputs.Write(Path.Combine(c.Run, RunLayout.SamResultsFile));
            });

            Assert.That(run.Subject.ClosureIdentical, Is.False, run.Subject.ClosureDetail);
            AssertFails(run, "closure|model|-|samPToB1a");
            Assert.That(run.Compare.Rows.Where(x => x.Layer == "samP").All(x => x.Status == "BLOCKED"), Is.True, "every SAM-P row blocks without the closure");
        }

        // ---- sizing, design load, versions, meters, basis -----------------------------------------------------------

        [Test]
        public void ZoneSizesWithDesignDaySourceNone_Blocks()
        {
            SyntheticRun synthetic = Synthetic();
            synthetic.ZoneSizes.Add(new ZoneSizeRow { ZoneName = synthetic.Zones[0].Zone, LoadType = "Heating", CalcDesLoad = 1234.5 });
            AssertBlocks(S11Support.Evaluate(synthetic, Two(), "c8-sizes-none"), "S11-C8");
        }

        [Test]
        public void DesignLoadMappedFromCalcDesLoad_WhenSizingRan_Passes()
        {
            SyntheticRun synthetic = Synthetic();
            foreach (SyntheticZone zone in synthetic.Zones)
            {
                synthetic.ZoneSizes.Add(new ZoneSizeRow { ZoneName = zone.Zone, LoadType = "Heating", CalcDesLoad = 2345.25 });
                synthetic.ZoneSizes.Add(new ZoneSizeRow { ZoneName = zone.Zone, LoadType = "Cooling", CalcDesLoad = 1875.125 });
            }

            S11Case run = S11Support.Evaluate(synthetic, Two(), "design-load", DesignDaySource.EmbeddedModel);
            Assert.That(run.Compare.Verdict, Is.EqualTo("PASS"), run.Summary());
            Assert.That(run.Row("b1a|" + run.Space(0) + "|Heating|designLoad").Reference, Is.EqualTo(2345.25));
            Assert.That(run.Row("samP|" + run.Space(1) + "|Cooling|designLoad").Status, Is.EqualTo("PASS"));
            Assert.That(run.Row("samI|" + run.Space(1) + "|Cooling|designLoad").Status, Is.EqualTo("INFO"), "the legacy SAM-I design-day path is informational");
        }

        [Test]
        public void DesignLoadPublishedAsZeroWithoutSizing_Fails()
        {
            S11Case run = S11Support.Evaluate(Synthetic(), Two(), "design-zero", afterProduce: c => S11Support.EditB1a(c, d => d.Spaces[0].Heating.DesignLoad = MetricValue.AvailableValue(0, MetricUnit.Watt)));
            Assert.That(run.Compare.Verdict, Is.EqualTo("FAIL"));
            Assert.That(run.Compare.Rows.Any(x => x.Layer == "b1a" && x.Quantity == "designLoad" && x.Status == "FAIL"), Is.True);
        }

        [Test]
        public void WrongEnergyPlusVersion_Blocks()
        {
            SyntheticRun synthetic = Synthetic();
            synthetic.SqlVersion = "EnergyPlus, Version 24.2.0-94a887817b, YMD=2026.10.24 08:00";
            AssertBlocks(S11Support.Evaluate(synthetic, Two(), "c9-version"), "S11-C9");
        }

        [Test]
        public void FacilityMeterDisagreesWithTheZones_BlocksConsumption()
        {
            SyntheticRun synthetic = Synthetic();
            synthetic.SqlEdit = connection =>
            {
                using (SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "UPDATE ReportData SET Value = Value + 1000 WHERE ReportDataIndex = (SELECT MIN(rd.ReportDataIndex) FROM ReportData rd JOIN ReportDataDictionary d ON d.ReportDataDictionaryIndex = rd.ReportDataDictionaryIndex WHERE d.Name = @n)";
                    command.Parameters.AddWithValue("@n", S11Names.CoolingMeter);
                    command.ExecuteNonQuery();
                }
            };

            S11Case run = S11Support.Evaluate(synthetic, Two(), "c7-meter");
            Assert.That(run.Row("b1a|model|Cooling|consumption").Status, Is.EqualTo("BLOCKED"));
            Assert.That(run.Row("b1a|model|Cooling|consumption").Reason, Does.Contain("S11-C7"));
            Assert.That(run.Row("b1a|model|Cooling|peakLoad").Status, Is.EqualTo("PASS"), "the meter only qualifies the consumption");
        }

        [Test]
        public void NegativeLatent_BlocksThatLoadType()
        {
            SyntheticRun synthetic = Synthetic();
            synthetic.EditSeries(true, true, s => s.Single(p => p.Month == 8 && p.Day == 2 && p.Hour == 12).Value += 5_000_000, k => k == SyntheticRun.Key(S11Names.SensibleCooling, synthetic.Zones[0].IdealLoads));
            S11Case run = S11Support.Evaluate(synthetic, Two(), "c6-negative-latent");

            Assert.That(run.Row("b1a|" + run.Space(0) + "|Cooling|peakLoad").Status, Is.EqualTo("BLOCKED"));
            Assert.That(run.Row("b1a|" + run.Space(0) + "|Cooling|peakLoad").Reason, Does.Contain("S11-C6"));
            Assert.That(run.Row("b1a|" + run.Space(0) + "|Heating|peakLoad").Status, Is.EqualTo("PASS"));
        }

        [Test]
        public void WrongUnit_Blocks()
        {
            SyntheticRun synthetic = Synthetic();
            synthetic.SqlEdit = connection =>
            {
                using (SQLiteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "UPDATE ReportDataDictionary SET Units = 'kWh' WHERE Name = @n";
                    command.Parameters.AddWithValue("@n", S11Names.TotalHeating);
                    command.ExecuteNonQuery();
                }
            };

            AssertBlocks(S11Support.Evaluate(synthetic, Two(), "c3-unit"), "S11-C3");
        }
    }
}
