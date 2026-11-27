using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Lidarr.Plugin.DiscogsLists.Api;
using NLog;
using NzbDrone.Common.Cache;

namespace Lidarr.Plugin.DiscogsLists.Mapping
{
    // Finds the MusicBrainz release group or artist that MusicBrainz editors have linked to each Discogs entry.
    // A link identifies the exact album, where a title search can pick another recording with a similar name.
    public class DiscogsLinkResolver
    {
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(1);

        private readonly IMusicBrainzClient _musicBrainz;
        private readonly ICached<string> _cache;
        private readonly Logger _logger;

        public DiscogsLinkResolver(IMusicBrainzClient musicBrainz, ICacheManager cacheManager, Logger logger)
        {
            _musicBrainz = musicBrainz;
            _cache = cacheManager.GetCache<string>(typeof(DiscogsLinkResolver), "links");
            _logger = logger;
        }

        public void Resolve(IReadOnlyCollection<DiscogsItem> items)
        {
            try
            {
                ResolveLinks(items);
            }
            catch (MusicBrainzException ex)
            {
                // Without links the items are still matched by name.
                _logger.Warn("Unable to look up MusicBrainz links, matching by name instead: {0}", ex.Message);
            }
        }

        internal static string ReleaseUrl(int id) => Url("release", id);

        internal static string MasterUrl(int id) => Url("master", id);

        internal static string ArtistUrl(int id) => Url("artist", id);

        private static string Url(string type, int id) => string.Create(CultureInfo.InvariantCulture, $"https://www.discogs.com/{type}/{id}");

        private void ResolveLinks(IReadOnlyCollection<DiscogsItem> items)
        {
            // Masters group every pressing of a recording, as release groups do, so they are tried first.
            Assign(items.Where(i => i.Album != null && i.MasterId > 0), i => MasterUrl(i.MasterId.Value), "release_group");
            Assign(items.Where(i => i.Album == null && i.DiscogsArtistId > 0), i => ArtistUrl(i.DiscogsArtistId.Value), "artist");

            // A release linked to a MusicBrainz release leads to that release's group.
            Assign(items.Where(i => i.Album != null && i.ReleaseId > 0), i => ReleaseUrl(i.ReleaseId.Value), "release");
        }

        private void Assign(IEnumerable<DiscogsItem> candidates, Func<DiscogsItem, string> url, string targetType)
        {
            var pending = candidates.Where(i => i.MusicBrainzId == null).ToList();
            var keys = pending.Select(i => (Item: i, Key: $"{targetType}:{url(i)}")).ToList();

            var unknown = keys.Where(k => _cache.Find(k.Key) == null).Select(k => url(k.Item)).Distinct().ToList();

            if (unknown.Any())
            {
                var links = _musicBrainz.GetLinks(unknown);

                foreach (var discogsUrl in unknown)
                {
                    var found = links.TryGetValue(discogsUrl, out var list) ? list : new List<MusicBrainzLink>();
                    var ids = found.Where(l => l.TargetType == targetType).Select(l => l.Id).Distinct().ToList();

                    // Only a single, unambiguous link is trusted. An empty value records that there is none.
                    var id = ids.Count == 1 ? ids[0] : string.Empty;

                    if (targetType == "release" && id.Length > 0)
                    {
                        id = _musicBrainz.GetReleaseGroupOfRelease(id) ?? string.Empty;
                    }

                    _cache.Set($"{targetType}:{discogsUrl}", id, CacheLifetime);
                }
            }

            foreach (var (item, key) in keys)
            {
                var id = _cache.Find(key);

                if (!string.IsNullOrEmpty(id))
                {
                    item.MusicBrainzId = id;
                }
            }
        }
    }
}
