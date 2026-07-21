// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Geometry.Spatial;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Convert
    {
        /// <summary>
        /// Converts an OpenStudio Space into a SAM Space.
        /// <para>
        /// One OpenStudio space becomes exactly one SAM space, even when several share a
        /// ThermalZone. Collapsing them would merge distinct geometry into a single shell and
        /// silently lose rooms; the shared zone is instead recorded on each space
        /// (<see cref="OpenStudioSourceParameter.ThermalZoneName"/>) so the controls pass can
        /// apply the zone's thermostat to all of them.
        /// </para>
        /// <para>
        /// Area, volume and the internal point are derived from the space's own closed shell,
        /// built from the surfaces the caller already converted, rather than from
        /// <c>Space.floorArea()</c>/<c>volume()</c>: those are autocalculated by OpenStudio from
        /// the same surfaces but include multipliers and autocalculation fallbacks that would not
        /// match the SAM geometry actually imported. The OpenStudio values are used only when the
        /// shell cannot be built.
        /// </para>
        /// </summary>
        /// <param name="space">OpenStudio space; null returns null.</param>
        /// <param name="face3Ds">Faces of the converted panels bounding this space; may be null.</param>
        /// <param name="openStudioImportContext">Import context.</param>
        /// <returns>The created SAM space, or null.</returns>
        public static Space ToSAM(this global::OpenStudio.Space space, IEnumerable<Face3D> face3Ds, OpenStudioImportContext openStudioImportContext)
        {
            if (space == null || openStudioImportContext == null)
            {
                return null;
            }

            string label = OpenStudioImportContext.OpenStudioObjectLabel(space);

            string name;
            if (!Core.OpenStudio.Query.TryGetSAMName(space, out name) || string.IsNullOrWhiteSpace(name))
            {
                name = space.nameString();
            }

            Point3D location = null;
            double area = double.NaN;
            double volume = double.NaN;

            List<Face3D> faces = face3Ds == null ? null : new List<Face3D>(face3Ds);
            if (faces != null && faces.Count >= 4)
            {
                try
                {
                    Shell shell = new Shell(faces);
                    if (shell != null)
                    {
                        location = shell.InternalPoint3D(openStudioImportContext.Options.DistanceTolerance);
                        area = Geometry.Spatial.Query.Area(shell, 0.1);
                        volume = Geometry.Spatial.Query.Volume(shell);
                    }
                }
                catch (Exception exception)
                {
                    openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ZoneIncomplete, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("The space shell could not be built from its surfaces ({0}); area, volume and the internal point fall back to the OpenStudio values", exception.GetType().Name), label);
                }
            }

            if (location == null)
            {
                // No usable shell: the space still imports (its surfaces are real), but nothing
                // that depends on a closed volume can be derived from SAM geometry.
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ZoneIncomplete, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("The space has no closed shell ({0} bounding face(s)); its SAM location is not set and area/volume come from OpenStudio where available", faces?.Count ?? 0), label);
            }

            if (double.IsNaN(area))
            {
                // floorArea is exposed as a property by the SWIG wrapper (volume as a method).
                area = TryGetDouble(() => space.floorArea);
            }

            if (double.IsNaN(volume))
            {
                volume = TryGetDouble(() => space.volume());
            }

            Guid guid = openStudioImportContext.ResolveGuid(space, typeof(Space).Name);
            Space result = new Space(guid, name, location);

            if (!double.IsNaN(area) && area > 0)
            {
                result.SetValue(SpaceParameter.Area, area);
            }

            if (!double.IsNaN(volume) && volume > 0)
            {
                result.SetValue(SpaceParameter.Volume, volume);
            }

            global::OpenStudio.OptionalBuildingStory optionalBuildingStory = space.buildingStory();
            if (optionalBuildingStory != null && !optionalBuildingStory.isNull())
            {
                result.SetValue(SpaceParameter.LevelName, optionalBuildingStory.get().nameString());
            }

            global::OpenStudio.OptionalThermalZone optionalThermalZone = space.thermalZone();
            if (optionalThermalZone == null || optionalThermalZone.isNull())
            {
                openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ZoneIncomplete, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, "The space belongs to no thermal zone; no setpoints or conditioning state could be imported for it", label, result);
            }
            else
            {
                global::OpenStudio.ThermalZone thermalZone = optionalThermalZone.get();
                result.SetValue(OpenStudioSourceParameter.ThermalZoneName, thermalZone.nameString());

                global::OpenStudio.SpaceVector spaceVector = thermalZone.spaces();
                if (spaceVector != null && spaceVector.Count > 1 && openStudioImportContext.RegisterOnce("SAM-OSI-ZONE-001:" + thermalZone.nameString()))
                {
                    openStudioImportContext.AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.ZoneMultiSpace, Core.OpenStudio.OpenStudioDiagnosticSeverity.Information, string.Format("ThermalZone '{0}' contains {1} spaces; each becomes its own SAM space (geometry is never merged) and they share the zone's setpoints and conditioning state", thermalZone.nameString(), spaceVector.Count), label, result);
                }
            }

            Modify.SetOpenStudioSource(result, space);

            openStudioImportContext.RegisterCreated();
            return result;
        }

        /// <summary>
        /// Evaluates a native double accessor, returning NaN when it throws. Used for
        /// autocalculated OpenStudio quantities that can fail on an incomplete model.
        /// </summary>
        private static double TryGetDouble(Func<double> accessor)
        {
            try
            {
                return accessor();
            }
            catch (Exception)
            {
                return double.NaN;
            }
        }
    }
}
