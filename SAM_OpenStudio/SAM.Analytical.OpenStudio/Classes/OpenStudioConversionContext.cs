// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;

namespace SAM.Analytical.OpenStudio
{
    /// <summary>
    /// Shared state passed to every SAM → OpenStudio mapper: the source model, the target
    /// OpenStudio model, options, caches keyed by SAM Guid (or cache key), and the diagnostics
    /// list. A single context instance is used for one conversion; it prevents repeated
    /// conversion, duplicate OpenStudio objects, unstable naming and lost warnings.
    /// </summary>
    public sealed class OpenStudioConversionContext
    {
        /// <summary>Source SAM analytical model. May be null only in unit tests of the contracts.</summary>
        public AnalyticalModel Source { get; }

        /// <summary>Target OpenStudio model being populated. Never null.</summary>
        public global::OpenStudio.Model Target { get; }

        /// <summary>Conversion options; never null.</summary>
        public Core.OpenStudio.OpenStudioConversionOptions Options { get; }

        /// <summary>SAM Guid → OpenStudio object name traceability records.</summary>
        public Core.OpenStudio.OpenStudioObjectMap References { get; } = new Core.OpenStudio.OpenStudioObjectMap();

        /// <summary>SAM Guid → live OpenStudio object created for it.</summary>
        public IDictionary<Guid, global::OpenStudio.ModelObject> ModelObjectMap { get; } = new Dictionary<Guid, global::OpenStudio.ModelObject>();

        /// <summary>SAM Panel Guid → primary (first created) OpenStudio Surface for internal panel pairing.</summary>
        public IDictionary<Guid, global::OpenStudio.Surface> PrimarySurfaceMap { get; } = new Dictionary<Guid, global::OpenStudio.Surface>();

        /// <summary>Cache key (material Guid or Guid+direction) → OpenStudio material.</summary>
        public IDictionary<string, global::OpenStudio.Material> MaterialMap { get; } = new Dictionary<string, global::OpenStudio.Material>();

        /// <summary>Cache key (construction Guid + ":Forward"/":Reverse") → OpenStudio construction.</summary>
        public IDictionary<string, global::OpenStudio.Construction> ConstructionMap { get; } = new Dictionary<string, global::OpenStudio.Construction>();

        /// <summary>Cache key (profile Guid + ":" + ProfileType) → OpenStudio schedule cache. The same SAM profile reused under a different ProfileType produces a separate schedule with its own type limits and name.</summary>
        public IDictionary<string, global::OpenStudio.Schedule> ScheduleMap { get; } = new Dictionary<string, global::OpenStudio.Schedule>();

        /// <summary>Diagnostics accumulated during the conversion.</summary>
        public IList<Core.OpenStudio.OpenStudioDiagnostic> Diagnostics { get; } = new List<Core.OpenStudio.OpenStudioDiagnostic>();

        /// <summary>Translation statistics (source/created/skipped/unsupported counts); snapshotted onto the result.</summary>
        public Core.OpenStudio.OpenStudioConversionStatistics Statistics { get; } = new Core.OpenStudio.OpenStudioConversionStatistics();

        /// <summary>
        /// Day-of-week offset of 1 Jan of the run calendar with Monday = 0 … Sunday = 6.
        /// Day-composed (weekly) profiles are rotated by this offset so sub-profile 0 (Monday per
        /// the SAM_LadybugTools ScheduleRuleset convention) lands on the first real Monday.
        /// Default 0 (1 Jan treated as Monday) for weather-free conversions.
        /// </summary>
        public int FirstDayOfWeekOffset { get; set; }

        /// <summary>True when design days were imported into the target model (drives sizing-period enablement).</summary>
        public bool DesignDaysImported { get; set; }

        /// <summary>Creates a conversion context.</summary>
        /// <param name="analyticalModel">Source SAM analytical model (null only in contract tests).</param>
        /// <param name="model">Target OpenStudio model; required.</param>
        /// <param name="openStudioConversionOptions">Options; a default instance is used when null.</param>
        public OpenStudioConversionContext(AnalyticalModel analyticalModel, global::OpenStudio.Model model, Core.OpenStudio.OpenStudioConversionOptions openStudioConversionOptions = null)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            Source = analyticalModel;
            Target = model;
            Options = openStudioConversionOptions ?? new Core.OpenStudio.OpenStudioConversionOptions();
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

        /// <summary>Adds a diagnostic, deriving SAM Guid and type from the given SAM object.</summary>
        /// <param name="code">Stable diagnostic code, see <see cref="Core.OpenStudio.OpenStudioDiagnosticCodes"/>.</param>
        /// <param name="severity">Severity.</param>
        /// <param name="message">Human-readable description.</param>
        /// <param name="sAMObject">Related SAM object, when applicable.</param>
        /// <param name="openStudioObjectName">Related OpenStudio object name, when applicable.</param>
        public void AddDiagnostic(string code, Core.OpenStudio.OpenStudioDiagnosticSeverity severity, string message, Core.SAMObject sAMObject = null, string openStudioObjectName = null)
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

            if (code == Core.OpenStudio.OpenStudioDiagnosticCodes.InternalConditionUnsupportedParameter)
            {
                Statistics.UnsupportedObjects++;
            }
        }

        /// <summary>Records an explicitly skipped source object or load in the statistics.</summary>
        public void RegisterSkip()
        {
            Statistics.SkippedObjects++;
        }

        /// <summary>
        /// Registers the OpenStudio object created for a SAM object in both the traceability map
        /// and the live-object map. Returns false (and changes nothing) when either argument is
        /// null or the SAM Guid is already registered — duplicate conversion is a programming
        /// error surfaced by the caller.
        /// </summary>
        /// <param name="sAMObject">Source SAM object.</param>
        /// <param name="modelObject">OpenStudio object created for it.</param>
        public bool RegisterModelObject(Core.SAMObject sAMObject, global::OpenStudio.ModelObject modelObject)
        {
            if (sAMObject == null || modelObject == null)
            {
                return false;
            }

            bool result = References.TryAdd(new Core.OpenStudio.OpenStudioObjectReference(sAMObject.Guid, sAMObject.GetType().Name, modelObject.nameString()));
            if (result)
            {
                ModelObjectMap[sAMObject.Guid] = modelObject;
                Statistics.CreatedObjects++;
            }

            return result;
        }

        /// <summary>Gets the live OpenStudio object registered for a SAM Guid, typed.</summary>
        /// <typeparam name="T">Expected OpenStudio object type.</typeparam>
        /// <param name="samGuid">SAM object Guid.</param>
        /// <param name="modelObject">The registered object when present and of type T.</param>
        public bool TryGetModelObject<T>(Guid samGuid, out T modelObject) where T : global::OpenStudio.ModelObject
        {
            modelObject = null;
            global::OpenStudio.ModelObject value;
            if (!ModelObjectMap.TryGetValue(samGuid, out value))
            {
                return false;
            }

            modelObject = value as T;
            return modelObject != null;
        }
    }
}
