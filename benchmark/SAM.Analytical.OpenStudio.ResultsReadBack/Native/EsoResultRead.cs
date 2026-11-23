// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SAM.Analytical.OpenStudio.ResultsReadBack
{
    /// <summary>One ESO data-dictionary entry.</summary>
    public sealed class EsoVariable
    {
        public int Id { get; set; }
        public string Key { get; set; }
        public string Name { get; set; }
        public string Units { get; set; }
        public string Frequency { get; set; }
        public bool IsMeter { get; set; }
    }

    /// <summary>
    /// The ESO channel: eplusout.eso read as text by its own parser (it shares no reader code with the SQL channel). The
    /// data dictionary maps report ids to key, variable, units and frequency. In the data section, record 1 opens an
    /// environment, record 2 stamps the following values (an hourly stamp has StartMinute 0 and EndMinute 60), and each
    /// Hourly variable must appear exactly once after each hourly stamp of the RunPeriod environment.
    /// </summary>
    public sealed class EsoResultRead
    {
        private static readonly CultureInfo ci = CultureInfo.InvariantCulture;

        public string Path { get; set; }
        public string Sha256Before { get; set; }
        public string Sha256After { get; set; }

        /// <summary>The first line of the file (EnergyPlus version and build).</summary>
        public string ProgramVersion { get; set; }

        public List<string> Environments { get; set; } = new List<string>();

        /// <summary>How many environment blocks carry the RunPeriod's name (exactly one is required).</summary>
        public int RunPeriodBlocks { get; set; }

        public Dictionary<string, ChannelSeries> Series { get; set; } = new Dictionary<string, ChannelSeries>(StringComparer.Ordinal);
        public List<MeterSum> Meters { get; set; } = new List<MeterSum>();
        public List<string> Problems { get; set; } = new List<string>();

        public static EsoResultRead Read(string esoPath, RunCalendar calendar)
        {
            EsoResultRead result = new EsoResultRead { Path = System.IO.Path.GetFullPath(esoPath), Sha256Before = Sha256.Of(esoPath) };
            try
            {
                Parse(File.ReadAllLines(esoPath), calendar, result);
            }
            catch (IOException exception)
            {
                result.Problems.Add("eplusout.eso could not be read: " + exception.Message);
            }

            result.Sha256After = Sha256.Of(esoPath);
            return result;
        }

        public static void Parse(string[] lines, RunCalendar calendar, EsoResultRead result)
        {
            HashSet<string> wanted = new HashSet<string>(S11Names.IdealLoadsVariables.Concat(S11Names.ZoneVariables), StringComparer.OrdinalIgnoreCase);
            HashSet<string> meters = new HashSet<string>(S11Names.Meters, StringComparer.OrdinalIgnoreCase);
            Dictionary<int, ChannelSeries> seriesById = new Dictionary<int, ChannelSeries>();
            Dictionary<int, MeterSum> meterById = new Dictionary<int, MeterSum>();

            int i = 0;
            if (lines.Length == 0)
            {
                result.Problems.Add("eplusout.eso is empty");
                return;
            }

            result.ProgramVersion = lines[0].Trim();
            bool dictionaryEnded = false;
            for (i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.StartsWith("End of Data Dictionary", StringComparison.Ordinal))
                {
                    dictionaryEnded = true;
                    i++;
                    break;
                }

                EsoVariable variable = DictionaryLine(line);
                if (variable == null || variable.Id <= 6)
                {
                    continue;
                }

                if (!variable.IsMeter && wanted.Contains(variable.Name))
                {
                    // Every dictionary entry of a required variable is judged: Hourly entries are the series, any other
                    // reporting frequency for the same key is unsupported and blocks it (its values are not read).
                    string seriesKey = S11Names.SeriesKey(Canonical(variable.Name), variable.Key);
                    bool hourly = string.Equals(variable.Frequency, "Hourly", StringComparison.OrdinalIgnoreCase);
                    if (!result.Series.TryGetValue(seriesKey, out ChannelSeries series))
                    {
                        series = new ChannelSeries { Channel = "eso", Variable = Canonical(variable.Name), Key = variable.Key, Units = hourly ? variable.Units : null };
                        result.Series[seriesKey] = series;
                    }

                    if (!hourly)
                    {
                        series.Problem("S11-C1", "unsupported reporting frequency '" + variable.Frequency + "' (report id " + variable.Id + "; only Hourly is permitted)");
                        continue;
                    }

                    series.DictionaryEntries++;
                    if (series.Units == null)
                    {
                        series.Units = variable.Units;
                    }
                    else if (!string.Equals(series.Units, variable.Units, StringComparison.Ordinal))
                    {
                        series.Units = series.Units + "," + variable.Units;
                    }

                    seriesById[variable.Id] = series;
                }
                else if (variable.IsMeter && meters.Contains(variable.Name) && !result.Meters.Any(x => string.Equals(x.Name, variable.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    // The finest frequency comes first in the dictionary only by convention; any one entry sums the run.
                    MeterSum sum = new MeterSum { Name = variable.Name, Frequency = variable.Frequency };
                    result.Meters.Add(sum);
                    meterById[variable.Id] = sum;
                }
            }

            if (!dictionaryEnded)
            {
                result.Problems.Add("eplusout.eso has no 'End of Data Dictionary' line");
                return;
            }

            bool inRunPeriod = false;
            bool hourlyStamp = false;
            HourKey stamp = default(HourKey);
            int dst = 0;
            HashSet<int> seenInBlock = new HashSet<int>();
            bool dataEnded = false;
            for (; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.StartsWith("End of Data", StringComparison.Ordinal))
                {
                    dataEnded = true;
                    break;
                }

                string[] fields = line.Split(',');
                if (!int.TryParse(fields[0].Trim(), NumberStyles.Integer, ci, out int id))
                {
                    result.Problems.Add("eplusout.eso data line " + (i + 1) + " does not start with a report id");
                    continue;
                }

                if (id == 1)
                {
                    string name = fields.Length > 1 ? fields[1].Trim() : string.Empty;
                    result.Environments.Add(name);
                    inRunPeriod = calendar.RunPeriodName != null && string.Equals(name, calendar.RunPeriodName, StringComparison.OrdinalIgnoreCase);
                    if (inRunPeriod)
                    {
                        result.RunPeriodBlocks++;
                    }

                    hourlyStamp = false;
                    seenInBlock.Clear();
                    continue;
                }

                if (id == 2)
                {
                    // 2,DayOfSimulation,Month,DayOfMonth,DST,Hour,StartMinute,EndMinute,DayType
                    seenInBlock.Clear();
                    hourlyStamp = false;
                    if (fields.Length < 8
                        || !int.TryParse(fields[2].Trim(), NumberStyles.Integer, ci, out int month)
                        || !int.TryParse(fields[3].Trim(), NumberStyles.Integer, ci, out int day)
                        || !int.TryParse(fields[4].Trim(), NumberStyles.Integer, ci, out dst)
                        || !int.TryParse(fields[5].Trim(), NumberStyles.Integer, ci, out int hour)
                        || !double.TryParse(fields[6].Trim(), NumberStyles.Float, ci, out double startMinute)
                        || !double.TryParse(fields[7].Trim(), NumberStyles.Float, ci, out double endMinute))
                    {
                        result.Problems.Add("eplusout.eso line " + (i + 1) + " is not a readable time stamp");
                        continue;
                    }

                    hourlyStamp = startMinute == 0 && endMinute == 60;
                    stamp = new HourKey(month, day, hour);
                    continue;
                }

                if (id <= 6)
                {
                    // Daily, monthly, run-period and annual stamps: no hourly value may follow them.
                    seenInBlock.Clear();
                    hourlyStamp = false;
                    continue;
                }

                if (!inRunPeriod)
                {
                    continue;
                }

                if (seriesById.TryGetValue(id, out ChannelSeries series))
                {
                    if (!hourlyStamp)
                    {
                        series.Problem("S11-C1", "a value of id " + id + " follows a non-hourly stamp (line " + (i + 1) + ")");
                        continue;
                    }

                    if (!seenInBlock.Add(id))
                    {
                        series.Problem("S11-C1", "id " + id + " appears more than once after the hourly stamp " + stamp);
                    }

                    if (dst != 0)
                    {
                        series.Problem("S11-C2", "the hourly stamp " + stamp + " has DST " + dst + " (0 is required)");
                    }

                    double value = fields.Length > 1 && double.TryParse(fields[1].Trim(), NumberStyles.Float, ci, out double parsed) ? parsed : double.NaN;
                    series.Points.Add(new HourlyPoint { Month = stamp.Month, Day = stamp.Day, Hour = stamp.Hour, Value = value });
                }
                else if (meterById.TryGetValue(id, out MeterSum meter) && fields.Length > 1 && double.TryParse(fields[1].Trim(), NumberStyles.Float, ci, out double meterValue))
                {
                    meter.Rows++;
                    meter.Sum += meterValue;
                }
            }

            if (!dataEnded)
            {
                result.Problems.Add("eplusout.eso has no 'End of Data' line (the run may not have finished)");
            }
        }

        /// <summary>The S11 spelling of a variable name read case-insensitively.</summary>
        private static string Canonical(string name)
        {
            return S11Names.IdealLoadsVariables.Concat(S11Names.ZoneVariables).First(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// "id,count,KEY,Variable Name [Units] !Frequency" for a variable, "id,count,Meter:Name [Units] !Frequency" for a
        /// meter. Null for a line this reader does not recognise.
        /// </summary>
        public static EsoVariable DictionaryLine(string line)
        {
            int bang = line.IndexOf('!');
            string frequency = bang >= 0 ? line.Substring(bang + 1).Trim() : null;
            string body = bang >= 0 ? line.Substring(0, bang) : line;
            if (frequency != null)
            {
                int space = frequency.IndexOf(' ');
                frequency = space >= 0 ? frequency.Substring(0, space) : frequency;
            }

            string[] parts = body.Split(new[] { ',' }, 3);
            if (parts.Length < 3 || !int.TryParse(parts[0].Trim(), NumberStyles.Integer, ci, out int id))
            {
                return null;
            }

            string rest = parts[2].Trim();
            string key = null;
            int comma = rest.IndexOf(',');
            int bracket = rest.IndexOf('[');
            if (comma >= 0 && (bracket < 0 || comma < bracket))
            {
                key = rest.Substring(0, comma).Trim();
                rest = rest.Substring(comma + 1).Trim();
                bracket = rest.IndexOf('[');
            }

            string name = bracket >= 0 ? rest.Substring(0, bracket).Trim() : rest;
            int close = bracket >= 0 ? rest.IndexOf(']', bracket) : -1;
            string units = bracket >= 0 && close > bracket ? rest.Substring(bracket + 1, close - bracket - 1).Trim() : null;
            return new EsoVariable { Id = id, Key = key ?? string.Empty, Name = name, Units = units, Frequency = frequency, IsMeter = key == null };
        }
    }
}
