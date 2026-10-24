// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical.Benchmark;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SAM.Analytical.OpenStudio.ResultsReadBack
{
    /// <summary>One compared row: <c>layer|scope|loadType|quantity</c>, PASS, FAIL, BLOCKED or INFO, with its evidence.</summary>
    public sealed class ResultRow
    {
        public string Id { get; set; }
        public string Layer { get; set; }
        public string Scope { get; set; }
        public string LoadType { get; set; }
        public string Quantity { get; set; }

        /// <summary>R0 exact, R1 single value, R2 annual sum, Text, Identity.</summary>
        public string Class { get; set; }
        public string Status { get; set; }
        public string Reason { get; set; }
        public double? Reference { get; set; }
        public double? Subject { get; set; }
        public double? Delta { get; set; }
        public double? Tolerance { get; set; }

        /// <summary>F3 discrimination: |reference(Total) - reference(Sensible)| / this row's tolerance (value rows of a Total quantity).</summary>
        public double? Margin { get; set; }

        /// <summary>F3 discrimination for an hour row: the Sensible series peaks at another hour.</summary>
        public bool? SensibleHourDiffers { get; set; }
    }

    /// <summary>The per-fixture F3 discrimination summary the OD-7 real-data requirement is judged on (informational here).</summary>
    public sealed class F3Discrimination
    {
        public double? CoolingConsumptionMargin { get; set; }
        public double? BestSpaceCoolingPeakMargin { get; set; }

        /// <summary>Both margins at least 1000 x tolerance (OD-7 recommended requirement for one fixture).</summary>
        public bool MeetsOd7 { get; set; }
        public string Detail { get; set; }
    }

    /// <summary>compare.json: the S11 verdict for one run.</summary>
    public sealed class CompareDocument
    {
        public const string CurrentSchema = "s11-compare/1";

        /// <summary>Carried in every S11 record (plan section 10).</summary>
        public const string S10Limitation = "Phase B remains subject to S10's declared validation limitation (SAM|FixB|Heating|designDayPeak.hourOfDay BLOCKED); an S11 PASS is not a Phase B PASS.";

        public string Schema { get; set; } = CurrentSchema;
        public int ProcessId { get; set; }
        public DateTime ProcessStartedUtc { get; set; }
        public string CompareAssemblySha256 { get; set; }
        public string FixtureId { get; set; }
        public string Verdict { get; set; }
        public int ExitCode { get; set; }
        public int Required { get; set; }
        public int Passed { get; set; }
        public int Failed { get; set; }
        public int Blocked { get; set; }
        public int Informational { get; set; }
        public Dictionary<string, string> Inputs { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public List<string> GlobalBlocks { get; set; } = new List<string>();
        public F3Discrimination F3 { get; set; } = new F3Discrimination();
        public List<ResultRow> Rows { get; set; } = new List<ResultRow>();
        public List<string> Notes { get; set; } = new List<string> { S10Limitation };
    }

    /// <summary>
    /// The S11 comparison (the CLI's <c>compare</c> step, a separate process). The reference is reference.json; the subject
    /// layers are SAM-P (the captured producer objects, read from sam.json here), SAM-I (the import, from subject.json) and
    /// B1a (the produced document). Every row is judged against the reference with the S11 classes; a row whose reference
    /// depends on a blocked quantity is BLOCKED, never passed.
    /// </summary>
    public static class ResultCompare
    {
        private static readonly CultureInfo ci = CultureInfo.InvariantCulture;
        private static readonly LoadKind[] Kinds = { LoadKind.Heating, LoadKind.Cooling };

        public static double ToleranceR1(double reference) => 1e-12 * Math.Max(1, Math.Abs(reference));

        public static double ToleranceR2(double reference) => 1e-9 * Math.Abs(reference) + 1e-9;

        public static CompareDocument Run(AnalyticalModel model, ReferenceDocument reference, SubjectDocument subject, BenchmarkDocument b1a, SamResults samP)
        {
            CompareDocument result = new CompareDocument
            {
                ProcessId = Environment.ProcessId,
                ProcessStartedUtc = InputReadBack.ProcessIdentity.CurrentStartedUtc(),
                CompareAssemblySha256 = Sha256.Of(typeof(ResultCompare).Assembly.Location),
                FixtureId = reference.FixtureId,
            };

            result.GlobalBlocks.AddRange(reference.GlobalBlocks);
            Context context = new Context { Model = model, Reference = reference, Subject = subject, B1a = b1a, SamP = samP, Document = result };
            context.Spaces = model?.AdjacencyCluster?.GetSpaces() ?? new List<Space>();
            context.Sizing = Sizing(reference, b1a);

            ReferenceChecks(context);
            Identity(context);
            Closure(context);
            SamPModel(context);
            foreach (Space space in context.Spaces)
            {
                foreach (LoadKind kind in Kinds)
                {
                    SpaceRows(context, "samP", space, kind, SamPAnnual(context, space, kind), SamPDesign(context, space, kind));
                    SpaceRows(context, "samI", space, kind, SamIAnnual(context, space, kind), SamIDesign(context, space, kind));
                    B1aSpace(context, space, kind);
                }
            }

            SamIUnattached(context);
            B1aModel(context);
            F3(context);
            Verdict(result);
            return result;
        }

        private sealed class Context
        {
            public AnalyticalModel Model;
            public ReferenceDocument Reference;
            public SubjectDocument Subject;
            public BenchmarkDocument B1a;
            public SamResults SamP;
            public CompareDocument Document;
            public List<Space> Spaces;

            /// <summary>Null when sizing is consistent with the run's design-day basis (S11-C8); otherwise why not.</summary>
            public string Sizing;
        }

        // ---- dependencies -----------------------------------------------------------------------------------------------

        /// <summary>The reasons a row that depends on these quantities must block (empty when none).</summary>
        private static List<string> Blocks(Context context, IEnumerable<Guid> spaces, IEnumerable<string> variables, LoadKind? basis)
        {
            List<string> result = new List<string>(context.Reference.GlobalBlocks);
            foreach (Guid guid in spaces)
            {
                SpaceReference space = context.Reference.Space(guid);
                if (space == null)
                {
                    result.Add("space " + guid.ToString("N") + " has no reference");
                    continue;
                }

                if (space.IdentityProblem != null)
                {
                    result.Add("identity " + guid.ToString("N") + ": " + space.IdentityProblem);
                }

                foreach (string variable in variables)
                {
                    if (space.Quantities.TryGetValue(variable, out QuantityReference quantity) && !quantity.Available)
                    {
                        result.AddRange(quantity.BlockReasons.Select(x => guid.ToString("N") + " " + x));
                    }
                }

                if (basis.HasValue && space.BasisProblems.TryGetValue(basis.Value.ToString(), out string problem))
                {
                    result.Add(guid.ToString("N") + " " + problem);
                }
            }

            return result.Distinct().ToList();
        }

        private static string[] Energy(LoadKind kind) => new[] { S11Names.Total(kind), S11Names.Sensible(kind) };

        private static IEnumerable<Guid> All(Context context) => context.Spaces.Select(x => x.Guid);

        // ---- rows -------------------------------------------------------------------------------------------------------

        private static ResultRow Row(Context context, string layer, string scope, string loadType, string quantity, string @class)
        {
            ResultRow row = new ResultRow { Layer = layer, Scope = scope, LoadType = loadType ?? "-", Quantity = quantity, Class = @class };
            row.Id = layer + "|" + scope + "|" + row.LoadType + "|" + quantity;
            context.Document.Rows.Add(row);
            return row;
        }

        private static void Status(ResultRow row, bool pass, string reason)
        {
            row.Status = pass ? "PASS" : "FAIL";
            row.Reason = reason;
        }

        /// <summary>BLOCKED when any reason is given; true when the row was blocked.</summary>
        private static bool Block(ResultRow row, IEnumerable<string> reasons)
        {
            List<string> list = reasons.ToList();
            if (list.Count == 0)
            {
                return false;
            }

            row.Status = "BLOCKED";
            row.Reason = string.Join("; ", list.Take(4)) + (list.Count > 4 ? "; (+" + (list.Count - 4) + " more)" : string.Empty);
            return true;
        }

        /// <summary>A value row: the subject must be present and within the class tolerance of the reference.</summary>
        private static void Value(ResultRow row, double reference, double? subject, string @class)
        {
            row.Reference = reference;
            row.Subject = subject;
            if (!subject.HasValue)
            {
                Status(row, false, "the subject has no value");
                return;
            }

            double tolerance = @class == "R2" ? ToleranceR2(reference) : @class == "R1" ? ToleranceR1(reference) : 0;
            double delta = subject.Value - reference;
            row.Delta = delta;
            row.Tolerance = tolerance;
            bool pass = !double.IsNaN(subject.Value) && Math.Abs(delta) <= tolerance;
            Status(row, pass, pass ? null : "|delta| " + Math.Abs(delta).ToString("R", ci) + " > " + @class + " tolerance " + tolerance.ToString("R", ci));
        }

        /// <summary>F3 margin of a value row: how many tolerances the Sensible value lies from the Total reference.</summary>
        private static void Margin(ResultRow row, double? sensible)
        {
            if (row.Reference.HasValue && row.Tolerance.HasValue && row.Tolerance.Value > 0 && sensible.HasValue)
            {
                row.Margin = Math.Abs(row.Reference.Value - sensible.Value) / row.Tolerance.Value;
            }
        }

        // ---- reference and identity -------------------------------------------------------------------------------------

        private static void ReferenceChecks(Context context)
        {
            ResultRow row = Row(context, "reference", "model", null, "checks", "Identity");
            List<ReferenceCheck> blocked = context.Reference.Checks.Where(x => x.Status == "BLOCKED").ToList();
            if (!Block(row, context.Reference.GlobalBlocks.Concat(blocked.Select(x => x.Id + " " + x.Scope + ": " + x.Detail))))
            {
                Status(row, true, context.Reference.Checks.Count(x => x.Status == "PASS") + " checks PASS, " + context.Reference.Checks.Count(x => x.Status == "INFO") + " informational");
            }
        }

        private static void Identity(Context context)
        {
            foreach (Space space in context.Spaces)
            {
                string scope = "space:" + space.Guid.ToString("N");
                SpaceReference reference = context.Reference.Space(space.Guid);
                ResultRow native = Row(context, "identity", scope, null, "native", "Identity");
                if (!Block(native, reference == null ? new[] { "no reference" } : reference.IdentityProblem == null ? new string[0] : new[] { reference.IdentityProblem }))
                {
                    Status(native, true, "'" + space.Name + "' = native space '" + reference.NativeSpace + "', zone '" + reference.Zone + "', Ideal Loads '" + reference.IdealLoads + "' (geometry, " + reference.SurfacePairs + " surfaces)");
                }

                ProductionKey key = context.Subject.ProductionKeys.FirstOrDefault(x => x.SpaceGuid == space.Guid);
                foreach (string facet in new[] { "idealLoadsKey", "zoneKey" })
                {
                    ResultRow row = Row(context, "identity", scope, null, facet, "Identity");
                    if (Block(row, reference?.IdentityProblem == null && reference != null ? new string[0] : new[] { "no native identity" }))
                    {
                        continue;
                    }

                    string nativeKey = facet == "idealLoadsKey" ? reference.IdealLoads : reference.Zone;
                    string productionKey = key == null ? null : facet == "idealLoadsKey" ? key.IdealLoads : key.ThermalZone;
                    bool pass = productionKey != null && string.Equals(nativeKey, productionKey, StringComparison.OrdinalIgnoreCase);
                    Status(row, pass, (pass ? "production key = native key '" : "production key '" + productionKey + "' is not the native key '") + nativeKey + (pass ? "'" : "'"));
                }
            }

            ResultRow set = Row(context, "identity", "model", null, "b1aSpaceSet", "Identity");
            List<string> expected = context.Spaces.Select(x => x.Guid.ToString("N")).OrderBy(x => x, StringComparer.Ordinal).ToList();
            List<string> actual = (context.B1a?.Spaces ?? new List<BenchmarkSpaceResult>()).Select(x => x.Guid).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Status(set, expected.SequenceEqual(actual), expected.SequenceEqual(actual) ? actual.Count + " B1a spaces = the SAM spaces" : "B1a spaces [" + string.Join(",", actual) + "] are not the SAM spaces [" + string.Join(",", expected) + "]");

            ResultRow provenance = Row(context, "identity", "model", null, "b1aProvenance", "Identity");
            BenchmarkProvenance p = context.B1a?.Provenance;
            List<string> problems = new List<string>();
            if (p == null) problems.Add("no provenance");
            else
            {
                if (p.Route != BenchmarkRoute.NativeOpenStudio) problems.Add("route " + p.Route);
                if (p.State != RunState.Success) problems.Add("state " + p.State);
                if (p.ResultSources == null || p.ResultSources.Count != 1 || p.ResultSources[0] != S11Names.ResultSource) problems.Add("resultSources [" + string.Join(",", p.ResultSources ?? new List<string>()) + "]");
            }

            Status(provenance, problems.Count == 0, problems.Count == 0 ? "Native-OpenStudio, Success, source " + S11Names.ResultSource : string.Join("; ", problems));
        }

        private static void Closure(Context context)
        {
            ResultRow row = Row(context, "closure", "model", null, "samPToB1a", "Identity");
            Status(row, context.Subject.ClosureIdentical, context.Subject.ClosureDetail);
        }

        /// <summary>Every SAM-P row also blocks when the captured objects are not shown to be the ones that produced the B1a.</summary>
        private static IEnumerable<string> ClosureBlock(Context context, string layer)
        {
            return layer == "samP" && !context.Subject.ClosureIdentical ? new[] { "closure: the captured objects do not re-map to the produced B1a" } : new string[0];
        }

        // ---- SAM-P model ------------------------------------------------------------------------------------------------

        private static void SamPModel(Context context)
        {
            AnalyticalModelSimulationResult model = context.SamP?.ModelResult;
            foreach (LoadKind kind in Kinds)
            {
                ModelLoadReference reference = context.Reference.ModelOf(kind);
                List<string> blocks = Blocks(context, All(context), Energy(kind), kind).Concat(ClosureBlock(context, "samP")).ToList();
                bool heating = kind == LoadKind.Heating;

                ResultRow consumption = Row(context, "samP", "model", kind.ToString(), "consumption", "R2");
                if (!Block(consumption, blocks.Concat(reference.MeterProblem == null ? new string[0] : new[] { reference.MeterProblem })))
                {
                    Value(consumption, reference.Total.ConsumptionKwh, SamResults.Double(model, heating ? AnalyticalModelSimulationResultParameter.ConsumptionHeating : AnalyticalModelSimulationResultParameter.ConsumptionCooling), "R2");
                    Margin(consumption, reference.Sensible.Available ? reference.Sensible.ConsumptionKwh : (double?)null);
                }

                ResultRow peak = Row(context, "samP", "model", kind.ToString(), "peakLoad", "R1");
                if (!Block(peak, blocks))
                {
                    Value(peak, reference.Total.CoincidentPeakKw, SamResults.Double(model, heating ? AnalyticalModelSimulationResultParameter.PeakHeatingLoad : AnalyticalModelSimulationResultParameter.PeakCoolingLoad), "R1");
                    Margin(peak, reference.Sensible.Available ? reference.Sensible.CoincidentPeakKw : (double?)null);
                }

                // SAM's own rule: earliest tie; an all-zero year ties everywhere, so its earliest hour 0 is expected exactly.
                ResultRow hour = Row(context, "samP", "model", kind.ToString(), "peakHour", "R0");
                if (!Block(hour, blocks))
                {
                    Value(hour, reference.Total.PeakHour, SamResults.Integer(model, heating ? AnalyticalModelSimulationResultParameter.PeakHeatingHour : AnalyticalModelSimulationResultParameter.PeakCoolingHour), "R0");
                    hour.SensibleHourDiffers = reference.Sensible.Available ? reference.Sensible.PeakHour != reference.Total.PeakHour : (bool?)null;
                }
            }

            ResultRow area = Row(context, "samP", "model", null, "floorArea", "R1");
            if (!Block(area, ClosureBlock(context, "samP")))
            {
                Value(area, context.Reference.FloorArea, SamResults.Double(model, AnalyticalModelSimulationResultParameter.FloorArea), "R1");
            }

            ResultRow volume = Row(context, "samP", "model", null, "volume", "R1");
            if (!Block(volume, ClosureBlock(context, "samP")))
            {
                Value(volume, context.Reference.Volume, SamResults.Double(model, AnalyticalModelSimulationResultParameter.Volume), "R1");
            }
        }

        // ---- space rows (SAM-P and SAM-I) -------------------------------------------------------------------------------

        private static List<SubjectResult> SamPAnnual(Context context, Space space, LoadKind kind)
        {
            return (context.SamP?.SpaceResults ?? new List<SpaceSimulationResult>()).Select(x => SamResults.Describe(x, null)).Where(x => Matches(x, space, kind) && x.IsAnnual).ToList();
        }

        private static List<SubjectResult> SamPDesign(Context context, Space space, LoadKind kind)
        {
            return (context.SamP?.DesignLoadResults ?? new List<SpaceSimulationResult>()).Select(x => SamResults.Describe(x, null)).Where(x => Matches(x, space, kind) && x.IsDesignLoad).ToList();
        }

        private static List<SubjectResult> SamIAnnual(Context context, Space space, LoadKind kind)
        {
            return context.Subject.SamI.Where(x => Matches(x, space, kind) && x.IsAnnual).ToList();
        }

        private static List<SubjectResult> SamIDesign(Context context, Space space, LoadKind kind)
        {
            return context.Subject.SamI.Where(x => Matches(x, space, kind) && x.IsDesignLoad).ToList();
        }

        private static bool Matches(SubjectResult result, Space space, LoadKind kind)
        {
            return string.Equals(result.Reference, space.Guid.ToString("N"), StringComparison.OrdinalIgnoreCase) && string.Equals(result.LoadType, kind.ToString(), StringComparison.Ordinal);
        }

        private static void SpaceRows(Context context, string layer, Space space, LoadKind kind, List<SubjectResult> annual, List<SubjectResult> design)
        {
            string scope = "space:" + space.Guid.ToString("N");
            SpaceReference reference = context.Reference.Space(space.Guid);
            List<string> closure = ClosureBlock(context, layer).ToList();
            List<string> energy = Blocks(context, new[] { space.Guid }, Energy(kind), kind).Concat(closure).ToList();

            // The annual result object exists because the Total series exists: whether it should exist, and the not-met
            // hours it carries, can only be judged when the reference has that series.
            List<string> exists = Blocks(context, new[] { space.Guid }, new[] { S11Names.Total(kind) }, null).Concat(closure).ToList();
            List<string> unmet = Blocks(context, new[] { space.Guid }, new[] { S11Names.Unmet(kind), S11Names.Total(kind) }, null).Concat(closure).ToList();

            // Exactly one annual result per space and load type, from the OpenStudio source; SAM-I also attached to its space.
            ResultRow one = Row(context, layer, scope, kind.ToString(), "result", "Identity");
            SubjectResult result = annual.Count == 1 ? annual[0] : null;
            if (!Block(one, exists))
            {
                List<string> problems = new List<string>();
                if (annual.Count != 1) problems.Add(annual.Count + " annual results (exactly one is required)");
                if (result != null && result.Source != S11Names.ResultSource) problems.Add("source '" + result.Source + "'");
                if (layer == "samI" && result != null && result.AttachedTo != space.Guid) problems.Add("not attached to its space");
                Status(one, problems.Count == 0, problems.Count == 0 ? "one result, source " + S11Names.ResultSource + (layer == "samI" ? ", attached" : string.Empty) : string.Join("; ", problems));
            }

            QuantityReference total = reference?.Quantities.TryGetValue(S11Names.Total(kind), out QuantityReference t) == true ? t : null;
            QuantityReference sensible = reference?.Quantities.TryGetValue(S11Names.Sensible(kind), out QuantityReference s) == true ? s : null;
            QuantityReference unmetReference = reference?.Quantities.TryGetValue(S11Names.Unmet(kind), out QuantityReference u) == true ? u : null;

            ResultRow peak = Row(context, layer, scope, kind.ToString(), "peakLoad", "R1");
            if (!Block(peak, energy))
            {
                Value(peak, total.PeakWatts, result?.Load, "R1");
                Margin(peak, sensible?.Available == true ? sensible.PeakWatts : (double?)null);
            }

            ResultRow hour = Row(context, layer, scope, kind.ToString(), "peakHour", "R0");
            if (!Block(hour, energy))
            {
                Value(hour, total.PeakHour, result?.LoadIndex, "R0");
                hour.SensibleHourDiffers = sensible?.Available == true ? sensible.PeakHour != total.PeakHour : (bool?)null;
            }

            ResultRow unmetRow = Row(context, layer, scope, kind.ToString(), "unmetHours", "R2");
            if (!Block(unmetRow, unmet))
            {
                Value(unmetRow, unmetReference.Sum, result?.UnmetHours, "R2");
            }

            DesignRow(context, layer, scope, kind, reference, design.Select(x => x.DesignLoad).ToList(), closure);
        }

        /// <summary>
        /// The design load: absent when the run did no sizing (no ZoneSizes); otherwise ZoneSizes.CalcDesLoad of the space's
        /// zone [W]. For SAM-I (the legacy design-day import path) only availability is gated; its value is informational.
        /// </summary>
        private static void DesignRow(Context context, string layer, string scope, LoadKind kind, SpaceReference reference, List<double?> designLoads, List<string> closure)
        {
            ResultRow row = Row(context, layer, scope, kind.ToString(), "designLoad", "R0");
            List<string> blocks = new List<string>(context.Reference.GlobalBlocks.Concat(closure));
            if (context.Sizing != null) blocks.Add(context.Sizing);
            if (reference?.IdentityProblem != null) blocks.Add("identity: " + reference.IdentityProblem);
            if (Block(row, blocks))
            {
                return;
            }

            List<ZoneSizeRow> sizes = context.Reference.ZoneSizes.Where(x => string.Equals(x.ZoneName, reference.Zone, StringComparison.OrdinalIgnoreCase) && string.Equals(x.LoadType, kind.ToString(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (sizes.Count == 0)
            {
                Status(row, designLoads.Count == 0, designLoads.Count == 0 ? "no sizing: design load absent" : designLoads.Count + " design load(s) present without a ZoneSizes row");
                return;
            }

            if (layer == "samI")
            {
                row.Status = "INFO";
                row.Reason = designLoads.Count + " SAM-I design load(s) beside " + sizes.Count + " ZoneSizes row(s): the legacy design-day import path is out of S11 scope";
                return;
            }

            row.Class = "R1";
            if (sizes.Count > 1 || designLoads.Count != 1)
            {
                Status(row, false, sizes.Count + " ZoneSizes rows and " + designLoads.Count + " design load results (exactly one of each is required)");
                return;
            }

            Value(row, sizes[0].CalcDesLoad, designLoads[0], "R1");
        }

        private static void SamIUnattached(Context context)
        {
            ResultRow row = Row(context, "samI", "model", null, "unattached", "Identity");
            HashSet<Guid> spaces = new HashSet<Guid>(context.Spaces.Select(x => x.Guid));
            List<SubjectResult> unattached = context.Subject.SamI.Where(x => !x.AttachedTo.HasValue || !spaces.Contains(x.AttachedTo.Value)).ToList();
            Status(row, unattached.Count == 0, unattached.Count == 0 ? context.Subject.SamI.Count + " space results, all attached" : unattached.Count + " unattached result(s), e.g. '" + unattached[0].Name + "'");
        }

        // ---- B1a --------------------------------------------------------------------------------------------------------

        private static void B1aModel(Context context)
        {
            BenchmarkModelResult model = context.B1a?.Model;
            foreach (LoadKind kind in Kinds)
            {
                ModelLoadReference reference = context.Reference.ModelOf(kind);
                List<string> blocks = Blocks(context, All(context), Energy(kind), kind);
                bool heating = kind == LoadKind.Heating;

                ResultRow consumption = Row(context, "b1a", "model", kind.ToString(), "consumption", "R2");
                if (!Block(consumption, blocks.Concat(reference.MeterProblem == null ? new string[0] : new[] { reference.MeterProblem })))
                {
                    Value(consumption, reference.Total.ConsumptionKwh, Available(heating ? model?.ConsumptionHeating : model?.ConsumptionCooling), "R2");
                    Margin(consumption, reference.Sensible.Available ? reference.Sensible.ConsumptionKwh : (double?)null);
                }

                PeakRows(context, "model", kind, heating ? model?.PeakHeatingLoad : model?.PeakCoolingLoad, heating ? model?.PeakHeatingHour : model?.PeakCoolingHour, blocks, reference.Total.CoincidentPeakKw, reference.Total.PeakHour, reference.Total.AllZero, reference.Sensible.Available ? reference.Sensible.CoincidentPeakKw : (double?)null, reference.Sensible.Available ? reference.Sensible.PeakHour : (int?)null);
            }

            Value(Row(context, "b1a", "model", null, "floorArea", "R1"), context.Reference.FloorArea, Available(model?.FloorArea), "R1");
            Value(Row(context, "b1a", "model", null, "volume", "R1"), context.Reference.Volume, Available(model?.Volume), "R1");

            ResultRow note = Row(context, "b1a", "model", null, "totalBasisNote", "Text");
            bool hasNote = context.B1a?.Provenance?.Notes?.Contains(S11Names.TotalBasisNote) == true;
            Status(note, hasNote, hasNote ? "notes carry the Total-basis label" : "notes do not carry the Total-basis label '" + S11Names.TotalBasisNote + "'");

            ResultRow sizing = Row(context, "b1a", "model", null, "designDaySource", "Identity");
            if (!Block(sizing, context.Sizing == null ? new string[0] : new[] { context.Sizing }))
            {
                Status(sizing, true, "designDaySource " + context.B1a?.Provenance?.DesignDaySource + " with " + context.Reference.ZoneSizes.Count + " ZoneSizes rows");
            }
        }

        private static void B1aSpace(Context context, Space space, LoadKind kind)
        {
            string scope = "space:" + space.Guid.ToString("N");
            BenchmarkSpaceResult b1aSpace = context.B1a?.Spaces?.FirstOrDefault(x => string.Equals(x.Guid, space.Guid.ToString("N"), StringComparison.OrdinalIgnoreCase));
            BenchmarkConditionResult condition = kind == LoadKind.Heating ? b1aSpace?.Heating : b1aSpace?.Cooling;
            SpaceReference reference = context.Reference.Space(space.Guid);
            QuantityReference total = reference?.Quantities.TryGetValue(S11Names.Total(kind), out QuantityReference t) == true ? t : null;
            QuantityReference sensible = reference?.Quantities.TryGetValue(S11Names.Sensible(kind), out QuantityReference s) == true ? s : null;
            QuantityReference unmet = reference?.Quantities.TryGetValue(S11Names.Unmet(kind), out QuantityReference u) == true ? u : null;

            List<string> blocks = Blocks(context, new[] { space.Guid }, Energy(kind), kind);
            PeakRows(context, scope, kind, condition?.PeakLoad, condition?.PeakHour, blocks, total?.PeakWatts ?? double.NaN, total?.PeakHour ?? -1, total?.AllZero == true, sensible?.Available == true ? sensible.PeakWatts : (double?)null, sensible?.Available == true ? sensible.PeakHour : (int?)null);

            ResultRow unmetRow = Row(context, "b1a", scope, kind.ToString(), "unmetHours", "R2");
            if (!Block(unmetRow, Blocks(context, new[] { space.Guid }, new[] { S11Names.Unmet(kind), S11Names.Total(kind) }, null)))
            {
                Value(unmetRow, unmet.Sum, Available(condition?.UnmetHours), "R2");
            }

            List<double?> design = condition?.DesignLoad?.Available == true ? new List<double?> { condition.DesignLoad.Value } : new List<double?>();
            DesignRow(context, "b1a", scope, kind, reference, design, new List<string>());
        }

        /// <summary>
        /// The B1a peak and its hour under the S11 zero rule: an all-zero series is a load of 0 with NO hour; otherwise the
        /// load and the earliest peak hour.
        /// </summary>
        private static void PeakRows(Context context, string scope, LoadKind kind, MetricValue load, MetricValue hour, List<string> blocks, double reference, int referenceHour, bool allZero, double? sensible, int? sensibleHour)
        {
            ResultRow peak = Row(context, "b1a", scope, kind.ToString(), "peakLoad", "R1");
            if (!Block(peak, blocks))
            {
                Value(peak, reference, Available(load), "R1");
                Margin(peak, sensible);
            }

            ResultRow hourRow = Row(context, "b1a", scope, kind.ToString(), "peakHour", "R0");
            if (Block(hourRow, blocks))
            {
                return;
            }

            if (allZero)
            {
                bool unavailable = hour != null && !hour.Available;
                hourRow.Subject = Available(hour);
                Status(hourRow, unavailable, unavailable ? "all-zero year: hour unavailable (zero rule)" : "all-zero year but the B1a publishes hour " + Available(hour)?.ToString("R", ci) + " (the zero rule requires it unavailable)");
                return;
            }

            Value(hourRow, referenceHour, Available(hour), "R0");
            hourRow.SensibleHourDiffers = sensibleHour.HasValue ? sensibleHour.Value != referenceHour : (bool?)null;
        }

        private static double? Available(MetricValue metric)
        {
            return metric != null && metric.Available && metric.Value.HasValue ? metric.Value.Value : (double?)null;
        }

        /// <summary>S11-C8: ZoneSizes present exactly when the run had a design-day basis.</summary>
        private static string Sizing(ReferenceDocument reference, BenchmarkDocument b1a)
        {
            DesignDaySource? source = b1a?.Provenance?.DesignDaySource;
            if (source == null)
            {
                return "S11-C8: the B1a states no designDaySource";
            }

            if (source == DesignDaySource.None && reference.ZoneSizes.Count > 0)
            {
                return "S11-C8: " + reference.ZoneSizes.Count + " ZoneSizes rows although designDaySource is None";
            }

            if (source != DesignDaySource.None && reference.ZoneSizes.Count == 0)
            {
                return "S11-C8: designDaySource " + source + " but no ZoneSizes rows";
            }

            return null;
        }

        // ---- F3 and verdict ---------------------------------------------------------------------------------------------

        private static void F3(Context context)
        {
            F3Discrimination f3 = context.Document.F3;
            f3.CoolingConsumptionMargin = context.Document.Rows.FirstOrDefault(x => x.Id == "b1a|model|Cooling|consumption")?.Margin;
            f3.BestSpaceCoolingPeakMargin = context.Document.Rows.Where(x => x.Layer == "b1a" && x.Scope.StartsWith("space:", StringComparison.Ordinal) && x.LoadType == "Cooling" && x.Quantity == "peakLoad" && x.Margin.HasValue).Select(x => x.Margin).DefaultIfEmpty(null).Max();
            f3.MeetsOd7 = f3.CoolingConsumptionMargin >= 1000 && f3.BestSpaceCoolingPeakMargin >= 1000;
            f3.Detail = f3.MeetsOd7
                ? "Total and Sensible cooling are distinguishable on this run (consumption and at least one space peak >= 1000 x tolerance)"
                : "Total and Sensible cooling are NOT distinguishable to 1000 x tolerance on this run (consumption margin " + Text(f3.CoolingConsumptionMargin) + ", best space peak margin " + Text(f3.BestSpaceCoolingPeakMargin) + ")";
        }

        private static string Text(double? value) => value.HasValue ? value.Value.ToString("G6", ci) : "n/a";

        private static void Verdict(CompareDocument document)
        {
            List<ResultRow> gated = document.Rows.Where(x => x.Status != "INFO").ToList();
            document.Required = gated.Count;
            document.Passed = gated.Count(x => x.Status == "PASS");
            document.Failed = gated.Count(x => x.Status == "FAIL");
            document.Blocked = gated.Count(x => x.Status == "BLOCKED");
            document.Informational = document.Rows.Count - gated.Count;
            if (gated.Any(x => x.Status == null))
            {
                throw new InvalidOperationException("row without a status: " + gated.First(x => x.Status == null).Id);
            }

            document.Verdict = document.Failed > 0 ? "FAIL" : document.Blocked > 0 ? "BLOCKED" : "PASS";
            document.ExitCode = document.Failed > 0 ? InputReadBack.ReadBackExit.Fail : document.Blocked > 0 ? InputReadBack.ReadBackExit.BlockedTechnical : InputReadBack.ReadBackExit.Pass;
        }
    }
}
