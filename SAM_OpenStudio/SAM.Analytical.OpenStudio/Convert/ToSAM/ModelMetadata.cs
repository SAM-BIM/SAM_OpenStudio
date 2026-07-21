// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Assembles the final SAM <see cref="AnalyticalModel"/> from the imported topology and
        /// libraries, and attaches the model-level metadata: name, site location, north axis,
        /// design days and the weather-file reference.
        /// <para>
        /// Nothing absent is fabricated. A model with no Site object gets no SAM
        /// <see cref="Core.Location"/>; a WeatherFile reference is recorded as a path only and
        /// never presented as embedded hourly weather.
        /// </para>
        /// </summary>
        /// <param name="openStudioImportContext">Import context.</param>
        /// <param name="adjacencyCluster">Imported topology.</param>
        /// <param name="materialLibrary">Imported material library.</param>
        /// <param name="profileLibrary">Imported profile library.</param>
        /// <returns>The assembled SAM analytical model.</returns>
        internal static AnalyticalModel ToSAM_AnalyticalModel(this OpenStudioImportContext openStudioImportContext, AdjacencyCluster adjacencyCluster, Core.MaterialLibrary materialLibrary, ProfileLibrary profileLibrary)
        {
            global::OpenStudio.Model model = openStudioImportContext.Source;
            global::OpenStudio.Building building = model.getBuilding();

            string name = null;
            string description = null;
            Guid guid = Guid.Empty;

            if (building != null)
            {
                if (!Core.OpenStudio.Query.TryGetSAMName(building, out name) || string.IsNullOrWhiteSpace(name))
                {
                    name = building.nameString();
                }

                if (openStudioImportContext.Options.RestoreSAMIdentity)
                {
                    Core.OpenStudio.Query.TryGetSAMGuid(building, out guid);
                }
            }

            Core.Location location = ToSAM_Location(openStudioImportContext);

            AnalyticalModel result = new AnalyticalModel(name, description, location, null, adjacencyCluster, materialLibrary, profileLibrary);

            if (guid != Guid.Empty && guid != result.Guid)
            {
                // AnalyticalModel exposes no (Guid, AnalyticalModel) constructor, so the
                // model-level Guid can only be restored through a JSON round trip — the same
                // approach SAM_LadybugTools uses. A failed reconstruction must never destroy an
                // otherwise valid model, so the original instance is kept and the loss reported.
                AnalyticalModel restored = TryRestoreGuid(result, guid);
                if (restored != null)
                {
                    result = restored;
                }
                else
                {
                    openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.IdentityCollision, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("The model-level SAM Guid {0} could not be restored onto the imported AnalyticalModel; the model is valid but carries a new Guid", guid), OpenStudioImportContext.OpenStudioObjectLabel(building));
                }
            }

            ToSAM_NorthAxis(building, result, openStudioImportContext);
            ToSAM_WeatherReference(openStudioImportContext, result);
            ToSAM_DesignDays(openStudioImportContext, result);
            ToSAM_SimulationSettings(openStudioImportContext);

            return result;
        }

        /// <summary>
        /// Site → SAM <see cref="Core.Location"/>. Returns null when the model carries no Site, or
        /// when its latitude, longitude and elevation are all exactly zero.
        /// <para>
        /// Every OpenStudio model has a Site object with an auto-assigned name, so the name says
        /// nothing about whether the site was ever set; the coordinates do. All three exactly
        /// zero is the OpenStudio default, and importing it would place the building in the Gulf
        /// of Guinea — a fabricated location is worse than none.
        /// </para>
        /// </summary>
        private static Core.Location ToSAM_Location(OpenStudioImportContext openStudioImportContext)
        {
            global::OpenStudio.Site site;
            try
            {
                site = openStudioImportContext.Source.getSite();
            }
            catch (Exception)
            {
                return null;
            }

            if (site == null)
            {
                return null;
            }

            string name = site.nameString();
            double latitude = site.latitude();
            double longitude = site.longitude();
            double elevation = site.elevation();

            if (latitude == 0 && longitude == 0 && elevation == 0)
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.WeatherLimitation, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, "The model's Site still carries the default (0, 0, 0) coordinates, so no site was ever set; no SAM Location was created rather than placing the model at the equator", OpenStudioImportContext.OpenStudioObjectLabel(site));
                return null;
            }

            double timeZone = site.timeZone();
            string terrain = site.terrain();
            if (!string.IsNullOrWhiteSpace(terrain))
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.WeatherLimitation, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format(CultureInfo.InvariantCulture, "Site terrain '{0}' and time zone {1:G4} have no SAM Location equivalent and were not imported; latitude, longitude and elevation were", terrain, timeZone), OpenStudioImportContext.OpenStudioObjectLabel(site));
            }

            return new Core.Location(name, longitude, latitude, elevation);
        }

        /// <summary>
        /// Building north axis [degrees clockwise from north] → SAM
        /// <see cref="AnalyticalModelParameter.NorthAngle"/> [radians], the exact inverse of the
        /// forward conversion in Convert/ToOpenStudio/SimulationSettings.cs.
        /// </summary>
        private static void ToSAM_NorthAxis(global::OpenStudio.Building building, AnalyticalModel analyticalModel, OpenStudioImportContext openStudioImportContext)
        {
            if (building == null)
            {
                return;
            }

            double northAxisDegrees;
            try
            {
                northAxisDegrees = building.northAxis();
            }
            catch (Exception)
            {
                return;
            }

            if (double.IsNaN(northAxisDegrees))
            {
                return;
            }

            analyticalModel.SetValue(AnalyticalModelParameter.NorthAngle, northAxisDegrees * Math.PI / 180.0);
        }

        /// <summary>
        /// Records the model's weather-file reference as metadata.
        /// <para>
        /// A <c>OS:WeatherFile</c> object names an EPW; it does not contain one. Creating SAM
        /// <c>WeatherData</c> from it would present a file path as a year of hourly weather, so
        /// the path is stored on <see cref="OpenStudioSourceParameter.WeatherFilePath"/> and the
        /// distinction is stated in a diagnostic.
        /// </para>
        /// </summary>
        private static void ToSAM_WeatherReference(OpenStudioImportContext openStudioImportContext, AnalyticalModel analyticalModel)
        {
            global::OpenStudio.OptionalWeatherFile optionalWeatherFile;
            try
            {
                optionalWeatherFile = openStudioImportContext.Source.weatherFile();
            }
            catch (Exception)
            {
                return;
            }

            if (optionalWeatherFile == null || optionalWeatherFile.isNull())
            {
                return;
            }

            global::OpenStudio.WeatherFile weatherFile = optionalWeatherFile.get();

            string path = null;
            global::OpenStudio.OptionalString optionalUrl = weatherFile.url();
            if (optionalUrl != null && !optionalUrl.isNull())
            {
                path = optionalUrl.get();
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                global::OpenStudio.OptionalPath optionalPath = weatherFile.path();
                if (optionalPath != null && !optionalPath.isNull())
                {
                    path = optionalPath.get().__str__();
                }
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                analyticalModel.SetValue(OpenStudioSourceParameter.WeatherFilePath, path);
            }

            openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.WeatherLimitation, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format("The model references the weather file '{0}' ({1}, {2}); an OSM stores only the reference, so no SAM WeatherData was embedded - supply the EPW separately for annual simulation", string.IsNullOrWhiteSpace(path) ? "path unavailable" : path, weatherFile.city(), weatherFile.country()), OpenStudioImportContext.OpenStudioObjectLabel(weatherFile));
        }

        /// <summary>
        /// SizingPeriod:DesignDay objects → SAM heating/cooling
        /// <see cref="DesignDay"/> collections, classified by their day type.
        /// <para>
        /// A SAM DesignDay is an hourly weather day, whereas an EnergyPlus design day is a
        /// parametric definition (maximum dry bulb, daily range, a temperature-range profile
        /// type). The dates and names transfer; the hourly reconstruction does not, so the design
        /// days are imported as identity and date only and the limitation is reported.
        /// </para>
        /// </summary>
        private static void ToSAM_DesignDays(OpenStudioImportContext openStudioImportContext, AnalyticalModel analyticalModel)
        {
            global::OpenStudio.DesignDayVector designDayVector;
            try
            {
                designDayVector = openStudioImportContext.Source.getDesignDays();
            }
            catch (Exception)
            {
                return;
            }

            if (designDayVector == null || designDayVector.Count == 0)
            {
                return;
            }

            Core.SAMCollection<DesignDay> heatingDesignDays = new Core.SAMCollection<DesignDay>();
            Core.SAMCollection<DesignDay> coolingDesignDays = new Core.SAMCollection<DesignDay>();

            foreach (global::OpenStudio.DesignDay openStudioDesignDay in designDayVector)
            {
                if (openStudioDesignDay == null)
                {
                    continue;
                }

                DesignDay designDay;
                try
                {
                    designDay = new DesignDay(openStudioDesignDay.nameString(), 0, (byte)openStudioDesignDay.month(), (byte)openStudioDesignDay.dayOfMonth());
                }
                catch (Exception)
                {
                    continue;
                }

                // "WinterDesignDay" is the only EnergyPlus day type that means heating sizing;
                // everything else (SummerDesignDay and the day-of-week types) sizes cooling.
                if (string.Equals(openStudioDesignDay.dayType(), "WinterDesignDay", StringComparison.OrdinalIgnoreCase))
                {
                    heatingDesignDays.Add(designDay);
                }
                else
                {
                    coolingDesignDays.Add(designDay);
                }
            }

            if (heatingDesignDays.Count > 0)
            {
                analyticalModel.SetValue(AnalyticalModelParameter.HeatingDesignDays, heatingDesignDays);
            }

            if (coolingDesignDays.Count > 0)
            {
                analyticalModel.SetValue(AnalyticalModelParameter.CoolingDesignDays, coolingDesignDays);
            }

            openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.WeatherLimitation, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("{0} design day(s) imported by name and date only: an EnergyPlus design day is parametric (maximum dry bulb, daily range, humidity indicating type) while a SAM DesignDay is an hourly weather day, so no hourly profile was reconstructed", designDayVector.Count), (string)null);
        }

        /// <summary>
        /// Reports simulation settings that have no SAM equivalent. SAM's analytical model
        /// carries no run period, timestep or shadow-calculation configuration — those are
        /// conversion options on the way out — so they are named rather than imported.
        /// </summary>
        private static void ToSAM_SimulationSettings(OpenStudioImportContext openStudioImportContext)
        {
            List<string> settings = new List<string>();

            try
            {
                global::OpenStudio.RunPeriod runPeriod = openStudioImportContext.Source.getRunPeriod();
                if (runPeriod != null)
                {
                    settings.Add(string.Format(CultureInfo.InvariantCulture, "run period {0}/{1} - {2}/{3}", runPeriod.getBeginDayOfMonth(), runPeriod.getBeginMonth(), runPeriod.getEndDayOfMonth(), runPeriod.getEndMonth()));
                }
            }
            catch (Exception)
            {
                // best effort — settings are descriptive only
            }

            try
            {
                global::OpenStudio.Timestep timestep = openStudioImportContext.Source.getTimestep();
                if (timestep != null)
                {
                    settings.Add(string.Format(CultureInfo.InvariantCulture, "{0} timesteps per hour", timestep.numberOfTimestepsPerHour()));
                }
            }
            catch (Exception)
            {
                // best effort
            }

            if (settings.Count == 0)
            {
                return;
            }

            openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.SimulationSettingUnsupported, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format("Simulation settings ({0}) have no SAM AnalyticalModel equivalent and were not imported; they are set again by the conversion options on export", string.Join(", ", settings)), (string)null);
        }

        /// <summary>
        /// Rebuilds an analytical model with a preserved model-level Guid through a JSON round
        /// trip, validating that the reconstruction kept the content. Returns null on any
        /// failure, so the caller keeps the original valid instance.
        /// </summary>
        private static AnalyticalModel TryRestoreGuid(AnalyticalModel analyticalModel, Guid guid)
        {
            if (analyticalModel == null || guid == Guid.Empty)
            {
                return null;
            }

            try
            {
                System.Text.Json.Nodes.JsonObject jsonObject = analyticalModel.ToJsonObject();
                if (jsonObject == null)
                {
                    return null;
                }

                jsonObject["Guid"] = guid.ToString();

                AnalyticalModel result = new AnalyticalModel(jsonObject);
                if (result == null || result.Guid != guid)
                {
                    return null;
                }

                AdjacencyCluster original = analyticalModel.AdjacencyCluster;
                AdjacencyCluster restored = result.AdjacencyCluster;
                if (restored == null)
                {
                    return null;
                }

                if ((original?.GetPanels()?.Count ?? 0) != (restored.GetPanels()?.Count ?? 0)
                    || (original?.GetSpaces()?.Count ?? 0) != (restored.GetSpaces()?.Count ?? 0))
                {
                    return null;
                }

                return result;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
