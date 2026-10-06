// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Core.OpenStudio
{
    public static partial class Create
    {
        /// <summary>
        /// Reads model from give path (*.osm). The OpenStudio VersionTranslator is used so OSM
        /// files written by older OpenStudio versions are upgraded on load instead of being
        /// rejected.
        /// </summary>
        /// <param name="path">OSM file path example: C:\MyModels\model.osm</param>
        /// <returns>Model</returns>
        public static global::OpenStudio.Model Model(string path)
        {
            string originalVersion;
            string failureReason;
            System.Collections.Generic.List<string> translationMessages;
            return Model(path, out originalVersion, out translationMessages, out failureReason);
        }

        /// <summary>
        /// Reads a model from the given path (*.osm) through the OpenStudio VersionTranslator and
        /// reports what happened. The plain <see cref="Model(string)"/> overload collapses every
        /// failure mode — missing file, unreadable path, unsupported version, corrupt content —
        /// into a bare null, which the reverse importer cannot turn into an actionable
        /// diagnostic; this overload keeps that information.
        /// </summary>
        /// <param name="path">OSM file path, for example C:\MyModels\model.osm.</param>
        /// <param name="originalVersion">OSM version recorded in the file before translation, when the translator reached it; null otherwise.</param>
        /// <param name="translationMessages">VersionTranslator errors and warnings, formatted one per entry; never null.</param>
        /// <param name="failureReason">Single-line description of why the load failed; null on success.</param>
        /// <returns>The loaded model, or null when it could not be loaded.</returns>
        public static global::OpenStudio.Model Model(string path, out string originalVersion, out System.Collections.Generic.List<string> translationMessages, out string failureReason)
        {
            originalVersion = null;
            translationMessages = new System.Collections.Generic.List<string>();
            failureReason = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                failureReason = "no path was supplied";
                return null;
            }

            if (!System.IO.File.Exists(path))
            {
                failureReason = string.Format("the file does not exist: {0}", path);
                return null;
            }

            global::OpenStudio.Path openStudioPath = global::OpenStudio.OpenStudioUtilitiesCore.toPath(path);
            if (openStudioPath == null)
            {
                failureReason = string.Format("the path could not be converted to an OpenStudio path: {0}", path);
                return null;
            }

            global::OpenStudio.VersionTranslator versionTranslator = new global::OpenStudio.VersionTranslator();
            global::OpenStudio.OptionalModel optionalModel;
            try
            {
                optionalModel = versionTranslator.loadModel(openStudioPath);
            }
            catch (System.Exception exception)
            {
                failureReason = string.Format("the VersionTranslator threw {0}: {1}", exception.GetType().Name, exception.Message);
                return null;
            }

            CollectTranslatorMessages(versionTranslator, translationMessages, out originalVersion);

            if (optionalModel == null || optionalModel.isNull())
            {
                failureReason = translationMessages.Count == 0
                    ? "the VersionTranslator returned no model (unsupported version or corrupt file)"
                    : string.Format("the VersionTranslator returned no model: {0}", string.Join("; ", translationMessages));
                return null;
            }

            return optionalModel.get();
        }

        /// <summary>
        /// Collects the translator's original-version string and its error/warning messages.
        /// Best effort throughout: a diagnostic accessor that throws must never turn a
        /// successfully translated model into a load failure.
        /// </summary>
        private static void CollectTranslatorMessages(global::OpenStudio.VersionTranslator versionTranslator, System.Collections.Generic.List<string> translationMessages, out string originalVersion)
        {
            originalVersion = null;

            try
            {
                global::OpenStudio.VersionString versionString = versionTranslator.originalVersion();
                if (versionString != null)
                {
                    originalVersion = versionString.str();
                }
            }
            catch (System.Exception)
            {
                // best effort — the version is reported when available, never required
            }

            AppendLogMessages(translationMessages, () => versionTranslator.errors(), "error");
            AppendLogMessages(translationMessages, () => versionTranslator.warnings(), "warning");
        }

        private static void AppendLogMessages(System.Collections.Generic.List<string> translationMessages, System.Func<global::OpenStudio.LogMessageVector> accessor, string kind)
        {
            global::OpenStudio.LogMessageVector logMessageVector;
            try
            {
                logMessageVector = accessor();
            }
            catch (System.Exception)
            {
                return;
            }

            if (logMessageVector == null)
            {
                return;
            }

            foreach (global::OpenStudio.LogMessage logMessage in logMessageVector)
            {
                if (logMessage == null)
                {
                    continue;
                }

                translationMessages.Add(string.Format("{0}: {1}", kind, logMessage.logMessage()));
            }
        }
    }
}