using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Music;
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

    // Adds MusicBrainz IDs to items, which Lidarr needs to add them.
    //
    // Items that MusicBrainz links to a Discogs master, release or artist are looked up by that ID. The rest are
    // searched for by name, and the best result is only used if its title and artist are close enough.
    //
    // Lidarr would otherwise search for each item itself, take the first result, and abandon the entire sync if
    // one search fails. Items that are returned already have IDs, so Lidarr does not search for them again.
    public class MusicBrainzMatcher
    {
        internal const int MaxConsecutiveFailures = 25;
        internal const double MinimumTitleScore = 0.5;
        internal const double MinimumArtistScore = 0.34;

        private readonly Lazy<ISearchForNewAlbum> _albumSearch;
        private readonly Lazy<ISearchForNewArtist> _artistSearch;
        private readonly DiscogsLinkResolver _links;
        private readonly Logger _logger;

        // The searches must be Lazy: Lidarr's search service depends on the factory that builds import lists.
        public MusicBrainzMatcher(Lazy<ISearchForNewAlbum> albumSearch, Lazy<ISearchForNewArtist> artistSearch, DiscogsLinkResolver links, Logger logger)
        {
            _albumSearch = albumSearch;
            _artistSearch = artistSearch;
            _links = links;
            _logger = logger;
        }

        public IEnumerable<ImportListItemInfo> Match(IEnumerable<DiscogsItem> items, string listName)
        {
            var unique = items.DistinctBy(i => (i.ReleaseId, i.MasterId, i.DiscogsArtistId, i.Artist, i.Album)).ToList();
            var consecutiveFailures = 0;

            _links.Resolve(unique);

            foreach (var item in unique.DistinctBy(i => (i.MusicBrainzId ?? string.Empty, i.Artist, i.Album)))
            {
                bool matched;

                try
                {
                    matched = item.Album == null ? MatchArtist(item) : MatchAlbum(item);
                    consecutiveFailures = 0;
                }
                catch (Exception ex)
                {
                    _logger.Warn("{0}: skipped '{1}' because the metadata search failed: {2}", listName, item, ex.Message);

                    // Many failures in a row mean the metadata service is down rather than one record being bad.
                    if (++consecutiveFailures >= MaxConsecutiveFailures)
                    {
                        throw new MetadataUnavailableException(ex);
                    }

                    continue;
                }

                if (matched)
                {
                    yield return item.Item;
                }
                else
                {
                    _logger.Debug("{0}: no match found for '{1}'", listName, item);
                }
            }
        }

        private static ArtistMetadata ArtistOf(Album album) => album?.ArtistMetadata?.Value ?? album?.Artist?.Value?.Metadata?.Value;

        private bool MatchAlbum(DiscogsItem item)
        {
            Album album = null;

            if (item.MusicBrainzId != null)
            {
                album = _albumSearch.Value.SearchForNewAlbum($"lidarr:{item.MusicBrainzId}", item.Artist).FirstOrDefault();

                if (album == null)
                {
                    _logger.Debug("'{0}' is linked to MusicBrainz release group {1}, which Lidarr does not have. Searching by name.", item, item.MusicBrainzId);
                }
            }

            album ??= BestByName(item, _albumSearch.Value.SearchForNewAlbum(item.Album, item.Artist));

            var artist = ArtistOf(album);

            if (string.IsNullOrWhiteSpace(album?.ForeignAlbumId) || string.IsNullOrWhiteSpace(artist?.ForeignArtistId))
            {
                return false;
            }

            item.Item.AlbumMusicBrainzId = album.ForeignAlbumId;
            item.Item.Album = album.Title;
            item.Item.Artist = artist.Name;
            item.Item.ArtistMusicBrainzId = artist.ForeignArtistId;

            return true;
        }

        // Lidarr's search returns albums with similar names first, which may be a different recording entirely.
        private static Album BestByName(DiscogsItem item, IEnumerable<Album> candidates)
        {
            return candidates
                .Select(a => new
                {
                    Album = a,
                    Title = TitleSimilarity.Score(item.Album, a.Title),
                    Artist = ArtistOf(a)?.Name == null ? 1 : TitleSimilarity.Score(item.Artist, ArtistOf(a).Name),
                    YearGap = item.Year > 0 && a.ReleaseDate.HasValue ? Math.Abs(a.ReleaseDate.Value.Year - item.Year) : int.MaxValue
                })
                .Where(c => c.Title >= MinimumTitleScore && c.Artist >= MinimumArtistScore)
                .OrderByDescending(c => c.Title)
                .ThenByDescending(c => c.Artist)
                .ThenBy(c => c.YearGap)
                .Select(c => c.Album)
                .FirstOrDefault();
        }

        private bool MatchArtist(DiscogsItem item)
        {
            ArtistMetadata artist = null;

            if (item.MusicBrainzId != null)
            {
                artist = _artistSearch.Value.SearchForNewArtist($"lidarr:{item.MusicBrainzId}").FirstOrDefault()?.Metadata?.Value;
            }

            artist ??= _artistSearch.Value.SearchForNewArtist(item.Artist)
                .Select(a => a.Metadata?.Value)
                .Where(a => a != null && TitleSimilarity.Score(item.Artist, a.Name) >= MinimumTitleScore)
                .OrderByDescending(a => TitleSimilarity.Score(item.Artist, a.Name))
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(artist?.ForeignArtistId))
            {
                return false;
            }

            item.Item.ArtistMusicBrainzId = artist.ForeignArtistId;
            item.Item.Artist = artist.Name;

            return true;
        }
    }
}
