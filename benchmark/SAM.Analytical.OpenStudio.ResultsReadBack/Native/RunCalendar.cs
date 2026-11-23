// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical.OpenStudio.InputReadBack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SAM.Analytical.OpenStudio.ResultsReadBack
{
    /// <summary>One expected hourly interval: month, day and EnergyPlus hour 1-24 (the interval END), and its 0-based hour of year.</summary>
    public readonly struct HourKey : IEquatable<HourKey>
    {
        public HourKey(int month, int day, int hour)
        {
            Month = month;
            Day = day;
            Hour = hour;
        }

        public int Month { get; }
        public int Day { get; }
        public int Hour { get; }

        public bool Equals(HourKey other) => Month == other.Month && Day == other.Day && Hour == other.Hour;

        public override bool Equals(object obj) => obj is HourKey other && Equals(other);

        public override int GetHashCode() => (Month * 32 + Day) * 32 + Hour;

        public override string ToString() => Month.ToString(CultureInfo.InvariantCulture) + "/" + Day.ToString(CultureInfo.InvariantCulture) + " h" + Hour.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The expected hourly calendar of the run, built from the single RunPeriod of the run's in.idf (the input EnergyPlus
    /// ran), never from the result data. S11 requires a full non-leap year 1/1-12/31; anything else is a
    /// <see cref="Problem"/> and every row blocks. Hour <c>h</c> of the year (0-based) is the interval h:00-h+1:00, which
    /// EnergyPlus stamps with its END hour <c>h mod 24 + 1</c> on that day.
    /// </summary>
    public sealed class RunCalendar
    {
        private static readonly int[] DaysInMonth = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

        private readonly Dictionary<HourKey, int> index = new Dictionary<HourKey, int>();

        public string RunPeriodName { get; private set; }

        /// <summary>The run period's evidence as read (begin/end month/day/year).</summary>
        public string Evidence { get; private set; }

        /// <summary>Null when the calendar is valid; otherwise why no calendar can be defined.</summary>
        public string Problem { get; private set; }

        /// <summary>The expected hourly keys in order (8760 when valid, empty otherwise).</summary>
        public List<HourKey> Hours { get; } = new List<HourKey>();

        public int Count => Hours.Count;

        /// <summary>The 0-based hour of year of <paramref name="key"/>, or -1 when the key is not in the calendar.</summary>
        public int IndexOf(HourKey key) => index.TryGetValue(key, out int value) ? value : -1;

        public static RunCalendar FromIdf(IEnumerable<IdfObject> idf)
        {
            RunCalendar result = new RunCalendar();
            List<IdfObject> runPeriods = (idf ?? Enumerable.Empty<IdfObject>()).Where(x => string.Equals(x.Type, "RunPeriod", StringComparison.OrdinalIgnoreCase)).ToList();
            if (runPeriods.Count != 1)
            {
                result.Problem = "in.idf has " + runPeriods.Count + " RunPeriod objects (exactly one is required)";
                return result;
            }

            IdfObject runPeriod = runPeriods[0];
            result.RunPeriodName = runPeriod.Name;
            int? beginMonth = Integer(runPeriod, 1, "Begin Month", out string p1);
            int? beginDay = Integer(runPeriod, 2, "Begin Day of Month", out string p2);
            int? beginYear = Integer(runPeriod, 3, "Begin Year", out string p3);
            int? endMonth = Integer(runPeriod, 4, "End Month", out string p4);
            int? endDay = Integer(runPeriod, 5, "End Day of Month", out string p5);
            int? endYear = Integer(runPeriod, 6, "End Year", out string p6);
            string problem = new[] { p1, p2, p3, p4, p5, p6 }.FirstOrDefault(x => x != null);
            result.Evidence = string.Format(CultureInfo.InvariantCulture, "RunPeriod '{0}': {1}/{2}/{3} to {4}/{5}/{6}", runPeriod.Name, beginMonth, beginDay, beginYear?.ToString(CultureInfo.InvariantCulture) ?? "(no year)", endMonth, endDay, endYear?.ToString(CultureInfo.InvariantCulture) ?? "(no year)");
            if (problem != null)
            {
                result.Problem = problem;
                return result;
            }

            if (string.IsNullOrWhiteSpace(runPeriod.Name))
            {
                result.Problem = "the RunPeriod has no name, so its environment cannot be identified";
            }
            else if (beginMonth != 1 || beginDay != 1 || endMonth != 12 || endDay != 31)
            {
                result.Problem = result.Evidence + " is not the full year 1/1-12/31";
            }
            else if (beginYear.HasValue && DateTime.IsLeapYear(beginYear.Value))
            {
                result.Problem = result.Evidence + " is a leap year (S11 requires 8760 hours)";
            }
            else if (beginYear.HasValue && endYear.HasValue && beginYear.Value != endYear.Value)
            {
                result.Problem = result.Evidence + " spans more than one year";
            }
            else if (!beginYear.HasValue && endYear.HasValue)
            {
                result.Problem = result.Evidence + " states an end year without a begin year";
            }

            if (result.Problem != null)
            {
                return result;
            }

            for (int month = 1; month <= 12; month++)
            {
                for (int day = 1; day <= DaysInMonth[month - 1]; day++)
                {
                    for (int hour = 1; hour <= 24; hour++)
                    {
                        HourKey key = new HourKey(month, day, hour);
                        result.index[key] = result.Hours.Count;
                        result.Hours.Add(key);
                    }
                }
            }

            return result;
        }

        /// <summary>A blank field is null (EnergyPlus default); text that is not an integer is a problem.</summary>
        private static int? Integer(IdfObject o, int field, string label, out string problem)
        {
            string text = o.Value(field, label, out problem);
            if (problem != null || string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                return value;
            }

            problem = "RunPeriod '" + o.Name + "' " + label + " is not an integer: '" + text + "'";
            return null;
        }
    }
}
