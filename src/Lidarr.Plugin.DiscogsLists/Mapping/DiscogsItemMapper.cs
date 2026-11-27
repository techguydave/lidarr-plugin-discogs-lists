using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Lidarr.Plugin.DiscogsLists.Api.Models;
using Lidarr.Plugin.DiscogsLists.Editions;
using NzbDrone.Core.Parser.Model;

namespace Lidarr.Plugin.DiscogsLists.Mapping
{
    // Discogs has no MusicBrainz IDs, so items carry names and Lidarr resolves them by search.
    public static class DiscogsItemMapper
    {
        private const string TitleSeparator = " - ";

        // Discogs disambiguates same-named artists as "Name (2)" and marks name variations with "*".
        private static readonly Regex Disambiguator = new (@"\s\([1-9]\d{0,2}\)(?=\*?(\s|,|$))", RegexOptions.Compiled);
        private static readonly Regex VariationMarker = new (@"(?<=\S)\*(?=\s|,|$)", RegexOptions.Compiled);

        private static readonly string[] PlaceholderArtists = { "Various", "Various Artists", "Unknown Artist", "No Artist" };

        private static readonly string[] NonMediaFormats = { "Box Set", "All Media" };

        public static DiscogsItem Map(DiscogsRelease release)
        {
            var information = release?.BasicInformation;
            var artist = CleanArtist(information?.Artists?.FirstOrDefault()?.Name);
            var item = Album(artist, information?.Title);

            if (item == null)
            {
                return null;
            }

            // Packaging such as "Box Set" is not a medium.
            var media = (information.Formats ?? new List<DiscogsFormat>())
                .Where(f => !string.IsNullOrWhiteSpace(f?.Name) && !NonMediaFormats.Contains(f.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            return new DiscogsItem(item)
            {
                ReleaseId = release.Id,
                MasterId = information.MasterId,
                Year = information.Year,
                Formats = media.Select(f => OwnedEdition.FormatFamily(f.Name)).Distinct().ToList(),
                MediumCount = media.Sum(f => int.TryParse(f.Quantity, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 1),
                Labels = (information.Labels ?? new List<DiscogsLabel>())
                    .Select(l => OwnedEdition.NormalizeLabel(l?.Name))
                    .Where(l => !string.IsNullOrEmpty(l))
                    .Distinct()
                    .ToList()
            };
        }

        // Returns null for entries that cannot be imported, such as labels.
        public static DiscogsItem Map(DiscogsListItem item)
        {
            var title = item?.DisplayTitle;

            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            if (string.Equals(item.Type, "artist", StringComparison.OrdinalIgnoreCase))
            {
                var artist = CleanArtist(title);

                return IsUsableArtist(artist) ? new DiscogsItem(new ImportListItemInfo { Artist = artist }) { DiscogsArtistId = item.Id } : null;
            }

            if (!string.Equals(item.Type, "release", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(item.Type, "master", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var separator = title.IndexOf(TitleSeparator, StringComparison.Ordinal);

            if (separator < 0)
            {
                return null;
            }

            var album = Album(CleanArtist(title[..separator]), title[(separator + TitleSeparator.Length)..]);
            var isMaster = string.Equals(item.Type, "master", StringComparison.OrdinalIgnoreCase);

            return album == null ? null : new DiscogsItem(album)
            {
                ReleaseId = isMaster ? null : item.Id,
                MasterId = isMaster ? item.Id : null
            };
        }

        internal static string CleanArtist(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var cleaned = Disambiguator.Replace(name.Trim(), string.Empty);

            return VariationMarker.Replace(cleaned, string.Empty).Trim();
        }

        private static bool IsUsableArtist(string artist) =>
            !string.IsNullOrWhiteSpace(artist) && !PlaceholderArtists.Contains(artist, StringComparer.OrdinalIgnoreCase);

        private static ImportListItemInfo Album(string artist, string album)
        {
            if (!IsUsableArtist(artist) || string.IsNullOrWhiteSpace(album))
            {
                return null;
            }

            return new ImportListItemInfo { Artist = artist, Album = album.Trim() };
        }
    }
}
