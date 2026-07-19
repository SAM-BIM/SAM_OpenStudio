// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using AnalyticalCreate = SAM.Analytical.Create;

namespace SAM.Analytical.OpenStudio.Tests
{
    /// <summary>
    /// Deterministic SAM fixture models for the MVP test suite (plan §12). Geometry is metric,
    /// axis-aligned and hand-checkable: box A spans x 0–5, box B x 5–10, both y 0–4, z 0–3,
    /// sharing the internal wall at x = 5. Box A's south wall carries one 2×1.4 m window.
    /// Materials/constructions are physically plausible; wall layers are stored inside → outside
    /// (SAM convention): Plasterboard, Insulation, Brick.
    /// </summary>
    public static class AnalyticalModelFixtures
    {
        /// <summary>Opaque wall construction, layers inside → outside: Plasterboard 12.5 mm, Insulation 100 mm, Brick 100 mm.</summary>
        public static readonly Construction WallConstruction = new Construction(new Guid("11111111-1111-1111-1111-111111111111"), "Fixture Construction", new List<ConstructionLayer>
        {
            new ConstructionLayer("Plasterboard", 0.0125),
            new ConstructionLayer("Insulation", 0.1),
            new ConstructionLayer("Brick", 0.1),
        });

        /// <summary>Double-glazing aperture construction, pane layers inside → outside: Glass 6 mm, Air 12 mm, Glass 6 mm.</summary>
        public static readonly ApertureConstruction WindowConstruction = new ApertureConstruction(new Guid("22222222-2222-2222-2222-222222222222"), "Fixture Window", ApertureType.Window, new List<ConstructionLayer>
        {
            new ConstructionLayer("Glass", 0.006),
            new ConstructionLayer("Air", 0.012),
            new ConstructionLayer("Glass", 0.006),
        });

        /// <summary>Opaque wall construction with a gas cavity, layers inside → outside: Plasterboard 12.5 mm, Air Cavity 50 mm (h = 1.25 W/m²K → R = 0.8 m²K/W), Brick 100 mm.</summary>
        public static readonly Construction CavityWallConstruction = new Construction(new Guid("11111111-2222-2222-2222-222222222222"), "Fixture Cavity Wall", new List<ConstructionLayer>
        {
            new ConstructionLayer("Plasterboard", 0.0125),
            new ConstructionLayer("Fixture Air Cavity", 0.05),
            new ConstructionLayer("Brick", 0.1),
        });

        /// <summary>Gas cavity material with explicit conductance h [W/m²K] (R = 1/h m²K/W) — mirrors the real AR90UP 50 mm materials.</summary>
        public static GasMaterial CreateCavityGasMaterial(double heatTransferCoefficient = 1.25)
        {
            GasMaterial result = new GasMaterial(new Guid("33333333-0000-0000-0000-000000000006"), "Fixture Air Cavity");
            result.SetValue(GasMaterialParameter.HeatTransferCoefficient, heatTransferCoefficient);
            result.SetValue(GasMaterialParameter.DefaultGasType, "Air");
            return result;
        }

        private static Point3D P(double x, double y, double z)
        {
            return new Point3D(x, y, z);
        }

        private static Face3D F(params Point3D[] point3Ds)
        {
            return new Face3D(new Polygon3D(point3Ds));
        }

        /// <summary>
        /// Material library backing the fixture constructions: Brick, Insulation, Plasterboard
        /// (opaque), Glass (transparent), Air (gas). Values are round, physically plausible and
        /// asserted by the M4 tests.
        /// </summary>
        public static MaterialLibrary CreateMaterialLibrary()
        {
            OpaqueMaterial brick = new OpaqueMaterial(new Guid("33333333-0000-0000-0000-000000000001"), "Brick", "Brick", "Fixture brick", 0.84, 1700, 800);
            brick.SetValue(OpaqueMaterialParameter.ExternalEmissivity, 0.9);
            brick.SetValue(OpaqueMaterialParameter.ExternalSolarReflectance, 0.3);
            brick.SetValue(OpaqueMaterialParameter.ExternalLightReflectance, 0.3);

            OpaqueMaterial insulation = new OpaqueMaterial(new Guid("33333333-0000-0000-0000-000000000002"), "Insulation", "Insulation", "Fixture insulation", 0.035, 25, 1400);
            insulation.SetValue(OpaqueMaterialParameter.ExternalEmissivity, 0.9);
            insulation.SetValue(OpaqueMaterialParameter.ExternalSolarReflectance, 0.3);
            insulation.SetValue(OpaqueMaterialParameter.ExternalLightReflectance, 0.3);

            OpaqueMaterial plasterboard = new OpaqueMaterial(new Guid("33333333-0000-0000-0000-000000000003"), "Plasterboard", "Plasterboard", "Fixture plasterboard", 0.25, 900, 1000);
            plasterboard.SetValue(OpaqueMaterialParameter.ExternalEmissivity, 0.9);
            plasterboard.SetValue(OpaqueMaterialParameter.ExternalSolarReflectance, 0.5);
            plasterboard.SetValue(OpaqueMaterialParameter.ExternalLightReflectance, 0.5);

            TransparentMaterial glass = new TransparentMaterial(new Guid("33333333-0000-0000-0000-000000000004"), "Glass", "Glass", "Fixture glass", 1.0, 2500, 840);
            glass.SetValue(TransparentMaterialParameter.SolarTransmittance, 0.7);
            glass.SetValue(TransparentMaterialParameter.InternalSolarReflectance, 0.07);
            glass.SetValue(TransparentMaterialParameter.ExternalSolarReflectance, 0.07);
            glass.SetValue(TransparentMaterialParameter.LightTransmittance, 0.8);
            glass.SetValue(TransparentMaterialParameter.InternalLightReflectance, 0.07);
            glass.SetValue(TransparentMaterialParameter.ExternalLightReflectance, 0.07);
            glass.SetValue(TransparentMaterialParameter.InternalEmissivity, 0.84);
            glass.SetValue(TransparentMaterialParameter.ExternalEmissivity, 0.84);

            GasMaterial air = new GasMaterial(new Guid("33333333-0000-0000-0000-000000000005"), "Air");

            MaterialLibrary result = new MaterialLibrary("Fixture Material Library");
            result.Add(brick);
            result.Add(insulation);
            result.Add(plasterboard);
            result.Add(glass);
            result.Add(air);
            return result;
        }

        /// <summary>
        /// Office-hours (08:00–17:59) fraction profiles, constant equipment/infiltration and
        /// 21/25 °C setpoint profiles backing the fixture internal condition.
        /// </summary>
        public static ProfileLibrary CreateProfileLibrary(double heatingSetpoint = 21, double coolingSetpoint = 25)
        {
            double[] officeHours = new double[24];
            for (int i = 8; i <= 17; i++)
            {
                officeHours[i] = 1;
            }

            double[] alwaysOn = new double[24];
            double[] heating = new double[24];
            double[] cooling = new double[24];
            for (int i = 0; i < 24; i++)
            {
                alwaysOn[i] = 1;
                heating[i] = heatingSetpoint;
                cooling[i] = coolingSetpoint;
            }

            ProfileLibrary result = new ProfileLibrary("Fixture Profile Library");
            result.Add(new Profile("Office Occupancy", ProfileType.Occupancy, officeHours));
            result.Add(new Profile("Office Lighting", ProfileType.Lighting, officeHours));
            result.Add(new Profile("Office Equipment", ProfileType.EquipmentSensible, alwaysOn));
            result.Add(new Profile("Office Infiltration", ProfileType.Infiltration, alwaysOn));
            result.Add(new Profile("Office Heating", ProfileType.Heating, heating));
            result.Add(new Profile("Office Cooling", ProfileType.Cooling, cooling));
            return result;
        }

        /// <summary>
        /// Conditioned office internal condition: 10 m²/person, 75/55 W sensible/latent per
        /// person, 8 W/m² lighting, 12 W/m² equipment, 0.5 ACH infiltration, 0.02 m³/s outdoor
        /// air, 21/25 °C setpoints; profile references into <see cref="CreateProfileLibrary"/>.
        /// </summary>
        public static InternalCondition CreateOfficeInternalCondition()
        {
            InternalCondition result = new InternalCondition(new Guid("44444444-0000-0000-0000-000000000001"), "Office");
            result.SetValue(InternalConditionParameter.AreaPerPerson, 10.0);
            result.SetValue(InternalConditionParameter.OccupancySensibleGainPerPerson, 75.0);
            result.SetValue(InternalConditionParameter.OccupancyLatentGainPerPerson, 55.0);
            result.SetValue(InternalConditionParameter.LightingGainPerArea, 8.0);
            result.SetValue(InternalConditionParameter.EquipmentSensibleGainPerArea, 12.0);
            result.SetValue(InternalConditionParameter.InfiltrationAirChangesPerHour, 0.5);
            result.SetValue(InternalConditionParameter.OccupancyProfileName, "Office Occupancy");
            result.SetValue(InternalConditionParameter.LightingProfileName, "Office Lighting");
            result.SetValue(InternalConditionParameter.EquipmentSensibleProfileName, "Office Equipment");
            result.SetValue(InternalConditionParameter.InfiltrationProfileName, "Office Infiltration");
            result.SetValue(InternalConditionParameter.HeatingProfileName, "Office Heating");
            result.SetValue(InternalConditionParameter.CoolingProfileName, "Office Cooling");
            return result;
        }

        /// <summary>
        /// Two adjacent boxes sharing one internal wall; box A has a window in its south wall.
        /// 2 spaces (both conditioned offices sharing one InternalCondition, unless
        /// <paramref name="spaceBUnconditioned"/>), 11 panels (12 OpenStudio surfaces once the
        /// shared wall is duplicated per side), 1 aperture, full material and profile libraries.
        /// </summary>
        public static AnalyticalModel TwoAdjacentBoxes(bool spaceBUnconditioned = false, double? spaceBVolume = null)
        {
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster();

            InternalCondition officeInternalCondition = CreateOfficeInternalCondition();

            Space spaceA = new Space(new Guid("aaaaaaaa-0000-0000-0000-000000000001"), "Space A", P(2.5, 2, 1.5));
            spaceA.SetValue(SpaceParameter.Area, 20.0);
            spaceA.SetValue(SpaceParameter.Volume, 60.0);
            spaceA.SetValue(SpaceParameter.OutsideSupplyAirFlow, 0.02);
            spaceA.InternalCondition = officeInternalCondition;

            Space spaceB = new Space(new Guid("bbbbbbbb-0000-0000-0000-000000000002"), "Space B", P(7.5, 2, 1.5));
            spaceB.SetValue(SpaceParameter.Area, 20.0);
            spaceB.SetValue(SpaceParameter.Volume, spaceBVolume ?? 60.0);
            spaceB.SetValue(SpaceParameter.OutsideSupplyAirFlow, 0.02);
            spaceB.InternalCondition = spaceBUnconditioned ? new InternalCondition("Office Unconditioned", officeInternalCondition) : officeInternalCondition;

            adjacencyCluster.AddObject(spaceA);
            adjacencyCluster.AddObject(spaceB);

            Panel floorA = AnalyticalCreate.Panel(WallConstruction, PanelType.SlabOnGrade, F(P(0, 0, 0), P(5, 0, 0), P(5, 4, 0), P(0, 4, 0)));
            Panel roofA = AnalyticalCreate.Panel(WallConstruction, PanelType.Roof, F(P(0, 0, 3), P(5, 0, 3), P(5, 4, 3), P(0, 4, 3)));
            Panel wallSouthA = AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(0, 0, 0), P(5, 0, 0), P(5, 0, 3), P(0, 0, 3)));
            Panel wallWestA = AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(0, 0, 0), P(0, 4, 0), P(0, 4, 3), P(0, 0, 3)));
            Panel wallNorthA = AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(0, 4, 0), P(5, 4, 0), P(5, 4, 3), P(0, 4, 3)));

            Panel wallShared = AnalyticalCreate.Panel(WallConstruction, PanelType.WallInternal, F(P(5, 0, 0), P(5, 4, 0), P(5, 4, 3), P(5, 0, 3)));

            Panel floorB = AnalyticalCreate.Panel(WallConstruction, PanelType.SlabOnGrade, F(P(5, 0, 0), P(10, 0, 0), P(10, 4, 0), P(5, 4, 0)));
            Panel roofB = AnalyticalCreate.Panel(WallConstruction, PanelType.Roof, F(P(5, 0, 3), P(10, 0, 3), P(10, 4, 3), P(5, 4, 3)));
            Panel wallSouthB = AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(5, 0, 0), P(10, 0, 0), P(10, 0, 3), P(5, 0, 3)));
            Panel wallEastB = AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(10, 0, 0), P(10, 4, 0), P(10, 4, 3), P(10, 0, 3)));
            Panel wallNorthB = AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(5, 4, 0), P(10, 4, 0), P(10, 4, 3), P(5, 4, 3)));

            Aperture window = AnalyticalCreate.Aperture(WindowConstruction, F(P(1, 0, 0.8), P(3, 0, 0.8), P(3, 0, 2.2), P(1, 0, 2.2)));
            wallSouthA.AddAperture(window);

            List<Panel> panelsA = new List<Panel> { floorA, roofA, wallSouthA, wallWestA, wallNorthA, wallShared };
            List<Panel> panelsB = new List<Panel> { floorB, roofB, wallSouthB, wallEastB, wallNorthB, wallShared };

            foreach (Panel panel in panelsA)
            {
                adjacencyCluster.AddObject(panel);
                adjacencyCluster.AddRelation(spaceA, panel);
            }

            foreach (Panel panel in panelsB)
            {
                if (panel != wallShared)
                {
                    adjacencyCluster.AddObject(panel);
                }

                adjacencyCluster.AddRelation(spaceB, panel);
            }

            return new AnalyticalModel("Two Box Model", "MVP two adjacent boxes fixture", null, null, adjacencyCluster, CreateMaterialLibrary(), CreateProfileLibrary());
        }

        /// <summary>
        /// One-zone box (x 0–5, y 0–4, z 0–3) with an optional south window; all panels use the
        /// given construction (fixture default when null). Used by simulation and failure-policy
        /// tests.
        /// </summary>
        public static AnalyticalModel SingleBox(Construction wallConstruction = null, bool includeWindow = true, bool withProfiles = true, InternalCondition internalConditionOverride = null, ProfileLibrary profileLibraryOverride = null)
        {
            Construction construction = wallConstruction ?? WallConstruction;

            AdjacencyCluster adjacencyCluster = new AdjacencyCluster();

            Space space = new Space(new Guid("cccccccc-0000-0000-0000-000000000001"), "Space Single", P(2.5, 2, 1.5));
            space.SetValue(SpaceParameter.Area, 20.0);
            space.SetValue(SpaceParameter.Volume, 60.0);
            space.SetValue(SpaceParameter.OutsideSupplyAirFlow, 0.02);
            space.InternalCondition = internalConditionOverride ?? CreateOfficeInternalCondition();
            adjacencyCluster.AddObject(space);

            List<Panel> panels = new List<Panel>
            {
                AnalyticalCreate.Panel(construction, PanelType.SlabOnGrade, F(P(0, 0, 0), P(5, 0, 0), P(5, 4, 0), P(0, 4, 0))),
                AnalyticalCreate.Panel(construction, PanelType.Roof, F(P(0, 0, 3), P(5, 0, 3), P(5, 4, 3), P(0, 4, 3))),
                AnalyticalCreate.Panel(construction, PanelType.WallExternal, F(P(0, 0, 0), P(5, 0, 0), P(5, 0, 3), P(0, 0, 3))),
                AnalyticalCreate.Panel(construction, PanelType.WallExternal, F(P(0, 0, 0), P(0, 4, 0), P(0, 4, 3), P(0, 0, 3))),
                AnalyticalCreate.Panel(construction, PanelType.WallExternal, F(P(0, 4, 0), P(5, 4, 0), P(5, 4, 3), P(0, 4, 3))),
                AnalyticalCreate.Panel(construction, PanelType.WallExternal, F(P(5, 0, 0), P(5, 4, 0), P(5, 4, 3), P(5, 0, 3))),
            };

            if (includeWindow)
            {
                panels[2].AddAperture(AnalyticalCreate.Aperture(WindowConstruction, F(P(1, 0, 0.8), P(3, 0, 0.8), P(3, 0, 2.2), P(1, 0, 2.2))));
            }

            foreach (Panel panel in panels)
            {
                adjacencyCluster.AddObject(panel);
                adjacencyCluster.AddRelation(space, panel);
            }

            return new AnalyticalModel("Single Box Model", "MVP one-zone box fixture", null, null, adjacencyCluster, CreateMaterialLibrary(), withProfiles ? (profileLibraryOverride ?? CreateProfileLibrary()) : new ProfileLibrary("Empty Profile Library"));
        }

        /// <summary>
        /// Two vertically stacked boxes (A: z 0–3, C: z 3–6, both 5×4 m) sharing an internal
        /// floor at z = 3. Exercises BuildingStory assignment and the floor/ceiling
        /// surface-type rule on the shared panel. Both spaces are conditioned offices.
        /// </summary>
        public static AnalyticalModel TwoStackedBoxes()
        {
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster();

            InternalCondition officeInternalCondition = CreateOfficeInternalCondition();

            Space spaceA = new Space(new Guid("aaaaaaaa-0000-0000-0000-000000000011"), "Space Lower", P(2.5, 2, 1.5));
            spaceA.SetValue(SpaceParameter.Area, 20.0);
            spaceA.SetValue(SpaceParameter.Volume, 60.0);
            spaceA.InternalCondition = officeInternalCondition;

            Space spaceC = new Space(new Guid("cccccccc-0000-0000-0000-000000000012"), "Space Upper", P(2.5, 2, 4.5));
            spaceC.SetValue(SpaceParameter.Area, 20.0);
            spaceC.SetValue(SpaceParameter.Volume, 60.0);
            spaceC.InternalCondition = officeInternalCondition;

            adjacencyCluster.AddObject(spaceA);
            adjacencyCluster.AddObject(spaceC);

            Panel sharedFloor = AnalyticalCreate.Panel(WallConstruction, PanelType.FloorInternal, F(P(0, 0, 3), P(5, 0, 3), P(5, 4, 3), P(0, 4, 3)));

            List<Panel> panelsA = new List<Panel>
            {
                AnalyticalCreate.Panel(WallConstruction, PanelType.SlabOnGrade, F(P(0, 0, 0), P(5, 0, 0), P(5, 4, 0), P(0, 4, 0))),
                AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(0, 0, 0), P(5, 0, 0), P(5, 0, 3), P(0, 0, 3))),
                AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(0, 0, 0), P(0, 4, 0), P(0, 4, 3), P(0, 0, 3))),
                AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(0, 4, 0), P(5, 4, 0), P(5, 4, 3), P(0, 4, 3))),
                AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(5, 0, 0), P(5, 4, 0), P(5, 4, 3), P(5, 0, 3))),
                sharedFloor,
            };

            List<Panel> panelsC = new List<Panel>
            {
                AnalyticalCreate.Panel(WallConstruction, PanelType.Roof, F(P(0, 0, 6), P(5, 0, 6), P(5, 4, 6), P(0, 4, 6))),
                AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(0, 0, 3), P(5, 0, 3), P(5, 0, 6), P(0, 0, 6))),
                AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(0, 0, 3), P(0, 4, 3), P(0, 4, 6), P(0, 0, 6))),
                AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(0, 4, 3), P(5, 4, 3), P(5, 4, 6), P(0, 4, 6))),
                AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(5, 0, 3), P(5, 4, 3), P(5, 4, 6), P(5, 0, 6))),
                sharedFloor,
            };

            foreach (Panel panel in panelsA)
            {
                adjacencyCluster.AddObject(panel);
                adjacencyCluster.AddRelation(spaceA, panel);
            }

            foreach (Panel panel in panelsC)
            {
                if (panel != sharedFloor)
                {
                    adjacencyCluster.AddObject(panel);
                }

                adjacencyCluster.AddRelation(spaceC, panel);
            }

            return new AnalyticalModel("Two Stacked Box Model", "MVP two-level fixture", null, null, adjacencyCluster, CreateMaterialLibrary(), CreateProfileLibrary());
        }

        /// <summary>
        /// One-zone box whose floor polygon carries a collinear mid-edge vertex and a duplicate
        /// closing vertex — exercises vertex cleaning (SAM-OS-GEO-002 warning, unchanged area).
        /// </summary>
        public static AnalyticalModel IrregularPlanarBox()
        {
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster();

            Space space = new Space(new Guid("dddddddd-0000-0000-0000-000000000001"), "Space Irregular", P(2.5, 2, 1.5));
            space.SetValue(SpaceParameter.Area, 20.0);
            space.SetValue(SpaceParameter.Volume, 60.0);
            space.InternalCondition = CreateOfficeInternalCondition();
            adjacencyCluster.AddObject(space);

            List<Panel> panels = new List<Panel>
            {
                AnalyticalCreate.Panel(WallConstruction, PanelType.SlabOnGrade, F(P(0, 0, 0), P(2.5, 0, 0), P(5, 0, 0), P(5, 4, 0), P(0, 4, 0), P(0, 0, 0))),
                AnalyticalCreate.Panel(WallConstruction, PanelType.Roof, F(P(0, 0, 3), P(5, 0, 3), P(5, 4, 3), P(0, 4, 3))),
                AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(0, 0, 0), P(5, 0, 0), P(5, 0, 3), P(0, 0, 3))),
                AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(0, 0, 0), P(0, 4, 0), P(0, 4, 3), P(0, 0, 3))),
                AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(0, 4, 0), P(5, 4, 0), P(5, 4, 3), P(0, 4, 3))),
                AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(5, 0, 0), P(5, 4, 0), P(5, 4, 3), P(5, 0, 3))),
            };

            foreach (Panel panel in panels)
            {
                adjacencyCluster.AddObject(panel);
                adjacencyCluster.AddRelation(space, panel);
            }

            return new AnalyticalModel("Irregular Box Model", "MVP polygon-cleaning fixture", null, null, adjacencyCluster, CreateMaterialLibrary(), CreateProfileLibrary());
        }

        /// <summary>
        /// One-zone box with one degenerate (near-zero-area sliver) wall panel — exercises the
        /// failure policy: SAM-OS-GEO-001 error, surface skipped, result invalid, no repair.
        /// </summary>
        public static AnalyticalModel DegeneratePanelBox()
        {
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster();

            Space space = new Space(new Guid("eeeeeeee-0000-0000-0000-000000000001"), "Space Degenerate", P(2.5, 2, 1.5));
            space.SetValue(SpaceParameter.Area, 20.0);
            space.SetValue(SpaceParameter.Volume, 60.0);
            space.InternalCondition = CreateOfficeInternalCondition();
            adjacencyCluster.AddObject(space);

            List<Panel> panels = new List<Panel>
            {
                AnalyticalCreate.Panel(WallConstruction, PanelType.SlabOnGrade, F(P(0, 0, 0), P(5, 0, 0), P(5, 4, 0), P(0, 4, 0))),
                AnalyticalCreate.Panel(WallConstruction, PanelType.Roof, F(P(0, 0, 3), P(5, 0, 3), P(5, 4, 3), P(0, 4, 3))),
                AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(0, 0, 0), P(5, 0, 0), P(5, 0, 3), P(0, 0, 3))),
                AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(0, 0, 0), P(0, 4, 0), P(0, 4, 3), P(0, 0, 3))),
                AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(0, 4, 0), P(5, 4, 0), P(5, 4, 3), P(0, 4, 3))),
                // degenerate sliver: 5 m long, 0.00001 m tall — area 4e-5 m², below the 1e-4 m² minimum
                AnalyticalCreate.Panel(WallConstruction, PanelType.WallExternal, F(P(5, 0, 0), P(5, 4, 0), P(5, 4, 0.00001), P(5, 0, 0.00001))),
            };

            foreach (Panel panel in panels)
            {
                adjacencyCluster.AddObject(panel);
                adjacencyCluster.AddRelation(space, panel);
            }

            return new AnalyticalModel("Degenerate Box Model", "MVP failure-policy fixture", null, null, adjacencyCluster, CreateMaterialLibrary(), CreateProfileLibrary());
        }

        /// <summary>
        /// One-zone box whose opaque wall construction carries a 50 mm gas cavity
        /// (h = 1.25 W/m²K → R = 0.8 m²K/W) — reduced reproduction of the real-model failure
        /// (review P1-06). An optional second gas material can be added to the library.
        /// </summary>
        public static AnalyticalModel OpaqueAirGapBox(double heatTransferCoefficient = 1.25, GasMaterial additionalGasMaterial = null, bool includeWindow = true)
        {
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster();

            Space space = new Space(new Guid("ffffffff-0000-0000-0000-000000000001"), "Space AirGap", P(2.5, 2, 1.5));
            space.SetValue(SpaceParameter.Area, 20.0);
            space.SetValue(SpaceParameter.Volume, 60.0);
            space.SetValue(SpaceParameter.OutsideSupplyAirFlow, 0.02);
            space.InternalCondition = CreateOfficeInternalCondition();
            adjacencyCluster.AddObject(space);

            List<Panel> panels = new List<Panel>
            {
                AnalyticalCreate.Panel(CavityWallConstruction, PanelType.SlabOnGrade, F(P(0, 0, 0), P(5, 0, 0), P(5, 4, 0), P(0, 4, 0))),
                AnalyticalCreate.Panel(CavityWallConstruction, PanelType.Roof, F(P(0, 0, 3), P(5, 0, 3), P(5, 4, 3), P(0, 4, 3))),
                AnalyticalCreate.Panel(CavityWallConstruction, PanelType.WallExternal, F(P(0, 0, 0), P(5, 0, 0), P(5, 0, 3), P(0, 0, 3))),
                AnalyticalCreate.Panel(CavityWallConstruction, PanelType.WallExternal, F(P(0, 0, 0), P(0, 4, 0), P(0, 4, 3), P(0, 0, 3))),
                AnalyticalCreate.Panel(CavityWallConstruction, PanelType.WallExternal, F(P(0, 4, 0), P(5, 4, 0), P(5, 4, 3), P(0, 4, 3))),
                AnalyticalCreate.Panel(CavityWallConstruction, PanelType.WallExternal, F(P(5, 0, 0), P(5, 4, 0), P(5, 4, 3), P(5, 0, 3))),
            };

            if (includeWindow)
            {
                panels[2].AddAperture(AnalyticalCreate.Aperture(WindowConstruction, F(P(1, 0, 0.8), P(3, 0, 0.8), P(3, 0, 2.2), P(1, 0, 2.2))));
            }

            foreach (Panel panel in panels)
            {
                adjacencyCluster.AddObject(panel);
                adjacencyCluster.AddRelation(space, panel);
            }

            MaterialLibrary materialLibrary = CreateMaterialLibrary();
            materialLibrary.Add(CreateCavityGasMaterial(heatTransferCoefficient));
            if (additionalGasMaterial != null)
            {
                materialLibrary.Add(additionalGasMaterial);
            }

            return new AnalyticalModel("Opaque AirGap Box Model", "MVP opaque air-gap fixture (review P1-06)", null, null, adjacencyCluster, materialLibrary, CreateProfileLibrary());
        }
    }
}
