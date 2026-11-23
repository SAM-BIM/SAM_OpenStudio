// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical.OpenStudio.InputReadBack;
using SAM.Analytical.Tas.DirectT3D.Validation.ReadBack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SAM.Analytical.OpenStudio.ResultsReadBack
{
    public static class S11Json
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new JsonStringEnumConverter() },
        };

        public static void Save<T>(T value, string path)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(value, Options).Replace("\r\n", "\n") + "\n", new System.Text.UTF8Encoding(false));
        }

        public static T Load<T>(string path)
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
    }

    /// <summary>One S11 check (S11-C1..C9) as the reference read judged it.</summary>
    public sealed class ReferenceCheck
    {
        public string Id { get; set; }
        public string Scope { get; set; }

        /// <summary>PASS, BLOCKED or INFO (informational, never gating).</summary>
        public string Status { get; set; }
        public string Detail { get; set; }
    }

    /// <summary>The reference value of one quantity (variable x zone) from the raw SQL series after S11-C1..C4.</summary>
    public sealed class QuantityReference
    {
        public string Variable { get; set; }
        public string Key { get; set; }
        public bool Available { get; set; }
        public List<string> BlockReasons { get; set; } = new List<string>();

        /// <summary>Annual sum in the file unit (J or hr).</summary>
        public double Sum { get; set; }

        /// <summary>Largest hourly value (J per hour, or hr).</summary>
        public double Peak { get; set; }

        /// <summary>Peak power: the largest hourly energy / 3600 s [W] (energy variables only).</summary>
        public double PeakWatts { get; set; }

        /// <summary>Earliest 0-based hour of year holding the largest value (SAM's own tie rule).</summary>
        public int PeakHour { get; set; } = -1;

        /// <summary>Hours holding exactly the largest value.</summary>
        public int TieCount { get; set; }

        /// <summary>Every hour is exactly 0.</summary>
        public bool AllZero { get; set; }
    }

    /// <summary>The reference of one SAM space: its native identity and its quantities.</summary>
    public sealed class SpaceReference
    {
        public Guid SpaceGuid { get; set; }
        public string SpaceName { get; set; }
        public string NativeSpace { get; set; }
        public string Zone { get; set; }
        public string IdealLoads { get; set; }
        public int SurfacePairs { get; set; }
        public bool NameCarrierAgrees { get; set; }
        public string IdentityProblem { get; set; }

        /// <summary>Area and volume as the SAM space states them (inputs, not engine values).</summary>
        public double Area { get; set; } = double.NaN;
        public double Volume { get; set; } = double.NaN;

        public Dictionary<string, QuantityReference> Quantities { get; set; } = new Dictionary<string, QuantityReference>(StringComparer.Ordinal);

        /// <summary>Per load kind, why the Total/Sensible basis cannot be trusted (S11-C6: negative latent); absent when it can.</summary>
        public Dictionary<string, string> BasisProblems { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Total minus Sensible over the year [J], per load kind (informational).</summary>
        public Dictionary<string, double> LatentJoules { get; set; } = new Dictionary<string, double>(StringComparer.Ordinal);
    }

    /// <summary>The model-level reference of one load kind for one basis (Total or Sensible).</summary>
    public sealed class ModelBasisReference
    {
        public bool Available { get; set; }
        public double ConsumptionKwh { get; set; }

        /// <summary>Coincident peak: the largest hourly sum over zones / 3600 s / 1000 [kW].</summary>
        public double CoincidentPeakKw { get; set; }
        public int PeakHour { get; set; } = -1;
        public int TieCount { get; set; }
        public bool AllZero { get; set; }
    }

    public sealed class ModelLoadReference
    {
        public LoadKind Kind { get; set; }
        public ModelBasisReference Total { get; set; } = new ModelBasisReference();
        public ModelBasisReference Sensible { get; set; } = new ModelBasisReference();

        /// <summary>Why the model consumption cannot be trusted (S11-C7 meter disagreement); null when it can.</summary>
        public string MeterProblem { get; set; }
    }

    /// <summary>reference.json: everything the fresh-process reference read established, with its inputs' hashes.</summary>
    public sealed class ReferenceDocument
    {
        public const string CurrentSchema = "s11-reference/1";

        public string Schema { get; set; } = CurrentSchema;
        public int ProcessId { get; set; }
        public DateTime ProcessStartedUtc { get; set; }
        public DateTime StartedUtc { get; set; }
        public DateTime FinishedUtc { get; set; }
        public string ReaderAssemblySha256 { get; set; }
        public string FixtureId { get; set; }

        public string ModelPath { get; set; }
        public string ModelSha256 { get; set; }
        public string IdfPath { get; set; }
        public string IdfSha256Before { get; set; }
        public string IdfSha256After { get; set; }
        public string SqlPath { get; set; }
        public string SqlSha256Before { get; set; }
        public string SqlSha256After { get; set; }
        public string EsoPath { get; set; }
        public string EsoSha256Before { get; set; }
        public string EsoSha256After { get; set; }

        public string RunPeriod { get; set; }
        public int CalendarHours { get; set; }
        public List<string> SqlEnergyPlusVersions { get; set; } = new List<string>();
        public string EsoProgramVersion { get; set; }
        public List<string> SqlEnvironments { get; set; } = new List<string>();
        public List<string> EsoEnvironments { get; set; } = new List<string>();
        public int SqlOtherEnvironmentRows { get; set; }

        /// <summary>Reasons that block every row (calendar, environment, version, unreadable file, unknown keys).</summary>
        public List<string> GlobalBlocks { get; set; } = new List<string>();

        public List<ReferenceCheck> Checks { get; set; } = new List<ReferenceCheck>();
        public List<CompletenessResult> Completeness { get; set; } = new List<CompletenessResult>();
        public List<SpaceReference> Spaces { get; set; } = new List<SpaceReference>();
        public List<ModelLoadReference> Model { get; set; } = new List<ModelLoadReference>();
        public double FloorArea { get; set; }
        public double Volume { get; set; }
        public List<MeterSum> SqlMeters { get; set; } = new List<MeterSum>();
        public List<MeterSum> EsoMeters { get; set; } = new List<MeterSum>();
        public List<ZoneSizeRow> ZoneSizes { get; set; } = new List<ZoneSizeRow>();

        public SpaceReference Space(Guid guid) => Spaces.FirstOrDefault(x => x.SpaceGuid == guid);

        public ModelLoadReference ModelOf(LoadKind kind) => Model.FirstOrDefault(x => x.Kind == kind);
    }

    /// <summary>
    /// The S11 reference read (the CLI's <c>read</c> step, a separate process). Inputs are the frozen SAM model (expected
    /// spaces only), the run's in.idf, eplusout.sql and eplusout.eso. Nothing here calls the SAM_OpenStudio importer,
    /// writer or naming: the space identity is the S09 native identity and the values are the raw SQL series.
    /// </summary>
    public static class ResultReference
    {
        /// <summary>The S11 class E tolerance (ESO text against SQL double, and Total &gt;= Sensible).</summary>
        public static double ToleranceE(double reference) => 1e-12 * Math.Max(1, Math.Abs(reference));

        /// <summary>The S11 class R2 tolerance (sums over the year), in the row's unit.</summary>
        public static double ToleranceR2(double reference) => 1e-9 * Math.Abs(reference) + 1e-9;

        public static ReferenceDocument Build(AnalyticalModel model, string modelPath, string idfPath, string sqlPath, string esoPath, string fixtureId)
        {
            ReferenceDocument result = new ReferenceDocument
            {
                ProcessId = Environment.ProcessId,
                ProcessStartedUtc = ProcessIdentity.CurrentStartedUtc(),
                StartedUtc = DateTime.UtcNow,
                ReaderAssemblySha256 = Sha256.Of(typeof(ResultReference).Assembly.Location),
                FixtureId = fixtureId,
                ModelPath = modelPath == null ? null : Path.GetFullPath(modelPath),
                ModelSha256 = Sha256.Of(modelPath),
                IdfPath = Path.GetFullPath(idfPath),
                IdfSha256Before = Sha256.Of(idfPath),
            };

            // Native IDF (the input EnergyPlus ran) as text; the calendar and the identity come from it alone.
            List<IdfObject> idf = IdfText.Parse(File.ReadAllText(idfPath)).Where(x => NativeReader.IdfTypes.Contains(x.Type, StringComparer.OrdinalIgnoreCase)).ToList();
            result.IdfSha256After = Sha256.Of(idfPath);
            RunCalendar calendar = RunCalendar.FromIdf(idf);
            result.RunPeriod = calendar.Evidence;
            result.CalendarHours = calendar.Count;
            Check(result, "S11-C1", "calendar", calendar.Problem == null, calendar.Problem ?? calendar.Evidence + ": " + calendar.Count + " expected hourly intervals");
            if (calendar.Problem != null)
            {
                result.GlobalBlocks.Add("calendar: " + calendar.Problem);
            }

            SqlResultRead sql = SqlResultRead.Read(sqlPath, calendar);
            EsoResultRead eso = EsoResultRead.Read(esoPath, calendar);
            result.SqlPath = sql.Path;
            result.SqlSha256Before = sql.Sha256Before;
            result.SqlSha256After = sql.Sha256After;
            result.EsoPath = eso.Path;
            result.EsoSha256Before = eso.Sha256Before;
            result.EsoSha256After = eso.Sha256After;
            result.SqlEnergyPlusVersions = sql.EnergyPlusVersions;
            result.EsoProgramVersion = eso.ProgramVersion;
            result.SqlEnvironments = sql.Environments.Select(x => x.Name + " (type " + x.Type.ToString(CultureInfo.InvariantCulture) + ")").ToList();
            result.EsoEnvironments = eso.Environments;
            result.SqlOtherEnvironmentRows = sql.OtherEnvironmentRows;
            result.SqlMeters = sql.Meters;
            result.EsoMeters = eso.Meters;
            result.ZoneSizes = sql.ZoneSizes;

            Files(result, sql, eso);
            Version(result, sql, eso);

            ExpectedModel expected = ReadBackExpected.Build(model, null, fixtureId);
            NativeModel native = new NativeModel(new NativeSnapshot { Idf = idf });
            List<NativeZoneIdentityEntry> identity = NativeZoneIdentity.Resolve(native, expected);
            Identity(result, model, identity);
            KeySets(result, identity, sql, eso);
            Dictionary<QuantityReference, double[]> hourly = Quantities(result, calendar, sql, eso);
            ModelValues(result, hourly);
            Meters(result);

            Check(result, "S11-C8", "sizing", true, "ZoneSizes rows: " + result.ZoneSizes.Count + " (judged against designDaySource in compare)", "INFO");
            result.FinishedUtc = DateTime.UtcNow;
            return result;
        }

        private static void Check(ReferenceDocument document, string id, string scope, bool pass, string detail, string status = null)
        {
            document.Checks.Add(new ReferenceCheck { Id = id, Scope = scope, Status = status ?? (pass ? "PASS" : "BLOCKED"), Detail = detail });
        }

        private static void Files(ReferenceDocument document, SqlResultRead sql, EsoResultRead eso)
        {
            List<string> problems = new List<string>();
            if (sql.Problem != null) problems.Add(sql.Problem);
            if (sql.EnvironmentProblem != null) problems.Add("sql: " + sql.EnvironmentProblem);
            problems.AddRange(eso.Problems.Select(x => "eso: " + x));
            if (eso.RunPeriodBlocks != 1) problems.Add("eso: " + eso.RunPeriodBlocks + " environment blocks are named as the RunPeriod (exactly one is required); environments: " + string.Join(", ", eso.Environments));
            if (sql.Sha256Before != sql.Sha256After) problems.Add("eplusout.sql changed while it was read");
            if (eso.Sha256Before != eso.Sha256After) problems.Add("eplusout.eso changed while it was read");
            if (document.IdfSha256Before != document.IdfSha256After) problems.Add("in.idf changed while it was read");
            document.GlobalBlocks.AddRange(problems);
            Check(document, "S11-C1", "files and RunPeriod environment", problems.Count == 0, problems.Count == 0 ? "SQL environment and one ESO RunPeriod block found; files unchanged by the read" : string.Join("; ", problems));
        }

        private static void Version(ReferenceDocument document, SqlResultRead sql, EsoResultRead eso)
        {
            bool sqlOk = sql.EnergyPlusVersions.Count == 1 && sql.EnergyPlusVersions[0] != null && sql.EnergyPlusVersions[0].Contains("Version 25.1.");
            bool esoOk = eso.ProgramVersion != null && eso.ProgramVersion.Contains("Version 25.1.");
            string detail = "SQL Simulations: " + string.Join(" | ", sql.EnergyPlusVersions) + "; ESO: " + eso.ProgramVersion;
            Check(document, "S11-C9", "EnergyPlus version", sqlOk && esoOk, detail);
            if (!(sqlOk && esoOk))
            {
                document.GlobalBlocks.Add("S11-C9: EnergyPlus 25.1 is required in both files (" + detail + ")");
            }
        }

        private static void Identity(ReferenceDocument document, AnalyticalModel model, List<NativeZoneIdentityEntry> identity)
        {
            List<Space> spaces = model?.AdjacencyCluster?.GetSpaces() ?? new List<Space>();
            foreach (NativeZoneIdentityEntry entry in identity)
            {
                Space space = spaces.FirstOrDefault(x => x.Guid == entry.SpaceGuid);
                SpaceReference reference = new SpaceReference
                {
                    SpaceGuid = entry.SpaceGuid,
                    SpaceName = entry.SpaceName,
                    NativeSpace = entry.NativeSpace,
                    Zone = entry.Zone,
                    IdealLoads = entry.IdealLoads,
                    SurfacePairs = entry.SurfacePairs,
                    NameCarrierAgrees = entry.NameCarrierAgrees,
                    IdentityProblem = entry.Problem,
                };

                if (space != null && space.TryGetValue(SpaceParameter.Area, out double area)) reference.Area = area;
                if (space != null && space.TryGetValue(SpaceParameter.Volume, out double volume)) reference.Volume = volume;
                document.Spaces.Add(reference);
                Check(document, "S11-C5", "identity " + entry.SpaceGuid.ToString("N"), entry.Problem == null, entry.Problem ?? ("'" + entry.SpaceName + "' = native space '" + entry.NativeSpace + "' (" + entry.SurfacePairs + " surfaces one to one), zone '" + entry.Zone + "', Ideal Loads '" + entry.IdealLoads + "'; name carrier " + (entry.NameCarrierAgrees ? "agrees" : "CONTRADICTS")));
            }

            // Floor area and volume of the model are the sums of the SAM space inputs (as SAM states them).
            document.FloorArea = 0;
            document.Volume = 0;
            foreach (Space space in spaces)
            {
                if (space.TryGetValue(SpaceParameter.Area, out double area) && !double.IsNaN(area)) document.FloorArea += area;
                if (space.TryGetValue(SpaceParameter.Volume, out double volume) && !double.IsNaN(volume)) document.Volume += volume;
            }

            if (identity.Count == 0)
            {
                document.GlobalBlocks.Add("S11-C5: the model has no space to identify");
            }
        }

        /// <summary>S11-C5: the report keys in each file are exactly the identity's Ideal Loads and Zone names.</summary>
        private static void KeySets(ReferenceDocument document, List<NativeZoneIdentityEntry> identity, SqlResultRead sql, EsoResultRead eso)
        {
            HashSet<string> idealLoads = new HashSet<string>(identity.Where(x => x.IdealLoads != null).Select(x => x.IdealLoads.ToUpperInvariant()));
            HashSet<string> zones = new HashSet<string>(identity.Where(x => x.Zone != null).Select(x => x.Zone.ToUpperInvariant()));
            List<string> problems = new List<string>();
            foreach (KeyValuePair<string, Dictionary<string, ChannelSeries>> channel in new[] { new KeyValuePair<string, Dictionary<string, ChannelSeries>>("sql", sql.Series), new KeyValuePair<string, Dictionary<string, ChannelSeries>>("eso", eso.Series) })
            {
                foreach (ChannelSeries series in channel.Value.Values)
                {
                    HashSet<string> expected = S11Names.ZoneVariables.Contains(series.Variable) ? zones : idealLoads;
                    if (!expected.Contains((series.Key ?? string.Empty).ToUpperInvariant()))
                    {
                        problems.Add(channel.Key + ": '" + series.Variable + "' key '" + series.Key + "' is not a native identity of any SAM space");
                    }
                }
            }

            Check(document, "S11-C5", "report keys", problems.Count == 0, problems.Count == 0 ? "every SQL and ESO key is an identified Zone or Ideal Loads" : string.Join("; ", problems.Take(10)));
            if (problems.Count > 0)
            {
                document.GlobalBlocks.Add("S11-C5: " + problems.Count + " unknown report key(s), e.g. " + problems[0]);
            }
        }

        /// <summary>
        /// Judges every (space x variable) in both files and fills its reference values. Returns the hourly SQL values of
        /// each available quantity for the model sums (reference.json carries the derived values, not the 8760-value arrays).
        /// </summary>
        private static Dictionary<QuantityReference, double[]> Quantities(ReferenceDocument document, RunCalendar calendar, SqlResultRead sql, EsoResultRead eso)
        {
            Dictionary<QuantityReference, double[]> hourly = new Dictionary<QuantityReference, double[]>();
            foreach (SpaceReference space in document.Spaces)
            {
                Dictionary<string, double[]> values = new Dictionary<string, double[]>(StringComparer.Ordinal);
                foreach (string variable in S11Names.IdealLoadsVariables.Concat(S11Names.ZoneVariables))
                {
                    string key = S11Names.ZoneVariables.Contains(variable) ? space.Zone : space.IdealLoads;
                    QuantityReference quantity = new QuantityReference { Variable = variable, Key = key };
                    space.Quantities[variable] = quantity;
                    if (space.IdentityProblem != null || key == null)
                    {
                        quantity.BlockReasons.Add("identity: " + (space.IdentityProblem ?? "no native key"));
                        continue;
                    }

                    sql.Series.TryGetValue(S11Names.SeriesKey(variable, key), out ChannelSeries sqlSeries);
                    eso.Series.TryGetValue(S11Names.SeriesKey(variable, key), out ChannelSeries esoSeries);
                    double[] sqlValues = Judge(document, calendar, "sql", variable, key, sqlSeries, quantity);
                    double[] esoValues = Judge(document, calendar, "eso", variable, key, esoSeries, quantity);
                    if (sqlValues == null || esoValues == null)
                    {
                        continue;
                    }

                    // S11-C4: the two channels agree hour by hour (class E), judged only once both are complete.
                    int disagreements = 0;
                    string first = null;
                    for (int h = 0; h < sqlValues.Length; h++)
                    {
                        if (!(Math.Abs(sqlValues[h] - esoValues[h]) <= ToleranceE(sqlValues[h])))
                        {
                            disagreements++;
                            first = first ?? (calendar.Hours[h] + " sql " + sqlValues[h].ToString("R", CultureInfo.InvariantCulture) + " eso " + esoValues[h].ToString("R", CultureInfo.InvariantCulture));
                        }
                    }

                    if (disagreements > 0)
                    {
                        quantity.BlockReasons.Add("S11-C4: SQL and ESO disagree in " + disagreements + " hour(s), first " + first);
                        Check(document, "S11-C4", variable + " " + key, false, "SQL and ESO disagree in " + disagreements + " hour(s), first " + first);
                        continue;
                    }

                    values[variable] = sqlValues;
                    hourly[quantity] = sqlValues;
                    Fill(quantity, sqlValues);
                    quantity.Available = true;
                }

                foreach (LoadKind kind in new[] { LoadKind.Heating, LoadKind.Cooling })
                {
                    if (values.TryGetValue(S11Names.Total(kind), out double[] total) && values.TryGetValue(S11Names.Sensible(kind), out double[] sensible))
                    {
                        int negative = 0;
                        string first = null;
                        double latent = 0;
                        for (int h = 0; h < total.Length; h++)
                        {
                            latent += total[h] - sensible[h];
                            if (total[h] < sensible[h] - ToleranceE(total[h]))
                            {
                                negative++;
                                first = first ?? (calendar.Hours[h] + " Total " + total[h].ToString("R", CultureInfo.InvariantCulture) + " < Sensible " + sensible[h].ToString("R", CultureInfo.InvariantCulture));
                            }
                        }

                        space.LatentJoules[kind.ToString()] = latent;
                        if (negative > 0)
                        {
                            space.BasisProblems[kind.ToString()] = "S11-C6: Total < Sensible (negative latent) in " + negative + " hour(s), first " + first;
                        }

                        Check(document, "S11-C6", kind + " " + space.SpaceGuid.ToString("N"), negative == 0, negative == 0 ? "Total >= Sensible every hour; annual latent " + latent.ToString("R", CultureInfo.InvariantCulture) + " J" : space.BasisProblems[kind.ToString()]);
                    }
                }
            }

            return hourly;
        }

        /// <summary>One channel's series judged against the calendar (S11-C1/C1b) and S11-C3; its values, or null with the reason recorded.</summary>
        private static double[] Judge(ReferenceDocument document, RunCalendar calendar, string channel, string variable, string key, ChannelSeries series, QuantityReference quantity)
        {
            if (series == null)
            {
                string missing = channel + ": no Hourly '" + variable + "' for key '" + key + "'";
                quantity.BlockReasons.Add("S11-C1b: " + missing);
                document.Completeness.Add(new CompletenessResult { Channel = channel, Variable = variable, Key = key, Expected = calendar.Count, Problems = { "absent from the file" } });
                return null;
            }

            CompletenessResult completeness = Completeness.Check(calendar, series);
            document.Completeness.Add(completeness);
            if (!completeness.Complete)
            {
                quantity.BlockReasons.Add("S11-C1: " + channel + " '" + variable + "' '" + key + "' incomplete: missing " + completeness.Missing + ", duplicated " + completeness.Duplicated + ", extra " + completeness.Extra + (completeness.Problems.Count > 0 ? "; " + string.Join("; ", completeness.Problems.Take(3)) : string.Empty) + (completeness.Examples.Count > 0 ? "; e.g. " + string.Join(", ", completeness.Examples.Take(3)) : string.Empty));
                return null;
            }

            // S11-C3: units, finite, non-negative.
            string unit = S11Names.UnitOf(variable);
            if (!string.Equals(series.Units, unit, StringComparison.Ordinal))
            {
                quantity.BlockReasons.Add("S11-C3: " + channel + " '" + variable + "' unit is '" + series.Units + "', not '" + unit + "'");
                return null;
            }

            int bad = completeness.Values.Count(x => double.IsNaN(x) || double.IsInfinity(x) || x < 0);
            if (bad > 0)
            {
                quantity.BlockReasons.Add("S11-C3: " + channel + " '" + variable + "' '" + key + "' has " + bad + " non-finite or negative value(s)");
                return null;
            }

            return completeness.Values;
        }

        private static void Fill(QuantityReference quantity, double[] values)
        {
            double sum = 0;
            double peak = double.MinValue;
            int peakHour = -1;
            foreach (double value in values)
            {
                sum += value;
            }

            for (int h = 0; h < values.Length; h++)
            {
                if (values[h] > peak)
                {
                    peak = values[h];
                    peakHour = h;
                }
            }

            quantity.Sum = sum;
            quantity.Peak = peak;
            quantity.PeakHour = peakHour;
            quantity.TieCount = values.Count(x => x == peak);
            quantity.AllZero = values.All(x => x == 0);
            quantity.PeakWatts = S11Names.ZoneVariables.Contains(quantity.Variable) ? double.NaN : peak / 3600.0;
        }

        /// <summary>Model consumption and coincident peak per load kind and basis, from the identified zones' SQL series.</summary>
        private static void ModelValues(ReferenceDocument document, Dictionary<QuantityReference, double[]> hourly)
        {
            foreach (LoadKind kind in new[] { LoadKind.Heating, LoadKind.Cooling })
            {
                ModelLoadReference model = new ModelLoadReference { Kind = kind };
                model.Total = Basis(document, hourly, S11Names.Total(kind));
                model.Sensible = Basis(document, hourly, S11Names.Sensible(kind));
                document.Model.Add(model);
            }
        }

        /// <summary>Available only when every identified space's series of <paramref name="variable"/> is available.</summary>
        private static ModelBasisReference Basis(ReferenceDocument document, Dictionary<QuantityReference, double[]> hourly, string variable)
        {
            ModelBasisReference result = new ModelBasisReference();
            List<double[]> series = new List<double[]>();
            foreach (SpaceReference space in document.Spaces)
            {
                if (!space.Quantities.TryGetValue(variable, out QuantityReference quantity) || !quantity.Available || !hourly.TryGetValue(quantity, out double[] values))
                {
                    return result;
                }

                series.Add(values);
            }

            if (series.Count == 0)
            {
                return result;
            }

            double joules = 0;
            foreach (double[] values in series)
            {
                double zone = 0;
                foreach (double value in values)
                {
                    zone += value;
                }

                joules += zone;
            }

            double[] totals = new double[series[0].Length];
            foreach (double[] values in series)
            {
                for (int h = 0; h < totals.Length; h++)
                {
                    totals[h] += values[h];
                }
            }

            double peak = double.MinValue;
            int peakHour = -1;
            for (int h = 0; h < totals.Length; h++)
            {
                if (totals[h] > peak)
                {
                    peak = totals[h];
                    peakHour = h;
                }
            }

            result.Available = true;
            result.ConsumptionKwh = joules / 3.6e6;
            result.CoincidentPeakKw = peak / 3600.0 / 1000.0;
            result.PeakHour = peakHour;
            result.TieCount = totals.Count(x => x == peak);
            result.AllZero = totals.All(x => x == 0);
            return result;
        }

        /// <summary>S11-C7: each facility meter equals the sum of the zones' Total energy (class R2), in each file where it exists.</summary>
        private static void Meters(ReferenceDocument document)
        {
            foreach (LoadKind kind in new[] { LoadKind.Heating, LoadKind.Cooling })
            {
                ModelLoadReference model = document.ModelOf(kind);
                if (!model.Total.Available)
                {
                    continue;
                }

                double zonesJoules = model.Total.ConsumptionKwh * 3.6e6;
                foreach (KeyValuePair<string, List<MeterSum>> channel in new[] { new KeyValuePair<string, List<MeterSum>>("sql", document.SqlMeters), new KeyValuePair<string, List<MeterSum>>("eso", document.EsoMeters) })
                {
                    MeterSum meter = channel.Value.FirstOrDefault(x => string.Equals(x.Name, S11Names.Meter(kind), StringComparison.OrdinalIgnoreCase));
                    if (meter == null)
                    {
                        Check(document, "S11-C7", kind + " " + channel.Key, true, channel.Key + " has no " + S11Names.Meter(kind) + " meter (informational)", "INFO");
                        continue;
                    }

                    double delta = Math.Abs(meter.Sum - zonesJoules);
                    bool pass = delta <= ToleranceR2(zonesJoules);
                    string detail = channel.Key + " " + meter.Name + " (" + meter.Frequency + ", " + meter.Rows + " rows) " + meter.Sum.ToString("R", CultureInfo.InvariantCulture) + " J vs zones " + zonesJoules.ToString("R", CultureInfo.InvariantCulture) + " J, |delta| " + delta.ToString("R", CultureInfo.InvariantCulture);
                    Check(document, "S11-C7", kind + " " + channel.Key, pass, detail);
                    if (!pass)
                    {
                        model.MeterProblem = (model.MeterProblem == null ? string.Empty : model.MeterProblem + "; ") + "S11-C7: " + detail;
                    }
                }
            }
        }
    }
}
