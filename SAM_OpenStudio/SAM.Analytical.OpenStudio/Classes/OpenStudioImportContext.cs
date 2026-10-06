// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;

namespace SAM.Analytical.OpenStudio
{
    /// <summary>
    /// Shared state passed to every OpenStudio → SAM mapper: the source OpenStudio model, the
    /// import options, the caches that keep one SAM object per OpenStudio source object, the
    /// identity ledger that guarantees restored SAM Guids stay unique, and the diagnostics list.
    /// One instance per import.
    /// <para>
    /// The mirror image of <see cref="OpenStudioConversionContext"/>, and deliberately a separate
    /// class: the forward context is keyed by SAM Guid and owns a target OpenStudio model, the
    /// reverse one is keyed by OpenStudio object name and owns nothing native.
    /// </para>
    /// </summary>
    public sealed class OpenStudioImportContext
    {
        /// <summary>Source OpenStudio model. Never null. NOT owned — the caller disposes it.</summary>
        public global::OpenStudio.Model Source { get; }

        /// <summary>Import options; never null.</summary>
        public Core.OpenStudio.OpenStudioImportOptions Options { get; }

        /// <summary>
        /// Directory of the source OSM, when the import came from a path. Used to resolve a
        /// weather file recorded relative to the model. Null for an in-memory model, where no
        /// source directory exists.
        /// </summary>
        public string SourceDirectory { get; set; }

        /// <summary>Diagnostics accumulated during the import.</summary>
        public IList<Core.OpenStudio.OpenStudioDiagnostic> Diagnostics { get; } = new List<Core.OpenStudio.OpenStudioDiagnostic>();

        /// <summary>Import statistics (source/created/skipped/unsupported counts and severity tallies).</summary>
        public Core.OpenStudio.OpenStudioConversionStatistics Statistics { get; } = new Core.OpenStudio.OpenStudioConversionStatistics();

        /// <summary>OpenStudio material name → SAM material created for it.</summary>
        public IDictionary<string, Core.IMaterial> MaterialMap { get; } = new Dictionary<string, Core.IMaterial>();

        /// <summary>OpenStudio construction name → SAM opaque Construction created for it.</summary>
        public IDictionary<string, Construction> ConstructionMap { get; } = new Dictionary<string, Construction>();

        /// <summary>OpenStudio construction name → SAM ApertureConstruction created for it.</summary>
        public IDictionary<string, ApertureConstruction> ApertureConstructionMap { get; } = new Dictionary<string, ApertureConstruction>();

        /// <summary>OpenStudio schedule name → SAM Profile created for it.</summary>
        public IDictionary<string, Profile> ProfileMap { get; } = new Dictionary<string, Profile>();

        /// <summary>OpenStudio SpaceType name → SAM InternalCondition created for it.</summary>
        public IDictionary<string, InternalCondition> InternalConditionMap { get; } = new Dictionary<string, InternalCondition>();

        /// <summary>OpenStudio Surface name → SAM Panel created for it (both sides of a paired interzone surface map to the same panel).</summary>
        public IDictionary<string, Panel> PanelMap { get; } = new Dictionary<string, Panel>();

        /// <summary>OpenStudio Space name → SAM Space created for it.</summary>
        public IDictionary<string, Space> SpaceMap { get; } = new Dictionary<string, Space>();

        /// <summary>Keys already seen through <see cref="RegisterOnce"/>.</summary>
        private readonly HashSet<string> onceKeys = new HashSet<string>();

        /// <summary>SAM Guids already issued or restored in this import; guards identity collisions.</summary>
        private readonly HashSet<Guid> usedGuids = new HashSet<Guid>();

        /// <summary>Creates an import context.</summary>
        /// <param name="model">Source OpenStudio model; required.</param>
        /// <param name="openStudioImportOptions">Options; a default instance is used when null.</param>
        public OpenStudioImportContext(global::OpenStudio.Model model, Core.OpenStudio.OpenStudioImportOptions openStudioImportOptions = null)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            Source = model;
            Options = openStudioImportOptions ?? new Core.OpenStudio.OpenStudioImportOptions();
        }

        /// <summary>True when at least one diagnostic has Error severity.</summary>
        public bool HasErrors
        {
            get
            {
                foreach (Core.OpenStudio.OpenStudioDiagnostic openStudioDiagnostic in Diagnostics)
                {
                    if (openStudioDiagnostic != null && openStudioDiagnostic.Severity == Core.OpenStudio.OpenStudioDiagnosticSeverity.Error)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Adds a diagnostic naming the OpenStudio source object it refers to.
        /// </summary>
        /// <param name="code">Stable code, see <see cref="Core.OpenStudio.OpenStudioImportDiagnosticCodes"/>.</param>
        /// <param name="severity">Severity.</param>
        /// <param name="message">Human-readable description.</param>
        /// <param name="modelObject">OpenStudio source object, when applicable; its name and handle are recorded.</param>
        /// <param name="sAMObject">SAM object produced for it, when one already exists.</param>
        public void AddDiagnostic(string code, Core.OpenStudio.OpenStudioDiagnosticSeverity severity, string message, global::OpenStudio.ModelObject modelObject = null, Core.SAMObject sAMObject = null)
        {
            AddDiagnostic(code, severity, message, OpenStudioObjectLabel(modelObject), sAMObject);
        }

        /// <summary>
        /// Adds a diagnostic naming an OpenStudio source object by label (used where no live
        /// ModelObject is at hand, for example while parsing an OSW).
        /// </summary>
        /// <param name="code">Stable code, see <see cref="Core.OpenStudio.OpenStudioImportDiagnosticCodes"/>.</param>
        /// <param name="severity">Severity.</param>
        /// <param name="message">Human-readable description.</param>
        /// <param name="openStudioObjectName">Source object label, when applicable.</param>
        /// <param name="sAMObject">SAM object produced for it, when one already exists.</param>
        public void AddDiagnostic(string code, Core.OpenStudio.OpenStudioDiagnosticSeverity severity, string message, string openStudioObjectName, Core.SAMObject sAMObject = null)
        {
            // Lock: an OSW import reports from the worker thread running the CLI while the
            // caller may be reading the list.
            lock (Diagnostics)
            {
                Diagnostics.Add(new Core.OpenStudio.OpenStudioDiagnostic(code, severity, message, sAMObject?.Guid, sAMObject?.GetType().Name, openStudioObjectName));

                switch (severity)
                {
                    case Core.OpenStudio.OpenStudioDiagnosticSeverity.Information:
                        Statistics.InformationCount++;
                        break;

                    case Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning:
                        Statistics.WarningCount++;
                        break;

                    case Core.OpenStudio.OpenStudioDiagnosticSeverity.Error:
                        Statistics.ErrorCount++;
                        break;
                }

                if (IsUnsupportedCode(code))
                {
                    Statistics.UnsupportedObjects++;
                }
            }
        }

        private static bool IsUnsupportedCode(string code)
        {
            switch (code)
            {
                case Core.OpenStudio.OpenStudioImportDiagnosticCodes.BoundaryConditionUnsupported:
                case Core.OpenStudio.OpenStudioImportDiagnosticCodes.SubSurfaceUnsupported:
                case Core.OpenStudio.OpenStudioImportDiagnosticCodes.MaterialUnsupported:
                case Core.OpenStudio.OpenStudioImportDiagnosticCodes.ConstructionUnsupported:
                case Core.OpenStudio.OpenStudioImportDiagnosticCodes.ScheduleUnsupported:
                case Core.OpenStudio.OpenStudioImportDiagnosticCodes.LoadUnsupported:
                case Core.OpenStudio.OpenStudioImportDiagnosticCodes.HvacUnsupported:
                case Core.OpenStudio.OpenStudioImportDiagnosticCodes.SimulationSettingUnsupported:
                    return true;
            }

            return false;
        }

        /// <summary>
        /// "&lt;name&gt; [&lt;handle&gt;]" for an OpenStudio object — the pair the audit requires
        /// on every geometry diagnostic, because names are not unique across object types while
        /// handles are.
        /// </summary>
        public static string OpenStudioObjectLabel(global::OpenStudio.ModelObject modelObject)
        {
            if (modelObject == null)
            {
                return null;
            }

            string name;
            try
            {
                name = modelObject.nameString();
            }
            catch (Exception)
            {
                name = modelObject.GetType().Name;
            }

            string handle;
            try
            {
                // __str__ is the SWIG string conversion; UUID does not override ToString.
                handle = modelObject.handle()?.__str__();
            }
            catch (Exception)
            {
                handle = null;
            }

            return string.IsNullOrWhiteSpace(handle) ? name : string.Format("{0} [{1}]", name, handle);
        }

        /// <summary>
        /// True the first time the given key is seen in this import. Converters use it to emit a
        /// per-source-object diagnostic exactly once when the same OpenStudio object is reached
        /// through several paths (both sides of an interzone pair, a construction shared by many
        /// surfaces).
        /// </summary>
        /// <param name="key">Stable key, conventionally "code:topic:name"; null returns false.</param>
        public bool RegisterOnce(string key)
        {
            if (key == null)
            {
                return false;
            }

            lock (onceKeys)
            {
                return onceKeys.Add(key);
            }
        }

        /// <summary>Records an explicitly skipped source object in the statistics.</summary>
        public void RegisterSkip()
        {
            Statistics.SkippedObjects++;
        }

        /// <summary>Records a created SAM object in the statistics.</summary>
        public void RegisterCreated()
        {
            Statistics.CreatedObjects++;
        }

        /// <summary>
        /// Resolves the SAM Guid to use for the SAM object being created from
        /// <paramref name="modelObject"/>.
        /// <para>
        /// The full Guid stamped in <c>AdditionalProperties</c> is restored when
        /// <see cref="Core.OpenStudio.OpenStudioImportOptions.RestoreSAMIdentity"/> is on, the
        /// value parses, and it has not already been claimed by another object in this import.
        /// Otherwise a fresh Guid is issued. A stamped-but-unusable Guid — malformed, empty, or a
        /// duplicate — raises SAM-OSI-ID-001 rather than silently producing two SAM objects that
        /// claim the same identity. A third-party OSM with no metadata takes the fresh-Guid path
        /// with no diagnostic, which is the normal case.
        /// </para>
        /// <para>
        /// The 8-character name suffix is never consulted here: it is 32 bits of a 128-bit Guid
        /// and is only ever used for matching and diagnostics.
        /// </para>
        /// </summary>
        /// <param name="modelObject">OpenStudio source object; null yields a fresh Guid.</param>
        /// <param name="expectedType">SAM type name the identity must belong to; ignored when null.</param>
        /// <returns>The Guid to assign to the SAM object. Never <see cref="Guid.Empty"/>.</returns>
        public Guid ResolveGuid(global::OpenStudio.ModelObject modelObject, string expectedType = null)
        {
            if (modelObject == null || !Options.RestoreSAMIdentity)
            {
                return NewGuid();
            }

            Guid guid;
            if (!Core.OpenStudio.Query.TryGetSAMGuid(modelObject, out guid))
            {
                // Distinguish "no metadata at all" (normal, silent) from "metadata present but
                // unusable" (reported): only the latter had an intent to preserve identity.
                string raw;
                if (Core.OpenStudio.Query.TryGetSAMFeature(modelObject, Core.OpenStudio.OpenStudioIdentityKeys.Guid, out raw))
                {
                    AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.IdentityCollision, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("SAM identity metadata '{0}' is not a valid Guid; a new SAM Guid was issued", raw), modelObject);
                }

                return NewGuid();
            }

            if (expectedType != null)
            {
                string type;
                if (Core.OpenStudio.Query.TryGetSAMType(modelObject, out type) && !string.Equals(type, expectedType, StringComparison.Ordinal))
                {
                    AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.IdentityCollision, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("SAM identity metadata declares type '{0}' but a {1} is being imported; a new SAM Guid was issued", type, expectedType), modelObject);
                    return NewGuid();
                }
            }

            lock (usedGuids)
            {
                if (usedGuids.Add(guid))
                {
                    return guid;
                }
            }

            AddDiagnostic(Core.OpenStudio.OpenStudioImportDiagnosticCodes.IdentityCollision, Core.OpenStudio.OpenStudioDiagnosticSeverity.Warning, string.Format("SAM Guid {0} is claimed by more than one OpenStudio object; a new SAM Guid was issued for this one", guid), modelObject);
            return NewGuid();
        }

        /// <summary>Issues a fresh SAM Guid and records it in the identity ledger.</summary>
        private Guid NewGuid()
        {
            lock (usedGuids)
            {
                Guid guid;
                do
                {
                    guid = Guid.NewGuid();
                }
                while (!usedGuids.Add(guid));

                return guid;
            }
        }
    }
}
