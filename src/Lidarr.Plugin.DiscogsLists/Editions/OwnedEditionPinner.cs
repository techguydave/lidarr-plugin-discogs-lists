using System;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Events;

namespace Lidarr.Plugin.DiscogsLists.Editions
{
    public interface IOwnedEditions
    {
        // Records the edition owned on Discogs, to monitor once Lidarr adds the album.
        void Remember(string releaseGroupId, OwnedEdition edition);
    }

    // Lidarr monitors the edition of an album with the most tracks, which is often a deluxe edition rather than the
    // pressing the user owns, and then rejects the user's files because tracks are missing. This monitors the owned
    // edition instead.
    //
    // Only albums that are new to the library are changed, once, before they have files, so an edition chosen in
    // Lidarr is never overridden. Nothing is stored outside memory.
    public class OwnedEditionPinner : IOwnedEditions, IHandle<AlbumUpdatedEvent>
    {
        private static readonly TimeSpan PendingLifetime = TimeSpan.FromDays(1);

        private readonly ICached<OwnedEdition> _pending;
        private readonly Lazy<IAlbumService> _albums;
        private readonly Lazy<IReleaseService> _releases;
        private readonly Lazy<IMediaFileService> _files;
        private readonly Logger _logger;

        // Lazy, as Lidarr's music services and the import lists that use this depend on each other.
        public OwnedEditionPinner(ICacheManager cacheManager,
                                  Lazy<IAlbumService> albums,
                                  Lazy<IReleaseService> releases,
                                  Lazy<IMediaFileService> files,
                                  Logger logger)
        {
            _pending = cacheManager.GetCache<OwnedEdition>(typeof(OwnedEditionPinner), "pending");
            _albums = albums;
            _releases = releases;
            _files = files;
            _logger = logger;
        }

        public void Remember(string releaseGroupId, OwnedEdition edition)
        {
            if (string.IsNullOrWhiteSpace(releaseGroupId) || edition == null || !edition.IsKnown)
            {
                return;
            }

            // An album already in the library keeps the edition Lidarr or the user chose.
            if (_albums.Value.FindById(releaseGroupId) != null)
            {
                return;
            }

            _pending.Set(releaseGroupId, edition, PendingLifetime);
        }

        public void Handle(AlbumUpdatedEvent message)
        {
            var album = message.Album;
            var edition = album?.ForeignAlbumId == null ? null : _pending.Find(album.ForeignAlbumId);

            if (edition == null)
            {
                return;
            }

            try
            {
                Pin(album, edition);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to monitor the Discogs edition of '{0}'", album.Title);
            }
        }

        private void Pin(Album album, OwnedEdition edition)
        {
            var releases = _releases.Value.GetReleasesByAlbum(album.Id);

            if (!releases.Any())
            {
                // Lidarr has not saved the editions yet; they arrive with a later update.
                return;
            }

            _pending.Remove(album.ForeignAlbumId);

            if (_files.Value.GetFilesByAlbum(album.Id).Any())
            {
                return;
            }

            var chosen = edition.Choose(releases);

            if (chosen == null)
            {
                _logger.Debug("'{0}': no edition in Lidarr matches the Discogs release, keeping Lidarr's choice", album.Title);
                return;
            }

            if (chosen.Monitored)
            {
                return;
            }

            _releases.Value.SetMonitored(chosen);

            _logger.Info("'{0}': monitoring the {1} edition, which matches the Discogs release ({2})",
                album.Title,
                Describe(chosen),
                edition.MusicBrainzReleaseId != null && chosen.ForeignReleaseId == edition.MusicBrainzReleaseId ? "linked on MusicBrainz" : "same format");
        }

        private static string Describe(AlbumRelease release)
        {
            var formats = string.Join(" + ", (release.Media ?? new ()).GroupBy(m => m.Format).Select(g => g.Count() > 1 ? $"{g.Count()}×{g.Key}" : g.Key));
            var details = new[] { formats, release.Country?.FirstOrDefault(), release.ReleaseDate?.Year.ToString(System.Globalization.CultureInfo.InvariantCulture) }
                .Where(d => !string.IsNullOrWhiteSpace(d));

            return $"{release.Title} [{string.Join(", ", details)}]";
        }
    }
}
