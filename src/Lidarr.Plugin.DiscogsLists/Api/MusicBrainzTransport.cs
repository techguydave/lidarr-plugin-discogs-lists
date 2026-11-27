using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using NzbDrone.Common.Http;
using NzbDrone.Common.Http.Proxy;
using HttpClient = System.Net.Http.HttpClient;

namespace Lidarr.Plugin.DiscogsLists.Api
{
    public interface IMusicBrainzTransport
    {
        HttpResponseMessage Get(string pathAndQuery);
    }

    // Looks up which MusicBrainz entities link to Discogs entries. Like the Discogs transport, it can send
    // nothing but GET requests, and only to musicbrainz.org.
    public class MusicBrainzTransport : IMusicBrainzTransport
    {
        private static readonly Uri BaseUri = new ("https://musicbrainz.org/ws/2/");
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

        private readonly IHttpProxySettingsProvider _proxySettingsProvider;
        private readonly ICreateManagedWebProxy _proxyFactory;
        private readonly ConcurrentDictionary<string, HttpClient> _clients = new ();

        public MusicBrainzTransport(IHttpProxySettingsProvider proxySettingsProvider, ICreateManagedWebProxy proxyFactory)
        {
            _proxySettingsProvider = proxySettingsProvider;
            _proxyFactory = proxyFactory;
        }

        // MusicBrainz asks for an application name, version and contact address.
        internal static string UserAgent { get; } = BuildUserAgent();

        public HttpResponseMessage Get(string pathAndQuery)
        {
            if (string.IsNullOrEmpty(pathAndQuery) || pathAndQuery[0] == '/' || pathAndQuery.Contains("//", StringComparison.Ordinal) || pathAndQuery.Contains(':', StringComparison.Ordinal))
            {
                throw new ArgumentException("A path relative to the MusicBrainz web service root is required.", nameof(pathAndQuery));
            }

            var uri = new Uri(BaseUri, pathAndQuery);

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            return GetClient(uri).SendAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        }

        protected virtual HttpMessageHandler CreateHandler(IWebProxy proxy)
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                UseCookies = false
            };

            if (proxy != null)
            {
                handler.Proxy = proxy;
            }

            return handler;
        }

        private static string BuildUserAgent()
        {
            var version = typeof(MusicBrainzTransport).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

            return $"LidarrListsForDiscogs/{version} ( {DiscogsListsPlugin.RepositoryUrl} )";
        }

        private HttpClient GetClient(Uri uri)
        {
            var proxySettings = _proxySettingsProvider.GetProxySettings(new HttpUri(uri.AbsoluteUri));

            return _clients.GetOrAdd(proxySettings?.Key ?? string.Empty, _ =>
            {
                var proxy = proxySettings == null ? null : _proxyFactory.GetWebProxy(proxySettings);

                return new HttpClient(CreateHandler(proxy)) { Timeout = Timeout };
            });
        }
    }
}
