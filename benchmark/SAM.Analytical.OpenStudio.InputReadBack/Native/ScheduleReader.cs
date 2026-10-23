// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;

namespace SAM.Analytical.OpenStudio.InputReadBack
{
    /// <summary>
    /// One native schedule expanded to hourly values of the run year. <see cref="Values"/> is null when the schedule cannot
    /// be read (<see cref="Problem"/> says why); a value that does not parse is never turned into a number.
    /// </summary>
    public sealed class NativeSchedule
    {
        public string Source { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
        public double[] Values { get; set; }
        public string Problem { get; set; }

        /// <summary>True when the file itself labels every hour (S07 section 7): see <see cref="AlignmentEvidence"/>.</summary>
        public bool AlignmentVerified { get; set; }
        public string AlignmentEvidence { get; set; }
    }

    /// <summary>
    /// Expands native schedules to hourly values. Hour index 0 is 1 January 00:00-01:00 of the run year. The supported forms
    /// are the ones the OpenStudio route writes: IDF Schedule:Compact with "For: AllDays" and whole-hour "Until:" labels,
    /// Schedule:Constant, and OSM OS:Schedule:FixedInterval. Anything else is unreadable, never approximated.
    /// </summary>
    public static class ScheduleReader
    {
        private static readonly CultureInfo ci = CultureInfo.InvariantCulture;

        public static NativeSchedule Read(IdfObject idfObject, int dayCount, string source)
        {
            NativeSchedule result = new NativeSchedule { Source = source, Type = idfObject.Type, Name = idfObject.Type.StartsWith("OS:", StringComparison.Ordinal) ? idfObject.Value(1) : idfObject.Name };
            try
            {
                switch (idfObject.Type.ToLowerInvariant())
                {
                    case "schedule:compact":
                        Compact(idfObject, dayCount, result);
                        break;
                    case "schedule:constant":
                        Constant(idfObject, dayCount, result);
                        break;
                    case "os:schedule:fixedinterval":
                        FixedInterval(idfObject, dayCount, result);
                        break;
                    default:
                        result.Problem = "schedule type " + idfObject.Type + " is not read by the S09 reader";
                        break;
                }
            }
            catch (FormatException exception)
            {
                result.Values = null;
                result.AlignmentVerified = false;
                result.Problem = exception.Message;
            }

            return result;
        }

        private static void Constant(IdfObject idfObject, int dayCount, NativeSchedule result)
        {
            double value = Number(idfObject.Value(2), "Schedule:Constant hourly value");
            result.Values = Fill(dayCount * 24, value);
            result.AlignmentVerified = true;
            result.AlignmentEvidence = "constant: every hour has the same value";
        }

        private static void Compact(IdfObject idfObject, int dayCount, NativeSchedule result)
        {
            double[] values = new double[dayCount * 24];
            bool[] set = new bool[values.Length];
            int day = 0;
            int i = 2;
            int throughCount = 0;
            while (i < idfObject.Fields.Count)
            {
                string through = idfObject.Value(i);
                if (string.IsNullOrWhiteSpace(through))
                {
                    i++;
                    continue;
                }

                if (!through.StartsWith("Through:", StringComparison.OrdinalIgnoreCase))
                {
                    throw new FormatException("Schedule:Compact '" + result.Name + "': expected 'Through:' at field " + i + ", found '" + through + "'");
                }

                int lastDay = DayOfYear(through.Substring(8).Trim(), dayCount, result.Name);
                if (lastDay < day)
                {
                    throw new FormatException("Schedule:Compact '" + result.Name + "': 'Through:' dates go backwards at field " + i);
                }

                throughCount++;
                i++;
                string forDays = idfObject.Value(i);
                if (forDays == null || !string.Equals(forDays.Replace(" ", string.Empty), "For:AllDays", StringComparison.OrdinalIgnoreCase))
                {
                    throw new FormatException("Schedule:Compact '" + result.Name + "': only 'For: AllDays' is read, found '" + forDays + "'");
                }

                i++;
                double[] dayValues = new double[24];
                int hour = 0;
                while (i + 1 < idfObject.Fields.Count && idfObject.Value(i) != null && idfObject.Value(i).StartsWith("Until:", StringComparison.OrdinalIgnoreCase))
                {
                    int until = Hour(idfObject.Value(i).Substring(6).Trim(), result.Name);
                    double value = Number(idfObject.Value(i + 1), "Schedule:Compact '" + result.Name + "' value");
                    if (until <= hour)
                    {
                        throw new FormatException("Schedule:Compact '" + result.Name + "': 'Until:' hours do not increase at field " + i);
                    }

                    for (int h = hour; h < until; h++)
                    {
                        dayValues[h] = value;
                    }

                    hour = until;
                    i += 2;
                }

                if (hour != 24)
                {
                    throw new FormatException("Schedule:Compact '" + result.Name + "': a day ends at hour " + hour + ", not 24:00");
                }

                for (int d = day; d <= lastDay; d++)
                {
                    Array.Copy(dayValues, 0, values, d * 24, 24);
                    for (int h = 0; h < 24; h++)
                    {
                        set[d * 24 + h] = true;
                    }
                }

                day = lastDay + 1;
            }

            if (day != dayCount || Array.IndexOf(set, false) >= 0)
            {
                throw new FormatException("Schedule:Compact '" + result.Name + "' covers " + day + " of " + dayCount + " days");
            }

            result.Values = values;
            result.AlignmentVerified = true;
            result.AlignmentEvidence = string.Format(ci, "Schedule:Compact: {0} 'Through:' blocks from 01/01 to 12/31, each 'For: AllDays' with whole-hour 'Until:' labels ending 24:00 (EnergyPlus: 'Until: hh:00' ends the interval [previous, hh))", throughCount);
        }

        private static void FixedInterval(IdfObject idfObject, int dayCount, NativeSchedule result)
        {
            string interpolate = idfObject.Value(4);
            double interval = Number(idfObject.Value(5), "OS:Schedule:FixedInterval interval length");
            double month = Number(idfObject.Value(7), "OS:Schedule:FixedInterval start month");
            double startDay = Number(idfObject.Value(8), "OS:Schedule:FixedInterval start day");
            if (interval != 60 || month != 1 || startDay != 1 || !string.Equals(interpolate, "No", StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException(string.Format(ci, "OS:Schedule:FixedInterval '{0}' is not hourly from 1 January without interpolation (interval {1}, start {2}/{3}, interpolate '{4}')", result.Name, interval, month, startDay, interpolate));
            }

            int count = idfObject.Fields.Count - 9;
            if (count != dayCount * 24)
            {
                throw new FormatException("OS:Schedule:FixedInterval '" + result.Name + "' has " + count + " values for a " + dayCount + "-day year");
            }

            double[] values = new double[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = Number(idfObject.Value(9 + i), "OS:Schedule:FixedInterval '" + result.Name + "' value " + (i + 1));
            }

            result.Values = values;
            result.AlignmentVerified = true;
            result.AlignmentEvidence = "OS:Schedule:FixedInterval: 60-minute values from 1 January, interpolation off (value k covers hour k-1)";
        }

        private static int DayOfYear(string date, int dayCount, string name)
        {
            string[] parts = date.Split('/');
            if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.Integer, ci, out int month) || !int.TryParse(parts[1], NumberStyles.Integer, ci, out int day))
            {
                throw new FormatException("Schedule:Compact '" + name + "': unreadable date '" + date + "'");
            }

            DateTime start = new DateTime(dayCount == 366 ? 2004 : 2006, 1, 1);
            try
            {
                return (new DateTime(start.Year, month, day) - start).Days;
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new FormatException("Schedule:Compact '" + name + "': invalid date '" + date + "'");
            }
        }

        private static int Hour(string time, string name)
        {
            string[] parts = time.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.Integer, ci, out int hour) || parts[1] != "00" || hour < 1 || hour > 24)
            {
                throw new FormatException("Schedule:Compact '" + name + "': 'Until: " + time + "' is not a whole hour");
            }

            return hour;
        }

        private static double Number(string text, string what)
        {
            if (string.IsNullOrWhiteSpace(text) || !double.TryParse(text, NumberStyles.Float, ci, out double value) || double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new FormatException(what + " is not a finite number: '" + text + "'");
            }

            return value;
        }

        private static double[] Fill(int count, double value)
        {
            double[] result = new double[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = value;
            }

            return result;
        }
    }
}
