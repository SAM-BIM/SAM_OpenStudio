// SPDX-License-Identifier: LGPL-3.0-only

using System.Text;

namespace SAM.Core.OpenStudio
{
    public static partial class Query
    {
        /// <summary>
        /// Sanitizes a name for use in OpenStudio/EnergyPlus object names: characters that break
        /// IDF parsing (comma, semicolon, exclamation mark), whitespace and control characters are
        /// replaced with underscores, runs of underscores are collapsed, and leading/trailing
        /// underscores are trimmed. Deterministic: equal inputs always produce equal outputs.
        /// </summary>
        /// <param name="name">Name to sanitize; null returns null.</param>
        /// <returns>Sanitized name (possibly empty), or null when input is null.</returns>
        public static string SanitizeName(string name)
        {
            if (name == null)
            {
                return null;
            }

            StringBuilder stringBuilder = new StringBuilder(name.Length);
            bool previousUnderscore = false;
            foreach (char @char in name)
            {
                bool invalid = @char == ',' || @char == ';' || @char == '!' || char.IsWhiteSpace(@char) || char.IsControl(@char) || @char == '_';
                if (invalid)
                {
                    if (!previousUnderscore)
                    {
                        stringBuilder.Append('_');
                        previousUnderscore = true;
                    }

                    continue;
                }

                stringBuilder.Append(@char);
                previousUnderscore = false;
            }

            return stringBuilder.ToString().Trim('_');
        }
    }
}
