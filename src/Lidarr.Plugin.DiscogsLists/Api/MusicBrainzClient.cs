using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Lidarr.Plugin.DiscogsLists.Api
{
    public class MusicBrainzException : Exception
    {
        public MusicBrainzException(string message, Exception innerException = null)
            : base(message, innerException)
        {
        }
    }

    public record MusicBrainzLink(string TargetType, string Id);

    public interface IMusicBrainzClient
    {
        // Returns the MusicBrainz entities that link to each of the given Discogs URLs.
        Dictionary<string, List<MusicBrainzLink>> GetLinks(IReadOnlyCollection<string> discogsUrls);

        string GetReleaseGroupOfRelease(string releaseId);
    }

    public class MusicBrainzClient : IMusicBrainzClient
    {
        internal const int UrlsPerRequest = 50;
        internal const int Attempts = 3;

        // MusicBrainz allows one request per second on average.
        internal static readonly TimeSpan RequestInterval = TimeSpan.FromSeconds(1.1);
        internal static readonly TimeSpan BusyPause = TimeSpan.FromSeconds(5);

        private readonly IMusicBrainzTransport _transport;
        private readonly IDiscogsClock _clock;
        private readonly object _gate = new ();
        private DateTime _nextRequest = DateTime.MinValue;

        public MusicBrainzClient(IMusicBrainzTransport transport, IDiscogsClock clock)
        {
            _transport = transport;
            _clock = clock;
        }

        public Dictionary<string, List<MusicBrainzLink>> GetLinks(IReadOnlyCollection<string> discogsUrls)
        {
            var links = new Dictionary<string, List<MusicBrainzLink>>(StringComparer.OrdinalIgnoreCase);

            foreach (var chunk in discogsUrls.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(UrlsPerRequest))
            {
                var query = string.Join("&", chunk.Select(u => "resource=" + Uri.EscapeDataString(u)));
                var response = Fetch<UrlLookupResponse>($"url?{query}&inc=release-rels+release-group-rels+artist-rels&fmt=json", allowNotFound: true);

                // A lookup of one URL returns it directly rather than in a list.
                var urls = response?.Urls ?? (response?.Resource != null ? new List<UrlResource> { response } : new List<UrlResource>());

                foreach (var url in urls.Where(u => u.Resource != null))
                {
                    links[url.Resource] = (url.Relations ?? new List<Relation>())
                        .Where(r => r.Type == "discogs")
                        .Select(r => new MusicBrainzLink(r.TargetType, (r.ReleaseGroup ?? r.Release ?? r.Artist)?.Id))
                        .Where(l => l.Id != null)
                        .ToList();
                }
            }

            return links;
        }

        public string GetReleaseGroupOfRelease(string releaseId)
        {
            if (!Guid.TryParse(releaseId, out var id))
            {
                throw new ArgumentException("A MusicBrainz release ID is required.", nameof(releaseId));
            }

            return Fetch<ReleaseResponse>($"release/{id}?inc=release-groups&fmt=json", allowNotFound: true)?.ReleaseGroup?.Id;
        }

        private T Fetch<T>(string pathAndQuery, bool allowNotFound)
            where T : class
        {
            for (var attempt = 1; ; attempt++)
            {
                WaitForTurn();

                try
                {
                    using var response = _transport.Get(pathAndQuery);

                    if (response.StatusCode == HttpStatusCode.NotFound && allowNotFound)
                    {
                        return null;
                    }

                    // MusicBrainz answers 503 when it is busy or the rate limit is exceeded.
                    if (response.StatusCode == HttpStatusCode.ServiceUnavailable && attempt < Attempts)
                    {
                        Pause(response.Headers.RetryAfter?.Delta ?? BusyPause);
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new MusicBrainzException($"MusicBrainz returned HTTP {(int)response.StatusCode}.");
                    }

                    var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    return JsonConvert.DeserializeObject<T>(body);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
                {
                    throw new MusicBrainzException($"Unable to reach MusicBrainz: {ex.Message}", ex);
                }
                catch (JsonException ex)
                {
                    throw new MusicBrainzException("MusicBrainz returned a response that could not be read.", ex);
                }
            }
        }

        private void WaitForTurn()
        {
            lock (_gate)
            {
                var now = _clock.UtcNow;

                if (_nextRequest > now)
                {
                    _clock.Sleep(_nextRequest - now);
                    now = _nextRequest;
                }

                _nextRequest = now + RequestInterval;
            }
        }

        private void Pause(TimeSpan duration)
        {
            lock (_gate)
            {
                var until = _clock.UtcNow + (duration > TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : duration);

                if (until > _nextRequest)
                {
                    _nextRequest = until;
                }
            }
        }

        private class Entity
        {
            public string Id { get; set; }
        }

        private class Relation
        {
            public string Type { get; set; }

            [JsonProperty("target-type")]
            public string TargetType { get; set; }

            public Entity Release { get; set; }

            [JsonProperty("release_group")]
            public Entity ReleaseGroup { get; set; }

            public Entity Artist { get; set; }
        }

        private class UrlResource
        {
            public string Resource { get; set; }
            public List<Relation> Relations { get; set; }
        }

        private class UrlLookupResponse : UrlResource
        {
            public List<UrlResource> Urls { get; set; }
        }

        private class ReleaseResponse
        {
            [JsonProperty("release-group")]
            public Entity ReleaseGroup { get; set; }
        }
    }
}
