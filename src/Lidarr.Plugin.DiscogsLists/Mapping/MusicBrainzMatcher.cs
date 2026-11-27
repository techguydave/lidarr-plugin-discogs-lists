using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser.Model;

namespace Lidarr.Plugin.DiscogsLists.Mapping
{
    public class MetadataUnavailableException : Exception
    {
        public MetadataUnavailableException(Exception innerException)
            : base("Lidarr's metadata search keeps failing, so the rest of this list was not imported.", innerException)
        {
        }
    }

    // Adds MusicBrainz IDs to items, the same way Lidarr's import list sync would.
    //
    // Lidarr searches for any item without IDs, and a single search that fails (for example on a record
    // Lidarr cannot read) abandons the entire sync, including other lists. Searching here means only that
    // item is lost. Items that are returned already have IDs, so Lidarr does not search for them again.
    public class MusicBrainzMatcher
    {
        internal const int MaxConsecutiveFailures = 25;

        private readonly Lazy<ISearchForNewAlbum> _albumSearch;
        private readonly Lazy<ISearchForNewArtist> _artistSearch;
        private readonly Logger _logger;

        // The searches must be Lazy: Lidarr's search service depends on the factory that builds import lists.
        public MusicBrainzMatcher(Lazy<ISearchForNewAlbum> albumSearch, Lazy<ISearchForNewArtist> artistSearch, Logger logger)
        {
            _albumSearch = albumSearch;
            _artistSearch = artistSearch;
            _logger = logger;
        }

        public IEnumerable<ImportListItemInfo> Match(IEnumerable<ImportListItemInfo> items, string listName)
        {
            var consecutiveFailures = 0;

            foreach (var item in items.DistinctBy(i => (i.Artist, i.Album)))
            {
                var description = item.Album == null ? item.Artist : $"{item.Artist} - {item.Album}";
                bool matched;

                try
                {
                    matched = item.Album == null ? MatchArtist(item) : MatchAlbum(item);
                    consecutiveFailures = 0;
                }
                catch (Exception ex)
                {
                    _logger.Warn("{0}: skipped '{1}' because the metadata search failed: {2}", listName, description, ex.Message);

                    // Many failures in a row mean the metadata service is down rather than one record being bad.
                    if (++consecutiveFailures >= MaxConsecutiveFailures)
                    {
                        throw new MetadataUnavailableException(ex);
                    }

                    continue;
                }

                if (matched)
                {
                    yield return item;
                }
                else
                {
                    _logger.Debug("{0}: no match found for '{1}'", listName, description);
                }
            }
        }

        private bool MatchAlbum(ImportListItemInfo item)
        {
            var album = _albumSearch.Value.SearchForNewAlbum(item.Album, item.Artist).FirstOrDefault();
            var artist = album?.ArtistMetadata?.Value;

            if (string.IsNullOrWhiteSpace(album?.ForeignAlbumId) || string.IsNullOrWhiteSpace(artist?.ForeignArtistId))
            {
                return false;
            }

            item.AlbumMusicBrainzId = album.ForeignAlbumId;
            item.Album = album.Title;
            item.Artist ??= artist.Name;
            item.ArtistMusicBrainzId = artist.ForeignArtistId;

            return true;
        }

        private bool MatchArtist(ImportListItemInfo item)
        {
            var artist = _artistSearch.Value.SearchForNewArtist(item.Artist).FirstOrDefault()?.Metadata?.Value;

            if (string.IsNullOrWhiteSpace(artist?.ForeignArtistId))
            {
                return false;
            }

            item.ArtistMusicBrainzId = artist.ForeignArtistId;
            item.Artist = artist.Name;

            return true;
        }
    }
}
