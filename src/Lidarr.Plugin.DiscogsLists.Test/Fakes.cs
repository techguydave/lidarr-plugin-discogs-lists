using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.DiscogsLists.Api;
using Lidarr.Plugin.DiscogsLists.Editions;
using Lidarr.Plugin.DiscogsLists.Mapping;
using Moq;
using NLog;
using NzbDrone.Common.Http.Proxy;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Music;

namespace Lidarr.Plugin.DiscogsLists.Test
{
    internal class FakeClock : IDiscogsClock
    {
        public DateTime UtcNow { get; private set; } = new (2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public List<TimeSpan> Sleeps { get; } = new ();

        public void Sleep(TimeSpan duration)
        {
            Sleeps.Add(duration);
            UtcNow += duration;
        }

        public void Advance(TimeSpan duration) => UtcNow += duration;
    }

    // Stands in for Lidarr's metadata search: everything matches unless told otherwise.
    internal class FakeMetadata
    {
        public FakeMetadata()
        {
            Albums.Setup(s => s.SearchForNewAlbum(It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string title, string artist) => new List<Album>
                {
                    new ()
                    {
                        ForeignAlbumId = $"album:{title}",
                        Title = title,
                        ArtistMetadata = new ArtistMetadata { ForeignArtistId = $"artist:{artist}", Name = artist }
                    }
                });

            Artists.Setup(s => s.SearchForNewArtist(It.IsAny<string>()))
                .Returns((string name) => new List<Artist>
                {
                    new () { Metadata = new ArtistMetadata { ForeignArtistId = $"artist:{name}", Name = name } }
                });
        }

        public Mock<ISearchForNewAlbum> Albums { get; } = new ();

        public Mock<ISearchForNewArtist> Artists { get; } = new ();

        // Discogs URL => MusicBrainz links, as MusicBrainz would return them.
        public Dictionary<string, List<MusicBrainzLink>> Links { get; } = new ();

        public Mock<IMusicBrainzClient> MusicBrainz { get; } = new ();

        public Mock<IOwnedEditions> Editions { get; } = new ();

        public MusicBrainzMatcher CreateMatcher()
        {
            LinkLookupFromDictionary();

            return CreateMatcherKeepingSetup();
        }

        public MusicBrainzMatcher CreateMatcherKeepingSetup() => new (new Lazy<ISearchForNewAlbum>(() => Albums.Object),
            new Lazy<ISearchForNewArtist>(() => Artists.Object),
            new DiscogsLinkResolver(MusicBrainz.Object, new NzbDrone.Common.Cache.CacheManager(), LogManager.CreateNullLogger()),
            Editions.Object,
            LogManager.CreateNullLogger());

        private void LinkLookupFromDictionary()
        {
            MusicBrainz.Setup(m => m.GetLinks(It.IsAny<IReadOnlyCollection<string>>()))
                .Returns((IReadOnlyCollection<string> urls) => urls.Where(Links.ContainsKey).ToDictionary(u => u, u => Links[u]));
        }

        // Makes the release group exist in Lidarr's metadata.
        public void HasReleaseGroup(string id, string title, string artist)
        {
            Albums.Setup(s => s.SearchForNewAlbum($"lidarr:{id}", It.IsAny<string>()))
                .Returns(new List<Album> { new () { ForeignAlbumId = id, Title = title, ArtistMetadata = new ArtistMetadata { ForeignArtistId = $"artist:{artist}", Name = artist } } });
        }

        public void SearchReturns(string title, params Album[] albums)
        {
            Albums.Setup(s => s.SearchForNewAlbum(title, It.IsAny<string>())).Returns(albums.ToList());
        }

        public static Album Candidate(string title, string artist, int year = 0) => new ()
        {
            ForeignAlbumId = $"album:{title}:{year}",
            Title = title,
            ReleaseDate = year > 0 ? new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc) : null,
            ArtistMetadata = new ArtistMetadata { ForeignArtistId = $"artist:{artist}", Name = artist }
        };

        public void Fails(params string[] titles)
        {
            foreach (var title in titles)
            {
                Albums.Setup(s => s.SearchForNewAlbum(title, It.IsAny<string>()))
                    .Throws(new InvalidOperationException("Invalid response received from LidarrAPI."));
            }
        }

        public void HasNoMatch(string title)
        {
            Albums.Setup(s => s.SearchForNewAlbum(title, It.IsAny<string>())).Returns(new List<Album>());
        }
    }

    internal record SentRequest(HttpMethod Method, Uri Uri, string Authorization, string UserAgent, bool HasBody);

    // Stands in for the network: records what was sent and answers from canned responses.
    internal class FakeDiscogs : HttpMessageHandler
    {
        private readonly Dictionary<string, Queue<Func<HttpResponseMessage>>> _responses = new ();

        public List<SentRequest> Requests { get; } = new ();

        public FakeDiscogs Returns(string pathAndQuery, string json, HttpStatusCode status = HttpStatusCode.OK, Action<HttpResponseMessage> configure = null)
        {
            if (!_responses.TryGetValue(pathAndQuery, out var queue))
            {
                _responses[pathAndQuery] = queue = new Queue<Func<HttpResponseMessage>>();
            }

            queue.Enqueue(() =>
            {
                var response = new HttpResponseMessage(status) { Content = new StringContent(json) };
                configure?.Invoke(response);
                return response;
            });

            return this;
        }

        public FakeDiscogs WithIdentity() => Returns("/oauth/identity", @"{""id"": 1, ""username"": ""rodneyfool""}");

        public DiscogsClient CreateClient(IDiscogsThrottle throttle = null)
        {
            return new DiscogsClient(new Transport(this), throttle ?? new DiscogsThrottle(new FakeClock()));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new SentRequest(request.Method,
                request.RequestUri,
                request.Headers.TryGetValues("Authorization", out var auth) ? auth.Single() : null,
                string.Join(" ", request.Headers.GetValues("User-Agent")),
                request.Content != null));

            var key = request.RequestUri.PathAndQuery;

            if (!_responses.TryGetValue(key, out var queue))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") });
            }

            // The last canned response keeps being returned.
            var factory = queue.Count > 1 ? queue.Dequeue() : queue.Peek();

            return Task.FromResult(factory());
        }

        internal class Transport : DiscogsTransport
        {
            private readonly HttpMessageHandler _handler;

            public Transport(HttpMessageHandler handler)
                : base(Mock.Of<IHttpProxySettingsProvider>(), Mock.Of<ICreateManagedWebProxy>())
            {
                _handler = handler;
            }

            protected override HttpMessageHandler CreateHandler(IWebProxy proxy) => _handler;
        }
    }
}
