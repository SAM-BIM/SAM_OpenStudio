// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Analytical.OpenStudio.Benchmark;
using SAM.Analytical.OpenStudio.ResultsReadBack;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OpenStudio.InputReadBack.Tests
{
    /// <summary>
    /// F3 (revision-2 change 2), the mandatory offline proof: Sensible substituted for Total is detected. The synthetic
    /// two-zone data cool for 1530 hours with a latent part of at least 10 % of Total, and the Sensible peak is at another
    /// hour. A precondition asserts that every targeted row discriminates Total from Sensible by at least 10^6 x its
    /// tolerance (hours: a different hour), so the test cannot silently stop discriminating. Each substitution, made in the
    /// producer objects (the B1a is mapped from them, so the closure holds) and in the import, must FAIL, never BLOCK.
    /// </summary>
    [TestFixture]
    public class S11F3Tests
    {
        public enum Substitution
        {
            Consumption,
            SpacePeak,
            ModelPeak,
            All,
        }

        private static string Model() => S11Support.TwoDistinctPrefix();

        /// <summary>The rows each substitution targets (load type filled in by the test).</summary>
        private static IEnumerable<string> Targets(S11Case run, Substitution substitution, LoadKind kind)
        {
            List<string> result = new List<string>();
            if (substitution == Substitution.Consumption || substitution == Substitution.All)
            {
                result.Add("samP|model|" + kind + "|consumption");
                result.Add("b1a|model|" + kind + "|consumption");
            }

            if (substitution == Substitution.ModelPeak || substitution == Substitution.All)
            {
                foreach (string layer in new[] { "samP", "b1a" })
                {
                    result.Add(layer + "|model|" + kind + "|peakLoad");
                    result.Add(layer + "|model|" + kind + "|peakHour");
                }
            }

            if (substitution == Substitution.SpacePeak || substitution == Substitution.All)
            {
                for (int i = 0; i < run.Synthetic.Zones.Count; i++)
                {
                    foreach (string layer in new[] { "samP", "samI", "b1a" })
                    {
                        result.Add(layer + "|" + run.Space(i) + "|" + kind + "|peakLoad");
                        result.Add(layer + "|" + run.Space(i) + "|" + kind + "|peakHour");
                    }
                }
            }

            return result;
        }

        [Test]
        public void Precondition_EveryTargetedRowDiscriminatesTotalFromSensibleByAMillionTolerances([Values] LoadKind kind)
        {
            S11Case run = S11Support.Evaluate(SyntheticRun.For(Model(), "two-distinct"), Model(), "f3-precondition-" + kind);
            Assert.That(run.Compare.Verdict, Is.EqualTo("PASS"), run.Summary());

            foreach (string id in Targets(run, Substitution.All, kind))
            {
                ResultRow row = run.Row(id);
                if (id.EndsWith("peakHour", StringComparison.Ordinal))
                {
                    Assert.That(row.SensibleHourDiffers, Is.True, id + ": the Sensible peak must be at another hour (invalid test data otherwise)");
                }
                else
                {
                    Assert.That(row.Margin, Is.GreaterThanOrEqualTo(1e6), id + ": invalid test data, Total and Sensible are not 10^6 tolerances apart");
                }
            }

            int hours = run.Synthetic.Sql[SyntheticRun.Key(S11Names.Total(kind), run.Synthetic.Zones[0].IdealLoads)].Count(x => x.Value > 0);
            Assert.That(hours, Is.GreaterThanOrEqualTo(1000), "at least 1000 hours with load");
            foreach (SpaceReference space in run.Reference.Spaces)
            {
                double latent = space.LatentJoules[kind.ToString()];
                double total = space.Quantities[S11Names.Total(kind)].Sum;
                Assert.That(latent / total, Is.GreaterThanOrEqualTo(0.10), "latent at least 10 % of Total");
            }
        }

        [Test]
        public void SensibleSubstitutedForTotal_Fails([Values] Substitution substitution, [Values] LoadKind kind)
        {
            string model = Model();
            SyntheticRun synthetic = SyntheticRun.For(model, "two-distinct");
            S11Case reference = S11Support.Evaluate(synthetic, model, "f3-reference");
            Assert.That(reference.Compare.Verdict, Is.EqualTo("PASS"), "unmutated control: " + reference.Summary());
            ModelLoadReference sensible = reference.Reference.ModelOf(kind);

            void Mutate(OpenStudioBenchmarkInputs inputs)
            {
                bool heating = kind == LoadKind.Heating;
                if (substitution == Substitution.Consumption || substitution == Substitution.All)
                {
                    inputs.ModelResult.SetValue(heating ? AnalyticalModelSimulationResultParameter.ConsumptionHeating : AnalyticalModelSimulationResultParameter.ConsumptionCooling, sensible.Sensible.ConsumptionKwh);
                }

                if (substitution == Substitution.ModelPeak || substitution == Substitution.All)
                {
                    inputs.ModelResult.SetValue(heating ? AnalyticalModelSimulationResultParameter.PeakHeatingLoad : AnalyticalModelSimulationResultParameter.PeakCoolingLoad, sensible.Sensible.CoincidentPeakKw);
                    inputs.ModelResult.SetValue(heating ? AnalyticalModelSimulationResultParameter.PeakHeatingHour : AnalyticalModelSimulationResultParameter.PeakCoolingHour, sensible.Sensible.PeakHour);
                }

                if (substitution == Substitution.SpacePeak || substitution == Substitution.All)
                {
                    foreach (SyntheticZone zone in synthetic.Zones)
                    {
                        QuantityReference space = S11Support.Sensible(reference.Reference, zone.SpaceGuid, kind);
                        foreach (SpaceSimulationResult result in S11Support.Annual(inputs, zone.SpaceGuid, kind))
                        {
                            result.SetValue(Analytical.SpaceSimulationResultParameter.Load, space.PeakWatts);
                            result.SetValue(Analytical.SpaceSimulationResultParameter.LoadIndex, space.PeakHour);
                        }
                    }
                }
            }

            S11Case run = S11Support.Evaluate(synthetic, model, "f3-" + substitution + "-" + kind, mutateInputs: Mutate, beforeCompare: c =>
            {
                if (substitution == Substitution.SpacePeak || substitution == Substitution.All)
                {
                    foreach (SubjectResult result in c.Subject.SamI.Where(x => x.IsAnnual && x.LoadType == kind.ToString()))
                    {
                        QuantityReference space = S11Support.Sensible(c.Reference, Guid.ParseExact(result.Reference, "N"), kind);
                        result.Load = space.PeakWatts;
                        result.LoadIndex = space.PeakHour;
                    }
                }
            });

            Assert.That(run.Subject.ClosureIdentical, Is.True, "the B1a is mapped from the substituted objects: the closure holds");
            Assert.That(run.Compare.Verdict, Is.EqualTo("FAIL"), run.Summary());
            Assert.That(run.Compare.Blocked, Is.Zero, "a substitution must FAIL, never BLOCK: " + run.Summary());
            foreach (string id in Targets(run, substitution, kind))
            {
                Assert.That(run.Row(id).Status, Is.EqualTo("FAIL"), id);
            }

            Assert.That(run.Row("b1a|model|-|totalBasisNote").Status, Is.EqualTo("PASS"), "the note still says Total while the values are Sensible: the value rows catch it");
            LoadKind other = kind == LoadKind.Heating ? LoadKind.Cooling : LoadKind.Heating;
            Assert.That(run.Row("b1a|model|" + other + "|consumption").Status, Is.EqualTo("PASS"), "the other load type is untouched");
        }

        [Test]
        public void MissingTotalBasisNote_Fails()
        {
            string model = Model();
            S11Case run = S11Support.Evaluate(SyntheticRun.For(model, "two-distinct"), model, "f3-no-note", afterProduce: c => S11Support.EditB1a(c, d => d.Provenance.Notes.Remove(S11Names.TotalBasisNote)));

            Assert.That(run.Row("b1a|model|-|totalBasisNote").Status, Is.EqualTo("FAIL"));
            Assert.That(run.Compare.Verdict, Is.EqualTo("FAIL"));
        }
    }
}
