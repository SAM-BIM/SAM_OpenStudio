// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SAM.Analytical.OpenStudio.InputReadBack
{
    /// <summary>One field of an IDF or OSM object: its raw text and the "!- label" the file writes beside it (null when none).</summary>
    public sealed class IdfField
    {
        public string Value { get; set; }
        public string Label { get; set; }
    }

    /// <summary>One IDF or OSM object: its class name and its fields in file order (index 0 is the field after the class name).</summary>
    public sealed class IdfObject
    {
        public string Type { get; set; }
        public List<IdfField> Fields { get; set; } = new List<IdfField>();

        public string Name => Fields.Count > 0 ? Fields[0].Value : null;

        /// <summary>The raw value of field <paramref name="index"/>, or null when the object is shorter (EnergyPlus treats a missing trailing field as blank).</summary>
        public string Value(int index)
        {
            return index >= 0 && index < Fields.Count ? Fields[index].Value : null;
        }

        /// <summary>
        /// Field <paramref name="index"/> after checking the file's own label for it: when the file labels the field and the
        /// label does not start with <paramref name="label"/>, the layout is not the one this reader knows and the field is
        /// unreadable (<paramref name="problem"/> says why), never silently read from another position.
        /// </summary>
        public string Value(int index, string label, out string problem)
        {
            problem = null;
            if (index < 0 || index >= Fields.Count)
            {
                return null;
            }

            IdfField field = Fields[index];
            if (label != null && field.Label != null && !field.Label.StartsWith(label, StringComparison.OrdinalIgnoreCase))
            {
                problem = string.Format("{0} '{1}' field {2} is labelled '{3}', expected '{4}'", Type, Name, index, field.Label, label);
                return null;
            }

            return field.Value;
        }

        /// <summary>
        /// The value of the first field the file labels <paramref name="label"/> (prefix, case-insensitive), for OSM objects
        /// whose trailing blank fields may be omitted. <paramref name="found"/> is false when no field carries the label.
        /// </summary>
        public string ValueByLabel(string label, out bool found)
        {
            IdfField field = Fields.FirstOrDefault(x => x.Label != null && x.Label.StartsWith(label, StringComparison.OrdinalIgnoreCase));
            found = field != null;
            return field?.Value;
        }
    }

    /// <summary>
    /// A minimal, IDD-free reader of the IDF/OSM text syntax: objects end with ';', fields are separated by ',', and '!'
    /// starts a comment. It is deliberately independent of the OpenStudio SDK that wrote the files (S07 section 3: native
    /// values come from read-back, not from the writer).
    /// </summary>
    public static class IdfText
    {
        public static List<IdfObject> Parse(string text)
        {
            List<IdfObject> result = new List<IdfObject>();
            if (string.IsNullOrEmpty(text))
            {
                return result;
            }

            List<IdfField> current = new List<IdfField>();
            StringBuilder token = new StringBuilder();
            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                string label = null;
                int bang = line.IndexOf('!');
                if (bang >= 0)
                {
                    string comment = line.Substring(bang + 1);
                    if (comment.StartsWith("-", StringComparison.Ordinal))
                    {
                        label = comment.Substring(1).Trim();
                    }

                    line = line.Substring(0, bang);
                }

                List<IdfField> lineFields = new List<IdfField>();
                foreach (char c in line)
                {
                    if (c == ',' || c == ';')
                    {
                        IdfField field = new IdfField { Value = token.ToString().Trim() };
                        token.Clear();
                        current.Add(field);
                        lineFields.Add(field);
                        if (c == ';')
                        {
                            Finish(current, result);
                            current = new List<IdfField>();
                        }
                    }
                    else
                    {
                        token.Append(c);
                    }
                }

                // A label describes the last field completed on its line ("0, 0, 3.05, !- X,Y,Z Vertex 3" labels the z).
                if (label != null && lineFields.Count > 0)
                {
                    lineFields[lineFields.Count - 1].Label = label;
                }

                if (token.Length > 0 && !string.IsNullOrWhiteSpace(token.ToString()))
                {
                    token.Append(' ');
                }
                else
                {
                    token.Clear();
                }
            }

            return result;
        }

        private static void Finish(List<IdfField> fields, List<IdfObject> result)
        {
            if (fields.Count == 0 || string.IsNullOrWhiteSpace(fields[0].Value))
            {
                return;
            }

            result.Add(new IdfObject { Type = fields[0].Value, Fields = fields.Skip(1).ToList() });
        }
    }
}
