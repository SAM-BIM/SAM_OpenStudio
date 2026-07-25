// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using NUnit.Framework;
using SAM.Analytical.Benchmark;

namespace SAM.Analytical.OpenStudio.Benchmark.Tests
{
    /// <summary>
    /// <c>provenance.designDaySource</c> is derived, never fixed. A hardcoded <see
    /// cref="DesignDaySource.None"/> made every cross-engine pair on a model carrying design days
    /// look provenance-incompatible to the B3 comparator (the TAS producer derives
    /// <see cref="DesignDaySource.EmbeddedModel"/> from the same model), so the field is asserted
    /// on both layers the producer uses: the model-derived pre-run value (mirroring the TAS
    /// derivation) and the mapping of the design-day basis the OpenStudio route reports. Fully
    /// offline — no OpenStudio/EnergyPlus install involved.
    /// </summary>
    [TestFixture]
    public class DesignDaySourceTests
    {
        private static DesignDay Day(string name, byte month, byte day)
        {
            return new DesignDay(name, 2026, month, day);
        }

        [Test]
        public void ModelWithEmbeddedDesignDays_DerivesEmbeddedModel_NotNone()
        {
            AnalyticalModel analyticalModel = BenchmarkFixture.SingleSpaceModel();
            Analytical.Modify.UpdateWeather(
                analyticalModel,
                null,
                new List<DesignDay> { Day("Cooling Design Day", 7, 21) },
                new List<DesignDay> { Day("Heating Design Day", 1, 21) });

            DesignDaySource designDaySource = Program.ResolveDesignDaySource(analyticalModel);

            Assert.That(designDaySource, Is.EqualTo(DesignDaySource.EmbeddedModel), "A model carrying heating/cooling design days must not report None — the B3 comparator compares this field across engines");
        }

        [Test]
        public void ModelWithHeatingDesignDaysOnly_DerivesEmbeddedModel()
        {
            AnalyticalModel analyticalModel = BenchmarkFixture.SingleSpaceModel();
            Analytical.Modify.UpdateWeather(analyticalModel, null, null, new List<DesignDay> { Day("Heating Design Day", 1, 21) });

            Assert.That(Program.ResolveDesignDaySource(analyticalModel), Is.EqualTo(DesignDaySource.EmbeddedModel), "One side is enough for an embedded basis");
        }

        [Test]
        public void ModelWithCoolingDesignDaysOnly_DerivesEmbeddedModel()
        {
            AnalyticalModel analyticalModel = BenchmarkFixture.SingleSpaceModel();
            Analytical.Modify.UpdateWeather(analyticalModel, null, new List<DesignDay> { Day("Cooling Design Day", 7, 21) }, null);

            Assert.That(Program.ResolveDesignDaySource(analyticalModel), Is.EqualTo(DesignDaySource.EmbeddedModel), "One side is enough for an embedded basis");
        }

        [Test]
        public void ModelWithoutDesignDays_DerivesNone()
        {
            Assert.That(Program.ResolveDesignDaySource(BenchmarkFixture.SingleSpaceModel()), Is.EqualTo(DesignDaySource.None), "No design days in the model → None");
        }

        [Test]
        public void EmptyDesignDayCollection_DerivesNone()
        {
            AnalyticalModel analyticalModel = BenchmarkFixture.SingleSpaceModel();
            Analytical.Modify.UpdateWeather(analyticalModel, null, new List<DesignDay>(), new List<DesignDay>());

            Assert.That(Program.ResolveDesignDaySource(analyticalModel), Is.EqualTo(DesignDaySource.None), "An empty collection is not a design-day basis");
        }

        [Test]
        public void NullModel_DerivesNone()
        {
            Assert.That(Program.ResolveDesignDaySource(null), Is.EqualTo(DesignDaySource.None));
        }

        [TestCase(Core.OpenStudio.OpenStudioDesignDaySource.Ddy, DesignDaySource.Ddy)]
        [TestCase(Core.OpenStudio.OpenStudioDesignDaySource.EmbeddedModel, DesignDaySource.EmbeddedModel)]
        [TestCase(Core.OpenStudio.OpenStudioDesignDaySource.None, DesignDaySource.None)]
        public void RouteDesignDayBasis_MapsOntoProvenanceEnum(Core.OpenStudio.OpenStudioDesignDaySource routeBasis, DesignDaySource expected)
        {
            Assert.That(Program.ToBenchmarkDesignDaySource(routeBasis), Is.EqualTo(expected));
        }

        [Test]
        public void ProvenanceEnum_NeverReportsUnknown()
        {
            // Unknown is the schema's "unrecognised token" value; the validator rejects a document
            // carrying it, so no mapped route basis may produce it.
            foreach (Core.OpenStudio.OpenStudioDesignDaySource routeBasis in System.Enum.GetValues(typeof(Core.OpenStudio.OpenStudioDesignDaySource)))
            {
                Assert.That(Program.ToBenchmarkDesignDaySource(routeBasis), Is.Not.EqualTo(DesignDaySource.Unknown), routeBasis.ToString());
            }
        }
    }
}
