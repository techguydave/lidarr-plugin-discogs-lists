using System;
using System.Collections.Generic;
using Lidarr.Plugin.DiscogsLists.Editions;
using NzbDrone.Core.Parser.Model;

namespace Lidarr.Plugin.DiscogsLists.Mapping
{
    // An entry from Discogs, with the Discogs IDs used to find the matching MusicBrainz release group or artist.
    public class DiscogsItem
    {
        public DiscogsItem(ImportListItemInfo item)
        {
            Item = item;
        }

        public ImportListItemInfo Item { get; }

        public string Artist => Item.Artist;

        public string Album => Item.Album;

        public int? ReleaseId { get; init; }

        public int? MasterId { get; init; }

        public int? DiscogsArtistId { get; init; }

        // Zero when Discogs does not know the year.
        public int Year { get; init; }

        // Filled in when MusicBrainz links the Discogs entry to a release group or artist.
        public string MusicBrainzId { get; set; }

        // Filled in when MusicBrainz links the Discogs release to one of its releases, which is the exact edition.
        public string MusicBrainzReleaseId { get; set; }

        // Format families of the pressing, such as "vinyl", when Discogs sends them.
        public IReadOnlyCollection<string> Formats { get; init; } = Array.Empty<string>();

        // Number of discs or tapes, or zero if unknown.
        public int MediumCount { get; init; }

        // Normalized label names.
        public IReadOnlyCollection<string> Labels { get; init; } = Array.Empty<string>();

        public OwnedEdition Edition => new (MusicBrainzReleaseId, Formats, MediumCount, Labels, Year);

        public override string ToString() => Album == null ? Artist : $"{Artist} - {Album}";
    }
}
