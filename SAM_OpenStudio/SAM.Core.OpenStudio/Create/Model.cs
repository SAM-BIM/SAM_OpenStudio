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
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                return null;

            global::OpenStudio.Path openStudioPath = global::OpenStudio.OpenStudioUtilitiesCore.toPath(path);
            if (openStudioPath == null)
                return null;

            global::OpenStudio.VersionTranslator versionTranslator = new global::OpenStudio.VersionTranslator();
            global::OpenStudio.OptionalModel optionalModel = versionTranslator.loadModel(openStudioPath);
            if(optionalModel == null || optionalModel.isNull())
            {
                return null;
            }

            return optionalModel.get();
        }
    }
}