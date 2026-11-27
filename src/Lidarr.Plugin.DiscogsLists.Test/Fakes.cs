using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.DiscogsLists.Api;
using Moq;
using NzbDrone.Common.Http.Proxy;

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
