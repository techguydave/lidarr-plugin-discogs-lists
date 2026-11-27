using System;
using System.Linq;
using System.Text.RegularExpressions;
using Lidarr.Plugin.DiscogsLists.Api.Models;
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

        public static ImportListItemInfo Map(DiscogsRelease release)
        {
            var information = release?.BasicInformation;
            var artist = CleanArtist(information?.Artists?.FirstOrDefault()?.Name);

            return Album(artist, information?.Title);
        }

        // Returns null for entries that cannot be imported, such as labels.
        public static ImportListItemInfo Map(DiscogsListItem item)
        {
            var title = item?.DisplayTitle;

            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            if (string.Equals(item.Type, "artist", StringComparison.OrdinalIgnoreCase))
            {
                var artist = CleanArtist(title);

                return IsUsableArtist(artist) ? new ImportListItemInfo { Artist = artist } : null;
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

            return Album(CleanArtist(title[..separator]), title[(separator + TitleSeparator.Length)..]);
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
