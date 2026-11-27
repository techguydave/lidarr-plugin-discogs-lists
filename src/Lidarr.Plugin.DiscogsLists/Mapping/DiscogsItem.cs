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

        public override string ToString() => Album == null ? Artist : $"{Artist} - {Album}";
    }
}
