using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Music;

namespace Lidarr.Plugin.DiscogsLists.Editions
{
    // What is known about the pressing on Discogs, used to pick the same edition among an album's releases in Lidarr.
    public record OwnedEdition(string MusicBrainzReleaseId,
                               IReadOnlyCollection<string> Formats,
                               int MediumCount,
                               IReadOnlyCollection<string> Labels,
                               int Year)
    {
        internal const int MinimumScore = 2;

        public bool IsKnown => MusicBrainzReleaseId != null || Formats.Any();

        // Returns the release that matches, or null to leave Lidarr's choice alone.
        public AlbumRelease Choose(IReadOnlyCollection<AlbumRelease> releases)
        {
            if (MusicBrainzReleaseId != null)
            {
                var exact = releases.FirstOrDefault(r => r.ForeignReleaseId == MusicBrainzReleaseId ||
                                                         (r.OldForeignReleaseIds?.Contains(MusicBrainzReleaseId) ?? false));

                if (exact != null)
                {
                    return exact;
                }
            }

            var scored = releases.Select(r => (Release: r, Score: Score(r))).Where(s => s.Score >= MinimumScore).ToList();

            if (!scored.Any())
            {
                return null;
            }

            var best = scored.Max(s => s.Score);
            var top = scored.Where(s => s.Score == best).Select(s => s.Release).ToList();

            // Between equally close editions, Lidarr's own choice stands.
            return top.FirstOrDefault(r => r.Monitored) ?? top.OrderByDescending(r => r.TrackCount).First();
        }

        // Discogs and MusicBrainz name formats differently, so both are reduced to a family such as "vinyl" or "cd".
        public static string FormatFamily(string format)
        {
            if (string.IsNullOrWhiteSpace(format))
            {
                return null;
            }

            var f = format.Trim().ToLowerInvariant();

            if (f.Contains("vinyl") || f == "lathe cut" || f == "acetate" || f == "flexi-disc" || f == "shellac")
            {
                return "vinyl";
            }

            if (f.Contains("sacd"))
            {
                return "sacd";
            }

            if (f.Contains("cassette"))
            {
                return "cassette";
            }

            if (f.Contains("blu-ray"))
            {
                return "blu-ray";
            }

            if (f.Contains("dvd"))
            {
                return "dvd";
            }

            if (f == "file" || f.Contains("digital"))
            {
                return "digital";
            }

            if (f.Contains("cd"))
            {
                return "cd";
            }

            return f;
        }

        // Comparing labels ignores case, punctuation and Discogs's "(2)" disambiguators.
        public static string NormalizeLabel(string label)
        {
            var cleaned = Mapping.DiscogsItemMapper.CleanArtist(label);

            return cleaned == null ? null : new string(cleaned.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }

        private int Score(AlbumRelease release)
        {
            var media = release.Media ?? new List<Medium>();
            var families = media.Select(m => FormatFamily(m.Format)).ToList();

            // The format must match: a vinyl pressing is never swapped for a CD edition.
            if (!Formats.Any() || !families.Any() || families.Any(f => f == null) || !families.ToHashSet().SetEquals(Formats))
            {
                return 0;
            }

            var score = 2;

            if (MediumCount > 0 && media.Count == MediumCount)
            {
                score += 2;
            }

            if (Labels.Any() && (release.Label ?? new List<string>()).Select(NormalizeLabel).Any(l => l != null && Labels.Contains(l)))
            {
                score += 2;
            }

            if (Year > 0 && release.ReleaseDate.HasValue)
            {
                var gap = Math.Abs(release.ReleaseDate.Value.Year - Year);
                score += gap == 0 ? 2 : gap == 1 ? 1 : 0;
            }

            return score;
        }
    }
}
