using System;

namespace SAM.Core.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Authoritative conversion of one EnergyPlus SQLite <c>Time</c> row to a .NET
        /// <see cref="DateTime"/>. EnergyPlus convention (verified against live
        /// <c>eplusout.sql</c> output): the fields denote the <b>end of the reporting
        /// interval</b> — <c>Hour</c> runs 0–24 and <c>Minute</c> 0–60, so hour 24 means
        /// midnight at the end of the day (next day 00:00), minute 60 means the full hour, and
        /// sub-hourly rows start the day at <c>Hour = 0</c> (a <c>(0, 10)</c> row is the
        /// interval ending 00:10). Sizing/design-day environments write <c>Year = 0</c>.
        /// Normalisation: validate the calendar date, then add a <see cref="TimeSpan"/> —
        /// day/month/year rollover (including December 31 24:00 and leap years) is handled by
        /// <see cref="DateTime"/> arithmetic, never by clamping 24 → 23 or 60 → 59. Rows that
        /// cannot form a valid calendar date (for example February 29 under a non-leap
        /// calendar, month/day 0 warmup rows, hour &gt; 24, minute &gt; 60) return false with
        /// a diagnostic instead of throwing.
        /// </summary>
        /// <param name="year">SQL Year value; values ≤ 0 fall back to <paramref name="defaultYear"/>.</param>
        /// <param name="month">SQL Month value (1–12).</param>
        /// <param name="day">SQL Day value (1–31, validated against the resolved year's calendar).</param>
        /// <param name="hour">SQL Hour value (0–24; end-of-interval).</param>
        /// <param name="minute">SQL Minute value (0–60; end-of-interval).</param>
        /// <param name="second">Seconds value (0–60).</param>
        /// <param name="defaultYear">Calendar year used when the SQL row carries no year (sizing environments); values ≤ 0 fall back to 2017.</param>
        /// <param name="dateTime">Normalised interval-end timestamp.</param>
        /// <param name="diagnostic">Structured description of a rejected row; null on success.</param>
        /// <returns>True when the row was normalised.</returns>
        public static bool TryGetDateTime(int year, int month, int day, int hour, int minute, int second, int defaultYear, out System.DateTime dateTime, out string diagnostic)
        {
            dateTime = default(System.DateTime);
            diagnostic = null;

            if (year <= 0)
            {
                year = defaultYear > 0 ? defaultYear : 2017;
            }

            if (month < 1 || month > 12)
            {
                diagnostic = string.Format("Invalid EnergyPlus timestamp: month {0} is out of range (row {1}-{2}-{3} {4}:{5})", month, year, month, day, hour, minute);
                return false;
            }

            int daysInMonth = System.DateTime.DaysInMonth(year, month);
            if (day < 1 || day > daysInMonth)
            {
                diagnostic = string.Format("Invalid EnergyPlus timestamp: day {0} does not exist in {1}-{2} ({3} is {4}a leap year, {5} has {6} days) — the row was skipped, not clamped", day, year, month, year, System.DateTime.IsLeapYear(year) ? "" : "not ", month, daysInMonth);
                return false;
            }

            if (hour < 0 || hour > 24)
            {
                diagnostic = string.Format("Invalid EnergyPlus timestamp: hour {0} is out of range (0–24, end-of-interval convention)", hour);
                return false;
            }

            if (minute < 0 || minute > 60)
            {
                diagnostic = string.Format("Invalid EnergyPlus timestamp: minute {0} is out of range (0–60, end-of-interval convention)", minute);
                return false;
            }

            if (second < 0 || second > 60)
            {
                diagnostic = string.Format("Invalid EnergyPlus timestamp: second {0} is out of range (0–60)", second);
                return false;
            }

            dateTime = new System.DateTime(year, month, day).Add(new TimeSpan(0, hour, minute, second));
            return true;
        }

        /// <summary>
        /// The 0-based hour-of-year index of the reporting interval ending at
        /// <paramref name="endOfInterval"/>: the interval index, not the endpoint's hour
        /// (01:00 is the end of interval 0; December 31 24:00 is the end of interval 8759).
        /// Computed by stepping one tick back into the interval, so sub-hourly rows map to
        /// their containing hour and day/month/year rollovers stay correct.
        /// </summary>
        public static int IntervalHourOfYear(System.DateTime endOfInterval)
        {
            if (endOfInterval == System.DateTime.MinValue)
            {
                return -1;
            }

            return Core.Query.HourOfYear(endOfInterval.AddTicks(-1));
        }
    }
}
