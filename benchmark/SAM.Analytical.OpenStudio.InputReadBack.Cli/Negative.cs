// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SAM.Analytical.OpenStudio.InputReadBack.Cli
{
    /// <summary>
    /// Disposable negative checks: a copy of an export with one targeted mutation of the native files, so the read and
    /// compare steps can be shown to refuse it. The original run is never touched. The copy's export.json records the
    /// mutation and the mutated hashes (the comparison judges the mutated files, not a hash mismatch).
    /// </summary>
    internal static class Negative
    {
        public static readonly string[] Cases =
        {
            "neg-identity-swap-names",
            "neg-schedule-swap",
            "neg-v1-unscheduled",
            "neg-missing-dsoa-link",
            "neg-missing-schedule-object",
            "neg-people-density",
            "neg-layer-conductivity",
            "neg-osm-dsoa-schedule",
            "neg-zone-multiplier",
            "neg-window-multiplier",
            "neg-north-axis",
            "neg-zone-multiplier-both",
            "neg-window-multiplier-both",
            "neg-north-axis-both",
            "neg-osm-missing-thermal-zone",
            "neg-osm-missing-subsurfaces",
            "neg-lighting-previous-mapping",
            "neg-lighting-visible-zero",
            "neg-lighting-radiant-is-p",
            "neg-lighting-return-air",
            "neg-lighting-swapped"
        };

        public static int Run(string runDirectory, string caseId, string outputDirectory)
        {
            if (!Cases.Contains(caseId))
            {
                throw new ArgumentException("unknown case '" + caseId + "'; cases: " + string.Join(", ", Cases));
            }

            if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
            {
                throw new IOException("The negative directory must be new or empty: " + outputDirectory);
            }

            Directory.CreateDirectory(outputDirectory);
            foreach (string file in new[] { Export.ModelFile, Export.EpwFile, Export.OsmFile, Export.IdfFile, Export.ManifestFile })
            {
                File.Copy(Path.Combine(runDirectory, file), Path.Combine(outputDirectory, file));
            }

            string idfPath = Path.Combine(outputDirectory, Export.IdfFile);
            string osmPath = Path.Combine(outputDirectory, Export.OsmFile);
            string idf = File.ReadAllText(idfPath);
            string osm = File.ReadAllText(osmPath);
            string description = Apply(caseId, ref idf, ref osm);
            File.WriteAllText(idfPath, idf);
            File.WriteAllText(osmPath, osm);

            ExportManifest manifest = ExportManifest.Load(Path.Combine(outputDirectory, Export.ManifestFile));
            manifest.Label = (manifest.Label ?? string.Empty) + " / " + caseId;
            manifest.Mutation = caseId + ": " + description;
            manifest.OsmSha256 = Hashing.Sha256(osmPath);
            manifest.IdfSha256 = Hashing.Sha256(idfPath);
            manifest.Save(Path.Combine(outputDirectory, Export.ManifestFile));
            Console.WriteLine("negative {0}: {1}", caseId, description);
            return 0;
        }

        internal static string Apply(string caseId, ref string idf, ref string osm)
        {
            List<IdfObject> idfObjects = IdfText.Parse(idf);
            switch (caseId)
            {
                case "neg-identity-swap-names":
                {
                    // Two plain external walls of one space trade names (every reference follows): the geometry stays, the
                    // writer's GUID carrier now contradicts it.
                    HashSet<string> hosts = new HashSet<string>(idfObjects.Where(x => x.Type == "FenestrationSurface:Detailed").Select(x => x.Value(3)), StringComparer.OrdinalIgnoreCase);
                    IGrouping<string, IdfObject> space = idfObjects.Where(x => x.Type == "BuildingSurface:Detailed" && x.Value(1) == "Wall" && x.Value(5) == "Outdoors" && !hosts.Contains(x.Name)).GroupBy(x => x.Value(4)).First(x => x.Count() >= 2);
                    string a = space.ElementAt(0).Name, b = space.ElementAt(1).Name;
                    idf = Regex.Replace(idf, Regex.Escape(a) + "|" + Regex.Escape(b), m => m.Value == a ? b : a);
                    return "IDF surfaces '" + a + "' and '" + b + "' trade names";
                }

                case "neg-schedule-swap":
                {
                    IdfObject dsoa = idfObjects.First(x => x.Type == "DesignSpecification:OutdoorAir");
                    string occupancy = idfObjects.First(x => x.Type == "People").Value(2);
                    idf = SetField(idf, "DesignSpecification:OutdoorAir", dsoa.Name, "Outdoor Air Schedule Name", occupancy);
                    return "IDF DSOA '" + dsoa.Name + "' schedule -> the occupancy schedule '" + occupancy + "'";
                }

                case "neg-v1-unscheduled":
                {
                    // The pre-S09 behaviour: the requirement delivered unscheduled (fraction 1), in both native views.
                    foreach (IdfObject dsoa in idfObjects.Where(x => x.Type == "DesignSpecification:OutdoorAir"))
                    {
                        idf = SetField(idf, "DesignSpecification:OutdoorAir", dsoa.Name, "Outdoor Air Schedule Name", string.Empty);
                    }

                    foreach (IdfObject dsoa in IdfText.Parse(osm).Where(x => x.Type == "OS:DesignSpecification:OutdoorAir"))
                    {
                        osm = SetField(osm, "OS:DesignSpecification:OutdoorAir", dsoa.Value(1), "Outdoor Air Flow Rate Fraction Schedule Name", string.Empty, true);
                    }

                    return "every DSOA fraction schedule removed in the IDF and the OSM (unscheduled delivery)";
                }

                case "neg-missing-dsoa-link":
                {
                    IdfObject idealLoads = idfObjects.First(x => x.Type == "ZoneHVAC:IdealLoadsAirSystem");
                    idf = SetField(idf, "ZoneHVAC:IdealLoadsAirSystem", idealLoads.Name, "Design Specification Outdoor Air Object Name", string.Empty);
                    return "IDF Ideal Loads '" + idealLoads.Name + "' names no DSOA";
                }

                case "neg-missing-schedule-object":
                {
                    string schedule = idfObjects.First(x => x.Type == "DesignSpecification:OutdoorAir").Value(6);
                    idf = SetField(idf, "Schedule:Compact", schedule, "Name", schedule + "_MISSING");
                    return "IDF schedule '" + schedule + "' renamed: the DSOA names an undefined schedule";
                }

                case "neg-people-density":
                {
                    IdfObject people = idfObjects.First(x => x.Type == "People");
                    double value = double.Parse(people.Value(5), CultureInfo.InvariantCulture) * 1.01;
                    idf = SetField(idf, "People", people.Name, "People per Floor Area", value.ToString("R", CultureInfo.InvariantCulture));
                    return "IDF People '" + people.Name + "' density x 1.01";
                }

                case "neg-layer-conductivity":
                {
                    IdfObject material = idfObjects.First(x => x.Type == "Material");
                    double value = double.Parse(material.Value(3), CultureInfo.InvariantCulture) * 1.001;
                    idf = SetField(idf, "Material", material.Name, "Conductivity", value.ToString("R", CultureInfo.InvariantCulture));
                    return "IDF Material '" + material.Name + "' conductivity x 1.001";
                }

                case "neg-osm-dsoa-schedule":
                {
                    // OSM only: the SpaceType DSOA loses its schedule; the IDF is unchanged, so the two native views disagree.
                    IdfObject dsoa = IdfText.Parse(osm).First(x => x.Type == "OS:DesignSpecification:OutdoorAir");
                    osm = SetField(osm, "OS:DesignSpecification:OutdoorAir", dsoa.Value(1), "Outdoor Air Flow Rate Fraction Schedule Name", string.Empty, true);
                    return "OSM DSOA '" + dsoa.Value(1) + "' fraction schedule removed (IDF unchanged)";
                }

                // Multiplicity and orientation (independent review of d6d1cc5b). The plain cases are the reviewer's IDF-only
                // mutations; the "-both" cases make the OSM agree, so only the check against SAM intent can refuse them.
                case "neg-zone-multiplier":
                case "neg-zone-multiplier-both":
                {
                    IdfObject zone = idfObjects.First(x => x.Type == "Zone");
                    idf = SetField(idf, "Zone", zone.Name, "Multiplier", "2");
                    if (caseId.EndsWith("-both", StringComparison.Ordinal))
                    {
                        osm = SetField(osm, "OS:ThermalZone", zone.Name, "Multiplier", "2", true);
                    }

                    return "IDF Zone '" + zone.Name + "' Multiplier 1 -> 2" + (caseId.EndsWith("-both", StringComparison.Ordinal) ? " and OSM OS:ThermalZone Multiplier blank -> 2" : " (OSM unchanged)");
                }

                case "neg-window-multiplier":
                case "neg-window-multiplier-both":
                {
                    List<string> names = idfObjects.Where(x => x.Type == "FenestrationSurface:Detailed").Select(x => x.Name).ToList();
                    foreach (string name in names)
                    {
                        idf = SetField(idf, "FenestrationSurface:Detailed", name, "Multiplier", "2");
                        if (caseId.EndsWith("-both", StringComparison.Ordinal))
                        {
                            osm = SetField(osm, "OS:SubSurface", name, "Multiplier", "2", true);
                        }
                    }

                    return "IDF FenestrationSurface:Detailed Multiplier blank -> 2 on " + names.Count + " subsurface(s)" + (caseId.EndsWith("-both", StringComparison.Ordinal) ? " and OSM OS:SubSurface Multiplier blank -> 2" : " (OSM unchanged)");
                }

                case "neg-north-axis":
                case "neg-north-axis-both":
                {
                    IdfObject building = idfObjects.First(x => x.Type == "Building");
                    idf = SetField(idf, "Building", building.Name, "North Axis", "90");
                    if (caseId.EndsWith("-both", StringComparison.Ordinal))
                    {
                        osm = SetField(osm, "OS:Building", IdfText.Parse(osm).First(x => x.Type == "OS:Building").Value(1), "North Axis", "90", true);
                    }

                    return "IDF Building '" + building.Name + "' North Axis blank -> 90" + (caseId.EndsWith("-both", StringComparison.Ordinal) ? " and OSM OS:Building North Axis blank -> 90" : " (OSM unchanged)");
                }

                // Missing OSM counterparts (independent review of 6f7e6b7d): the IDF is unchanged, so the IDF object has no
                // OSM object to be consistent with. The reviewed head skipped the check and kept its verdict.
                case "neg-osm-missing-thermal-zone":
                {
                    string zone = idfObjects.First(x => x.Type == "Zone").Name;
                    osm = RemoveObject(osm, "OS:ThermalZone", zone, true);
                    return "OSM OS:ThermalZone '" + zone + "' removed (IDF unchanged)";
                }

                case "neg-osm-missing-subsurfaces":
                {
                    List<string> names = idfObjects.Where(x => x.Type == "FenestrationSurface:Detailed").Select(x => x.Name).ToList();
                    foreach (string name in names)
                    {
                        osm = RemoveObject(osm, "OS:SubSurface", name, true);
                    }

                    return "OSM OS:SubSurface removed for " + names.Count + " subsurface(s) (IDF unchanged)";
                }

                // S07 §5.1 T-LIGHT-SPLIT (amendment L1): the first IDF Lights object, whose written p = Fraction Radiant +
                // Fraction Visible is positive on both fixtures. The OSM is unchanged; the lighting rows must refuse it.
                case "neg-lighting-previous-mapping":
                case "neg-lighting-visible-zero":
                case "neg-lighting-radiant-is-p":
                case "neg-lighting-return-air":
                case "neg-lighting-swapped":
                {
                    IdfObject lights = idfObjects.First(x => x.Type == "Lights");
                    string radiant = lights.Value(8), visible = lights.Value(9);
                    string p = (double.Parse(radiant, CultureInfo.InvariantCulture) + double.Parse(visible, CultureInfo.InvariantCulture)).ToString("R", CultureInfo.InvariantCulture);
                    switch (caseId)
                    {
                        case "neg-lighting-previous-mapping":
                            // The pre-L1 writer: Fraction Radiant = p and the view-coefficient default 0.25 as Fraction Visible.
                            idf = SetField(idf, "Lights", lights.Name, "Fraction Radiant", p);
                            idf = SetField(idf, "Lights", lights.Name, "Fraction Visible", "0.25");
                            return "IDF Lights '" + lights.Name + "' Fraction Radiant -> p = " + p + ", Fraction Visible -> 0.25 (the previous mapping)";
                        case "neg-lighting-visible-zero":
                            idf = SetField(idf, "Lights", lights.Name, "Fraction Visible", "0");
                            return "IDF Lights '" + lights.Name + "' Fraction Visible " + visible + " -> 0 with a positive authored p";
                        case "neg-lighting-radiant-is-p":
                            idf = SetField(idf, "Lights", lights.Name, "Fraction Radiant", p);
                            return "IDF Lights '" + lights.Name + "' Fraction Radiant -> p = " + p + " with Fraction Visible " + visible + " kept";
                        case "neg-lighting-return-air":
                            idf = SetField(idf, "Lights", lights.Name, "Return Air Fraction", "0.1");
                            return "IDF Lights '" + lights.Name + "' Return Air Fraction blank -> 0.1";
                        default:
                            idf = SetField(idf, "Lights", lights.Name, "Fraction Radiant", visible);
                            idf = SetField(idf, "Lights", lights.Name, "Fraction Visible", radiant);
                            return "IDF Lights '" + lights.Name + "' Fraction Radiant " + radiant + " and Fraction Visible " + visible + " swapped";
                    }
                }
            }

            throw new ArgumentException(caseId);
        }

        /// <summary>
        /// Removes the object of <paramref name="type"/> named <paramref name="name"/> (located as in <see cref="SetField"/>),
        /// from its class line through the line that ends it, with the blank line after it. Line-based: the rest of the
        /// file is kept byte for byte.
        /// </summary>
        internal static string RemoveObject(string text, string type, string name, bool osm = false)
        {
            string newline = text.Contains("\r\n") ? "\r\n" : "\n";
            List<string> lines = text.Split(new[] { newline }, StringSplitOptions.None).ToList();
            for (int i = 0; i < lines.Count; i++)
            {
                int nameLine = i + (osm ? 2 : 1);
                if (!string.Equals(lines[i].Trim(), type + ",", StringComparison.OrdinalIgnoreCase) || nameLine >= lines.Count || !string.Equals(Data(lines[nameLine]), name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int end = i + 1;
                while (end < lines.Count && !Code(lines[end]).TrimEnd().EndsWith(";", StringComparison.Ordinal))
                {
                    end++;
                }

                if (end >= lines.Count)
                {
                    break;
                }

                if (end + 1 < lines.Count && lines[end + 1].Trim().Length == 0)
                {
                    end++;
                }

                lines.RemoveRange(i, end - i + 1);
                return string.Join(newline, lines);
            }

            throw new InvalidOperationException(type + " '" + name + "' not found");
        }

        private static string Code(string line)
        {
            int bang = line.IndexOf('!');
            return bang >= 0 ? line.Substring(0, bang) : line;
        }

        /// <summary>
        /// Replaces the value of the field labelled <paramref name="label"/> in the object of <paramref name="type"/> named
        /// <paramref name="name"/> (the first field, or the second in an OSM where the handle comes first). Line-based:
        /// the file's formatting is kept.
        /// </summary>
        internal static string SetField(string text, string type, string name, string label, string value, bool osm = false)
        {
            string newline = text.Contains("\r\n") ? "\r\n" : "\n";
            List<string> lines = text.Split(new[] { newline }, StringSplitOptions.None).ToList();
            for (int i = 0; i < lines.Count; i++)
            {
                if (!string.Equals(lines[i].Trim(), type + ",", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int nameLine = i + (osm ? 2 : 1);
                if (nameLine >= lines.Count || !string.Equals(Data(lines[nameLine]), name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                for (int j = i + 1; j < lines.Count; j++)
                {
                    int bang = lines[j].IndexOf("!-", StringComparison.Ordinal);
                    if (bang >= 0 && lines[j].Substring(bang + 2).Trim().StartsWith(label, StringComparison.OrdinalIgnoreCase))
                    {
                        string data = lines[j].Substring(0, bang).TrimEnd();
                        char terminator = data[data.Length - 1];
                        string indent = lines[j].Substring(0, lines[j].Length - lines[j].TrimStart().Length);
                        lines[j] = (indent + value + terminator).PadRight(bang) + lines[j].Substring(bang);
                        return string.Join(newline, lines);
                    }

                    if (lines[j].Contains(";"))
                    {
                        break;
                    }
                }
            }

            throw new InvalidOperationException("field '" + label + "' of " + type + " '" + name + "' not found");
        }

        private static string Data(string line)
        {
            int bang = line.IndexOf('!');
            return (bang >= 0 ? line.Substring(0, bang) : line).Trim().TrimEnd(',', ';').Trim();
        }
    }
}
