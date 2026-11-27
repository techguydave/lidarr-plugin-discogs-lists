using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.DiscogsLists.Api;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http.Proxy;

namespace Lidarr.Plugin.DiscogsLists.Test
{
    [TestFixture]
    public class MusicBrainzClientTests
    {
        private const string Master = "https://www.discogs.com/master/372576";
        private const string Release = "https://www.discogs.com/release/2218836";

        private Handler _handler;
        private FakeClock _clock;
        private MusicBrainzClient _client;

        [SetUp]
        public void SetUp()
        {
            _handler = new Handler();
            _clock = new FakeClock();
            _client = new MusicBrainzClient(new Transport(_handler), _clock);
        }

        [Test]
        public void looks_up_discogs_urls_with_a_get_to_musicbrainz()
        {
            _handler.Respond(HttpStatusCode.OK, @"{""urls"": [{""resource"": """ + Master + @""", ""relations"": [{""type"": ""discogs"", ""target-type"": ""release_group"", ""release_group"": {""id"": ""rg-1""}}]}]}");

            var links = _client.GetLinks(new[] { Master, Release });

            var request = _handler.Requests.Single();
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(request.Uri.GetLeftPart(UriPartial.Path), Is.EqualTo("https://musicbrainz.org/ws/2/url"));
            Assert.That(Uri.UnescapeDataString(request.Uri.Query), Does.Contain("resource=" + Master).And.Contain("resource=" + Release));
            Assert.That(request.UserAgent, Does.StartWith("LidarrListsForDiscogs/").And.Contain("github.com/techguydave/lidarr-plugin-discogs-lists"));
            Assert.That(links[Master].Single(), Is.EqualTo(new MusicBrainzLink("release_group", "rg-1")));
            Assert.That(links.ContainsKey(Release), Is.False);
        }

        [Test]
        public void reads_a_single_url_response()
        {
            _handler.Respond(HttpStatusCode.OK, @"{""resource"": """ + Release + @""", ""relations"": [{""type"": ""discogs"", ""target-type"": ""release"", ""release"": {""id"": ""r-1""}}, {""type"": ""other"", ""target-type"": ""release"", ""release"": {""id"": ""r-2""}}]}");

            var links = _client.GetLinks(new[] { Release });

            Assert.That(links[Release].Single().Id, Is.EqualTo("r-1"));
        }

        [Test]
        public void unknown_urls_are_not_an_error()
        {
            _handler.Respond(HttpStatusCode.NotFound, @"{""error"": ""Not Found""}");

            Assert.That(_client.GetLinks(new[] { Release }), Is.Empty);
        }

        [Test]
        public void sends_large_lookups_in_batches_spaced_by_the_rate_limit()
        {
            _handler.Respond(HttpStatusCode.OK, @"{""urls"": []}");
            var urls = Enumerable.Range(1, MusicBrainzClient.UrlsPerRequest + 1).Select(i => $"https://www.discogs.com/master/{i}").ToList();

            _client.GetLinks(urls);

            Assert.That(_handler.Requests, Has.Count.EqualTo(2));
            Assert.That(_clock.Sleeps.Sum(s => s.TotalSeconds), Is.GreaterThanOrEqualTo(MusicBrainzClient.RequestInterval.TotalSeconds));
        }

        [Test]
        public void retries_when_musicbrainz_is_busy()
        {
            _handler.Respond(HttpStatusCode.ServiceUnavailable, "{}");
            _handler.Respond(HttpStatusCode.OK, @"{""release-group"": {""id"": ""rg-9""}}");

            Assert.That(_client.GetReleaseGroupOfRelease(Guid.Empty.ToString()), Is.EqualTo("rg-9"));
            Assert.That(_handler.Requests, Has.Count.EqualTo(2));
        }

        [Test]
        public void gives_up_when_musicbrainz_stays_busy()
        {
            _handler.Respond(HttpStatusCode.ServiceUnavailable, "{}");

            Assert.Throws<MusicBrainzException>(() => _client.GetLinks(new[] { Master }));
            Assert.That(_handler.Requests, Has.Count.EqualTo(MusicBrainzClient.Attempts));
        }

        [TestCase("https://example.com/x")]
        [TestCase("/ws/2/url")]
        [TestCase("//example.com/x")]
        public void transport_refuses_anything_but_a_musicbrainz_path(string path)
        {
            Assert.Throws<ArgumentException>(() => new Transport(_handler).Get(path));
            Assert.That(_handler.Requests, Is.Empty);
        }

        private class Handler : HttpMessageHandler
        {
            private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new ();

            public List<SentRequest> Requests { get; } = new ();

            public void Respond(HttpStatusCode status, string body) => _responses.Enqueue((status, body));

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(new SentRequest(request.Method, request.RequestUri, null, string.Join(" ", request.Headers.GetValues("User-Agent")), request.Content != null));
                var (status, body) = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();

                return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
            }
        }

        private class Transport : MusicBrainzTransport
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
