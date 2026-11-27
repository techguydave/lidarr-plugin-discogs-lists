using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Lidarr.Plugin.DiscogsLists.Mapping
{
    internal static class TitleSimilarity
    {
        private static readonly HashSet<string> Ignored = new () { "the", "a", "an", "and", "&", "de", "la", "le" };

        // 1 for the same words in any order, 0 for none in common.
        public static double Score(string left, string right)
        {
            var a = Words(left);
            var b = Words(right);

            if (a.Count == 0 || b.Count == 0)
            {
                return 0;
            }

            return (double)a.Intersect(b).Count() / a.Union(b).Count();
        }

        internal static HashSet<string> Words(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new HashSet<string>();
            }

            var builder = new StringBuilder();

            foreach (var c in text.Normalize(NormalizationForm.FormD).ToLowerInvariant())
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);

                if (category == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
            }

            var words = builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var meaningful = words.Where(w => !Ignored.Contains(w)).ToHashSet();

            // Names made only of common words, such as "The The", are compared as they are.
            return meaningful.Count > 0 ? meaningful : words;
        }
    }
}
