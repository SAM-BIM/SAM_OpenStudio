// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Resolves an annual EPW path from the WeatherData embedded in the AnalyticalModel
        /// (AnalyticalModelParameter.WeatherData). SAM WeatherData does not retain its source
        /// EPW path, but it can be exported through the existing SAM.Weather API
        /// (Weather.Convert.ToEPW) when it carries hourly weather years; the export is written
        /// to a deterministic temp file (content-hash name, reused across runs). Returns null
        /// when no WeatherData is embedded or it holds no weather years — the caller then
        /// applies the documented metadata-only fallback (location, ground temperatures).
        /// </summary>
        /// <param name="analyticalModel">Source SAM analytical model.</param>
        /// <returns>Path of the exported EPW file, or null when the embedded WeatherData cannot provide an annual EPW.</returns>
        public static string EmbeddedAnnualWeatherPath(this AnalyticalModel analyticalModel)
        {
            if (analyticalModel == null)
            {
                return null;
            }

            Weather.WeatherData weatherData = null;
            if (!analyticalModel.TryGetValue(AnalyticalModelParameter.WeatherData, out weatherData) || weatherData == null)
            {
                return null;
            }

            System.Collections.Generic.IEnumerable<int> years = weatherData.Years;
            if (years == null || !years.Any())
            {
                return null;
            }

            string epw;
            try
            {
                epw = Weather.Convert.ToEPW(weatherData);
            }
            catch (System.Exception)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(epw))
            {
                return null;
            }

            try
            {
                string directory = Path.Combine(Path.GetTempPath(), "SAM_OpenStudio");
                Directory.CreateDirectory(directory);

                string path = Path.Combine(directory, "SAM_WeatherData_" + Hash(epw) + ".epw");
                if (!File.Exists(path) || File.ReadAllText(path) != epw)
                {
                    File.WriteAllText(path, epw);
                }

                return path;
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private static string Hash(string value)
        {
            using (SHA256 sHA256 = SHA256.Create())
            {
                byte[] bytes = sHA256.ComputeHash(Encoding.UTF8.GetBytes(value));
                StringBuilder stringBuilder = new StringBuilder();
                for (int i = 0; i < 8; i++)
                {
                    stringBuilder.Append(bytes[i].ToString("x2"));
                }

                return stringBuilder.ToString();
            }
        }
    }
}
