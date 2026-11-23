// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical.InputEquivalence;
using SAM.Analytical.Tas.DirectT3D.Validation.ReadBack;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using EQ = SAM.Analytical.InputEquivalence.Query;

namespace SAM.Analytical.OpenStudio.InputReadBack
{
    /// <summary>One native surface of a space with its vertices in building coordinates, or the reason they are unreadable.</summary>
    public sealed class NativeSurface
    {
        public IdfObject Object;
        public List<double[]> Vertices;
        public string Problem;
    }

    /// <summary>
    /// The native space whose surfaces are, one to one, a SAM space's panel polygons. <see cref="NativeSpace"/> is null when
    /// none matches or when more than one does (<see cref="Ambiguous"/>); <see cref="Match"/> and <see cref="Surfaces"/> then
    /// still hold the first bijective candidate, exactly as the S09 comparison has always kept them.
    /// </summary>
    public sealed class NativeSpaceMatch
    {
        public string NativeSpace;
        public GeometryMatchResult Match;
        public List<NativeSurface> Surfaces;
        public bool Ambiguous;
    }

    /// <summary>The native identity of one SAM space: its native Space, the Zone that holds it and the Zone's Ideal Loads.</summary>
    public sealed class NativeZoneIdentityEntry
    {
        public Guid SpaceGuid { get; set; }
        public string SpaceName { get; set; }
        public string NativeSpace { get; set; }
        public int SurfacePairs { get; set; }

        /// <summary>The IDF Zone name: the report key of zone variables (setpoint not-met time).</summary>
        public string Zone { get; set; }

        /// <summary>The ZoneHVAC:IdealLoadsAirSystem name: the report key of the Ideal Loads variables.</summary>
        public string IdealLoads { get; set; }

        public int IdealLoadsCount { get; set; }

        /// <summary>True when the writer's GUID suffix agrees with the geometry match (it may only contradict it, never make it).</summary>
        public bool NameCarrierAgrees { get; set; }

        /// <summary>Null when the identity is established; otherwise why it is not.</summary>
        public string Problem { get; set; }
    }

    /// <summary>
    /// The S09 native identity, shared by the S09 input comparison and the S11 results reference. A SAM space is matched to
    /// the native IDF Space whose surfaces are its panel polygons one to one (geometry alone); the IDF Zone of that Space is
    /// followed through ZoneHVAC:EquipmentConnections and ZoneHVAC:EquipmentList to its ZoneHVAC:IdealLoadsAirSystem. Names
    /// are never used to make a match, so two SAM spaces with the same name are told apart by their geometry.
    /// </summary>
    public static class NativeZoneIdentity
    {
        /// <summary>The IDF BuildingSurface:Detailed objects of <paramref name="spaceName"/>, with their vertices in building coordinates.</summary>
        public static List<NativeSurface> SurfacesOf(NativeModel native, string spaceName)
        {
            List<NativeSurface> result = new List<NativeSurface>();
            foreach (IdfObject surface in native.Idf("BuildingSurface:Detailed"))
            {
                if (!string.Equals(surface.Value(4, "Space Name", out string _), spaceName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                IdfObject zone = native.IdfByName("Zone", surface.Value(3));
                List<double[]> vertices = native.Vertices(surface, 11, zone, out string problem);
                result.Add(new NativeSurface { Object = surface, Vertices = vertices, Problem = problem });
            }

            return result;
        }

        /// <summary>
        /// The native space whose surfaces are exactly the space's panel polygons. A candidate with an unreadable surface is
        /// skipped; a second bijective candidate makes the match ambiguous and ends the search.
        /// </summary>
        public static NativeSpaceMatch MatchSpace(NativeModel native, ExpectedSpace space)
        {
            NativeSpaceMatch result = new NativeSpaceMatch();
            List<Polygon3D> expectedPolygons = space.Panels.Select(x => x.Outer).ToList();
            foreach (IdfObject candidate in native.Idf("Space"))
            {
                List<NativeSurface> candidateSurfaces = SurfacesOf(native, candidate.Name);
                if (candidateSurfaces.Any(x => x.Vertices == null))
                {
                    continue;
                }

                GeometryMatchResult candidateMatch = EQ.MatchPolygons(expectedPolygons, candidateSurfaces.Select(x => OsGeometry.ToPolygon3D(x.Vertices)).ToList());
                if (candidateMatch.IsBijection)
                {
                    if (result.NativeSpace != null)
                    {
                        result.NativeSpace = null;
                        result.Ambiguous = true;
                        break;
                    }

                    result.NativeSpace = candidate.Name;
                    result.Match = candidateMatch;
                    result.Surfaces = candidateSurfaces;
                }
            }

            return result;
        }

        /// <summary>Every ZoneHVAC:IdealLoadsAirSystem the zone's equipment list names (empty when the chain is broken).</summary>
        public static List<IdfObject> IdealLoadsOf(NativeModel native, IdfObject zone)
        {
            List<IdfObject> result = new List<IdfObject>();
            if (zone == null)
            {
                return result;
            }

            IdfObject connections = native.Idf("ZoneHVAC:EquipmentConnections").FirstOrDefault(x => string.Equals(x.Value(0, "Zone Name", out string _), zone.Name, StringComparison.OrdinalIgnoreCase));
            IdfObject list = native.IdfByName("ZoneHVAC:EquipmentList", connections?.Value(1, "Zone Conditioning Equipment List Name", out string _));
            if (list == null)
            {
                return result;
            }

            for (int i = 1; i + 1 < list.Fields.Count; i++)
            {
                if (string.Equals(list.Value(i), "ZoneHVAC:IdealLoadsAirSystem", StringComparison.OrdinalIgnoreCase))
                {
                    IdfObject o = native.IdfByName("ZoneHVAC:IdealLoadsAirSystem", list.Value(i + 1));
                    if (o != null)
                    {
                        result.Add(o);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// The native identity of every expected space. An entry has a <see cref="NativeZoneIdentityEntry.Problem"/> when its
        /// space matches no native space or more than one, when its native space or Zone is shared with another SAM space
        /// (not bijective), or when its Zone does not have exactly one Ideal Loads system.
        /// </summary>
        public static List<NativeZoneIdentityEntry> Resolve(NativeModel native, ExpectedModel expected)
        {
            List<NativeZoneIdentityEntry> result = new List<NativeZoneIdentityEntry>();
            foreach (ExpectedSpace space in expected.Spaces)
            {
                NativeZoneIdentityEntry entry = new NativeZoneIdentityEntry { SpaceGuid = space.Guid, SpaceName = space.Name };
                NativeSpaceMatch match = MatchSpace(native, space);
                if (match.Ambiguous)
                {
                    entry.Problem = "more than one native space matches its geometry";
                }
                else if (match.NativeSpace == null)
                {
                    entry.Problem = "no native space has exactly its panel polygons";
                }
                else
                {
                    entry.NativeSpace = match.NativeSpace;
                    entry.SurfacePairs = match.Match.Pairs.Count;
                    entry.NameCarrierAgrees = match.NativeSpace.EndsWith("_" + space.Guid.ToString("N").Substring(0, 8), StringComparison.OrdinalIgnoreCase);
                    IdfObject zone = native.IdfByName("Zone", native.IdfByName("Space", match.NativeSpace)?.Value(1, "Zone Name", out string _));
                    if (zone == null)
                    {
                        entry.Problem = "native space '" + match.NativeSpace + "' names no IDF Zone";
                    }
                    else
                    {
                        entry.Zone = zone.Name;
                        List<IdfObject> idealLoads = IdealLoadsOf(native, zone);
                        entry.IdealLoadsCount = idealLoads.Count;
                        if (idealLoads.Count != 1)
                        {
                            entry.Problem = "zone '" + zone.Name + "' has " + idealLoads.Count + " Ideal Loads systems (exactly one is required)";
                        }
                        else
                        {
                            entry.IdealLoads = idealLoads[0].Name;
                        }
                    }
                }

                result.Add(entry);
            }

            // Bijective: no native space, zone or Ideal Loads may serve two SAM spaces.
            foreach (Func<NativeZoneIdentityEntry, string> facet in new Func<NativeZoneIdentityEntry, string>[] { x => x.NativeSpace, x => x.Zone, x => x.IdealLoads })
            {
                foreach (IGrouping<string, NativeZoneIdentityEntry> group in result.Where(x => facet(x) != null).GroupBy(facet, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
                {
                    foreach (NativeZoneIdentityEntry entry in group)
                    {
                        entry.Problem = (entry.Problem == null ? string.Empty : entry.Problem + "; ") + "native '" + group.Key + "' serves " + group.Count() + " SAM spaces (not bijective)";
                    }
                }
            }

            return result;
        }
    }
}
