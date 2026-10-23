// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical.InputEquivalence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SAM.Analytical.OpenStudio.InputReadBack
{
    /// <summary>
    /// A typed index over a <see cref="NativeSnapshot"/>: native objects by EnergyPlus name (case-insensitive, as EnergyPlus
    /// resolves them) and the native field readings the comparison needs. Every reading keeps the reader states of the
    /// evaluation core: absent (blank or missing), unreadable (layout or text not understood), NaN, zero, finite.
    /// </summary>
    public sealed class NativeModel
    {
        private static readonly CultureInfo ci = CultureInfo.InvariantCulture;
        private readonly Dictionary<string, NativeSchedule> idfSchedules;
        private readonly Dictionary<string, NativeSchedule> osmSchedules;
        private readonly Dictionary<string, string> osmHandles;

        public NativeModel(NativeSnapshot snapshot)
        {
            Snapshot = snapshot;
            idfSchedules = new Dictionary<string, NativeSchedule>(StringComparer.OrdinalIgnoreCase);
            foreach (NativeSchedule schedule in snapshot.IdfSchedules.Where(x => x.Name != null))
            {
                idfSchedules[schedule.Name] = schedule;
            }

            osmSchedules = new Dictionary<string, NativeSchedule>(StringComparer.OrdinalIgnoreCase);
            foreach (NativeSchedule schedule in snapshot.OsmSchedules.Where(x => x.Name != null))
            {
                osmSchedules[schedule.Name] = schedule;
            }

            osmHandles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (IdfObject o in snapshot.Osm)
            {
                // Every OSM object (and the reader's schedule handle records) starts with handle, name.
                string handle = o.Value(0);
                string name = o.Value(1);
                if (!string.IsNullOrWhiteSpace(handle) && handle.StartsWith("{", StringComparison.Ordinal) && name != null)
                {
                    osmHandles[handle] = name;
                }
            }

            string coordinateSystem = snapshot.IdfOf("GlobalGeometryRules").FirstOrDefault()?.Value(2, "Coordinate System", out string _);
            RelativeCoordinates = coordinateSystem == null || string.Equals(coordinateSystem, "Relative", StringComparison.OrdinalIgnoreCase);
        }

        public NativeSnapshot Snapshot { get; }

        public bool RelativeCoordinates { get; }

        public IEnumerable<IdfObject> Idf(string type) => Snapshot.IdfOf(type);

        public IdfObject IdfByName(string type, string name)
        {
            return string.IsNullOrWhiteSpace(name) ? null : Snapshot.IdfOf(type).FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>An OSM object of <paramref name="type"/> by its Name field (field 1, after the handle).</summary>
        public IdfObject OsmByName(string type, string name)
        {
            return string.IsNullOrWhiteSpace(name) ? null : Snapshot.OsmOf(type).FirstOrDefault(x => string.Equals(x.Value(1), name, StringComparison.OrdinalIgnoreCase));
        }

        public IdfObject OsmByHandle(string type, string handle)
        {
            return string.IsNullOrWhiteSpace(handle) ? null : Snapshot.OsmOf(type).FirstOrDefault(x => string.Equals(x.Value(0), handle, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The name an OSM handle reference points to, or null.</summary>
        public string OsmName(string handle)
        {
            return handle != null && osmHandles.TryGetValue(handle, out string name) ? name : null;
        }

        public NativeSchedule IdfSchedule(string name)
        {
            return name != null && idfSchedules.TryGetValue(name, out NativeSchedule schedule) ? schedule : null;
        }

        public NativeSchedule OsmSchedule(string name)
        {
            return name != null && osmSchedules.TryGetValue(name, out NativeSchedule schedule) ? schedule : null;
        }

        /// <summary>A numeric field as a reader result: blank is absent (EnergyPlus would apply its default), text that is not a number is unreadable.</summary>
        public static ReaderResult Number(IdfObject o, int index, string label)
        {
            if (o == null)
            {
                return ReaderResult.Absent("no native object");
            }

            string text = o.Value(index, label, out string problem);
            if (problem != null)
            {
                return ReaderResult.Unreadable(problem);
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return ReaderResult.Absent(o.Type + " '" + o.Name + "' " + label + " is blank");
            }

            if (string.Equals(text, "NaN", StringComparison.OrdinalIgnoreCase))
            {
                return ReaderResult.NotANumber(o.Type + " '" + o.Name + "' " + label + " is NaN");
            }

            return double.TryParse(text, NumberStyles.Float, ci, out double value)
                ? ReaderResult.FromValue(value)
                : ReaderResult.Unreadable(o.Type + " '" + o.Name + "' " + label + " is not a number: '" + text + "'");
        }

        public static string Text(IdfObject o, int index, string label, out string problem)
        {
            problem = null;
            if (o == null)
            {
                problem = "no native object";
                return null;
            }

            return o.Value(index, label, out problem);
        }

        /// <summary>
        /// The vertices of a surface (from <paramref name="firstVertexField"/>, three numbers each) in building coordinates:
        /// relative coordinates are moved by the zone origin. Null with a reason when unreadable or rotated.
        /// </summary>
        public List<double[]> Vertices(IdfObject surface, int firstVertexField, IdfObject zone, out string problem)
        {
            problem = null;
            double[] origin = { 0, 0, 0 };
            if (RelativeCoordinates && zone != null)
            {
                string north = zone.Value(1, "Direction of Relative North", out problem);
                if (problem != null)
                {
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(north) && (!double.TryParse(north, NumberStyles.Float, ci, out double degrees) || degrees != 0))
                {
                    problem = "zone '" + zone.Name + "' is rotated (" + north + " deg): not read by the S09 reader";
                    return null;
                }

                for (int k = 0; k < 3; k++)
                {
                    string text = zone.Value(2 + k);
                    origin[k] = string.IsNullOrWhiteSpace(text) ? 0 : double.Parse(text, NumberStyles.Float, ci);
                }
            }

            List<double[]> result = new List<double[]>();
            int count = surface.Fields.Count - firstVertexField;
            if (count < 9 || count % 3 != 0)
            {
                problem = surface.Type + " '" + surface.Name + "' has " + count + " vertex fields";
                return null;
            }

            for (int i = firstVertexField; i + 2 < surface.Fields.Count; i += 3)
            {
                double[] p = new double[3];
                for (int k = 0; k < 3; k++)
                {
                    if (!double.TryParse(surface.Value(i + k), NumberStyles.Float, ci, out p[k]))
                    {
                        problem = surface.Type + " '" + surface.Name + "' vertex field " + (i + k) + " is not a number";
                        return null;
                    }

                    p[k] += origin[k];
                }

                result.Add(p);
            }

            return result;
        }
    }
}
