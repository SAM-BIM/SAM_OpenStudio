// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NUnit.Framework;
using SAM.Analytical.OpenStudio.ResultsReadBack;
using SAM.Analytical.Tas.DirectT3D.Validation.ReadBack;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OpenStudio.InputReadBack.Tests
{
    /// <summary>
    /// The S11 identity is the S09 native identity (geometry bijection + IDF equipment chain). Owner direction at approval:
    /// duplicate room names are handled through geometry and identity and are never rejected only because the names match.
    /// </summary>
    [TestFixture]
    public class S11IdentityTests
    {
        private const string SpaceAName = "\"Name\": \"FixA\"";
        private const string SpaceBName = "\"Name\": \"FixB\"";

        [Test]
        public void DuplicateSpaceNames_DistinctGeometry_ResolvedByGeometryAndPass()
        {
            // Same display name: space B gets a distinct GUID prefix, because the same name AND the same prefix give
            // identical generated names (the separately tracked generated-name identity residual, next test).
            string model = S11Support.Variant(TestSupport.Two, "two-same-name-distinct-prefix", (S11Support.SpaceBGuid, "6181d111-0000-0000-0000-00000000c0b1"), (SpaceBName, SpaceAName));
            AnalyticalModel loaded = Core.Convert.ToSAM<AnalyticalModel>(model).Single();
            List<Space> spaces = loaded.AdjacencyCluster.GetSpaces();
            Assert.That(spaces.Select(x => x.Name).Distinct().Count(), Is.EqualTo(1), "both spaces carry the same name");

            S11Case run = S11Support.Evaluate(SyntheticRun.For(model, "two-same-name"), model, "same-name");

            Assert.That(run.Compare.Verdict, Is.EqualTo("PASS"), run.Summary());
            Assert.That(run.Reference.Spaces.Select(x => x.NativeSpace).Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(2), "two native spaces, told apart by geometry");
            Assert.That(run.Reference.Spaces.All(x => x.IdentityProblem == null && x.SurfacePairs > 0), Is.True);
            Assert.That(run.Compare.Rows.Where(x => x.Layer == "identity" && x.Quantity == "native").All(x => x.Status == "PASS"), Is.True);
        }

        /// <summary>
        /// Same names AND the shared GUID prefix of the signed-off fixture: the writer's names collide, so OpenStudio makes
        /// one unique. The reference still identifies both spaces by geometry; the production key cross-check then fails
        /// for the renamed one, which is a production naming collision, not a rejection of duplicate names. This is the
        /// separately tracked generated-name identity residual (exact-name lookup for identical generated names); F-S11-1
        /// (#35) deliberately leaves such keys unmapped rather than guessing.
        /// </summary>
        [Test]
        public void DuplicateSpaceNames_SharedGuidPrefix_IdentityStillByGeometry_ProductionKeyCollisionReported()
        {
            string model = S11Support.Variant(TestSupport.Two, "two-same-name-shared-prefix", (SpaceBName, SpaceAName));
            S11Case run = S11Support.Evaluate(SyntheticRun.For(model, "two-same-name-shared"), model, "same-name-shared");

            Assert.That(run.Reference.Spaces.All(x => x.IdentityProblem == null), Is.True, string.Join("; ", run.Reference.Spaces.Select(x => x.IdentityProblem)));
            Assert.That(run.Reference.Spaces.Select(x => x.IdealLoads).Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(2), "the native Ideal Loads are distinct");
            List<ResultRow> keyRows = run.Compare.Rows.Where(x => x.Layer == "identity" && x.Quantity == "idealLoadsKey").ToList();
            Assert.That(keyRows.Count(x => x.Status == "FAIL"), Is.GreaterThanOrEqualTo(1), "the colliding production key differs from one native key: " + run.Summary());
            Assert.That(run.Compare.Rows.Where(x => x.Layer == "identity" && x.Quantity == "native").All(x => x.Status == "PASS"), Is.True, "no space is rejected for its name");
        }

        [Test]
        public void NativeIdentity_OnTheExportedTwoZoneIdf_IsBijectiveAndIndependentOfNames()
        {
            string model = S11Support.Model(TestSupport.Two);
            AnalyticalModel loaded = Core.Convert.ToSAM<AnalyticalModel>(model).Single();
            List<IdfObject> idf = IdfText.Parse(SyntheticRun.ExportIdf(model, "two")).Where(x => NativeReader.IdfTypes.Contains(x.Type, StringComparer.OrdinalIgnoreCase)).ToList();
            ExpectedModel expected = ReadBackExpected.Build(loaded, null, TestSupport.Two);

            List<NativeZoneIdentityEntry> identity = NativeZoneIdentity.Resolve(new NativeModel(new NativeSnapshot { Idf = idf }), expected);
            Assert.That(identity, Has.Count.EqualTo(2));
            Assert.That(identity.All(x => x.Problem == null && x.NameCarrierAgrees), Is.True);
            Assert.That(identity.Select(x => x.Zone).Distinct().Count(), Is.EqualTo(2));

            // Swapping the names of the two native spaces does not change which geometry each SAM space matches.
            string a = identity[0].NativeSpace;
            string b = identity[1].NativeSpace;
            string swapped = SyntheticRun.ExportIdf(model, "two").Replace(a, "@@A@@").Replace(b, a).Replace("@@A@@", b);
            List<IdfObject> swappedIdf = IdfText.Parse(swapped).Where(x => NativeReader.IdfTypes.Contains(x.Type, StringComparer.OrdinalIgnoreCase)).ToList();
            List<NativeZoneIdentityEntry> after = NativeZoneIdentity.Resolve(new NativeModel(new NativeSnapshot { Idf = swappedIdf }), expected);
            Assert.That(after[0].NativeSpace, Is.EqualTo(b), "geometry, not the name, makes the match");
            Assert.That(after[0].NameCarrierAgrees, Is.True, "both names carry the shared GUID prefix, so the carrier cannot tell them apart");
        }
    }

    /// <summary>Unit checks of the S11 calendar, ESO dictionary parsing and the completeness judgement.</summary>
    [TestFixture]
    public class S11UnitTests
    {
        private static RunCalendar Calendar(string beginYear, string endMonth = "12", string endDay = "31", string endYear = null)
        {
            string idf = "RunPeriod,\n  Run 1, !- Name\n  1, !- Begin Month\n  1, !- Begin Day of Month\n  " + beginYear + ", !- Begin Year\n  " + endMonth + ", !- End Month\n  " + endDay + ", !- End Day of Month\n  " + (endYear ?? beginYear) + ", !- End Year\n  Sunday; !- Day of Week for Start Day\n";
            return RunCalendar.FromIdf(IdfText.Parse(idf));
        }

        [Test]
        public void Calendar_FullNonLeapYear_Has8760OrderedHours()
        {
            RunCalendar calendar = Calendar("2006");
            Assert.That(calendar.Problem, Is.Null);
            Assert.That(calendar.Count, Is.EqualTo(8760));
            Assert.That(calendar.IndexOf(new HourKey(1, 1, 1)), Is.EqualTo(0));
            Assert.That(calendar.IndexOf(new HourKey(12, 31, 24)), Is.EqualTo(8759));
            Assert.That(calendar.IndexOf(new HourKey(7, 7, 13)), Is.EqualTo(4500));
            Assert.That(calendar.IndexOf(new HourKey(2, 29, 1)), Is.EqualTo(-1));
            Assert.That(Calendar(string.Empty).Problem, Is.Null, "a blank year is EnergyPlus's non-leap default");
        }

        [TestCase("2024", "12", "31", null, "leap")]
        [TestCase("2006", "6", "30", null, "full year")]
        [TestCase("2006", "12", "31", "2007", "more than one year")]
        public void Calendar_OtherRunPeriods_AreProblems(string beginYear, string endMonth, string endDay, string endYear, string expected)
        {
            RunCalendar calendar = Calendar(beginYear, endMonth, endDay, endYear);
            Assert.That(calendar.Problem, Does.Contain(expected));
            Assert.That(calendar.Count, Is.Zero);
        }

        [Test]
        public void Calendar_TwoRunPeriods_IsAProblem()
        {
            string one = "RunPeriod, A, 1, 1, 2006, 12, 31, 2006;\n";
            Assert.That(RunCalendar.FromIdf(IdfText.Parse(one + one.Replace(" A,", " B,"))).Problem, Does.Contain("2 RunPeriod"));
        }

        [Test]
        public void EsoDictionaryLine_ParsesVariablesAndMeters()
        {
            EsoVariable variable = EsoResultRead.DictionaryLine("395,1,SAM_IDEALLOADS_OFFICE_CB68BED1,Zone Ideal Loads Supply Air Total Cooling Energy [J] !Hourly");
            Assert.That(variable.Id, Is.EqualTo(395));
            Assert.That(variable.Key, Is.EqualTo("SAM_IDEALLOADS_OFFICE_CB68BED1"));
            Assert.That(variable.Name, Is.EqualTo(S11Names.TotalCooling));
            Assert.That(variable.Units, Is.EqualTo("J"));
            Assert.That(variable.Frequency, Is.EqualTo("Hourly"));
            Assert.That(variable.IsMeter, Is.False);

            EsoVariable meter = EsoResultRead.DictionaryLine("396,1,DistrictCooling:Facility [J] !TimeStep");
            Assert.That(meter.IsMeter, Is.True);
            Assert.That(meter.Name, Is.EqualTo("DistrictCooling:Facility"));
            Assert.That(meter.Frequency, Is.EqualTo("TimeStep"));
        }

        [Test]
        public void Completeness_CountAloneIsNeverAccepted()
        {
            RunCalendar calendar = Calendar("2006");
            ChannelSeries series = new ChannelSeries { Channel = "sql", Variable = S11Names.TotalCooling, Key = "K", DictionaryEntries = 1 };
            series.Points.AddRange(calendar.Hours.Select(x => new HourlyPoint { Month = x.Month, Day = x.Day, Hour = x.Hour, Value = 1 }));
            Assert.That(Completeness.Check(calendar, series).Complete, Is.True);

            series.Points[101].Hour = series.Points[100].Hour;
            CompletenessResult shifted = Completeness.Check(calendar, series);
            Assert.That(shifted.Found, Is.EqualTo(8760));
            Assert.That(shifted.Complete, Is.False);
            Assert.That(shifted.Missing, Is.EqualTo(1));
            Assert.That(shifted.Duplicated, Is.EqualTo(1));
            Assert.That(shifted.Values, Is.Null);
        }
    }
}
