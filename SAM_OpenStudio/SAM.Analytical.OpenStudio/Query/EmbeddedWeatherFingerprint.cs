// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace SAM.Analytical.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Deterministic fingerprint of the weather data embedded in an AnalyticalModel
        /// (AnalyticalModelParameter.WeatherData / HeatingDesignDays / CoolingDesignDays),
        /// computed from the SAM JSON of those parameter values with volatile identity fields
        /// (Guid) stripped, so re-imported identical content yields the same fingerprint. Two
        /// models sharing a Guid but carrying different embedded weather or design-day content
        /// produce different fingerprints — asynchronous callers (Grasshopper) must key on
        /// this, never on the model Guid alone. Returns an empty string when nothing is
        /// embedded; null when the model is null.
        /// </summary>
        /// <param name="analyticalModel">Source SAM analytical model.</param>
        /// <returns>16-hex-character content fingerprint, empty when nothing is embedded, or null.</returns>
        public static string EmbeddedWeatherFingerprint(this AnalyticalModel analyticalModel)
        {
            if (analyticalModel == null)
            {
                return null;
            }

            StringBuilder stringBuilder = new StringBuilder();

            Weather.WeatherData weatherData = null;
            if (analyticalModel.TryGetValue(AnalyticalModelParameter.WeatherData, out weatherData) && weatherData != null)
            {
                stringBuilder.Append(Core.Convert.ToString(weatherData, Core.Formatting.None));
            }

            stringBuilder.Append('|');

            Core.SAMCollection<DesignDay> heatingDesignDays = null;
            if (analyticalModel.TryGetValue(AnalyticalModelParameter.HeatingDesignDays, out heatingDesignDays) && heatingDesignDays != null)
            {
                stringBuilder.Append(Core.Convert.ToString((Core.IJSAMObject)heatingDesignDays, Core.Formatting.None));
            }

            stringBuilder.Append('|');

            Core.SAMCollection<DesignDay> coolingDesignDays = null;
            if (analyticalModel.TryGetValue(AnalyticalModelParameter.CoolingDesignDays, out coolingDesignDays) && coolingDesignDays != null)
            {
                stringBuilder.Append(Core.Convert.ToString((Core.IJSAMObject)coolingDesignDays, Core.Formatting.None));
            }

            string content = Canonical(stringBuilder.ToString());
            if (content == "||")
            {
                return string.Empty;
            }

            using (SHA256 sHA256 = SHA256.Create())
            {
                byte[] bytes = sHA256.ComputeHash(Encoding.UTF8.GetBytes(content));
                StringBuilder result = new StringBuilder();
                for (int i = 0; i < 8; i++)
                {
                    result.Append(bytes[i].ToString("x2"));
                }

                return result.ToString();
            }
        }

        /// <summary>Removes volatile identity fields (Guid) so identical content hashes identically.</summary>
        private static string Canonical(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            StringBuilder result = new StringBuilder();
            string[] segments = value.Split('|');
            for (int i = 0; i < segments.Length; i++)
            {
                if (i > 0)
                {
                    result.Append('|');
                }

                if (string.IsNullOrWhiteSpace(segments[i]))
                {
                    continue;
                }

                JsonNode jsonNode = null;
                try
                {
                    jsonNode = JsonNode.Parse(segments[i]);
                    RemoveGuids(jsonNode);
                }
                catch (System.Exception)
                {
                    jsonNode = null;
                }

                result.Append(jsonNode == null ? segments[i] : jsonNode.ToJsonString());
            }

            return result.ToString();
        }

        private static void RemoveGuids(JsonNode jsonNode)
        {
            if (jsonNode is JsonObject jsonObject)
            {
                jsonObject.Remove("Guid");
                foreach (System.Collections.Generic.KeyValuePair<string, JsonNode> keyValuePair in jsonObject)
                {
                    RemoveGuids(keyValuePair.Value);
                }
            }
            else if (jsonNode is JsonArray jsonArray)
            {
                foreach (JsonNode item in jsonArray)
                {
                    RemoveGuids(item);
                }
            }
        }
    }
}
