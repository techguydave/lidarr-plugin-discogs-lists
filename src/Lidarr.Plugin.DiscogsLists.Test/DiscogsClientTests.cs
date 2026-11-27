using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Lidarr.Plugin.DiscogsLists.Api;
using NUnit.Framework;

namespace Lidarr.Plugin.DiscogsLists.Test
{
    [TestFixture]
    public class DiscogsClientTests
    {
        private const string Token = "abc123";

        private FakeDiscogs _discogs;

        [SetUp]
        public void SetUp()
        {
            _discogs = new FakeDiscogs();
        }

        [Test]
        public void every_endpoint_is_an_authenticated_get_to_discogs_with_our_user_agent()
        {
            _discogs.WithIdentity();
            var client = _discogs.CreateClient();

            client.GetUsername(Token);
            Ignore(() => client.GetFolders(Token, "rodneyfool"));
            Ignore(() => client.GetFolderReleases(Token, "rodneyfool", 3).ToList());
            Ignore(() => client.GetWants(Token, "rodneyfool").ToList());
            Ignore(() => client.GetUserLists(Token, "rodneyfool").ToList());
            Ignore(() => client.GetListItems(Token, 2).ToList());

            Assert.That(_discogs.Requests.Select(r => r.Uri.AbsolutePath), Is.EqualTo(new[]
            {
                "/oauth/identity",
                "/users/rodneyfool/collection/folders",
                "/users/rodneyfool/collection/folders/3/releases",
                "/users/rodneyfool/wants",
                "/users/rodneyfool/lists",
                "/lists/2"
            }));

            foreach (var request in _discogs.Requests)
            {
                Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
                Assert.That(request.HasBody, Is.False);
                Assert.That(request.Uri.GetLeftPart(UriPartial.Authority), Is.EqualTo("https://api.discogs.com"));
                Assert.That(request.Uri.Query, Does.Not.Contain(Token));
                Assert.That(request.Authorization, Is.EqualTo("Discogs token=abc123"));
                Assert.That(request.UserAgent, Does.StartWith("LidarrListsForDiscogs/").And.Contain("+https://github.com/techguydave/lidarr-plugin-discogs-lists"));
            }
        }

        [TestCase("https://example.com/users")]
        [TestCase("//example.com/users")]
        [TestCase("users")]
        [TestCase("")]
        public void transport_refuses_anything_but_a_discogs_path(string path)
        {
            var transport = new FakeDiscogs.Transport(_discogs);

            Assert.Throws<ArgumentException>(() => transport.Get(path, Token));
            Assert.That(_discogs.Requests, Is.Empty);
        }

        [Test]
        public void usernames_are_escaped()
        {
            Ignore(() => _discogs.CreateClient().GetFolders(Token, "a/b?c"));

            Assert.That(_discogs.Requests.Single().Uri.AbsolutePath, Is.EqualTo("/users/a%2Fb%3Fc/collection/folders"));
        }

        [Test]
        public void reads_every_page_without_following_response_urls()
        {
            _discogs
                .Returns("/users/rodneyfool/wants?page=1&per_page=100", @"{""pagination"": {""page"": 1, ""pages"": 2, ""urls"": {""next"": ""https://example.com/evil""}}, ""wants"": [{""id"": 1}]}")
                .Returns("/users/rodneyfool/wants?page=2&per_page=100", @"{""pagination"": {""page"": 2, ""pages"": 2}, ""wants"": [{""id"": 2}]}");

            var wants = _discogs.CreateClient().GetWants(Token, "rodneyfool").ToList();

            Assert.That(wants.Select(w => w.Id), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(_discogs.Requests, Has.Count.EqualTo(2));
        }

        [Test]
        public void parses_release_details()
        {
            _discogs.Returns("/users/rodneyfool/collection/folders/0/releases?page=1&per_page=100",
                @"{""releases"": [{""id"": 2464521, ""instance_id"": 1, ""basic_information"": {""title"": ""Information Chase"", ""year"": 2006, ""artists"": [{""name"": ""Bit Shifter"", ""id"": 1}]}}]}");

            var release = _discogs.CreateClient().GetFolderReleases(Token, "rodneyfool", 0).Single();

            Assert.That(release.BasicInformation.Title, Is.EqualTo("Information Chase"));
            Assert.That(release.BasicInformation.Artists.Single().Name, Is.EqualTo("Bit Shifter"));
        }

        [Test]
        public void list_without_pagination_is_read_once()
        {
            _discogs.Returns("/lists/2?page=1&per_page=100",
                @"{""list_id"": 2, ""items"": [{""id"": 4674, ""type"": ""release"", ""display_title"": ""Silent Phase - The Rewired Mixes""}]}");

            var item = _discogs.CreateClient().GetListItems(Token, 2).Single();

            Assert.That(item.DisplayTitle, Is.EqualTo("Silent Phase - The Rewired Mixes"));
            Assert.That(_discogs.Requests, Has.Count.EqualTo(1));
        }

        [TestCase(HttpStatusCode.Unauthorized, "rejected")]
        [TestCase(HttpStatusCode.Forbidden, "not allowed")]
        [TestCase(HttpStatusCode.NotFound, "could not find")]
        [TestCase(HttpStatusCode.InternalServerError, "HTTP 500")]
        public void http_errors_become_readable_exceptions(HttpStatusCode status, string message)
        {
            _discogs.Returns("/oauth/identity", "{}", status);

            var exception = Assert.Throws<DiscogsApiException>(() => _discogs.CreateClient().GetUsername(Token));

            Assert.That(exception.StatusCode, Is.EqualTo(status));
            Assert.That(exception.Message, Does.Contain(message).And.Not.Contain(Token));
        }

        [Test]
        public void too_many_requests_pauses_the_throttle_and_reports_retry_after()
        {
            var clock = new FakeClock();
            var client = _discogs
                .Returns("/oauth/identity", "{}", HttpStatusCode.TooManyRequests, r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30)))
                .Returns("/oauth/identity", @"{""username"": ""rodneyfool""}")
                .CreateClient(new DiscogsThrottle(clock));

            var exception = Assert.Throws<DiscogsRateLimitException>(() => client.GetUsername(Token));
            client.GetUsername(Token);

            Assert.That(exception.RetryAfter, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(clock.Sleeps, Is.EqualTo(new[] { TimeSpan.FromSeconds(30) }));
        }

        [Test]
        public void low_remaining_budget_slows_the_next_request()
        {
            var clock = new FakeClock();
            var client = _discogs
                .Returns("/oauth/identity", @"{""username"": ""rodneyfool""}", configure: r => r.Headers.Add("X-Discogs-Ratelimit-Remaining", "1"))
                .CreateClient(new DiscogsThrottle(clock));

            client.GetUsername(Token);
            client.GetUsername(Token);

            Assert.That(clock.Sleeps, Is.EqualTo(new[] { DiscogsThrottle.LowBudgetPause }));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("abc\r\nX-Injected: 1")]
        [TestCase("two words")]
        public void unusable_tokens_are_never_sent_or_echoed(string token)
        {
            var exception = Assert.Throws<DiscogsApiException>(() => _discogs.CreateClient().GetUsername(token));

            Assert.That(_discogs.Requests, Is.Empty);
            Assert.That(exception.Message, Does.Not.Contain("Injected").And.Not.Contain("two words"));
        }

        [Test]
        public void unreadable_responses_become_api_exceptions()
        {
            _discogs.Returns("/oauth/identity", "<html>");

            Assert.Throws<DiscogsApiException>(() => _discogs.CreateClient().GetUsername(Token));
        }

        private static void Ignore(Action call)
        {
            try
            {
                call();
            }
            catch (DiscogsApiException)
            {
            }
        }
    }
}
