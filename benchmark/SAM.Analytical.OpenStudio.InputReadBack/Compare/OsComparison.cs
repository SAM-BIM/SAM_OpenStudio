// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical.InputEquivalence;
using SAM.Analytical.Tas.DirectT3D.Validation.ReadBack;
using SAM.Core;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using EQ = SAM.Analytical.InputEquivalence.Query;

namespace SAM.Analytical.OpenStudio.InputReadBack
{
    using HeatFlowDirection = SAM.Analytical.InputEquivalence.HeatFlowDirection;

    /// <summary>One hourly series in the report: its size and the first element that does not pass.</summary>
    public sealed class SeriesSummary
    {
        public string Key { get; set; }
        public string QuantityId { get; set; }
        public int Count { get; set; }
        public int NotPass { get; set; }
        public string Status { get; set; }
        public string Outcome { get; set; }
        public string FirstNotPass { get; set; }
    }

    /// <summary>
    /// One row-3a opaque optical row on a face EnergyPlus does not use (S07 §5.2, amendment U1): OUT-OF-ENGINE-SCOPE,
    /// informational and not gated. Both values, their difference, the side and layer, and the scope reason are kept.
    /// </summary>
    public sealed class OutOfEngineScopeRow
    {
        public string Quantity { get; set; }
        public string Key { get; set; }
        public string Side { get; set; }

        /// <summary>The layer's EnergyPlus position on this side, outside first (as in the key), of <see cref="LayerCount"/>.</summary>
        public int Layer { get; set; }
        public int LayerCount { get; set; }
        public string Property { get; set; }

        /// <summary>The SAM face the row holds: internal or external.</summary>
        public string SamFace { get; set; }
        public string SamMaterial { get; set; }
        public string NativeObject { get; set; }
        public double SamValue { get; set; }
        public double NativeValue { get; set; }

        /// <summary>|native − SAM|.</summary>
        public double Delta { get; set; }

        /// <summary>The shared S07 numeric verdict of the two values (Identical, WithinTolerance, OutsideTolerance), for information.</summary>
        public string NumericVerdict { get; set; }

        /// <summary>True when the two values are outside the row-3a tolerance (the pre-U1 single-sided difference).</summary>
        public bool Differs { get; set; }
        public string ScopeReason { get; set; }
        public string Amendment { get; set; }
    }

    /// <summary>
    /// The S09 comparison of one fixture: SAM intent (ReadBackExpected and the core formulas, S07 section 3) against the
    /// native OSM/IDF read-back, quantity by quantity and hour by hour, gated by the S08 evaluation core.
    /// </summary>
    public sealed class OsComparison
    {
        private static readonly CultureInfo ci = CultureInfo.InvariantCulture;

        /// <summary>The verified inactive-delivery transformation of the S08 owner decision 3, applied with the same preconditions (S09 owner ventilation policy, V0/V1).</summary>
        public const string InactiveVentilation = "T-VENT-INACTIVE";

        /// <summary>The S07 §5.1 lighting energy split (amendment L1), verified for an authored p.</summary>
        public const string LightSplit = "T-LIGHT-SPLIT";

        /// <summary>
        /// S07 §5.2, amendment U1 (approved by the merge of SAM#218, b06dbe8c): row-3a opaque absorptance and emissivity are
        /// gated on the faces EnergyPlus 25.1 uses; a valid, comparable authored value on any other face is
        /// OUT-OF-ENGINE-SCOPE. It replaces the S09-only OS-MAT-SINGLE-SIDED listing (MAPPING-DIFFERENCE) of revisions 2-11.
        /// </summary>
        public const string OpaqueFaceScope = "S07 §5.2 amendment U1 (SAM#218 b06dbe8c)";

        /// <summary>The report's outcome name for an OUT-OF-ENGINE-SCOPE row (the shared core has no such outcome; it is OpenStudio-local).</summary>
        public const string OutOfEngineScopeOutcome = "OutOfEngineScope";

        public string FixtureId { get; private set; }
        public GateRuleSet RuleSet { get; private set; }
        public GateConfiguration Configuration { get; private set; }
        public List<RequiredComparison> Required { get; private set; } = new List<RequiredComparison>();
        public List<QuantityResult> Results { get; } = new List<QuantityResult>();
        public List<SeriesSummary> Series { get; } = new List<SeriesSummary>();
        public List<string> Supporting { get; } = new List<string>();

        /// <summary>Contradictions between the two native views (OSM, IDF) or with the run's own files. Any one fails the fixture.</summary>
        public List<string> Consistency { get; } = new List<string>();

        /// <summary>
        /// Native configurations the S09 reader does not interpret (an unreadable or unrecognised field, or a form S07 gives
        /// no rule for). Any one blocks the fixture as BLOCKED technical; never PASS.
        /// </summary>
        public List<string> Unsupported { get; } = new List<string>();

        public List<string> FixtureCriteria { get; } = new List<string>();
        public Dictionary<string, string> VentilationStates { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// The OUT-OF-ENGINE-SCOPE rows (S07 §5.2), in result order. Each one's result stays in <see cref="Results"/> for
        /// coverage, marked informational, so the gate does not count it; every other result gates as before.
        /// </summary>
        public List<OutOfEngineScopeRow> OutOfEngineScope { get; } = new List<OutOfEngineScopeRow>();

        public GateReport Gate { get; private set; }
        public GateStatus Status { get; private set; }

        private readonly HashSet<QuantityResult> outOfEngineScope = new HashSet<QuantityResult>();
        private NativeModel native;
        private AnalyticalModel model;
        private bool calendarVerified;

        public static OsComparison Run(AnalyticalModel model, NativeSnapshot snapshot, string epwPath, string fixtureId, GateRuleSet ruleSet)
        {
            OsComparison result = new OsComparison { FixtureId = fixtureId, RuleSet = ruleSet, model = model, native = new NativeModel(snapshot) };
            ExpectedModel expected = ReadBackExpected.Build(model, null, fixtureId);
            result.FixtureCriteria.AddRange(expected.FixtureCriteria);
            result.Required = RequiredFromModel(expected);
            result.Configuration = new GateConfiguration { RuleSet = ruleSet, RequiredComparisons = result.Required };

            result.Calendar(epwPath);
            result.Orientation();
            result.Multiplicity();
            List<IdfObject> matchedSurfaces = new List<IdfObject>();
            List<IdfObject> matchedSubSurfaces = new List<IdfObject>();
            List<string> matchedSpaces = new List<string>();
            foreach (ExpectedSpace space in expected.Spaces)
            {
                result.CompareSpace(space, matchedSpaces, matchedSurfaces, matchedSubSurfaces);
            }

            result.Extras(matchedSpaces, matchedSurfaces, matchedSubSurfaces);
            result.CompareWeather(epwPath, snapshot);
            result.CompareDesignDays(expected);

            if (result.OutOfEngineScope.Count > 0)
            {
                int differing = result.OutOfEngineScope.Count(x => x.Differs);
                result.Supporting.Add(OpaqueFaceScope + ": " + result.OutOfEngineScope.Count + " row-3a opaque optical row(s) on faces EnergyPlus 25.1 does not use are OUT-OF-ENGINE-SCOPE (informational, not gated): " + differing + " differ from the native value, " + (result.OutOfEngineScope.Count - differing) + " equal it. Each holds a valid authored value and a valid native value on a face whose role S09 derived from SAM, the side and the native boundary. Every used face (each innermost layer's zone face, each outdoor outermost face), every thickness, conductivity, density and specific heat, and every row that is not valid and comparable is gated.");
            }

            result.Gate = EQ.Gate(result.Results, result.Configuration);
            GateStatus status = result.Gate.Status;
            if (result.Consistency.Count > 0)
            {
                status = GateStatus.Fail;
            }
            else if (result.Unsupported.Count > 0 && (int)status > (int)GateStatus.BlockedTechnical)
            {
                status = GateStatus.BlockedTechnical;
            }
            else if (result.FixtureCriteria.Count > 0 && (int)status > (int)GateStatus.BlockedOwnerDecision)
            {
                status = GateStatus.BlockedOwnerDecision;
            }

            result.Status = status;
            return result;
        }

        /// <summary>True for a result reported as OUT-OF-ENGINE-SCOPE (S07 §5.2); it is informational and never gates.</summary>
        public bool IsOutOfEngineScope(QuantityResult result)
        {
            return result != null && outOfEngineScope.Contains(result);
        }

        /// <summary>A result's reported outcome: <see cref="OutOfEngineScopeOutcome"/> for an S07 §5.2 row, otherwise the shared outcome.</summary>
        public string ReportedOutcome(QuantityResult result)
        {
            return IsOutOfEngineScope(result) ? OutOfEngineScopeOutcome : result.Outcome.ToString();
        }

        // ---- coverage ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// Every quantity-on-object comparison the verdict needs, from the frozen model only (never from the native files):
        /// each space, each panel side and its layers, each aperture side, each internal-condition row, the weather and the
        /// design days.
        /// </summary>
        public static List<RequiredComparison> RequiredFromModel(ExpectedModel expected)
        {
            List<RequiredComparison> result = new List<RequiredComparison>();
            void Add(string id, string key) => result.Add(new RequiredComparison(id, key));

            foreach (ExpectedSpace space in expected.Spaces)
            {
                Add("1a", Keys.Space(space.Guid));
                Add("2", Keys.Space(space.Guid));
                foreach (ExpectedPanel panel in space.Panels)
                {
                    string side = Keys.Side(panel.Guid, space.Guid);
                    Add("1b", side);
                    for (int k = 1; k <= panel.Layers.Count; k++)
                    {
                        foreach (string property in LayerProperties.Opaque)
                        {
                            Add("3a", Keys.Layer(side, k, property));
                        }
                    }

                    Add(ElementKindOf(panel) == ElementKind.GroundOrAdiabatic ? "3b.ground" : "3b", side + ".U");
                    foreach (ExpectedAperture aperture in panel.Apertures)
                    {
                        string key = Keys.Aperture(aperture.Guid, space.Guid);
                        Add("1b", key + ".pane");
                        Add("1b", key + ".frame");
                        if (aperture.FrameLayers.Count > 0)
                        {
                            Add("T-FRAME.conductance", key + ".frameConductance");
                        }

                        for (int k = 1; k <= aperture.PaneLayers.Count; k++)
                        {
                            foreach (string property in LayerProperties.Transparent)
                            {
                                Add("3c.layer", Keys.Layer(key, k, property));
                            }
                        }

                        Add("3c.U", key + ".U");
                    }
                }

                if (!space.HasInternalCondition)
                {
                    continue;
                }

                Add("4a.people", Keys.Ic(space.Guid, "people"));
                foreach (string gain in LayerProperties.Gains)
                {
                    Add("4a.density", Keys.Ic(space.Guid, gain));
                    Add("4b", Keys.Ic(space.Guid, gain + ".total"));
                }

                foreach (string fraction in LayerProperties.Fractions)
                {
                    Add("4c", Keys.Ic(space.Guid, fraction));
                }

                Add(OsQuantities.LightingReturnAir.Id, Keys.Ic(space.Guid, LayerProperties.LightingReturnAir));

                Add("5.temperature", Keys.Ic(space.Guid, "heating"));
                Add("5.temperature", Keys.Ic(space.Guid, "cooling"));
                Add("5.humidity", Keys.Ic(space.Guid, "humidification"));
                Add("5.humidity", Keys.Ic(space.Guid, "dehumidification"));
                foreach (string schedule in LayerProperties.Schedules)
                {
                    Add("6.hourly", Keys.Ic(space.Guid, "schedule." + schedule));
                    Add("6.eflh", Keys.Ic(space.Guid, "schedule." + schedule));
                }

                Add("7a", Keys.Ic(space.Guid, "infiltration"));
                Add("7b", Keys.Ic(space.Guid, "infiltration"));
                foreach (string basis in LayerProperties.VentilationBases)
                {
                    Add("8a", Keys.Ic(space.Guid, "ventilation.requirement." + basis));
                }

                Add("8a", Keys.Ic(space.Guid, "ventilation.requirement.total"));
                Add("8b", Keys.Ic(space.Guid, "ventilation.delivered"));
            }

            foreach (string what in new[] { "epw", "latitude", "longitude", "elevation", "timezone" })
            {
                Add("9.os." + what, Keys.Weather(what));
            }

            Add("10.os.count", Keys.DesignDays);
            return result;
        }

        // ---- calendar --------------------------------------------------------------------------------------------------

        private void Calendar(string epwPath)
        {
            IdfObject runPeriod = native.Idf("RunPeriod").SingleOrDefault();
            List<string> problems = new List<string>();
            if (runPeriod == null)
            {
                problems.Add("the IDF has no single RunPeriod");
            }
            else
            {
                string[] labels = { "Begin Month", "Begin Day of Month", "Begin Year", "End Month", "End Day of Month", "End Year" };
                double[] values = new double[labels.Length];
                for (int i = 0; i < labels.Length; i++)
                {
                    ReaderResult r = NativeModel.Number(runPeriod, 1 + i, labels[i]);
                    values[i] = r.HasNumber ? r.Value : double.NaN;
                }

                if (values[0] != 1 || values[1] != 1 || values[3] != 12 || values[4] != 31 || values[2] != values[5])
                {
                    problems.Add(string.Format(ci, "RunPeriod is {0}/{1}/{2} to {3}/{4}/{5}, not one calendar year", values[0], values[1], values[2], values[3], values[4], values[5]));
                }
            }

            if (native.Snapshot.DayCount != 365)
            {
                problems.Add("the run year has " + native.Snapshot.DayCount + " days (leap-year handling is not verified, S07 section 7)");
            }

            int epwRows = File.Exists(epwPath) ? File.ReadLines(epwPath).Skip(8).Count(x => !string.IsNullOrWhiteSpace(x)) : -1;
            if (epwRows != 8760)
            {
                problems.Add("the EPW has " + epwRows + " data rows, not 8760");
            }

            calendarVerified = problems.Count == 0;
            Supporting.Add(calendarVerified
                ? "calendar: one non-leap run year 1/1-12/31 (" + native.Snapshot.DayCountEvidence + "), EPW 8760 rows; hour index 0 = 1 January 00:00-01:00. Each schedule's own hour labels are checked separately."
                : "calendar NOT verified: " + string.Join("; ", problems));
        }

        private AlignmentStatus Alignment(params NativeSchedule[] schedules)
        {
            return calendarVerified && schedules.All(x => x != null && x.Values != null && x.AlignmentVerified) ? AlignmentStatus.Verified : AlignmentStatus.Unverified;
        }

        // ---- spaces, surfaces, apertures --------------------------------------------------------------------------------

        private sealed class NativeSurface
        {
            public IdfObject Object;
            public List<double[]> Vertices;
            public string Problem;
        }

        private List<NativeSurface> SurfacesOf(string spaceName)
        {
            List<NativeSurface> result = new List<NativeSurface>();
            foreach (IdfObject surface in native.Idf("BuildingSurface:Detailed"))
            {
                if (!string.Equals(surface.Value(4, "Space Name", out string _), spaceName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                IdfObject zone = native.IdfByName("Zone", surface.Value(3));
                List<double[]> vertices = native.Vertices(surface, 11, zone, out string problem);
                result.Add(new NativeSurface { Object = surface, Vertices = vertices, Problem = problem });
            }

            return result;
        }

        private void CompareSpace(ExpectedSpace space, List<string> matchedSpaces, List<IdfObject> matchedSurfaces, List<IdfObject> matchedSubSurfaces)
        {
            string key = Keys.Space(space.Guid);
            string carrier = "_" + space.Guid.ToString("N").Substring(0, 8);
            List<Polygon3D> expectedPolygons = space.Panels.Select(x => x.Outer).ToList();

            // Identity: the native space whose surfaces are, one to one, the space's panel polygons (geometry alone). The
            // name suffix the writer mints from the SAM GUID may only contradict that match, never make it.
            string nativeSpace = null;
            GeometryMatchResult match = null;
            List<NativeSurface> surfaces = null;
            foreach (IdfObject candidate in native.Idf("Space"))
            {
                List<NativeSurface> candidateSurfaces = SurfacesOf(candidate.Name);
                if (candidateSurfaces.Any(x => x.Vertices == null))
                {
                    continue;
                }

                GeometryMatchResult candidateMatch = EQ.MatchPolygons(expectedPolygons, candidateSurfaces.Select(x => OsGeometry.ToPolygon3D(x.Vertices)).ToList());
                if (candidateMatch.IsBijection)
                {
                    if (nativeSpace != null)
                    {
                        nativeSpace = null;
                        Supporting.Add(key + ": more than one native space matches its geometry");
                        break;
                    }

                    nativeSpace = candidate.Name;
                    match = candidateMatch;
                    surfaces = candidateSurfaces;
                }
            }

            IdentityStatus identity;
            if (nativeSpace == null)
            {
                identity = IdentityStatus.Mismatch;
                Supporting.Add(key + " '" + space.Name + "': no native space has exactly its panel polygons");
            }
            else
            {
                identity = nativeSpace.EndsWith(carrier, StringComparison.OrdinalIgnoreCase) ? IdentityStatus.Verified : IdentityStatus.Mismatch;
                matchedSpaces.Add(nativeSpace);
                Supporting.Add(key + " '" + space.Name + "' = native space '" + nativeSpace + "' by geometry (" + match.Pairs.Count + " surfaces one to one); name carrier " + (identity == IdentityStatus.Verified ? "agrees" : "CONTRADICTS"));
            }

            IdfObject nativeSpaceObject = native.IdfByName("Space", nativeSpace);
            IdfObject zone = native.IdfByName("Zone", nativeSpaceObject?.Value(1, "Zone Name", out string _));

            // 1a, 2: floor area and volume from the native polygons (EnergyPlus autocalculates both from them).
            ReaderResult floorArea = ReaderResult.Absent("no native space");
            ReaderResult volume = ReaderResult.Absent("no native space");
            if (surfaces != null)
            {
                List<NativeSurface> floors = surfaces.Where(x => string.Equals(x.Object.Value(1, "Surface Type", out string _), "Floor", StringComparison.OrdinalIgnoreCase)).ToList();
                floorArea = floors.Count == 0 ? ReaderResult.Absent("no native floor surface") : ReaderResult.FromValue(floors.Sum(x => OsGeometry.Area(x.Vertices)));
                double v = OsGeometry.ShellVolume(surfaces.Select(x => (IReadOnlyList<double[]>)x.Vertices), out double signed);
                volume = ReaderResult.FromValue(v);
                Supporting.Add(key + ": native signed shell volume " + signed.ToString("R", ci) + " m3 (positive = outward-facing surfaces)");
                foreach (string field in new[] { "Volume", "Floor Area", "Ceiling Height" })
                {
                    ReaderResult stated = NativeModel.Number(zone, field == "Volume" ? 8 : field == "Floor Area" ? 9 : 7, field);
                    if (stated.Kind != ReaderKind.Absent)
                    {
                        Consistency.Add(key + ": the IDF Zone states " + field + " (" + stated.Value.ToString("R", ci) + ") instead of autocalculating it from the surfaces");
                    }
                }
            }

            Add("1a", key, space.FloorArea, floorArea, identity);
            Add("2", key, space.ShellVolume, volume, identity);

            ModelInterpretation interpretation = VolumeInterpretation(space, key);

            // Panel sides.
            if (match != null)
            {
                for (int i = 0; i < space.Panels.Count; i++)
                {
                    ExpectedPanel panel = space.Panels[i];
                    GeometryPair pair = match.PairForExpected(i);
                    NativeSurface surface = pair == null ? null : surfaces[pair.NativeIndex];
                    bool? carrierAgrees = surface == null ? (bool?)null : surface.Object.Name.EndsWith("_" + panel.Guid.ToString("N").Substring(0, 8), StringComparison.OrdinalIgnoreCase);
                    IdentityStatus sideIdentity = identity == IdentityStatus.Verified ? EQ.AssessIdentity(match, i, carrierAgrees) : identity;
                    if (surface != null)
                    {
                        matchedSurfaces.Add(surface.Object);
                    }

                    CompareSide(space, panel, surface, sideIdentity, matchedSubSurfaces);
                }
            }
            else
            {
                foreach (ExpectedPanel panel in space.Panels)
                {
                    CompareSide(space, panel, null, IdentityStatus.Mismatch, matchedSubSurfaces);
                }
            }

            if (space.HasInternalCondition)
            {
                CompareInternalCondition(space, nativeSpace, zone, floorArea, volume, identity, interpretation);
            }
        }

        private ModelInterpretation VolumeInterpretation(ExpectedSpace space, string key)
        {
            // D-F9 C: stored and geometric volume agree within row 2's tolerance, or the interpretation must be recorded.
            if (space.ShellVolume == null || space.ShellVolume.State != SamInputState.Value || double.IsNaN(space.StoredVolume))
            {
                Supporting.Add(key + ": D-F9 stored volume " + space.StoredVolume.ToString("R", ci) + " vs geometric " + space.ShellVolume);
                return ModelInterpretation.NotRequired;
            }

            NumericComparison comparison = EQ.Compare(space.ShellVolume.Value, space.StoredVolume, QuantityCatalog.Get("2").Tolerance);
            Supporting.Add(key + ": D-F9 stored volume " + space.StoredVolume.ToString("R", ci) + " m3 vs geometric " + space.ShellVolume.Value.ToString("R", ci) + " m3: " + comparison.Verdict);
            return comparison.IsWithin ? ModelInterpretation.NotRequired : ModelInterpretation.RequiredNotRecorded;
        }

        private static ElementKind ElementKindOf(ExpectedPanel panel)
        {
            if (panel.Adiabatic || IsGround(panel.PanelType))
            {
                return ElementKind.GroundOrAdiabatic;
            }

            return panel.SpaceOrder.Count > 1 ? ElementKind.Internal : ElementKind.External;
        }

        private static bool IsGround(PanelType panelType)
        {
            return panelType == PanelType.SlabOnGrade || panelType == PanelType.UndergroundSlab || panelType == PanelType.UndergroundWall || panelType == PanelType.UndergroundCeiling;
        }

        private static HeatFlowDirection DirectionOf(ExpectedPanel panel, bool firstSide)
        {
            switch (panel.PanelGroup)
            {
                case PanelGroup.Roof:
                    return HeatFlowDirection.Upward;
                case PanelGroup.Floor:
                    // A floor seen from the space above conducts downwards; from the space below it is a ceiling.
                    return firstSide || panel.SpaceOrder.Count < 2 ? HeatFlowDirection.Downward : HeatFlowDirection.Upward;
                default:
                    return HeatFlowDirection.Horizontal;
            }
        }

        private void CompareSide(ExpectedSpace space, ExpectedPanel panel, NativeSurface surface, IdentityStatus identity, List<IdfObject> matchedSubSurfaces)
        {
            string side = Keys.Side(panel.Guid, space.Guid);
            Add("1b", side, ExpectedValue.FromValue(panel.Outer == null ? double.NaN : EQ.GrossArea(panel.Outer)), surface == null ? ReaderResult.Absent("no native surface") : ReaderResult.FromValue(OsGeometry.Area(surface.Vertices)), identity);

            // Boundary: the native outside boundary must be the one SAM states for the panel.
            ElementKind kind = ElementKindOf(panel);
            if (surface != null)
            {
                string boundary = surface.Object.Value(5, "Outside Boundary Condition", out string _);
                string expectedBoundary = kind == ElementKind.Internal ? "Surface" : kind == ElementKind.External ? "Outdoors" : panel.Adiabatic ? "Adiabatic" : "Ground";
                if (!string.Equals(boundary, expectedBoundary, StringComparison.OrdinalIgnoreCase) && !(kind == ElementKind.GroundOrAdiabatic && string.Equals(boundary, "Ground", StringComparison.OrdinalIgnoreCase)))
                {
                    Consistency.Add(side + ": native outside boundary '" + boundary + "', SAM states '" + expectedBoundary + "'");
                }
            }

            // Layers, outside first as EnergyPlus lists them: SAM lists a construction from its first space inside to
            // outside (S07 section 1, UpdateReversed convention), so the first space sees the SAM list reversed.
            bool firstSide = panel.SpaceOrder.Count == 0 || panel.SpaceOrder[0] == space.Guid;
            List<ExpectedLayer> expectedLayers = firstSide ? Enumerable.Reverse(panel.Layers).ToList() : panel.Layers.ToList();
            List<IdfObject> nativeLayers = LayersOf(surface?.Object.Value(2, "Construction Name", out string _), side, surface != null);
            OpaqueSideRole role = OpaqueRole(space, panel, surface, identity, expectedLayers, nativeLayers, kind);
            if (!role.Established && role.Named)
            {
                Unsupported.Add("optics role: " + side + ": " + role.Problem + "; no optical face of this side is OUT-OF-ENGINE-SCOPE (" + OpaqueFaceScope + ")");
            }

            CompareLayers(side, expectedLayers, nativeLayers, identity, false, firstSide, kind, role);
            SingleLayerFaces(side, expectedLayers, kind);

            HeatFlowDirection direction = DirectionOf(panel, firstSide);
            string uId = kind == ElementKind.GroundOrAdiabatic ? "3b.ground" : "3b";
            Add(uId, side + ".U", EQ.ExpectedOpaqueU(expectedLayers.Select(x => LayerResistance.Solid(x.Thickness, (x.Material as Core.Material)?.ThermalConductivity ?? double.NaN)), direction, kind), NativeU(nativeLayers, direction, kind), identity);

            foreach (ExpectedAperture aperture in panel.Apertures)
            {
                CompareAperture(space, aperture, surface, identity, firstSide, matchedSubSurfaces);
            }
        }

        private List<IdfObject> LayersOf(string constructionName, string key, bool required)
        {
            IdfObject construction = native.IdfByName("Construction", constructionName);
            if (construction == null)
            {
                if (required)
                {
                    Supporting.Add(key + ": native construction '" + constructionName + "' not found");
                }

                return null;
            }

            List<IdfObject> result = new List<IdfObject>();
            for (int i = 1; i < construction.Fields.Count; i++)
            {
                string name = construction.Value(i);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                IdfObject layer = new[] { "Material", "Material:NoMass", "Material:AirGap", "WindowMaterial:Glazing", "WindowMaterial:Gas", "WindowMaterial:SimpleGlazingSystem" }
                    .Select(t => native.IdfByName(t, name)).FirstOrDefault(x => x != null);
                result.Add(layer ?? new IdfObject { Type = "missing", Fields = new List<IdfField> { new IdfField { Value = name } } });
            }

            return result;
        }

        private void CompareLayers(string key, List<ExpectedLayer> expected, List<IdfObject> nativeLayers, IdentityStatus identity, bool transparent, bool frontIsExternal, ElementKind kind, OpaqueSideRole role = null)
        {
            int count = Math.Max(expected.Count, nativeLayers?.Count ?? 0);
            for (int k = 0; k < count; k++)
            {
                ExpectedLayer layer = k < expected.Count ? expected[k] : null;
                IdfObject nativeLayer = nativeLayers != null && k < nativeLayers.Count ? nativeLayers[k] : null;
                foreach (string property in transparent ? LayerProperties.Transparent : LayerProperties.Opaque)
                {
                    string layerKey = Keys.Layer(key, k + 1, property);
                    ExpectedValue e = layer == null ? ExpectedValue.Unspecified("SAM states no layer " + (k + 1)) : ExpectedLayerValue(layer, property);
                    ReaderResult n = NativeLayerValue(nativeLayer, property, frontIsExternal);
                    QuantityResult result = EQ.Evaluate(Input(transparent ? "3c.layer" : "3a", layerKey, e, n, identity, AlignmentStatus.Unverified), Configuration);

                    // S07 §5.2: EnergyPlus opaque materials hold one value per property for both faces, and only the
                    // outermost layer's outside face (outdoors) and the innermost layer's zone face are used. Every
                    // other face's row is OUT-OF-ENGINE-SCOPE when it is valid and comparable; otherwise it gates.
                    if (!transparent && layer != null && TryGetOpaqueFace(property, out bool internalFace, out string _)
                        && !IsExposedOpaqueFace(k, expected.Count, internalFace, frontIsExternal, kind))
                    {
                        result = UnusedOpaqueFace(result, role, key, layer, nativeLayer, k, expected.Count, property, internalFace, frontIsExternal, kind);
                    }

                    Results.Add(result);
                }
            }
        }

        /// <summary>
        /// The optical face roles of one opaque panel side (S07 §5.2), or why they are not established. Established only
        /// from evidence that does not come from the writer's face selector: the raw SAM panel (its spaces in GetSpaces()
        /// order, adiabatic flag, type and layers) and the native surface it was matched to by geometry (its outside
        /// boundary and, for an interzone side, the partner surface and construction).
        /// </summary>
        private sealed class OpaqueSideRole
        {
            public string Problem;

            /// <summary>
            /// True when the problem is an uninterpretable or contradictory configuration, named as unsupported (BLOCKED
            /// technical). False when it is a missing native object or an unverified identity, which gate on their own rows.
            /// </summary>
            public bool Named;

            public bool Adiabatic;

            public bool Established => Problem == null;
        }

        private OpaqueSideRole OpaqueRole(ExpectedSpace space, ExpectedPanel panel, NativeSurface surface, IdentityStatus identity, List<ExpectedLayer> expectedLayers, List<IdfObject> nativeLayers, ElementKind kind)
        {
            OpaqueSideRole Problem(string problem, bool named) => new OpaqueSideRole { Problem = problem, Named = named };

            if (surface == null)
            {
                return Problem("no native surface", false);
            }

            if (identity != IdentityStatus.Verified)
            {
                return Problem("the side's identity is not verified (" + identity + ")", false);
            }

            if (nativeLayers == null)
            {
                return Problem("no native construction", false);
            }

            // SAM: the panel's spaces, its type and its boundary flags must state one boundary.
            if (panel.SpaceOrder.Count == 0 || !panel.SpaceOrder.Contains(space.Guid))
            {
                return Problem("the panel's SAM spaces (" + panel.SpaceOrder.Count + ") do not include this space", true);
            }

            if (panel.SpaceOrder.Count > 2 || panel.SpaceOrder.Distinct().Count() != panel.SpaceOrder.Count)
            {
                return Problem("the panel's SAM space list is malformed (" + panel.SpaceOrder.Count + " entries, " + panel.SpaceOrder.Distinct().Count() + " distinct)", true);
            }

            if (panel.PanelType == PanelType.Undefined || panel.PanelType == PanelType.Shade || panel.PanelType == PanelType.SolarPanel || panel.PanelType == PanelType.Air)
            {
                return Problem("SAM panel type " + panel.PanelType + " has no supported opaque boundary", true);
            }

            if (panel.SpaceOrder.Count == 2 && (panel.Adiabatic || IsGround(panel.PanelType)))
            {
                return Problem("contradictory SAM boundary: the panel bounds two spaces and is " + (panel.Adiabatic ? "adiabatic" : "a ground type (" + panel.PanelType + ")"), true);
            }

            // Native: the outside boundary must be exactly the SAM one; any other form (a ground preprocessor, other side
            // coefficients, a Foundation, an adiabatic panel written as Ground) is not read as one of the four.
            string expectedBoundary = kind == ElementKind.Internal ? "Surface" : kind == ElementKind.External ? "Outdoors" : panel.Adiabatic ? "Adiabatic" : "Ground";
            string boundary = NativeModel.Text(surface.Object, 5, "Outside Boundary Condition", out string boundaryProblem);
            if (boundaryProblem != null)
            {
                return Problem("the native outside boundary is unreadable: " + boundaryProblem, true);
            }

            if (!string.Equals(boundary?.Trim(), expectedBoundary, StringComparison.OrdinalIgnoreCase))
            {
                return Problem("the native outside boundary '" + boundary + "' is not the SAM boundary '" + expectedBoundary + "'", true);
            }

            // Layers: one native layer per SAM layer, so each position has one SAM layer.
            if (expectedLayers.Count == 0 || nativeLayers.Count != expectedLayers.Count)
            {
                return Problem("the native construction has " + nativeLayers.Count + " layer(s) and SAM " + expectedLayers.Count, true);
            }

            // Interzone: the outside face is the partner's zone face only when the partner names this surface back and
            // its construction is this one reversed.
            if (kind == ElementKind.Internal)
            {
                string partnerName = NativeModel.Text(surface.Object, 6, "Outside Boundary Condition Object", out string partnerProblem);
                IdfObject partner = partnerProblem == null ? native.IdfByName("BuildingSurface:Detailed", partnerName) : null;
                if (partner == null)
                {
                    return Problem("the interzone partner surface '" + partnerName + "' is " + (partnerProblem != null ? "unreadable: " + partnerProblem : "not in the IDF"), true);
                }

                string partnerBoundary = NativeModel.Text(partner, 5, "Outside Boundary Condition", out string _);
                string partnerObject = NativeModel.Text(partner, 6, "Outside Boundary Condition Object", out string _);
                if (!string.Equals(partnerBoundary?.Trim(), "Surface", StringComparison.OrdinalIgnoreCase) || !string.Equals(partnerObject?.Trim(), surface.Object.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return Problem("the interzone partner '" + partner.Name + "' does not name this surface back (" + partnerBoundary + " '" + partnerObject + "')", true);
                }

                List<IdfObject> partnerLayers = LayersOf(partner.Value(2, "Construction Name", out string _), null, false);
                List<string> mirror = nativeLayers.Select(x => x.Name).Reverse().ToList();
                if (partnerLayers == null || !partnerLayers.Select(x => x.Name).SequenceEqual(mirror, StringComparer.OrdinalIgnoreCase))
                {
                    return Problem("the interzone partner's construction is not this side's construction reversed", true);
                }
            }

            return new OpaqueSideRole { Adiabatic = panel.Adiabatic };
        }

        /// <summary>
        /// A row-3a optical row on a face EnergyPlus does not use (S07 §5.2). It is OUT-OF-ENGINE-SCOPE, informational, only
        /// when the side's role is established, the layer is an opaque SAM material written as an opaque Material, the SAM
        /// value is authored (S-VALUE, or S-ZERO: an authored zero is a value) and the native value is read, both in
        /// [0, 1], and the shared evaluation is a numeric comparison of the two (MATCH, WITHIN-TOLERANCE or
        /// OUT-OF-TOLERANCE, without any other trigger). Anything else keeps its shared, gated result, with the reason it
        /// was not excluded; a value outside [0, 1] is INPUT-INVALID (SAM) or EXPORT-DEFECT (native), never compared.
        /// </summary>
        private QuantityResult UnusedOpaqueFace(QuantityResult result, OpaqueSideRole role, string side, ExpectedLayer layer, IdfObject nativeLayer, int index, int count, string property, bool internalFace, bool firstSide, ElementKind kind)
        {
            QuantityInput input = result.Input;
            ExpectedValue e = input.Expected;
            ReaderResult n = input.Native;
            bool samAuthored = e.State == SamInputState.Value || e.State == SamInputState.Zero;

            string refused = null;
            if (role == null || !role.Established)
            {
                refused = "the side's face role is not established: " + (role?.Problem ?? "not assessed");
            }
            else if (!(layer.Material is OpaqueMaterial))
            {
                refused = "SAM layer '" + layer.Name + "' is not an opaque material of the frozen library";
            }
            else if (nativeLayer == null || !string.Equals(nativeLayer.Type, "Material", StringComparison.OrdinalIgnoreCase))
            {
                refused = "the native layer is " + (nativeLayer == null ? "absent" : nativeLayer.Type + " '" + nativeLayer.Name + "'") + ", not an opaque Material";
            }
            else if (!samAuthored)
            {
                refused = "the SAM value is " + e + ", not an authored value";
            }
            else if (!n.HasNumber)
            {
                refused = "the native value is " + n + ", not a read value";
            }

            if (refused == null && !InUnitInterval(e.Value))
            {
                QuantityResult invalid = EQ.Evaluate(new QuantityInput
                {
                    Definition = input.Definition,
                    FixtureId = input.FixtureId,
                    ObjectKey = input.ObjectKey,
                    Expected = ExpectedValue.Invalid("authored " + property + " " + e.Value.ToString("R", ci) + " of '" + layer.Name + "' is outside [0, 1]"),
                    Native = n,
                    Identity = input.Identity,
                    Alignment = input.Alignment
                }, Configuration);
                return Annotated(invalid, "the SAM value is outside [0, 1]");
            }

            if (refused == null && !InUnitInterval(n.Value))
            {
                return new QuantityResult(input, EquivalenceOutcome.ExportDefect, GateStatus.Fail, "The native " + property + " " + n.Value.ToString("R", ci) + " of " + nativeLayer.Type + " '" + nativeLayer.Name + "' is outside [0, 1]; it is not a comparable optical value (SAM " + e + "). Not OUT-OF-ENGINE-SCOPE (" + OpaqueFaceScope + ").", null, null, null, null, null);
            }

            if (refused == null && result.Outcome != EquivalenceOutcome.Match && result.Outcome != EquivalenceOutcome.WithinTolerance && result.Outcome != EquivalenceOutcome.OutOfTolerance)
            {
                refused = "the shared evaluation is " + result.Outcome + ", not a numeric comparison of two valid values";
            }

            if (refused != null)
            {
                return Annotated(result, refused);
            }

            NumericComparison comparison = EQ.Compare(e.Value, n.Value, input.Definition.Tolerance);
            string reason = UnusedFaceReason(index, count, internalFace, firstSide, kind, role.Adiabatic);
            OutOfEngineScopeRow row = new OutOfEngineScopeRow
            {
                Quantity = input.Definition.Id,
                Key = input.ObjectKey,
                Side = side,
                Layer = index + 1,
                LayerCount = count,
                Property = property,
                SamFace = internalFace ? "internal" : "external",
                SamMaterial = layer.Name,
                NativeObject = nativeLayer.Name,
                SamValue = e.Value,
                NativeValue = n.Value,
                Delta = Math.Abs(n.Value - e.Value),
                NumericVerdict = comparison.Verdict.ToString(),
                Differs = !comparison.IsWithin,
                ScopeReason = reason,
                Amendment = OpaqueFaceScope
            };

            QuantityResult scoped = new QuantityResult(input, result.Outcome, result.Status,
                "OUT-OF-ENGINE-SCOPE (" + OpaqueFaceScope + "): " + reason + "; SAM " + row.SamValue.ToString("R", ci) + ", native " + row.NativeValue.ToString("R", ci) + ", |d| " + row.Delta.ToString("R", ci) + " (" + row.NumericVerdict + "). Informational: EnergyPlus 25.1 does not use this face's optics, so it is not gated; it is not a MATCH and not an accepted limitation.",
                result.Comparison, null, null, null, null, true);
            OutOfEngineScope.Add(row);
            outOfEngineScope.Add(scoped);
            return scoped;
        }

        private static bool InUnitInterval(double value)
        {
            return value >= 0 && value <= 1;
        }

        private static QuantityResult Annotated(QuantityResult result, string refused)
        {
            return new QuantityResult(result.Input, result.Outcome, result.Status, result.Reason + " Not OUT-OF-ENGINE-SCOPE (" + OpaqueFaceScope + "): " + refused + ".", result.Comparison, result.Proposed, result.OwnerDecisionNamed, result.TechnicalBlockerNamed, result.AcceptedBy, result.IsInformational, result.InformationalElements);
        }

        /// <summary>Why EnergyPlus does not use a face (S07 §5.2): the outside face of the outermost layer on an interzone, ground or adiabatic side, or a buried layer face.</summary>
        private static string UnusedFaceReason(int index, int count, bool internalFace, bool firstSide, ElementKind kind, bool adiabatic)
        {
            if (index == 0 && internalFace != firstSide)
            {
                if (kind == ElementKind.Internal)
                {
                    return "interzone surface outer face (the partner side's zone face, gated on that side)";
                }

                if (kind == ElementKind.GroundOrAdiabatic)
                {
                    return adiabatic ? "adiabatic surface outer face" : "ground surface outer face";
                }
            }

            return "buried layer face (layer " + (index + 1) + " of " + count + ", SAM " + (internalFace ? "internal" : "external") + " face)";
        }

        /// <summary>The SAM face of an opaque optical layer property (<c>internal*</c> / <c>external*</c>) and the same property's other face.</summary>
        private static bool TryGetOpaqueFace(string property, out bool internalFace, out string otherFace)
        {
            internalFace = property.StartsWith("internal", StringComparison.Ordinal);
            if (internalFace)
            {
                otherFace = "external" + property.Substring("internal".Length);
                return true;
            }

            if (property.StartsWith("external", StringComparison.Ordinal))
            {
                otherFace = "internal" + property.Substring("external".Length);
                return true;
            }

            otherFace = null;
            return false;
        }

        /// <summary>
        /// Whether SAM face <paramref name="internalFace"/> of the layer at EnergyPlus position <paramref name="index"/>
        /// (outside first, of <paramref name="count"/>) is a face whose optics EnergyPlus uses on this side, derived
        /// from SAM and the side alone (S07 section 1; EnergyPlus 25.1 I/O Reference, Material, Construction):
        /// - the innermost layer's zone face. SAM lists layers inside → outside from its first space, so on the first
        ///   space's side the zone face is SAM's internal face, and on the second space's side SAM's external face;
        /// - the outermost layer's outside face, only on an outdoor surface (an interzone outside face is the other
        ///   side's zone face, compared there; ground and adiabatic outside faces have no optics), which on the first
        ///   (only) side of an outdoor surface is SAM's external face.
        /// </summary>
        private static bool IsExposedOpaqueFace(int index, int count, bool internalFace, bool firstSide, ElementKind kind)
        {
            bool zoneFace = index == count - 1 && internalFace == firstSide;
            bool outdoorFace = index == 0 && kind == ElementKind.External && internalFace != firstSide;
            return zoneFace || outdoorFace;
        }

        /// <summary>
        /// A single-layer opaque construction whose SAM faces differ on an outdoor or interzone side: both faces are
        /// used and one single-sided EnergyPlus material cannot hold both value sets. Named as unsupported (BLOCKED
        /// technical, never PASS); ground and adiabatic sides use only the zone face and are compared as usual.
        /// </summary>
        private void SingleLayerFaces(string side, List<ExpectedLayer> expected, ElementKind kind)
        {
            if (expected.Count != 1 || kind == ElementKind.GroundOrAdiabatic || !(expected[0].Material is OpaqueMaterial))
            {
                return;
            }

            List<string> differing = new List<string>();
            foreach (string property in new[] { "SolarReflectance", "LightReflectance", "Emissivity" })
            {
                ExpectedValue external = ExpectedLayerValue(expected[0], "external" + property);
                ExpectedValue internalValue = ExpectedLayerValue(expected[0], "internal" + property);
                if (external.State == SamInputState.Value && internalValue.State == SamInputState.Value && external.Value != internalValue.Value)
                {
                    differing.Add(property);
                }
            }

            if (differing.Count > 0)
            {
                Unsupported.Add("optics: " + side + ": single-layer construction '" + expected[0].Name + "' needs different " + string.Join(", ", differing) + " on its two used faces; one single-sided EnergyPlus material cannot hold both");
            }
        }

        private static ExpectedValue ExpectedLayerValue(ExpectedLayer layer, string property)
        {
            if (property == "thickness")
            {
                return ExpectedValue.FromValue(layer.Thickness);
            }

            if (!(layer.Material is Core.Material material))
            {
                return ExpectedValue.Invalid("material '" + layer.Name + "' is not in the frozen material library");
            }

            switch (property)
            {
                case "conductivity": return ExpectedValue.FromValue(material.ThermalConductivity);
                case "density": return ExpectedValue.FromValue(material.Density);
                case "specificHeat": return ExpectedValue.FromValue(material.SpecificHeatCapacity);
            }

            Enum parameter = null;
            if (material is TransparentMaterial)
            {
                switch (property)
                {
                    case "solarTransmittance": parameter = TransparentMaterialParameter.SolarTransmittance; break;
                    case "lightTransmittance": parameter = TransparentMaterialParameter.LightTransmittance; break;
                    case "externalSolarReflectance": parameter = TransparentMaterialParameter.ExternalSolarReflectance; break;
                    case "internalSolarReflectance": parameter = TransparentMaterialParameter.InternalSolarReflectance; break;
                    case "externalLightReflectance": parameter = TransparentMaterialParameter.ExternalLightReflectance; break;
                    case "internalLightReflectance": parameter = TransparentMaterialParameter.InternalLightReflectance; break;
                    case "externalEmissivity": parameter = TransparentMaterialParameter.ExternalEmissivity; break;
                    case "internalEmissivity": parameter = TransparentMaterialParameter.InternalEmissivity; break;
                }
            }
            else
            {
                switch (property)
                {
                    case "externalSolarReflectance": parameter = OpaqueMaterialParameter.ExternalSolarReflectance; break;
                    case "internalSolarReflectance": parameter = OpaqueMaterialParameter.InternalSolarReflectance; break;
                    case "externalLightReflectance": parameter = OpaqueMaterialParameter.ExternalLightReflectance; break;
                    case "internalLightReflectance": parameter = OpaqueMaterialParameter.InternalLightReflectance; break;
                    case "externalEmissivity": parameter = OpaqueMaterialParameter.ExternalEmissivity; break;
                    case "internalEmissivity": parameter = OpaqueMaterialParameter.InternalEmissivity; break;
                }
            }

            if (parameter == null)
            {
                return ExpectedValue.Invalid("no SAM source for layer property " + property);
            }

            // NaN is an unstated value (as for the lighting p): never compared as data, its provenance stays S-UNSPEC.
            if (!material.TryGetValue(parameter, out double value))
            {
                return ExpectedValue.Unspecified("not stated");
            }

            return double.IsNaN(value) ? ExpectedValue.Unspecified("NaN (not stated)") : ExpectedValue.FromValue(value);
        }

        /// <summary>
        /// A native layer property in SAM's terms. Opaque: reflectance = 1 - absorptance (no transmittance), emissivity =
        /// thermal absorptance, one value for both faces. Glazing: front is the face towards the outside of the side's
        /// construction, so it is SAM's external face on the first space's side and its internal face on the other.
        /// </summary>
        private static ReaderResult NativeLayerValue(IdfObject layer, string property, bool frontIsExternal)
        {
            if (layer == null)
            {
                return ReaderResult.Absent("no native layer");
            }

            if (layer.Type == "missing")
            {
                return ReaderResult.Absent("the construction names material '" + layer.Name + "', which the IDF does not define");
            }

            if (string.Equals(layer.Type, "Material", StringComparison.OrdinalIgnoreCase))
            {
                switch (property)
                {
                    case "thickness": return NativeModel.Number(layer, 2, "Thickness");
                    case "conductivity": return NativeModel.Number(layer, 3, "Conductivity");
                    case "density": return NativeModel.Number(layer, 4, "Density");
                    case "specificHeat": return NativeModel.Number(layer, 5, "Specific Heat");
                    case "externalSolarReflectance":
                    case "internalSolarReflectance": return OneMinus(NativeModel.Number(layer, 7, "Solar Absorptance"));
                    case "externalLightReflectance":
                    case "internalLightReflectance": return OneMinus(NativeModel.Number(layer, 8, "Visible Absorptance"));
                    case "externalEmissivity":
                    case "internalEmissivity": return NativeModel.Number(layer, 6, "Thermal Absorptance");
                }
            }

            if (string.Equals(layer.Type, "WindowMaterial:Glazing", StringComparison.OrdinalIgnoreCase))
            {
                bool external = property.StartsWith("external", StringComparison.Ordinal);
                bool front = external == frontIsExternal;
                switch (property)
                {
                    case "thickness": return NativeModel.Number(layer, 3, "Thickness");
                    case "conductivity": return NativeModel.Number(layer, 13, "Conductivity");
                    case "solarTransmittance": return NativeModel.Number(layer, 4, "Solar Transmittance");
                    case "lightTransmittance": return NativeModel.Number(layer, 7, "Visible Transmittance");
                    case "externalSolarReflectance":
                    case "internalSolarReflectance": return front ? NativeModel.Number(layer, 5, "Front Side Solar Reflectance") : NativeModel.Number(layer, 6, "Back Side Solar Reflectance");
                    case "externalLightReflectance":
                    case "internalLightReflectance": return front ? NativeModel.Number(layer, 8, "Front Side Visible Reflectance") : NativeModel.Number(layer, 9, "Back Side Visible Reflectance");
                    case "externalEmissivity":
                    case "internalEmissivity": return front ? NativeModel.Number(layer, 11, "Front Side Infrared Hemispherical Emissivity") : NativeModel.Number(layer, 12, "Back Side Infrared Hemispherical Emissivity");
                }
            }

            return ReaderResult.Unreadable("native layer " + layer.Type + " '" + layer.Name + "': property " + property + " is not read by the S09 reader");
        }

        private static ReaderResult OneMinus(ReaderResult absorptance)
        {
            return absorptance.HasNumber ? ReaderResult.FromValue(1 - absorptance.Value) : absorptance;
        }

        private static ReaderResult NativeU(List<IdfObject> layers, HeatFlowDirection direction, ElementKind kind)
        {
            if (layers == null || layers.Count == 0)
            {
                return ReaderResult.Absent("no native construction");
            }

            List<LayerResistance> resistances = new List<LayerResistance>();
            foreach (IdfObject layer in layers)
            {
                ReaderResult t = NativeLayerValue(layer, "thickness", true);
                ReaderResult k = NativeLayerValue(layer, "conductivity", true);
                if (!t.HasNumber || !k.HasNumber)
                {
                    return t.HasNumber ? k : t;
                }

                resistances.Add(LayerResistance.Solid(t.Value, k.Value));
            }

            ExpectedValue u = layers.Any(x => string.Equals(x.Type, "WindowMaterial:Glazing", StringComparison.OrdinalIgnoreCase)) ? EQ.ExpectedGlazingU(resistances) : EQ.ExpectedOpaqueU(resistances, direction, kind);
            return u.State == SamInputState.Value ? ReaderResult.FromValue(u.Value) : ReaderResult.Unreadable("native U not derivable: " + u);
        }

        private void CompareAperture(ExpectedSpace space, ExpectedAperture aperture, NativeSurface host, IdentityStatus hostIdentity, bool firstSide, List<IdfObject> matchedSubSurfaces)
        {
            string key = Keys.Aperture(aperture.Guid, space.Guid);
            IdfObject sub = null;
            List<double[]> pane = null;
            List<double[]> outline = null;
            IdfObject frame = null;
            IdentityStatus identity = IdentityStatus.Mismatch;
            if (host != null)
            {
                List<IdfObject> candidates = native.Idf("FenestrationSurface:Detailed").Where(x => string.Equals(x.Value(3, "Building Surface Name", out string _), host.Object.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                List<List<double[]>> outlines = new List<List<double[]>>();
                List<List<double[]>> panes = new List<List<double[]>>();
                foreach (IdfObject candidate in candidates)
                {
                    List<double[]> vertices = native.Vertices(candidate, 9, native.IdfByName("Zone", host.Object.Value(3)), out string _);
                    IdfObject candidateFrame = native.IdfByName("WindowProperty:FrameAndDivider", candidate.Value(6, "Frame and Divider Name", out string _));
                    ReaderResult width = NativeModel.Number(candidateFrame, 1, "Frame Width");
                    panes.Add(vertices);
                    outlines.Add(vertices == null ? null : candidateFrame == null ? vertices : width.HasNumber ? OsGeometry.OffsetOutwards(vertices, width.Value) : null);
                }

                // Identity by geometry: the native opening outline (the pane grown by the native frame width, as EnergyPlus
                // builds the frame) is the SAM aperture outline. The name suffix may only contradict it.
                GeometryMatchResult match = EQ.MatchPolygons(new List<Polygon3D> { aperture.Outer }, outlines.Select(x => OsGeometry.ToPolygon3D(x)).ToList());
                GeometryPair pair = match.PairForExpected(0);
                if (pair != null)
                {
                    sub = candidates[pair.NativeIndex];
                    pane = panes[pair.NativeIndex];
                    outline = outlines[pair.NativeIndex];
                    frame = native.IdfByName("WindowProperty:FrameAndDivider", sub.Value(6, "Frame and Divider Name", out string _));
                    matchedSubSurfaces.Add(sub);
                    bool carrierAgrees = sub.Name.EndsWith("_" + aperture.Guid.ToString("N").Substring(0, 8), StringComparison.OrdinalIgnoreCase);
                    identity = hostIdentity == IdentityStatus.Verified && carrierAgrees ? IdentityStatus.Verified : IdentityStatus.Mismatch;
                }

                Supporting.Add(key + ": " + (sub == null ? "no native subsurface outline matches the SAM aperture" : "native subsurface '" + sub.Name + "' " + (frame == null ? "(no frame)" : "with frame '" + frame.Name + "'") + ", identity " + identity));
            }

            double paneArea = pane == null ? double.NaN : OsGeometry.Area(pane);
            Add("1b", key + ".pane", aperture.PaneArea, sub == null ? ReaderResult.Absent("no native subsurface") : ReaderResult.FromValue(paneArea), identity);

            // EnergyPlus has no frame without a WindowProperty:FrameAndDivider: its frame area is then 0.
            ReaderResult frameArea = sub == null ? ReaderResult.Absent("no native subsurface") : frame == null ? ReaderResult.FromValue(0) : outline == null ? ReaderResult.Unreadable("the frame outline could not be built") : ReaderResult.FromValue(OsGeometry.Area(outline) - paneArea);
            Add("1b", key + ".frame", aperture.FrameArea, frameArea, identity);

            if (aperture.FrameLayers.Count > 0)
            {
                double resistance = 0;
                foreach (ExpectedLayer layer in aperture.FrameLayers)
                {
                    double conductivity = (layer.Material as Core.Material)?.ThermalConductivity ?? double.NaN;
                    resistance += layer.Thickness / conductivity;
                }

                ExpectedValue conductance = double.IsNaN(resistance) || resistance <= 0 ? ExpectedValue.Invalid("frame layers give no resistance") : ExpectedValue.FromValue(1 / resistance);
                Add("T-FRAME.conductance", key + ".frameConductance", conductance, NativeModel.Number(frame, 4, "Frame Conductance"), identity);
                Supporting.Add(key + ": native frame width " + NativeModel.Number(frame, 1, "Frame Width").Value.ToString("R", ci) + " m; SAM T-FRAME OpenStudio rule: DefaultFrameWidth, else the frame layer thickness (" + aperture.FrameRule + ")");
            }

            List<ExpectedLayer> expectedLayers = firstSide ? Enumerable.Reverse(aperture.PaneLayers).ToList() : aperture.PaneLayers.ToList();
            List<IdfObject> nativeLayers = LayersOf(sub?.Value(2, "Construction Name", out string _), key, sub != null);
            CompareLayers(key, expectedLayers, nativeLayers, identity, true, firstSide, ElementKind.External);
            Add("3c.U", key + ".U", EQ.ExpectedGlazingU(expectedLayers.Select(x => LayerResistance.Solid(x.Thickness, (x.Material as Core.Material)?.ThermalConductivity ?? double.NaN))), NativeU(nativeLayers, HeatFlowDirection.Horizontal, ElementKind.External), identity);
        }

        private void Extras(List<string> matchedSpaces, List<IdfObject> matchedSurfaces, List<IdfObject> matchedSubSurfaces)
        {
            foreach (IdfObject space in native.Idf("Space").Where(x => !matchedSpaces.Contains(x.Name, StringComparer.OrdinalIgnoreCase)))
            {
                Add("1a", "native:space:" + space.Name, ExpectedValue.Unspecified("no SAM space"), ReaderResult.Unreadable("native space with no SAM space"), IdentityStatus.Mismatch);
            }

            foreach (IdfObject surface in native.Idf("BuildingSurface:Detailed").Where(x => !matchedSurfaces.Contains(x)))
            {
                Add("1b", "native:surface:" + surface.Name, ExpectedValue.Unspecified("no SAM panel"), ReaderResult.Unreadable("native surface with no SAM panel"), IdentityStatus.Mismatch);
            }

            foreach (IdfObject sub in native.Idf("FenestrationSurface:Detailed").Where(x => !matchedSubSurfaces.Contains(x)))
            {
                Add("1b", "native:subsurface:" + sub.Name, ExpectedValue.Unspecified("no SAM aperture"), ReaderResult.Unreadable("native subsurface with no SAM aperture"), IdentityStatus.Mismatch);
            }

            // Objects that add air, heat or geometry SAM never stated for these fixtures.
            foreach (string type in new[] { "ZoneVentilation:DesignFlowRate", "ZoneVentilation:WindandStackOpenArea", "ZoneMixing", "ZoneCrossMixing", "AirflowNetwork:SimulationControl", "AirLoopHVAC", "GasEquipment", "HotWaterEquipment", "SteamEquipment", "OtherEquipment", "ZoneInfiltration:EffectiveLeakageArea", "ZoneInfiltration:FlowCoefficient", "InternalMass", "Wall:Detailed", "RoofCeiling:Detailed", "Floor:Detailed", "Window", "Door", "GlazedDoor", "Window:Interzone", "Door:Interzone", "GlazedDoor:Interzone", "Construction:AirBoundary" })
            {
                foreach (IdfObject o in native.Idf(type))
                {
                    Consistency.Add("native extra: " + type + " '" + o.Name + "' (SAM states no such object)");
                }
            }

            foreach (string type in new[] { "Sizing:Zone", "Shading:Building:Detailed", "Shading:Site:Detailed", "Shading:Zone:Detailed" })
            {
                int count = native.Idf(type).Count();
                if (count > 0)
                {
                    Supporting.Add("native " + type + ": " + count + " object(s)");
                }
            }
        }

        // ---- orientation and multiplicity -------------------------------------------------------------------------------

        /// <summary>
        /// The native building orientation against SAM intent. SAM states the building rotation only as
        /// AnalyticalModelParameter.NorthAngle (radians, clockwise from true North); unstated, the writer leaves the
        /// OpenStudio/EnergyPlus default 0. EnergyPlus 25.1 (GlobalGeometryRules, Zone): with Relative coordinates the
        /// vertices are rotated by the Building North Axis and each Zone's Direction of Relative North (and moved by the
        /// zone origin); with World coordinates neither north axis is used. S07 has no orientation row or tolerance, so only
        /// the unrotated configuration is supported: SAM 0 or unstated, and a native effective rotation of exactly 0. A
        /// native rotation SAM does not state FAILs; anything the reader cannot interpret blocks (BLOCKED technical).
        /// </summary>
        private void Orientation()
        {
            List<string> facts = new List<string>();
            double samDegrees = double.NaN;
            if (model.TryGetValue(AnalyticalModelParameter.NorthAngle, out double radians) && !double.IsNaN(radians))
            {
                samDegrees = radians * 180.0 / Math.PI;
            }

            facts.Add("SAM North Angle " + (double.IsNaN(samDegrees) ? "unstated (writer default 0)" : samDegrees.ToString("R", ci) + " deg"));
            if (!double.IsNaN(samDegrees) && samDegrees != 0)
            {
                Unsupported.Add("orientation: SAM states a North Angle of " + samDegrees.ToString("R", ci) + " deg; S07 has no orientation row or tolerance, so S09 supports only an unrotated model");
            }

            List<IdfObject> rules = native.Idf("GlobalGeometryRules").ToList();
            string problem = null;
            string system = rules.Count == 1 ? rules[0].Value(2, "Coordinate System", out problem) : null;
            bool relative = string.Equals(system, "Relative", StringComparison.OrdinalIgnoreCase);
            bool world = string.Equals(system, "World", StringComparison.OrdinalIgnoreCase) || string.Equals(system, "Absolute", StringComparison.OrdinalIgnoreCase);
            if (!relative && !world)
            {
                Unsupported.Add("orientation: " + (rules.Count != 1 ? "the IDF has " + rules.Count + " GlobalGeometryRules objects" : "GlobalGeometryRules Coordinate System '" + system + "' is not Relative or World" + (problem == null ? string.Empty : " (" + problem + ")")));
            }

            facts.Add("Coordinate System " + (system ?? "(none)"));

            List<IdfObject> buildings = native.Idf("Building").ToList();
            ReaderResult north = buildings.Count == 1 ? NativeModel.Number(buildings[0], 1, "North Axis") : ReaderResult.Unreadable("the IDF has " + buildings.Count + " Building objects");
            double northDegrees = Degrees(north, "IDF Building North Axis", facts);

            // The OSM must state the same building rotation as the IDF it was translated to.
            IdfObject osmBuilding = native.Snapshot.OsmOf("OS:Building").SingleOrDefault();
            string osmNorth = osmBuilding?.ValueByLabel("North Axis", out bool _);
            double osmDegrees = string.IsNullOrWhiteSpace(osmNorth) ? 0 : double.TryParse(osmNorth, NumberStyles.Float, ci, out double parsed) ? parsed : double.NaN;
            if (osmBuilding == null || double.IsNaN(osmDegrees))
            {
                Unsupported.Add("orientation: the OSM " + (osmBuilding == null ? "has no single OS:Building" : "OS:Building North Axis '" + osmNorth + "' is not a number"));
            }
            else if (!double.IsNaN(northDegrees) && osmDegrees != northDegrees)
            {
                Consistency.Add("orientation: OS:Building North Axis " + osmDegrees.ToString("R", ci) + " deg in the OSM, Building North Axis " + northDegrees.ToString("R", ci) + " deg in the IDF");
            }

            foreach (IdfObject osmSpace in native.Snapshot.OsmOf("OS:Space"))
            {
                foreach (string label in new[] { "Direction of Relative North", "X Origin", "Y Origin", "Z Origin" })
                {
                    string text = osmSpace.ValueByLabel(label, out bool _);
                    if (!string.IsNullOrWhiteSpace(text) && (!double.TryParse(text, NumberStyles.Float, ci, out double v) || v != 0))
                    {
                        Unsupported.Add("orientation: OSM OS:Space '" + osmSpace.Value(1) + "' " + label + " is '" + text + "' (a space transformation is not verified by S09)");
                    }
                }
            }

            double expected = double.IsNaN(samDegrees) ? 0 : samDegrees;
            foreach (IdfObject zone in native.Idf("Zone"))
            {
                double zoneDegrees = Degrees(NativeModel.Number(zone, 1, "Direction of Relative North"), "Zone '" + zone.Name + "' Direction of Relative North", null);
                if (world && !double.IsNaN(zoneDegrees) && zoneDegrees != 0)
                {
                    Unsupported.Add("orientation: Zone '" + zone.Name + "' states Direction of Relative North " + zoneDegrees.ToString("R", ci) + " deg under World coordinates, where EnergyPlus does not use it");
                }
                else if (relative && !double.IsNaN(zoneDegrees) && !double.IsNaN(northDegrees) && expected == 0 && northDegrees + zoneDegrees != 0)
                {
                    Consistency.Add("orientation: Zone '" + zone.Name + "' is rotated by " + (northDegrees + zoneDegrees).ToString("R", ci) + " deg (Building North Axis " + northDegrees.ToString("R", ci) + " + Direction of Relative North " + zoneDegrees.ToString("R", ci) + "; Relative coordinates); SAM states no rotation");
                }
            }

            if (world && !double.IsNaN(northDegrees) && northDegrees != 0)
            {
                Unsupported.Add("orientation: Building North Axis " + northDegrees.ToString("R", ci) + " deg under World coordinates, where EnergyPlus does not use it for the geometry");
            }
            else if (relative && !double.IsNaN(northDegrees) && expected == 0 && northDegrees != 0 && !native.Idf("Zone").Any())
            {
                Consistency.Add("orientation: Building North Axis " + northDegrees.ToString("R", ci) + " deg; SAM states no rotation");
            }

            Supporting.Add("orientation: " + string.Join("; ", facts) + (Unsupported.Any(x => x.StartsWith("orientation", StringComparison.Ordinal)) || Consistency.Any(x => x.StartsWith("orientation", StringComparison.Ordinal)) ? string.Empty : ": the native building is unrotated, as SAM states"));
        }

        /// <summary>A rotation field in degrees: blank is the EnergyPlus default 0; unreadable, NaN or non-finite blocks the fixture.</summary>
        private double Degrees(ReaderResult field, string what, List<string> facts)
        {
            facts?.Add(what + (field.Kind == ReaderKind.Absent ? " blank (EnergyPlus default 0)" : field.HasNumber ? " " + field.Value.ToString("R", ci) + " deg" : " " + field.Kind));
            if (field.Kind == ReaderKind.Absent)
            {
                return 0;
            }

            if (!field.HasNumber || double.IsInfinity(field.Value))
            {
                Unsupported.Add("orientation: " + what + " is " + field.Kind + (field.Detail == null ? string.Empty : " (" + field.Detail + ")"));
                return double.NaN;
            }

            return field.Value;
        }

        /// <summary>
        /// Native multiplicity against SAM intent. SAM has no zone, zone-list or aperture multiplier: each SAM space and
        /// aperture is one physical object, and every S07 row compares that one object. EnergyPlus 25.1: a Zone Multiplier
        /// multiplies the zone's floor area, loads and internal-gain energy (default 1); a ZoneGroup multiplies a zone list
        /// on top of it; a subsurface Multiplier multiplies the window area, frame and divider included (default 1,
        /// non-integers truncated). Blank is read as that documented default, never assumed. Any effective multiplier other
        /// than 1 contradicts SAM and FAILs; a field the reader cannot interpret blocks (BLOCKED technical). The OSM and IDF
        /// multipliers are compared only through each IDF object's one OSM counterpart (<see cref="OsmCounterpart"/>).
        /// </summary>
        private void Multiplicity()
        {
            int zones = 0, subSurfaces = 0, matchedZones = 0, matchedSubSurfaces = 0;
            foreach (IdfObject zone in native.Idf("Zone"))
            {
                zones++;
                double multiplier = Multiplier(NativeModel.Number(zone, 6, "Multiplier"), "IDF Zone '" + zone.Name + "' Multiplier");
                if (multiplier != 1 && !double.IsNaN(multiplier))
                {
                    Consistency.Add("multiplicity: IDF Zone '" + zone.Name + "' Multiplier " + multiplier.ToString("R", ci) + ": EnergyPlus multiplies the zone's floor area, loads and internal gains; SAM states one space and no multiplier");
                }

                IdfObject osmZone = OsmCounterpart("OS:ThermalZone", "IDF Zone", zone.Name);
                double osmMultiplier = osmZone == null ? double.NaN : OsmMultiplier(osmZone, 2, "OSM OS:ThermalZone '" + zone.Name + "' Multiplier");
                if (osmZone != null && !double.IsNaN(osmMultiplier) && !double.IsNaN(multiplier) && osmMultiplier != multiplier)
                {
                    Consistency.Add("multiplicity: zone '" + zone.Name + "' Multiplier " + osmMultiplier.ToString("R", ci) + " in the OSM, " + multiplier.ToString("R", ci) + " in the IDF");
                }

                matchedZones += osmZone == null ? 0 : 1;
            }

            foreach (IdfObject group in native.Idf("ZoneGroup"))
            {
                double multiplier = Multiplier(NativeModel.Number(group, 2, "Zone List Multiplier"), "IDF ZoneGroup '" + group.Name + "' Zone List Multiplier");
                if (multiplier != 1 && !double.IsNaN(multiplier))
                {
                    Consistency.Add("multiplicity: IDF ZoneGroup '" + group.Name + "' multiplies zone list '" + group.Value(1) + "' by " + multiplier.ToString("R", ci) + "; SAM states no multiplier");
                }
            }

            foreach (IdfObject sub in native.Idf("FenestrationSurface:Detailed"))
            {
                subSurfaces++;
                double multiplier = Multiplier(NativeModel.Number(sub, 7, "Multiplier"), "IDF FenestrationSurface:Detailed '" + sub.Name + "' Multiplier");
                if (multiplier != 1 && !double.IsNaN(multiplier))
                {
                    Consistency.Add("multiplicity: IDF FenestrationSurface:Detailed '" + sub.Name + "' Multiplier " + multiplier.ToString("R", ci) + ": EnergyPlus multiplies its area (frame and divider included); SAM states one aperture and no multiplier");
                }

                IdfObject osmSub = OsmCounterpart("OS:SubSurface", "IDF FenestrationSurface:Detailed", sub.Name);
                double osmMultiplier = osmSub == null ? double.NaN : OsmMultiplier(osmSub, 8, "OSM OS:SubSurface '" + sub.Name + "' Multiplier");
                if (osmSub != null && !double.IsNaN(osmMultiplier) && !double.IsNaN(multiplier) && osmMultiplier != multiplier)
                {
                    Consistency.Add("multiplicity: subsurface '" + sub.Name + "' Multiplier " + osmMultiplier.ToString("R", ci) + " in the OSM, " + multiplier.ToString("R", ci) + " in the IDF");
                }

                matchedSubSurfaces += osmSub == null ? 0 : 1;
            }

            Supporting.Add("multiplicity: " + zones + " IDF zone(s) (" + matchedZones + " with their OS:ThermalZone), " + native.Idf("ZoneGroup").Count() + " ZoneGroup(s), " + subSurfaces + " IDF subsurface(s) (" + matchedSubSurfaces + " with their OS:SubSurface) read; blank multipliers are the EnergyPlus default 1" + (Consistency.Any(x => x.StartsWith("multiplicity", StringComparison.Ordinal)) || Unsupported.Any(x => x.StartsWith("multiplicity", StringComparison.Ordinal)) ? string.Empty : "; every effective multiplier is 1, as SAM states"));
        }

        /// <summary>
        /// The one OSM object of <paramref name="type"/> the IDF object <paramref name="name"/> was translated from (the
        /// translator keeps the name). Consistency is claimed only against that unambiguous counterpart; a missing object is
        /// never read as a present one with default fields. When every OSM object of the type has a readable Name and none
        /// is <paramref name="name"/>, the counterpart is proven missing: Consistency (FAIL). A duplicate name, or an
        /// object whose Name the reader cannot read, leaves the identity unproven: Unsupported (BLOCKED technical). Null
        /// unless exactly one counterpart is identified.
        /// </summary>
        private IdfObject OsmCounterpart(string type, string idfWhat, string name)
        {
            List<IdfObject> matches = new List<IdfObject>();
            List<string> unreadable = new List<string>();
            int count = 0;
            foreach (IdfObject o in native.Snapshot.OsmOf(type))
            {
                count++;
                string osmName = o.Value(1, "Name", out string problem);
                if (problem != null || string.IsNullOrWhiteSpace(osmName))
                {
                    unreadable.Add(problem ?? type + " " + o.Value(0) + " has no Name");
                }
                else if (string.Equals(osmName, name, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(o);
                }
            }

            if (unreadable.Count > 0)
            {
                Unsupported.Add("multiplicity: the OSM counterpart of " + idfWhat + " '" + name + "' cannot be identified: " + string.Join("; ", unreadable));
                return null;
            }

            if (matches.Count > 1)
            {
                Unsupported.Add("multiplicity: the OSM counterpart of " + idfWhat + " '" + name + "' is ambiguous: " + matches.Count + " " + type + " objects carry that name");
                return null;
            }

            if (matches.Count == 0)
            {
                Consistency.Add("multiplicity: " + idfWhat + " '" + name + "' has no " + type + " in the OSM (none of its " + count + " " + type + " object(s) has that name); the OSM and IDF disagree");
                return null;
            }

            return matches[0];
        }

        /// <summary>A multiplier field: blank is the documented default 1; a non-integer, non-positive or unreadable value blocks the fixture.</summary>
        private double Multiplier(ReaderResult field, string what)
        {
            if (field.Kind == ReaderKind.Absent)
            {
                return 1;
            }

            if (!field.HasNumber || field.Value <= 0 || field.Value != Math.Floor(field.Value) || double.IsInfinity(field.Value))
            {
                Unsupported.Add("multiplicity: " + what + " is " + field.Kind + (field.HasNumber ? " " + field.Value.ToString("R", ci) : field.Detail == null ? string.Empty : " (" + field.Detail + ")") + ": not a positive integer");
                return double.NaN;
            }

            return field.Value;
        }

        /// <summary>
        /// The Multiplier of an OSM counterpart, read at its known position (<paramref name="index"/>, after the handle)
        /// under the file's own label. Blank is the documented default 1; a field the object does not have, or one labelled
        /// otherwise, is an unknown layout and blocks the fixture (never read as the default).
        /// </summary>
        private double OsmMultiplier(IdfObject o, int index, string what)
        {
            if (index >= o.Fields.Count)
            {
                Unsupported.Add("multiplicity: " + what + " is missing: " + o.Type + " '" + o.Value(1) + "' has " + o.Fields.Count + " fields, so the layout is not the one S09 reads");
                return double.NaN;
            }

            return Multiplier(NativeModel.Number(o, index, "Multiplier"), what);
        }

        // ---- internal condition -----------------------------------------------------------------------------------------

        /// <summary>The native spaces a load object applies to (its target may be a Space, SpaceList, Zone or ZoneList).</summary>
        private HashSet<string> Targets(string target)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(target))
            {
                return result;
            }

            IdfObject spaceList = native.IdfByName("SpaceList", target);
            if (spaceList != null)
            {
                foreach (IdfField field in spaceList.Fields.Skip(1))
                {
                    result.Add(field.Value);
                }

                return result;
            }

            if (native.IdfByName("Space", target) != null)
            {
                result.Add(target);
                return result;
            }

            List<string> zones = new List<string>();
            IdfObject zoneList = native.IdfByName("ZoneList", target);
            if (zoneList != null)
            {
                zones.AddRange(zoneList.Fields.Skip(1).Select(x => x.Value));
            }
            else if (native.IdfByName("Zone", target) != null)
            {
                zones.Add(target);
            }

            foreach (IdfObject space in native.Idf("Space").Where(x => zones.Contains(x.Value(1), StringComparer.OrdinalIgnoreCase)))
            {
                result.Add(space.Name);
            }

            return result;
        }

        private List<IdfObject> LoadsOf(string type, string nativeSpace)
        {
            return nativeSpace == null ? new List<IdfObject>() : native.Idf(type).Where(x => Targets(x.Value(1)).Contains(nativeSpace)).ToList();
        }

        private static double Parameter(ExpectedSpace space, string name)
        {
            return space.Parameters.TryGetValue(name, out double value) ? value : double.NaN;
        }

        private static double People(ExpectedSpace space)
        {
            if (!double.IsNaN(space.Occupancy))
            {
                return space.Occupancy;
            }

            double areaPerPerson = Parameter(space, "AreaPerPerson");
            return double.IsNaN(areaPerPerson) || areaPerPerson <= 0 || double.IsNaN(space.StoredArea) ? double.NaN : space.StoredArea / areaPerPerson;
        }

        /// <summary>A SAM gain total in W: perArea x stored Area + absolute + perPerson x people (S07 row 4b, as S08).</summary>
        private static double GainTotal(ExpectedSpace space, string perArea, string absolute, string perPerson)
        {
            double[] terms =
            {
                perArea == null ? double.NaN : Parameter(space, perArea) * space.StoredArea,
                absolute == null ? double.NaN : Parameter(space, absolute),
                perPerson == null ? double.NaN : Parameter(space, perPerson) * People(space)
            };

            return terms.All(double.IsNaN) ? double.NaN : terms.Where(x => !double.IsNaN(x)).Sum();
        }

        private void CompareInternalCondition(ExpectedSpace space, string nativeSpace, IdfObject zone, ReaderResult floorArea, ReaderResult volume, IdentityStatus identity, ModelInterpretation interpretation)
        {
            Guid g = space.Guid;
            double people = People(space);

            // --- occupancy ---
            List<IdfObject> peopleObjects = LoadsOf("People", nativeSpace);
            IdfObject peopleObject = peopleObjects.Count == 1 ? peopleObjects[0] : null;
            ReaderResult peopleDensity = peopleObjects.Count > 1 ? ReaderResult.Unreadable(peopleObjects.Count + " People objects apply to the space") : PeopleDensity(peopleObject, floorArea);
            Add("4a.people", Keys.Ic(g, "people"), EQ.ExpectedPeopleDensity(space.Occupancy, Parameter(space, "AreaPerPerson"), space.StoredArea), peopleDensity, identity);

            NativeSchedule activity = native.IdfSchedule(peopleObject?.Value(9, "Activity Level Schedule Name", out string _));
            ReaderResult activityLevel = Constant(activity, "activity level");
            ReaderResult shf = NativeModel.Number(peopleObject, 8, "Sensible Heat Fraction");
            ReaderResult perPersonTotal = EQ.Multiply(peopleDensity, activityLevel);
            ReaderResult sensibleDensity = EQ.Multiply(perPersonTotal, shf);
            ReaderResult latentDensity = shf.HasNumber ? EQ.Multiply(perPersonTotal, ReaderResult.FromValue(1 - shf.Value)) : EQ.Multiply(perPersonTotal, shf);
            Gain("occupancySensible", g, GainTotal(space, null, null, "OccupancySensibleGainPerPerson"), space.StoredArea, sensibleDensity, floorArea, identity, peopleObject != null);
            Gain("occupancyLatent", g, GainTotal(space, null, null, "OccupancyLatentGainPerPerson"), space.StoredArea, latentDensity, floorArea, identity, peopleObject != null);
            Fraction("occupancy.radiant", g, Parameter(space, "OccupancyRadiantProportion"), 0.3, NativeModel.Number(peopleObject, 7, "Fraction Radiant"), identity);
            Schedule("occupancy", g, space, "ticOSG", peopleObject == null ? null : peopleObject.Value(2, "Number of People Schedule Name", out string _), identity, null);

            // --- lighting ---
            List<IdfObject> lights = LoadsOf("Lights", nativeSpace);
            IdfObject light = lights.Count == 1 ? lights[0] : null;
            ReaderResult lightDensity = lights.Count > 1 ? ReaderResult.Unreadable(lights.Count + " Lights objects apply to the space") : DesignDensity(light, floorArea, peopleDensity, 4, 5, 6, "Lighting Level", "Watts per Floor Area", "Watts per Person", 1);
            Gain("lighting", g, GainTotal(space, "LightingGainPerArea", "LightingGain", "LightingGainPerPerson"), space.StoredArea, lightDensity, floorArea, identity, light != null);
            LightingSplit(g, Parameter(space, "LightingRadiantProportion"), light, identity);
            Schedule("lighting", g, space, "ticLG", light?.Value(2, "Schedule Name", out string _), identity, null);

            // --- equipment: the sensible instance (no latent fraction) and the latent one (Fraction Latent = 1) ---
            List<IdfObject> equipment = LoadsOf("ElectricEquipment", nativeSpace);
            List<IdfObject> sensible = equipment.Where(x => { ReaderResult fl = NativeModel.Number(x, 7, "Fraction Latent"); return fl.Kind == ReaderKind.Absent || fl.Kind == ReaderKind.Zero; }).ToList();
            List<IdfObject> latent = equipment.Where(x => { ReaderResult fl = NativeModel.Number(x, 7, "Fraction Latent"); return fl.HasNumber && fl.Value == 1; }).ToList();
            foreach (IdfObject other in equipment.Except(sensible).Except(latent))
            {
                Consistency.Add(Keys.Space(g) + ": ElectricEquipment '" + other.Name + "' mixes sensible and latent gain (not a SAM writer form)");
            }

            IdfObject sensibleObject = sensible.Count == 1 ? sensible[0] : null;
            IdfObject latentObject = latent.Count == 1 ? latent[0] : null;
            ReaderResult sensibleEquipment = sensible.Count > 1 ? ReaderResult.Unreadable(sensible.Count + " sensible ElectricEquipment objects apply") : EQ.Multiply(DesignDensity(sensibleObject, floorArea, peopleDensity, 4, 5, 6, "Design Level", "Watts per Floor Area", "Watts per Person", 1), OneMinusLost(sensibleObject));
            ReaderResult latentEquipment = latent.Count > 1 ? ReaderResult.Unreadable(latent.Count + " latent ElectricEquipment objects apply") : DesignDensity(latentObject, floorArea, peopleDensity, 4, 5, 6, "Design Level", "Watts per Floor Area", "Watts per Person", 1);
            Gain("equipmentSensible", g, GainTotal(space, "EquipmentSensibleGainPerArea", "EquipmentSensibleGain", "EquipmentSensibleGainPerPerson"), space.StoredArea, sensibleEquipment, floorArea, identity, sensibleObject != null);
            Gain("equipmentLatent", g, GainTotal(space, "EquipmentLatentGainPerArea", "EquipmentLatentGain", null), space.StoredArea, latentEquipment, floorArea, identity, latentObject != null);
            Fraction("equipment.radiant", g, Parameter(space, "EquipmentRadiantProportion"), 0, NativeModel.Number(sensibleObject, 8, "Fraction Radiant"), identity);
            Schedule("equipmentSensible", g, space, "ticESG", sensibleObject?.Value(2, "Schedule Name", out string _), identity, null);
            Schedule("equipmentLatent", g, space, "ticELG", latentObject?.Value(2, "Schedule Name", out string _), identity, null);

            // --- infiltration (7a, 7b) ---
            List<IdfObject> infiltrations = LoadsOf("ZoneInfiltration:DesignFlowRate", nativeSpace);
            IdfObject infiltration = infiltrations.Count == 1 ? infiltrations[0] : null;
            ReaderResult ach = infiltrations.Count > 1 ? ReaderResult.Unreadable(infiltrations.Count + " infiltration objects apply") : Infiltration(infiltration, g);
            double expectedAch = Parameter(space, "InfiltrationAirChangesPerHour");
            Add("7a", Keys.Ic(g, "infiltration"), ExpectedValue.FromValue(expectedAch), ach, identity);
            Add("7b", Keys.Ic(g, "infiltration"), EQ.ExpectedInfiltrationFlow(expectedAch, space.ShellVolume?.State == SamInputState.Value ? space.ShellVolume.Value : double.NaN), EQ.Multiply(ach, volume, 1 / 3600.0), identity, interpretation);
            Schedule("infiltration", g, space, "ticI", infiltration?.Value(2, "Schedule Name", out string _), identity, null);

            // --- setpoints (5) ---
            IdfObject dual = null;
            IdfObject thermostat = native.Idf("ZoneControl:Thermostat").FirstOrDefault(x => string.Equals(x.Value(1, "Zone or ZoneList Name", out string _), zone?.Name, StringComparison.OrdinalIgnoreCase));
            if (thermostat != null)
            {
                ReaderResult controlType = Constant(native.IdfSchedule(thermostat.Value(2, "Control Type Schedule Name", out string _)), "thermostat control type");
                for (int i = 3; i + 1 < thermostat.Fields.Count; i += 2)
                {
                    if (string.Equals(thermostat.Value(i), "ThermostatSetpoint:DualSetpoint", StringComparison.OrdinalIgnoreCase))
                    {
                        dual = native.IdfByName("ThermostatSetpoint:DualSetpoint", thermostat.Value(i + 1));
                    }
                }

                if (!controlType.HasNumber || controlType.Value != 4)
                {
                    Consistency.Add(Keys.Space(g) + ": thermostat control type is not constant 4 (dual setpoint): " + controlType.Kind + " " + controlType.Value.ToString("R", ci));
                }
            }

            Setpoint("heating", "5.temperature", g, space, "ticLL", dual?.Value(1, "Heating Setpoint Temperature Schedule Name", out string _), identity);
            Setpoint("cooling", "5.temperature", g, space, "ticUL", dual?.Value(2, "Cooling Setpoint Temperature Schedule Name", out string _), identity);

            IdfObject humidistat = native.Idf("ZoneControl:Humidistat").FirstOrDefault(x => string.Equals(x.Value(1, "Zone Name", out string _), zone?.Name, StringComparison.OrdinalIgnoreCase));
            Setpoint("humidification", "5.humidity", g, space, "ticHLL", humidistat?.Value(2, "Humidifying Relative Humidity Setpoint Schedule Name", out string _), identity);
            Setpoint("dehumidification", "5.humidity", g, space, "ticHUL", humidistat?.Value(3, "Dehumidifying Relative Humidity Setpoint Schedule Name", out string _), identity);

            // --- ventilation (6 ventilation, 8a, 8b) ---
            CompareVentilation(space, nativeSpace, zone, floorArea, volume, peopleDensity, identity, interpretation);
        }

        private ReaderResult PeopleDensity(IdfObject peopleObject, ReaderResult floorArea)
        {
            if (peopleObject == null)
            {
                return ReaderResult.Absent("no People object applies to the space");
            }

            string method = peopleObject.Value(3, "Number of People Calculation Method", out string problem);
            if (problem != null)
            {
                return ReaderResult.Unreadable(problem);
            }

            switch ((method ?? string.Empty).ToLowerInvariant())
            {
                case "people/area":
                    return NativeModel.Number(peopleObject, 5, "People per Floor Area");
                case "area/person":
                    ReaderResult areaPerPerson = NativeModel.Number(peopleObject, 6, "Floor Area per Person");
                    return areaPerPerson.HasNumber && areaPerPerson.Value > 0 ? ReaderResult.FromValue(1 / areaPerPerson.Value) : areaPerPerson;
                case "people":
                    ReaderResult count = NativeModel.Number(peopleObject, 4, "Number of People");
                    return count.HasNumber && floorArea.HasNumber && floorArea.Value > 0 ? ReaderResult.FromValue(count.Value / floorArea.Value) : ReaderResult.Unreadable("People count without a native floor area");
                default:
                    return ReaderResult.Unreadable("People calculation method '" + method + "' is not read");
            }
        }

        /// <summary>A design level in W/m2 by the object's calculation method (field 3).</summary>
        private static ReaderResult DesignDensity(IdfObject o, ReaderResult floorArea, ReaderResult peopleDensity, int levelField, int perAreaField, int perPersonField, string levelLabel, string perAreaLabel, string perPersonLabel, double scale)
        {
            if (o == null)
            {
                return ReaderResult.Absent("no native object applies to the space");
            }

            string method = o.Value(3, "Design Level Calculation Method", out string problem);
            if (problem != null)
            {
                return ReaderResult.Unreadable(problem);
            }

            switch ((method ?? string.Empty).ToLowerInvariant())
            {
                case "watts/area":
                    return NativeModel.Number(o, perAreaField, perAreaLabel);
                case "watts/person":
                    return EQ.Multiply(NativeModel.Number(o, perPersonField, perPersonLabel), peopleDensity, scale);
                case "lightinglevel":
                case "equipmentlevel":
                    ReaderResult level = NativeModel.Number(o, levelField, levelLabel);
                    return level.HasNumber && floorArea.HasNumber && floorArea.Value > 0 ? ReaderResult.FromValue(level.Value / floorArea.Value) : ReaderResult.Unreadable("design level without a native floor area");
                default:
                    return ReaderResult.Unreadable("calculation method '" + method + "' is not read");
            }
        }

        private static ReaderResult OneMinusLost(IdfObject o)
        {
            if (o == null)
            {
                return ReaderResult.Absent("no native object");
            }

            ReaderResult lost = NativeModel.Number(o, 9, "Fraction Lost");
            return lost.Kind == ReaderKind.Absent ? ReaderResult.FromValue(1) : lost.HasNumber ? ReaderResult.FromValue(1 - lost.Value) : lost;
        }

        private static ReaderResult Constant(NativeSchedule schedule, string what)
        {
            if (schedule == null)
            {
                return ReaderResult.Absent("no " + what + " schedule");
            }

            if (schedule.Values == null)
            {
                return ReaderResult.Unreadable(what + " schedule: " + schedule.Problem);
            }

            double first = schedule.Values[0];
            return schedule.Values.All(x => x == first) ? ReaderResult.FromValue(first) : ReaderResult.Unreadable(what + " schedule '" + schedule.Name + "' is not constant");
        }

        private void Gain(string what, Guid space, double total, double storedArea, ReaderResult density, ReaderResult floorArea, IdentityStatus identity, bool nativePresent)
        {
            ExpectedValue expectedDensity = EQ.ExpectedGainDensity(total, storedArea);
            ExpectedValue expectedTotal = ExpectedValue.FromValue(total);

            // T-OMIT0: the writer omits a gain whose SAM total is <= 0; verified only when no such object exists.
            string omitted = !nativePresent && expectedTotal.State == SamInputState.Zero ? "T-OMIT0" : null;
            Add("4a.density", Keys.Ic(space, what), expectedDensity, density, identity, ModelInterpretation.NotRequired, omitted);
            Add("4b", Keys.Ic(space, what + ".total"), expectedTotal, EQ.Multiply(density, floorArea), identity, ModelInterpretation.NotRequired, omitted);
        }

        private void Fraction(string what, Guid space, double authored, double listedDefault, ReaderResult nativeValue, IdentityStatus identity)
        {
            QuantityInput input = new QuantityInput
            {
                Definition = QuantityCatalog.Get("4c"),
                FixtureId = FixtureId,
                ObjectKey = Keys.Ic(space, what),
                Expected = double.IsNaN(authored) ? ExpectedValue.Unspecified("not authored; the writer default is " + listedDefault.ToString("R", ci)) : ExpectedValue.FromValue(authored),
                Native = nativeValue,
                Identity = identity,
                ListedDefault = listedDefault
            };

            Results.Add(EQ.Evaluate(input, Configuration));
        }

        /// <summary>
        /// S07 §5.1 T-LIGHT-SPLIT on the IDF <c>Lights</c> object: Return Air Fraction (blank reads as 0) against 0, exact;
        /// Fraction Radiant + Fraction Visible against p; Fraction Visible against p·s; Fraction Radiant against p·(1 − s); the
        /// convective remainder 1 − F_RA − F_R − F_V against 1 − p. Expectations come from the raw SAM p and the §5.1
        /// constants only (<see cref="OsQuantities.LightingShortWaveShare"/>), never from the writer or the view coefficient.
        /// An authored p (including 0) is verified as an accepted transformation. An absent or NaN p stays S-UNSPEC on every
        /// row, with the contract-listed default (p₀ split) as the listed default: DOCUMENTED-DEFAULT at best, never PASS.
        /// A p outside [0, 1] is S-INVALID.
        /// <para>
        /// Two guards run before any S07 tolerance is applied, so a tolerance can never accept an invalid or dynamic split:
        /// </para>
        /// <list type="bullet">
        /// <item><description><b>Native domain and energy conservation</b> (<see cref="LightsDomainProblem"/>): each of
        /// Return Air, Radiant and Visible within [0, 1] and their sum at most 1 (S07 §5.1: "A sum above 1 is an input
        /// error"). A violation is an EXPORT-DEFECT on all five rows, with the violation named; nothing is compared.</description></item>
        /// <item><description><b>Plenum-calculated return air</b> (<see cref="LightsPlenumProblem"/>): blank or No, the
        /// EnergyPlus default, is the static split S07 describes. Yes makes the return-air share follow the plenum
        /// temperature at run time, so no static T-LIGHT-SPLIT can be verified; any other or unreadable setting is
        /// uninterpretable. Either way every lighting row is UNVERIFIED (unreadable, BLOCKED technical) and the space is
        /// named in <see cref="Unsupported"/>.</description></item>
        /// </list>
        /// </summary>
        private void LightingSplit(Guid space, double p, IdfObject light, IdentityStatus identity)
        {
            double s = OsQuantities.LightingShortWaveShare;
            double p0 = OsQuantities.LightingReferenceRadiantProportion;

            // A blank field of a present Lights object is its EnergyPlus IDD default, 0 for all three (S07: "blank reads as
            // 0"). Read as 0, a blank cannot pass as native absence (NOT-APPLICABLE) for an unstated p.
            ReaderResult Field(int index, string label)
            {
                ReaderResult value = NativeModel.Number(light, index, label);
                return light != null && value.Kind == ReaderKind.Absent ? ReaderResult.FromValue(0) : value;
            }

            ReaderResult returnAir = Field(7, "Return Air Fraction");
            ReaderResult radiant = Field(8, "Fraction Radiant");
            ReaderResult visible = Field(9, "Fraction Visible");
            ReaderResult total = Combine(new[] { radiant, visible }, x => x[0] + x[1]);
            ReaderResult convective = Combine(new[] { returnAir, radiant, visible }, x => 1 - x[0] - x[1] - x[2]);

            string domain = LightsDomainProblem(light, returnAir, radiant, visible);
            string plenum = LightsPlenumProblem(light);
            if (plenum != null)
            {
                Unsupported.Add("lighting: " + Keys.Space(space) + ": " + plenum);
                ReaderResult dynamic = ReaderResult.Unreadable(plenum);
                returnAir = radiant = visible = total = convective = dynamic;
            }

            void Row(string what, QuantityDefinition definition, Func<double, double> expected, ReaderResult nativeValue)
            {
                ExpectedValue value;
                if (double.IsNaN(p))
                {
                    value = ExpectedValue.Unspecified("LightingRadiantProportion not authored; the S07 §5.1 listed default is p0 = " + p0.ToString("R", ci));
                }
                else if (p < 0 || p > 1)
                {
                    value = ExpectedValue.Invalid("LightingRadiantProportion " + p.ToString("R", ci) + " is not a fraction between 0 and 1");
                }
                else
                {
                    value = ExpectedValue.FromValue(expected(p));
                }

                QuantityInput input = new QuantityInput
                {
                    Definition = definition,
                    FixtureId = FixtureId,
                    ObjectKey = Keys.Ic(space, what),
                    Expected = value,
                    Native = nativeValue,
                    Identity = identity,
                    ListedDefault = expected(p0),
                    TransformationId = LightSplit,
                    TransformationVerified = plenum == null
                };

                // An invalid native split is decided before, and instead of, the numeric comparison.
                Results.Add(domain != null
                    ? new QuantityResult(input, EquivalenceOutcome.ExportDefect, GateStatus.Fail, "The native Lights split is invalid (" + domain + "); it is not compared, and no tolerance applies.", null, null, null, null, null)
                    : EQ.Evaluate(input, Configuration));
            }

            Row(LayerProperties.LightingReturnAir, OsQuantities.LightingReturnAir, x => 0, returnAir);
            Row("lighting.radiantTotal", QuantityCatalog.Get("4c"), x => x, total);
            Row("lighting.visible", QuantityCatalog.Get("4c"), x => x * s, visible);
            Row("lighting.radiant", QuantityCatalog.Get("4c"), x => x * (1 - s), radiant);
            Row("lighting.convective", QuantityCatalog.Get("4c"), x => 1 - x, convective);
        }

        /// <summary>
        /// The native-domain guard of T-LIGHT-SPLIT: null when every readable fraction of the Lights object lies in [0, 1] and
        /// Return Air + Radiant + Visible is at most 1; otherwise the violation, named. Unreadable and NaN fields are left to
        /// the row evaluation. <see cref="OsQuantities.LightingFractionBoundary"/> absorbs only the rounding of decimal IDF
        /// text to double at the bounds 0 and 1; it is no S07 tolerance and never widens one.
        /// </summary>
        private static string LightsDomainProblem(IdfObject light, ReaderResult returnAir, ReaderResult radiant, ReaderResult visible)
        {
            if (light == null)
            {
                return null;
            }

            double e = OsQuantities.LightingFractionBoundary;
            List<string> problems = new List<string>();
            foreach ((string label, ReaderResult value) in new[] { ("Return Air Fraction", returnAir), ("Fraction Radiant", radiant), ("Fraction Visible", visible) })
            {
                if (value.HasNumber && (value.Value < -e || value.Value > 1 + e))
                {
                    problems.Add("Lights '" + light.Name + "' " + label + " " + value.Value.ToString("R", ci) + " is outside [0, 1]");
                }
            }

            if (returnAir.HasNumber && radiant.HasNumber && visible.HasNumber)
            {
                double sum = returnAir.Value + radiant.Value + visible.Value;
                if (sum > 1 + e)
                {
                    problems.Add("Lights '" + light.Name + "' Return Air + Radiant + Visible = " + sum.ToString("R", ci) + " exceeds 1, a negative convective share (S07 §5.1: a sum above 1 is an input error)");
                }
            }

            return problems.Count == 0 ? null : string.Join("; ", problems);
        }

        /// <summary>
        /// The Lights "Return Air Fraction Calculated from Plenum Temperature" setting (index 12, counting Name as 0): null for blank, absent or
        /// No (the EnergyPlus default, a static return-air share); otherwise why no static T-LIGHT-SPLIT can be verified.
        /// </summary>
        private static string LightsPlenumProblem(IdfObject light)
        {
            if (light == null)
            {
                return null;
            }

            const string label = "Return Air Fraction Calculated from Plenum Temperature";
            string text = light.Value(12, label, out string problem);
            if (problem != null)
            {
                return "Lights '" + light.Name + "' " + label + " is unreadable (" + problem + "); the return-air setting cannot be interpreted, so T-LIGHT-SPLIT is not verified";
            }

            if (string.IsNullOrWhiteSpace(text) || string.Equals(text.Trim(), "No", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return string.Equals(text.Trim(), "Yes", StringComparison.OrdinalIgnoreCase)
                ? "Lights '" + light.Name + "' " + label + " = Yes: the return-air share follows the plenum temperature at run time, so a static T-LIGHT-SPLIT cannot be verified (unsupported by S09)"
                : "Lights '" + light.Name + "' " + label + " = '" + text + "' is neither Yes nor No (uninterpretable), so T-LIGHT-SPLIT is not verified";
        }

        /// <summary>A quantity derived from native fields: absent, unreadable or NaN when any input is; otherwise the value.</summary>
        private static ReaderResult Combine(ReaderResult[] inputs, Func<double[], double> combine)
        {
            ReaderResult problem = inputs.FirstOrDefault(x => x.Kind == ReaderKind.Unreadable) ?? inputs.FirstOrDefault(x => x.Kind == ReaderKind.NaN) ?? inputs.FirstOrDefault(x => x.Kind == ReaderKind.Absent);
            return problem ?? ReaderResult.FromValue(combine(inputs.Select(x => x.Value).ToArray()));
        }

        private ReaderResult Infiltration(IdfObject infiltration, Guid space)
        {
            if (infiltration == null)
            {
                return ReaderResult.Absent("no infiltration object applies to the space");
            }

            string method = infiltration.Value(3, "Design Flow Rate Calculation Method", out string problem);
            if (problem != null || !string.Equals(method, "AirChanges/Hour", StringComparison.OrdinalIgnoreCase))
            {
                return ReaderResult.Unreadable(problem ?? "infiltration method '" + method + "' is not AirChanges/Hour");
            }

            // EnergyPlus defaults (blank) are A = 1, B = C = D = 0: a constant design flow. Anything else changes the flow.
            string[] labels = { "Constant Term Coefficient", "Temperature Term Coefficient", "Velocity Term Coefficient", "Velocity Squared Term Coefficient" };
            double[] neutral = { 1, 0, 0, 0 };
            for (int i = 0; i < 4; i++)
            {
                ReaderResult coefficient = NativeModel.Number(infiltration, 8 + i, labels[i]);
                if (coefficient.Kind != ReaderKind.Absent && (!coefficient.HasNumber || coefficient.Value != neutral[i]))
                {
                    Consistency.Add(Keys.Space(space) + ": infiltration " + labels[i] + " is " + coefficient.Kind + " " + coefficient.Value.ToString("R", ci) + ", not the constant-flow value " + neutral[i]);
                }
            }

            return NativeModel.Number(infiltration, 7, "Air Changes per Hour");
        }

        // ---- schedules and setpoints ------------------------------------------------------------------------------------

        private NativeSchedule CheckedSchedule(string name, string key)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            NativeSchedule idf = native.IdfSchedule(name);
            NativeSchedule osm = native.OsmSchedule(name);
            if (idf == null)
            {
                // A reference to an undefined object is a broken export (EnergyPlus would stop), not an unreadable field.
                Consistency.Add(key + ": names schedule '" + name + "', which the IDF does not define");
                return new NativeSchedule { Name = name, Problem = "the IDF does not define schedule '" + name + "'" };
            }

            // The OSM holds the same schedule as an OS:Schedule:FixedInterval: both native views must agree hour by hour.
            if (osm != null && osm.Values != null && idf.Values != null && !osm.Values.SequenceEqual(idf.Values))
            {
                int first = Enumerable.Range(0, Math.Min(osm.Values.Length, idf.Values.Length)).FirstOrDefault(i => osm.Values[i] != idf.Values[i]);
                Consistency.Add(key + ": schedule '" + name + "' differs between the OSM and the IDF (first at hour " + first + ")");
            }

            return idf;
        }

        private void Schedule(string what, Guid space, ExpectedSpace expectedSpace, string slot, string nativeName, IdentityStatus identity, string inactive)
        {
            string key = Keys.Ic(space, "schedule." + what);
            expectedSpace.Profiles.TryGetValue(slot, out ExpectedProfile profile);
            NativeSchedule schedule = CheckedSchedule(nativeName, key);
            ScheduleRows(key, profile, schedule, identity, inactive, 1, "6.hourly", "6.eflh");
        }

        private void Setpoint(string what, string quantityId, Guid space, ExpectedSpace expectedSpace, string slot, string nativeName, IdentityStatus identity)
        {
            string key = Keys.Ic(space, what);
            expectedSpace.Profiles.TryGetValue(slot, out ExpectedProfile profile);
            NativeSchedule schedule = CheckedSchedule(nativeName, key);
            ScheduleRows(key, profile, schedule, identity, null, 1, quantityId, null);
        }

        private void ScheduleRows(string key, ExpectedProfile profile, NativeSchedule schedule, IdentityStatus identity, string inactive, double scale, string hourlyId, string eflhId)
        {
            AlignmentStatus alignment = Alignment(schedule);
            if (profile == null && schedule == null)
            {
                Add(hourlyId, key, ExpectedValue.Unspecified("SAM selects no profile"), ReaderResult.Absent("no native schedule"), identity);
                if (eflhId != null)
                {
                    Add(eflhId, key, ExpectedValue.Unspecified("SAM selects no profile"), ReaderResult.Absent("no native schedule"), identity);
                }

                return;
            }

            if (profile != null && !profile.Expanded.IsOk)
            {
                Add(hourlyId, key, ExpectedValue.Invalid("SAM profile '" + profile.Name + "' cannot be expanded: " + profile.Expanded.Status), Native(schedule, 0), identity);
                if (eflhId != null)
                {
                    Add(eflhId, key, ExpectedValue.Invalid("SAM profile '" + profile.Name + "' cannot be expanded"), Native(schedule, 0), identity);
                }

                return;
            }

            IReadOnlyList<ExpectedValue> expected = profile == null ? Enumerable.Repeat(ExpectedValue.Unspecified("SAM selects no profile"), 8760).ToList() : EQ.ExpectedHourlySeries(profile.Expanded, scale);
            List<ReaderResult> nativeSeries = schedule == null
                ? Enumerable.Repeat(ReaderResult.Absent("no native schedule"), expected.Count).ToList()
                : schedule.Values == null
                    ? Enumerable.Repeat(ReaderResult.Unreadable(schedule.Problem), expected.Count).ToList()
                    : schedule.Values.Select(x => ReaderResult.FromValue(x * scale)).ToList();

            AddSeries(hourlyId, key, expected, nativeSeries, identity, alignment, inactive, ModelInterpretation.NotRequired);
            if (eflhId != null)
            {
                ExpectedValue eflh = profile == null ? ExpectedValue.Unspecified("SAM selects no profile") : EQ.ExpectedEquivalentFullLoadHours(profile.Expanded);
                ReaderResult nativeEflh = schedule == null ? ReaderResult.Absent("no native schedule") : schedule.Values == null ? ReaderResult.Unreadable(schedule.Problem) : ReaderResult.FromValue(schedule.Values.Sum());
                Add(eflhId, key, eflh, nativeEflh, identity, ModelInterpretation.NotRequired, null, inactive);
            }
        }

        private static ReaderResult Native(NativeSchedule schedule, int hour)
        {
            return schedule == null ? ReaderResult.Absent("no native schedule") : schedule.Values == null ? ReaderResult.Unreadable(schedule.Problem) : ReaderResult.FromValue(schedule.Values[hour]);
        }

        // ---- ventilation (S07 D-F2, 8a, 8b; the S09 owner policy) -------------------------------------------------------

        private void CompareVentilation(ExpectedSpace space, string nativeSpace, IdfObject zone, ReaderResult floorArea, ReaderResult volume, ReaderResult peopleDensity, IdentityStatus identity, ModelInterpretation interpretation)
        {
            Guid g = space.Guid;
            string key = Keys.Space(g);
            Space samSpace = model.AdjacencyCluster.GetObject<Space>(g);
            double outsideSupplyAirFlow = samSpace != null && samSpace.TryGetValue(SpaceParameter.OutsideSupplyAirFlow, out double value) ? value : double.NaN;
            bool overrideActive = !double.IsNaN(outsideSupplyAirFlow) && outsideSupplyAirFlow > 0;

            // S07 section 3, OS outdoor air: the per-space OutsideSupplyAirFlow wins over the SpaceType (condition) bases.
            VentilationRequirement requirement = overrideActive
                ? new VentilationRequirement { Flow = outsideSupplyAirFlow }
                : new VentilationRequirement { Flow = Parameter(space, "SupplyAirFlow"), FlowPerArea = Parameter(space, "SupplyAirFlowPerArea"), FlowPerPerson = Parameter(space, "SupplyAirFlowPerPerson"), AirChangesPerHour = Parameter(space, "SupplyAirChangesPerHour") };
            bool selected = space.VentilationProfileSelection != null;
            space.Profiles.TryGetValue("ticV", out ExpectedProfile profile);
            VentilationState state = EQ.ClassifyVentilation(requirement, selected);
            double people = People(space);
            ExpectedValue required = EQ.ExpectedRequiredSupplyFlow(requirement, space.StoredArea, people, space.ShellVolume?.State == SamInputState.Value ? space.ShellVolume.Value : double.NaN);
            string unresolved = selected && profile == null ? "SAM selects the Ventilation profile '" + space.VentilationProfileSelection + "', which does not resolve to a Ventilation profile (missing, or of another type): an invalid input" : null;
            VentilationStates[key] = state + (overrideActive ? " (per-space OutsideSupplyAirFlow " + outsideSupplyAirFlow.ToString("R", ci) + " m3/s)" : string.Empty) + (unresolved != null ? " INVALID selection" : string.Empty);
            Supporting.Add(key + ": D-F2 " + VentilationStates[key] + "; selection " + (space.VentilationProfileSelection ?? "none") + "; requirement " + required);

            // The native delivery path: the zone's Ideal Loads, the DSOA it names and that DSOA's fraction schedule.
            IdfObject idealLoads = IdealLoadsOf(zone);
            IdfObject dsoa = native.IdfByName("DesignSpecification:OutdoorAir", idealLoads?.Value(20, "Design Specification Outdoor Air Object Name", out string _));
            string controls = DeliveryControls(idealLoads);
            InheritedDsoa(key, nativeSpace, dsoa);

            if (dsoa != null)
            {
                string method = dsoa.Value(1, "Outdoor Air Method", out string _);
                if (!string.Equals(method, "Sum", StringComparison.OrdinalIgnoreCase))
                {
                    Consistency.Add(key + ": DSOA '" + dsoa.Name + "' method is '" + method + "', not Sum");
                }
            }

            // 8a: each basis; an unstated basis contributes 0 to SAM's summed requirement (S07 8a; core
            // ExpectedRequiredSupplyFlow), so it is S-ZERO once any basis is stated, and S-UNSPEC in V0.
            bool anyStated = state != VentilationState.V0 && state != VentilationState.Indeterminate;
            ReaderResult[] nativeBases =
            {
                NativeModel.Number(dsoa, 4, "Outdoor Air Flow per Zone"),
                NativeModel.Number(dsoa, 3, "Outdoor Air Flow per Zone Floor Area"),
                NativeModel.Number(dsoa, 2, "Outdoor Air Flow per Person"),
                NativeModel.Number(dsoa, 5, "Outdoor Air Flow Air Changes per Hour")
            };
            double[] expectedBases = { requirement.Flow, requirement.FlowPerArea, requirement.FlowPerPerson, requirement.AirChangesPerHour };
            for (int i = 0; i < 4; i++)
            {
                ExpectedValue e = !double.IsNaN(expectedBases[i]) ? ExpectedValue.FromValue(expectedBases[i]) : anyStated ? ExpectedValue.Zero() : ExpectedValue.Unspecified("no requirement is stated (V0)");
                Add("8a", Keys.Ic(g, "ventilation.requirement." + LayerProperties.VentilationBases[i]), e, nativeBases[i], identity);
            }

            // The native summed requirement, from native rates and native geometry (EnergyPlus "Sum" at design occupancy).
            ReaderResult nativeRequired = dsoa == null ? ReaderResult.Absent("no DSOA is linked to the zone's Ideal Loads") : NativeRequired(nativeBases, peopleDensity, floorArea, volume);
            Add("8a", Keys.Ic(g, "ventilation.requirement.total"), required, nativeRequired, identity, interpretation);

            // 8b and the ventilation schedule (6): hour by hour.
            string scheduleName = dsoa?.Value(6, "Outdoor Air Schedule Name", out string _);
            NativeSchedule schedule = CheckedSchedule(scheduleName, key + ".ic.schedule.ventilation");
            if (dsoa != null && string.IsNullOrWhiteSpace(scheduleName))
            {
                schedule = null;
                Supporting.Add(key + ": the linked DSOA has no fraction schedule: EnergyPlus delivers it unscheduled (fraction 1)");
            }

            List<ReaderResult> delivered = new List<ReaderResult>(8760);
            for (int h = 0; h < 8760; h++)
            {
                if (dsoa == null)
                {
                    delivered.Add(ReaderResult.Absent(idealLoads == null ? "the zone has no Ideal Loads" : "the zone's Ideal Loads names no DSOA"));
                }
                else if (controls != null)
                {
                    delivered.Add(ReaderResult.Unreadable(controls));
                }
                else if (string.IsNullOrWhiteSpace(scheduleName))
                {
                    delivered.Add(nativeRequired);
                }
                else
                {
                    delivered.Add(EQ.Multiply(nativeRequired, Native(schedule, h)));
                }
            }

            string inactive = null;
            if (state == VentilationState.V0 || state == VentilationState.V1)
            {
                inactive = InactiveDelivery(key, selected, dsoa, schedule, scheduleName, nativeRequired, delivered);
            }

            AlignmentStatus alignment = Alignment(schedule);
            IReadOnlyList<ExpectedValue> expected;
            if (unresolved != null)
            {
                expected = Enumerable.Repeat(ExpectedValue.Invalid(unresolved), 8760).ToList();
            }
            else if (state == VentilationState.V2)
            {
                expected = required.State != SamInputState.Value && required.State != SamInputState.Zero
                    ? Enumerable.Repeat(required, 8760).ToList()
                    : profile.Expanded.IsOk ? EQ.ExpectedHourlySeries(profile.Expanded, required.Value) : Enumerable.Repeat(ExpectedValue.Invalid("the Ventilation profile cannot be expanded: " + profile.Expanded.Status), 8760).ToList();
            }
            else
            {
                expected = Enumerable.Repeat(EQ.ExpectedDeliveredOutdoorAir(state, required), 8760).ToList();
            }

            AddSeries("8b", Keys.Ic(g, "ventilation.delivered"), expected, delivered, identity, alignment, inactive, interpretation);

            // The ventilation schedule itself (6): SAM's selected profile against the DSOA fraction schedule.
            string scheduleKey = Keys.Ic(g, "schedule.ventilation");
            if (unresolved != null)
            {
                Add("6.hourly", scheduleKey, ExpectedValue.Invalid(unresolved), Native(schedule, 0), identity);
                Add("6.eflh", scheduleKey, ExpectedValue.Invalid(unresolved), schedule?.Values == null ? Native(schedule, 0) : ReaderResult.FromValue(schedule.Values.Sum()), identity);
            }
            else
            {
                ScheduleRows(scheduleKey, profile, schedule, identity, inactive, 1, "6.hourly", "6.eflh");
            }
        }

        private IdfObject IdealLoadsOf(IdfObject zone)
        {
            if (zone == null)
            {
                return null;
            }

            IdfObject connections = native.Idf("ZoneHVAC:EquipmentConnections").FirstOrDefault(x => string.Equals(x.Value(0, "Zone Name", out string _), zone.Name, StringComparison.OrdinalIgnoreCase));
            IdfObject list = native.IdfByName("ZoneHVAC:EquipmentList", connections?.Value(1, "Zone Conditioning Equipment List Name", out string _));
            if (list == null)
            {
                return null;
            }

            List<IdfObject> idealLoads = new List<IdfObject>();
            for (int i = 1; i + 1 < list.Fields.Count; i++)
            {
                if (string.Equals(list.Value(i), "ZoneHVAC:IdealLoadsAirSystem", StringComparison.OrdinalIgnoreCase))
                {
                    IdfObject o = native.IdfByName("ZoneHVAC:IdealLoadsAirSystem", list.Value(i + 1));
                    if (o != null)
                    {
                        idealLoads.Add(o);
                    }
                }
            }

            if (idealLoads.Count > 1)
            {
                Consistency.Add("zone '" + zone.Name + "' has " + idealLoads.Count + " Ideal Loads systems");
            }

            return idealLoads.FirstOrDefault();
        }

        /// <summary>
        /// Null when the Ideal Loads delivers exactly DSOA x schedule at design occupancy: always available, no
        /// demand-controlled ventilation, no economizer, no heat recovery (blank = the EnergyPlus default of each).
        /// Otherwise the reason the delivered flow cannot be derived from the DSOA alone.
        /// </summary>
        private string DeliveryControls(IdfObject idealLoads)
        {
            if (idealLoads == null)
            {
                return null;
            }

            List<string> problems = new List<string>();
            string availability = idealLoads.Value(1, "Availability Schedule Name", out string p1);
            if (p1 != null)
            {
                problems.Add(p1);
            }
            else if (!string.IsNullOrWhiteSpace(availability))
            {
                ReaderResult on = Constant(native.IdfSchedule(availability), "availability");
                if (!on.HasNumber || on.Value != 1)
                {
                    problems.Add("availability schedule '" + availability + "' is not always 1");
                }
            }

            foreach ((int index, string label, string neutral) in new[] { (22, "Demand Controlled Ventilation Type", "None"), (23, "Outdoor Air Economizer Type", "NoEconomizer"), (24, "Heat Recovery Type", "None") })
            {
                string text = idealLoads.Value(index, label, out string problem);
                if (problem != null)
                {
                    problems.Add(problem);
                }
                else if (!string.IsNullOrWhiteSpace(text) && !string.Equals(text, neutral, StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add(label + " is '" + text + "', not " + neutral);
                }
            }

            Supporting.Add("Ideal Loads '" + idealLoads.Name + "': DCV '" + idealLoads.Value(22) + "', economizer '" + idealLoads.Value(23) + "', heat recovery '" + idealLoads.Value(24) + "', availability '" + availability + "'");
            return problems.Count == 0 ? null : "Ideal Loads controls change the delivered outdoor air: " + string.Join("; ", problems);
        }

        /// <summary>
        /// The OSM view of the same delivery path: the space's own DSOA, else the one its SpaceType carries (inheritance),
        /// must be the DSOA the IDF Ideal Loads names, with the same rates and schedule.
        /// </summary>
        private void InheritedDsoa(string key, string nativeSpace, IdfObject idfDsoa)
        {
            IdfObject osmSpace = native.OsmByName("OS:Space", nativeSpace);
            if (osmSpace == null)
            {
                Consistency.Add(key + ": the OSM has no OS:Space '" + nativeSpace + "'");
                return;
            }

            string own = osmSpace.ValueByLabel("Design Specification Outdoor Air Object Name", out bool _);
            IdfObject spaceType = native.OsmByHandle("OS:SpaceType", osmSpace.Value(2, "Space Type Name", out string _));
            string inherited = spaceType?.ValueByLabel("Design Specification Outdoor Air Object Name", out bool _);
            string effectiveHandle = !string.IsNullOrWhiteSpace(own) ? own : inherited;
            IdfObject osmDsoa = native.OsmByHandle("OS:DesignSpecification:OutdoorAir", effectiveHandle);
            Supporting.Add(key + ": OSM DSOA " + (osmDsoa == null ? "none" : "'" + osmDsoa.Value(1) + "'") + (string.IsNullOrWhiteSpace(own) ? " inherited from SpaceType '" + spaceType?.Value(1) + "'" : " set on the space (per-space override)") + "; IDF Ideal Loads names " + (idfDsoa == null ? "none" : "'" + idfDsoa.Name + "'"));

            if ((osmDsoa == null) != (idfDsoa == null) || osmDsoa != null && !string.Equals(osmDsoa.Value(1), idfDsoa.Name, StringComparison.OrdinalIgnoreCase))
            {
                Consistency.Add(key + ": the OSM effective DSOA (" + (osmDsoa?.Value(1) ?? "none") + ") is not the one the IDF Ideal Loads delivers (" + (idfDsoa?.Name ?? "none") + ")");
                return;
            }

            if (osmDsoa == null)
            {
                return;
            }

            IdfObject osmIdealLoads = native.Snapshot.OsmOf("OS:ZoneHVAC:IdealLoadsAirSystem").FirstOrDefault(x => string.Equals(native.OsmName(x.ValueByLabel("Design Specification Outdoor Air Object Name", out bool _)), osmDsoa.Value(1), StringComparison.OrdinalIgnoreCase));
            if (osmIdealLoads == null)
            {
                Supporting.Add(key + ": no OSM Ideal Loads names the DSOA itself (the translator would link it from the space)");
            }

            string[] labels = { "Outdoor Air Method", "Outdoor Air Flow per Person", "Outdoor Air Flow per Floor Area", "Outdoor Air Flow Rate", "Outdoor Air Flow Air Changes per Hour" };
            int[] idfFields = { 1, 2, 3, 4, 5 };
            for (int i = 0; i < labels.Length; i++)
            {
                string osmValue = osmDsoa.ValueByLabel(labels[i], out bool _);
                string idfValue = idfDsoa.Value(idfFields[i]);
                bool equal = i == 0 ? string.Equals(osmValue, idfValue, StringComparison.OrdinalIgnoreCase) : double.TryParse(osmValue, NumberStyles.Float, ci, out double a) && double.TryParse(idfValue, NumberStyles.Float, ci, out double b) && a == b;
                if (!equal)
                {
                    Consistency.Add(key + ": DSOA " + labels[i] + " is '" + osmValue + "' in the OSM and '" + idfValue + "' in the IDF");
                }
            }

            string osmSchedule = native.OsmName(osmDsoa.ValueByLabel("Outdoor Air Flow Rate Fraction Schedule Name", out bool _));
            string idfSchedule = idfDsoa.Value(6);
            if (!string.Equals(osmSchedule ?? string.Empty, idfSchedule ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                Consistency.Add(key + ": DSOA fraction schedule is '" + osmSchedule + "' in the OSM and '" + idfSchedule + "' in the IDF");
            }
        }

        private static ReaderResult NativeRequired(ReaderResult[] bases, ReaderResult peopleDensity, ReaderResult floorArea, ReaderResult volume)
        {
            // bases: flow per zone, per floor area, per person, ACH. Blank rates are absent (EnergyPlus would default
            // per person to 0.00944 m3/s), so the sum needs every rate stated.
            if (bases.Any(x => !x.HasNumber))
            {
                return bases.First(x => !x.HasNumber).Kind == ReaderKind.Absent ? ReaderResult.Unreadable("a DSOA rate is blank: EnergyPlus would apply its default") : bases.First(x => !x.HasNumber);
            }

            ReaderResult people = EQ.Multiply(peopleDensity.Kind == ReaderKind.Absent ? ReaderResult.FromValue(0) : peopleDensity, floorArea);
            ReaderResult perPerson = bases[2].Value == 0 ? ReaderResult.FromValue(0) : EQ.Multiply(bases[2], people);
            ReaderResult perArea = bases[1].Value == 0 ? ReaderResult.FromValue(0) : EQ.Multiply(bases[1], floorArea);
            ReaderResult ach = bases[3].Value == 0 ? ReaderResult.FromValue(0) : EQ.Multiply(bases[3], volume, 1 / 3600.0);
            foreach (ReaderResult term in new[] { perPerson, perArea, ach })
            {
                if (!term.HasNumber)
                {
                    return term;
                }
            }

            return ReaderResult.FromValue(bases[0].Value + perPerson.Value + perArea.Value + ach.Value);
        }

        /// <summary>
        /// T-VENT-INACTIVE for OpenStudio (S08 owner decision 3, applied under the S09 owner ventilation policy): SAM selects
        /// no delivery profile; the native DSOA stays present with its requirement; its fraction schedule is read in both
        /// native views, hour-labelled, finite and exactly 0 in all 8760 hours; and every delivered flow is exactly 0.
        /// Anything else gets no exemption. Returns the transformation id, or null; the reason is recorded either way.
        /// </summary>
        private string InactiveDelivery(string key, bool selected, IdfObject dsoa, NativeSchedule schedule, string scheduleName, ReaderResult nativeRequired, List<ReaderResult> delivered)
        {
            List<string> refused = new List<string>();
            if (RuleSet != GateRuleSet.Approved)
            {
                refused.Add("not the approved rule set");
            }

            if (selected)
            {
                refused.Add("SAM selects a Ventilation profile");
            }

            if (dsoa == null)
            {
                refused.Add("no native DSOA is delivered (nothing to exempt)");
            }

            NativeSchedule osm = native.OsmSchedule(scheduleName);
            if (schedule == null || schedule.Values == null || osm == null || osm.Values == null)
            {
                refused.Add("the fraction schedule is not read in both the IDF and the OSM");
            }
            else
            {
                if (!schedule.AlignmentVerified || !osm.AlignmentVerified || !calendarVerified)
                {
                    refused.Add("the hour labels or the calendar are not verified");
                }

                if (schedule.Values.Length != 8760 || osm.Values.Length != 8760 || schedule.Values.Any(x => x != 0 || double.IsNaN(x)) || osm.Values.Any(x => x != 0 || double.IsNaN(x)))
                {
                    refused.Add("the fraction schedule is not exactly 0 in all 8760 hours of both views");
                }
            }

            if (!nativeRequired.HasNumber)
            {
                refused.Add("the native requirement is not a finite number (" + nativeRequired.Kind + ")");
            }

            if (delivered.Count != 8760 || delivered.Any(x => x.Kind != ReaderKind.Zero))
            {
                refused.Add("not every delivered flow is read as exactly 0");
            }

            Supporting.Add(key + ": " + InactiveVentilation + (refused.Count == 0 ? " applied (requirement kept, all 8760 schedule values and delivered flows exactly 0 in both views)" : " not applied: " + string.Join("; ", refused)));
            return refused.Count == 0 ? InactiveVentilation : null;
        }

        // ---- weather and design days ------------------------------------------------------------------------------------

        private void CompareWeather(string epwPath, NativeSnapshot snapshot)
        {
            string[] location = File.Exists(epwPath) ? File.ReadLines(epwPath).First().Split(',') : new string[0];
            double Header(int i) => location.Length > i && double.TryParse(location[i], NumberStyles.Float, ci, out double v) ? v : double.NaN;

            string expectedHash = File.Exists(epwPath) ? Hashing.Sha256(epwPath) : null;
            ReaderResult nativeHash = snapshot.WeatherFileSha256 == null ? ReaderResult.Absent(snapshot.WeatherFileProblem) : ReaderResult.FromValue(string.Equals(snapshot.WeatherFileSha256, expectedHash, StringComparison.OrdinalIgnoreCase) ? 1 : 0);
            Add("9.os.epw", Keys.Weather("epw"), expectedHash == null ? ExpectedValue.Invalid("the run's EPW is missing") : ExpectedValue.FromValue(1), nativeHash, IdentityStatus.Verified);
            Supporting.Add("weather: run EPW sha256 " + expectedHash + "; the OSM references a file with sha256 " + (snapshot.WeatherFileSha256 ?? "(none: " + snapshot.WeatherFileProblem + ")"));

            IdfObject site = native.Idf("Site:Location").SingleOrDefault();
            IdfObject weatherFile = native.Snapshot.OsmOf("OS:WeatherFile").SingleOrDefault();
            (string what, int epw, int idf, string idfLabel, int osm, string osmLabel)[] fields =
            {
                ("latitude", 6, 1, "Latitude", 6, "Latitude"),
                ("longitude", 7, 2, "Longitude", 7, "Longitude"),
                ("timezone", 8, 3, "Time Zone", 8, "Time Zone"),
                ("elevation", 9, 4, "Elevation", 9, "Elevation")
            };

            foreach ((string what, int epw, int idf, string idfLabel, int osm, string osmLabel) in fields)
            {
                ReaderResult idfValue = NativeModel.Number(site, idf, idfLabel);
                ReaderResult osmValue = NativeModel.Number(weatherFile, osm, osmLabel);
                if (idfValue.Kind != osmValue.Kind || idfValue.HasNumber && idfValue.Value != osmValue.Value)
                {
                    Consistency.Add("weather " + what + ": IDF Site:Location " + idfValue.Kind + " " + idfValue.Value.ToString("R", ci) + " vs OSM OS:WeatherFile " + osmValue.Kind + " " + osmValue.Value.ToString("R", ci));
                }

                Add("9.os." + what, Keys.Weather(what), ExpectedValue.FromValue(Header(epw)), idfValue, IdentityStatus.Verified);
            }

            IdfObject ground = native.Idf("Site:GroundTemperature:BuildingSurface").FirstOrDefault();
            Supporting.Add("weather: Site:GroundTemperature:BuildingSurface " + (ground == null ? "absent" : string.Join(", ", ground.Fields.Select(x => x.Value))) + " (9.ground, not gated)");
        }

        private void CompareDesignDays(ExpectedModel expected)
        {
            // S07 section 3, OS design days: an explicit DDY (the harness passes none) -> the model's lists. Never merged.
            int stated = expected.CoolingDesignDays.Count + expected.HeatingDesignDays.Count;
            int written = native.Idf("SizingPeriod:DesignDay").Count() + native.Idf("SizingPeriod:WeatherFileDays").Count() + native.Idf("SizingPeriod:WeatherFileConditionType").Count();
            Supporting.Add("design days: SAM states " + stated + " (" + expected.DesignDayRole + "); the IDF has " + written + " sizing period(s)");
            ExpectedValue e = stated == 0 ? ExpectedValue.Unspecified("no DDY and no design day in the model") : ExpectedValue.FromValue(stated);
            ReaderResult n = written == 0 ? ReaderResult.Absent("no sizing period") : stated == 0 ? ReaderResult.FromValue(written) : ReaderResult.Unreadable("T-DD parametric comparison is not implemented in S09 (the fixtures state no design day)");
            Add("10.os.count", Keys.DesignDays, e, n, IdentityStatus.Verified);
        }

        // ---- result plumbing --------------------------------------------------------------------------------------------

        private QuantityInput Input(string quantityId, string key, ExpectedValue expected, ReaderResult nativeValue, IdentityStatus identity, AlignmentStatus alignment, ContractListing listing = ContractListing.None, ModelInterpretation interpretation = ModelInterpretation.NotRequired, string transformation = null, string inactive = null)
        {
            return new QuantityInput
            {
                Definition = OsQuantities.Get(quantityId),
                FixtureId = FixtureId,
                ObjectKey = key,
                Expected = expected,
                Native = nativeValue,
                Identity = identity,
                Alignment = alignment,
                Listing = listing,
                TransformationId = transformation,
                TransformationVerified = transformation != null,
                Interpretation = interpretation,
                InactiveNativeTransformation = inactive
            };
        }

        private void Add(string quantityId, string key, ExpectedValue expected, ReaderResult nativeValue, IdentityStatus identity, ModelInterpretation interpretation = ModelInterpretation.NotRequired, string transformation = null, string inactive = null)
        {
            Results.Add(EQ.Evaluate(Input(quantityId, key, expected ?? ExpectedValue.Invalid("no expected value"), nativeValue, identity, AlignmentStatus.Unverified, ContractListing.None, interpretation, transformation, inactive), Configuration));
        }

        private void AddSeries(string quantityId, string key, IReadOnlyList<ExpectedValue> expected, IReadOnlyList<ReaderResult> nativeValues, IdentityStatus identity, AlignmentStatus alignment, string inactive, ModelInterpretation interpretation)
        {
            SeriesResult series = EQ.EvaluateSeries(Input(quantityId, key, null, null, identity, alignment, ContractListing.None, interpretation, null, inactive), expected, nativeValues, Configuration);
            Results.Add(series.GateResult);
            QuantityResult first = series.Elements.FirstOrDefault(x => !x.IsPass);
            Series.Add(new SeriesSummary
            {
                Key = key,
                QuantityId = quantityId,
                Count = series.Elements.Count,
                NotPass = series.NotPassCount,
                Status = series.GateResult.Status.ToString(),
                Outcome = series.GateResult.Outcome.ToString(),
                FirstNotPass = first == null ? null : first.Input.ObjectKey + ": " + first.Outcome + " (expected " + first.Input.Expected + ", native " + first.Input.Native?.Kind + " " + (first.Input.Native?.HasNumber == true ? first.Input.Native.Value.ToString("R", ci) : first.Input.Native?.Detail) + ")"
            });
        }
    }
}
