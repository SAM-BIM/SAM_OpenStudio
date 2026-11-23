// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OpenStudio.ResultsReadBack
{
    /// <summary>The EnergyPlus output names S11 reads, and the label the producer must carry (an independent copy of its text).</summary>
    public static class S11Names
    {
        public const string TotalHeating = "Zone Ideal Loads Supply Air Total Heating Energy";
        public const string TotalCooling = "Zone Ideal Loads Supply Air Total Cooling Energy";
        public const string SensibleHeating = "Zone Ideal Loads Supply Air Sensible Heating Energy";
        public const string SensibleCooling = "Zone Ideal Loads Supply Air Sensible Cooling Energy";
        public const string UnmetHeating = "Zone Heating Setpoint Not Met Time";
        public const string UnmetCooling = "Zone Cooling Setpoint Not Met Time";
        public const string HeatingMeter = "DistrictHeatingWater:Facility";
        public const string CoolingMeter = "DistrictCooling:Facility";

        /// <summary>The F3 basis label every successful OpenStudio B1a document must carry (S11 plan section 2).</summary>
        public const string TotalBasisNote = "Cooling metrics are EnergyPlus Ideal Loads Supply Air Total cooling (sensible + latent); heating is Total heating.";

        /// <summary>The SAM result source the OpenStudio importer stamps on its results.</summary>
        public const string ResultSource = "SAM.Analytical.OpenStudio";

        /// <summary>Ideal Loads variables: keyed by the ZoneHVAC:IdealLoadsAirSystem name, unit J.</summary>
        public static readonly string[] IdealLoadsVariables = { TotalHeating, TotalCooling, SensibleHeating, SensibleCooling };

        /// <summary>Zone variables: keyed by the IDF Zone name, unit hr.</summary>
        public static readonly string[] ZoneVariables = { UnmetHeating, UnmetCooling };

        public static readonly string[] Meters = { HeatingMeter, CoolingMeter };

        public static string UnitOf(string variable) => ZoneVariables.Contains(variable) ? "hr" : "J";

        public static string Total(LoadKind kind) => kind == LoadKind.Heating ? TotalHeating : TotalCooling;

        public static string Sensible(LoadKind kind) => kind == LoadKind.Heating ? SensibleHeating : SensibleCooling;

        public static string Unmet(LoadKind kind) => kind == LoadKind.Heating ? UnmetHeating : UnmetCooling;

        public static string Meter(LoadKind kind) => kind == LoadKind.Heating ? HeatingMeter : CoolingMeter;

        /// <summary>The series key of a variable and a report key (EnergyPlus keys are case-insensitive).</summary>
        public static string SeriesKey(string variable, string key) => variable + "|" + (key ?? string.Empty).ToUpperInvariant();
    }

    public enum LoadKind
    {
        Heating,
        Cooling
    }

    /// <summary>One value of an hourly series as a file stamps it: month, day, EnergyPlus hour 1-24 (interval end).</summary>
    public sealed class HourlyPoint
    {
        public int Month { get; set; }
        public int Day { get; set; }
        public int Hour { get; set; }
        public double Value { get; set; }

        public HourKey Key => new HourKey(Month, Day, Hour);
    }

    /// <summary>A structural problem one channel found in one series, with the S11 check it belongs to.</summary>
    public sealed class SeriesProblem
    {
        public string Check { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// One hourly series (variable x key) as ONE channel (SQL or ESO) holds it in the RunPeriod environment, in file order.
    /// Channel-specific readers fill it; <see cref="Completeness"/> judges it against the calendar alone.
    /// </summary>
    public sealed class ChannelSeries
    {
        public string Channel { get; set; }
        public string Variable { get; set; }
        public string Key { get; set; }
        public string Units { get; set; }

        /// <summary>The Hourly data-dictionary entries for this variable and key (exactly one is required).</summary>
        public int DictionaryEntries { get; set; }

        public List<HourlyPoint> Points { get; set; } = new List<HourlyPoint>();
        public List<SeriesProblem> Problems { get; set; } = new List<SeriesProblem>();

        public void Problem(string check, string message, int limit = 5)
        {
            if (Problems.Count(x => x.Check == check) < limit)
            {
                Problems.Add(new SeriesProblem { Check = check, Message = message });
            }
        }
    }

    /// <summary>Completeness of one series in one file against the expected calendar (S11-C1).</summary>
    public sealed class CompletenessResult
    {
        public string Channel { get; set; }
        public string Variable { get; set; }
        public string Key { get; set; }
        public int DictionaryEntries { get; set; }
        public int Expected { get; set; }
        public int Found { get; set; }
        public int Missing { get; set; }
        public int Duplicated { get; set; }
        public int Extra { get; set; }
        public List<string> Examples { get; set; } = new List<string>();
        public List<string> Problems { get; set; } = new List<string>();
        public bool Complete { get; set; }

        /// <summary>The value of each expected hour (by 0-based hour of year) when <see cref="Complete"/>; otherwise null.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double[] Values { get; set; }
    }

    public static class Completeness
    {
        /// <summary>
        /// The multiset of the series' timestamps must equal the calendar exactly: each expected hour once, none missing,
        /// none repeated, none outside the calendar. A count of 8760 alone is never accepted (a repeated hour and a
        /// missing one keep the count). Every structural problem the channel reported also makes the series incomplete.
        /// </summary>
        public static CompletenessResult Check(RunCalendar calendar, ChannelSeries series)
        {
            CompletenessResult result = new CompletenessResult
            {
                Channel = series.Channel,
                Variable = series.Variable,
                Key = series.Key,
                DictionaryEntries = series.DictionaryEntries,
                Expected = calendar.Count,
                Found = series.Points.Count,
            };

            result.Problems.AddRange(series.Problems.Select(x => x.Check + ": " + x.Message));
            if (series.DictionaryEntries != 1)
            {
                result.Problems.Add("S11-C1: " + series.DictionaryEntries + " Hourly data-dictionary entries (exactly one is required)");
            }

            if (calendar.Problem != null)
            {
                result.Problems.Add("calendar: " + calendar.Problem);
                return result;
            }

            int[] counts = new int[calendar.Count];
            double[] values = new double[calendar.Count];
            foreach (HourlyPoint point in series.Points)
            {
                int hour = calendar.IndexOf(point.Key);
                if (hour < 0)
                {
                    result.Extra++;
                    Example(result, "extra " + point.Key);
                    continue;
                }

                counts[hour]++;
                values[hour] = point.Value;
            }

            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] == 0)
                {
                    result.Missing++;
                    Example(result, "missing " + calendar.Hours[i]);
                }
                else if (counts[i] > 1)
                {
                    result.Duplicated++;
                    Example(result, "duplicated x" + counts[i] + " " + calendar.Hours[i]);
                }
            }

            result.Complete = result.Problems.Count == 0 && result.Missing == 0 && result.Duplicated == 0 && result.Extra == 0;
            result.Values = result.Complete ? values : null;
            return result;
        }

        private static void Example(CompletenessResult result, string text)
        {
            if (result.Examples.Count < 6)
            {
                result.Examples.Add(text);
            }
        }
    }
}
