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
            foreach (var (item, id) in Lookup(items.Where(i => i.Album != null && i.MasterId > 0), i => MasterUrl(i.MasterId.Value), "release_group"))
            {
                item.MusicBrainzId = id;
            }

            foreach (var (item, id) in Lookup(items.Where(i => i.Album == null && i.DiscogsArtistId > 0), i => ArtistUrl(i.DiscogsArtistId.Value), "artist"))
            {
                item.MusicBrainzId = id;
            }

            // A release linked to a MusicBrainz release identifies the exact edition, and leads to its group when the
            // master had no link. Every release is looked up, which is one batched request per 50 releases.
            foreach (var (item, id) in Lookup(items.Where(i => i.Album != null && i.ReleaseId > 0), i => ReleaseUrl(i.ReleaseId.Value), "release"))
            {
                item.MusicBrainzReleaseId = id;
                item.MusicBrainzId ??= GroupOf(id);
            }
        }

        // Returns the items that have a single, unambiguous link of the given type, with the linked ID.
        private List<(DiscogsItem Item, string Id)> Lookup(IEnumerable<DiscogsItem> candidates, Func<DiscogsItem, string> url, string targetType)
        {
            var keys = candidates.Select(i => (Item: i, Url: url(i))).ToList();
            var unknown = keys.Select(k => k.Url).Distinct().Where(u => _cache.Find(Key(targetType, u)) == null).ToList();

            if (unknown.Any())
            {
                var links = _musicBrainz.GetLinks(unknown);

                foreach (var discogsUrl in unknown)
                {
                    var found = links.TryGetValue(discogsUrl, out var list) ? list : new List<MusicBrainzLink>();
                    var ids = found.Where(l => l.TargetType == targetType).Select(l => l.Id).Distinct().ToList();

                    // An empty value records that there is no usable link.
                    _cache.Set(Key(targetType, discogsUrl), ids.Count == 1 ? ids[0] : string.Empty, CacheLifetime);
                }
            }

            return keys.Select(k => (k.Item, Id: _cache.Find(Key(targetType, k.Url))))
                .Where(k => !string.IsNullOrEmpty(k.Id))
                .ToList();
        }

        private string GroupOf(string releaseId)
        {
            var key = Key("group-of", releaseId);
            var group = _cache.Find(key);

            if (group == null)
            {
                group = _musicBrainz.GetReleaseGroupOfRelease(releaseId) ?? string.Empty;
                _cache.Set(key, group, CacheLifetime);
            }

            return group.Length > 0 ? group : null;
        }

        private static string Key(string type, string value) => $"{type}:{value}";
    }
}
