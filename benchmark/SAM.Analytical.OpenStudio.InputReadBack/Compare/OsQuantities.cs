// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical.InputEquivalence;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.OpenStudio.InputReadBack
{
    /// <summary>
    /// The S07 section 5 rows whose OpenStudio column differs from the Tas one held by the core catalogue. Each tolerance is
    /// the one S07 states for OpenStudio; none is new. Every other row uses <see cref="QuantityCatalog"/> unchanged.
    /// </summary>
    public static class OsQuantities
    {
        /// <summary>Row 9, OpenStudio: the EPW the OSM references is the effective weather file, by SHA-256 identity (1 = identical).</summary>
        public static readonly QuantityDefinition WeatherFile = new QuantityDefinition("9.os.epw", "Weather file identity (EPW SHA-256)", "-", ToleranceRule.Exact("-"), true);

        /// <summary>Row 9, OpenStudio: latitude and longitude at EPW precision, 1e-4 degrees.</summary>
        public static readonly QuantityDefinition Latitude = new QuantityDefinition("9.os.latitude", "Site latitude", "deg", ToleranceRule.Create(null, 1e-4, "deg"), true);

        public static readonly QuantityDefinition Longitude = new QuantityDefinition("9.os.longitude", "Site longitude", "deg", ToleranceRule.Create(null, 1e-4, "deg"), true);

        /// <summary>Row 9, OpenStudio: elevation, 0.5 m.</summary>
        public static readonly QuantityDefinition Elevation = new QuantityDefinition("9.os.elevation", "Site elevation", "m", ToleranceRule.Create(null, 0.5, "m"), true);

        /// <summary>Row 9: time zone. S07 states no OpenStudio tolerance for it, so it is exact.</summary>
        public static readonly QuantityDefinition TimeZone = new QuantityDefinition("9.os.timezone", "Site time zone", "h", ToleranceRule.Exact("h"), true);

        /// <summary>Row 10, OpenStudio: the number of native design days against the effective design-day source (T-DD needs one per stated day).</summary>
        public static readonly QuantityDefinition DesignDayCount = new QuantityDefinition("10.os.count", "Design days written", "-", ToleranceRule.Exact("-"), true);

        /// <summary>T-FRAME, OpenStudio: frame conductance 1/sum(t/lambda), 1e-5 W/m2K.</summary>
        public static readonly QuantityDefinition FrameConductance = new QuantityDefinition("T-FRAME.conductance", "Frame conductance (T-FRAME)", "W/m2K", ToleranceRule.Create(null, 1e-5, "W/m2K"), true);

        public static QuantityDefinition Get(string id)
        {
            switch (id)
            {
                case "9.os.epw": return WeatherFile;
                case "9.os.latitude": return Latitude;
                case "9.os.longitude": return Longitude;
                case "9.os.elevation": return Elevation;
                case "9.os.timezone": return TimeZone;
                case "10.os.count": return DesignDayCount;
                case "T-FRAME.conductance": return FrameConductance;
                default: return QuantityCatalog.Get(id);
            }
        }
    }

    /// <summary>The object keys of the S09 comparison, shaped like the S08 ones so the two engines' reports line up.</summary>
    public static class Keys
    {
        public static string Space(Guid space) => "space:" + space.ToString("D");

        public static string Side(Guid panel, Guid space) => "panel:" + panel.ToString("D") + "@space:" + space.ToString("D");

        public static string Layer(string side, int k, string property) => side + "#layer" + k.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + property;

        public static string Aperture(Guid aperture, Guid space) => "aperture:" + aperture.ToString("D") + "@space:" + space.ToString("D");

        public static string Ic(Guid space, string what) => Space(space) + ".ic." + what;

        public static string Weather(string what) => "weather." + what;

        public const string DesignDays = "designday.count";
    }

    /// <summary>The S07 layer properties compared per layer (names as in S08).</summary>
    public static class LayerProperties
    {
        public static readonly string[] Opaque = { "thickness", "conductivity", "density", "specificHeat", "externalSolarReflectance", "internalSolarReflectance", "externalLightReflectance", "internalLightReflectance", "externalEmissivity", "internalEmissivity" };
        public static readonly string[] Transparent = { "thickness", "conductivity", "solarTransmittance", "lightTransmittance", "externalSolarReflectance", "internalSolarReflectance", "externalLightReflectance", "internalLightReflectance", "externalEmissivity", "internalEmissivity" };
        public static readonly string[] VentilationBases = { "flow", "flowPerArea", "flowPerPerson", "airChangesPerHour" };
        public static readonly string[] Gains = { "occupancySensible", "occupancyLatent", "lighting", "equipmentSensible", "equipmentLatent" };
        public static readonly string[] Schedules = { "occupancy", "lighting", "equipmentSensible", "equipmentLatent", "infiltration", "ventilation" };
        public static readonly string[] Fractions = { "occupancy.radiant", "lighting.radiant", "lighting.visible", "equipment.radiant" };
    }
}
