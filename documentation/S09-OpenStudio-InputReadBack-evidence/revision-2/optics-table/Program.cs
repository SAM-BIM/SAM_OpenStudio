// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

// S09 revision 2 evidence: the opaque optics decision table from the comparator's own rows.
using SAM.Analytical;
using SAM.Analytical.InputEquivalence;
using SAM.Analytical.OpenStudio.InputReadBack;
using SAM.Analytical.Tas.DirectT3D.Validation.ReadBack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

class P
{
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;
    static string F(double v) => double.IsNaN(v) ? "-" : v.ToString("0.###", ci);
    static double Sam(ExpectedLayer l, string prop)
    {
        var m = l.Material as SAM.Core.Material;
        Enum p = null;
        switch (prop)
        {
            case "externalSolarReflectance": p = OpaqueMaterialParameter.ExternalSolarReflectance; break;
            case "internalSolarReflectance": p = OpaqueMaterialParameter.InternalSolarReflectance; break;
            case "externalLightReflectance": p = OpaqueMaterialParameter.ExternalLightReflectance; break;
            case "internalLightReflectance": p = OpaqueMaterialParameter.InternalLightReflectance; break;
            case "externalEmissivity": p = OpaqueMaterialParameter.ExternalEmissivity; break;
            case "internalEmissivity": p = OpaqueMaterialParameter.InternalEmissivity; break;
        }
        return m != null && m.TryGetValue(p, out double v) ? v : double.NaN;
    }

    static int Main(string[] a)
    {
        string fixture = a[0], native = a[1], epw = a[2], id = a[3];
        AnalyticalModel model = SAM.Core.Convert.ToSAM<AnalyticalModel>(fixture).Single();
        OsComparison cmp = OsComparison.Run(model, NativeSnapshot.Load(native), epw, id, GateRuleSet.Approved);
        Dictionary<string, QuantityResult> rows = cmp.Results.ToDictionary(x => x.Input.ObjectKey + "|" + x.Definition.Id, x => x);
        ExpectedModel e = ReadBackExpected.Build(model, null, id);
        string[] props = { "SolarReflectance", "LightReflectance", "Emissivity" };
        int md = 0, mdExposed = 0, mdBuried = 0;
        Console.WriteLine("fixture|zone|construction|panelType|kind|side|k/n|material(SAM)|face|needs SAM face|SAM ext rs/rv/e|SAM int rs/rv/e|native rs/rv/e|delta at face rs/rv/e|internal MD rows|exposed?");
        foreach (ExpectedSpace s in e.Spaces)
        foreach (ExpectedPanel p in s.Panels)
        {
            bool first = p.SpaceOrder.Count == 0 || p.SpaceOrder[0] == s.Guid;
            bool internalPanel = p.SpaceOrder.Count > 1;
            bool ground = p.Adiabatic || p.PanelType == PanelType.SlabOnGrade || p.PanelType == PanelType.UndergroundSlab || p.PanelType == PanelType.UndergroundWall || p.PanelType == PanelType.UndergroundCeiling;
            List<ExpectedLayer> layers = first ? Enumerable.Reverse(p.Layers).ToList() : p.Layers.ToList();
            string side = "panel:" + p.Guid.ToString("D") + "@space:" + s.Guid.ToString("D");
            int n = layers.Count;
            for (int k = 1; k <= n; k++)
            {
                ExpectedLayer l = layers[k - 1];
                // Native order is outside first. The face towards the E+ outside of this side is SAM's external face on the
                // first space's side and SAM's internal face on the other side; the zone-facing face is the opposite one.
                string outerSam = first ? "external" : "internal";
                string innerSam = first ? "internal" : "external";
                int mdRows = props.Count(pr => rows.TryGetValue(side + "#layer" + k + ".internal" + pr + "|3a", out var r) && r.Input.Listing == ContractListing.MappingDifference);
                md += mdRows;
                List<(string face, string need)> faces = new List<(string, string)>();
                if (k == 1) faces.Add((ground ? "outer (ground/adiabatic: optics unused)" : internalPanel ? "outer (interzone: the other side's inner face governs)" : "outer exposed (outdoors)", outerSam));
                if (k == n) faces.Add(("inner exposed (zone)", innerSam));
                if (faces.Count == 0) faces.Add(("buried", "-"));
                bool exposedInternal = faces.Any(f => f.face.StartsWith("inner exposed") && f.need == "internal") || faces.Any(f => f.face.StartsWith("outer exposed") && f.need == "internal");
                if (mdRows > 0) { if (exposedInternal) mdExposed += mdRows; else mdBuried += mdRows; }
                foreach (var f in faces)
                {
                    double[] ext = props.Select(pr => Sam(l, "external" + pr)).ToArray();
                    double[] inn = props.Select(pr => Sam(l, "internal" + pr)).ToArray();
                    double[] nat = props.Select(pr => { rows.TryGetValue(side + "#layer" + k + ".external" + pr + "|3a", out var r); return r?.Input.Native != null && r.Input.Native.HasNumber ? r.Input.Native.Value : double.NaN; }).ToArray();
                    double[] need = f.need == "internal" ? inn : f.need == "external" ? ext : null;
                    string delta = need == null ? "-" : string.Join("/", Enumerable.Range(0, 3).Select(i => F(nat[i] - need[i])));
                    Console.WriteLine(string.Join("|", id, s.Name, p.ConstructionName, p.PanelType, internalPanel ? "internal" : ground ? "ground/adiabatic" : "external", first ? "first" : "second", k + "/" + n, l.Name, f.face, f.need,
                        string.Join("/", ext.Select(F)), string.Join("/", inn.Select(F)), string.Join("/", nat.Select(F)), delta, mdRows, exposedInternal ? "exposed" : "buried"));
                }
            }
        }
        Console.WriteLine("TOTAL internal-side MappingDifference rows " + md + ": at an exposed face where SAM wants the internal value " + mdExposed + "; buried or SAM-external face " + mdBuried);
        return 0;
    }
}
