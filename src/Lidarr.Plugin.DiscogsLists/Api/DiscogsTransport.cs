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
    public interface IDiscogsTransport
    {
        HttpResponseMessage Get(string pathAndQuery, string token);
    }

    // The only code in the plugin that talks to the network. The Discogs integration is strictly
    // read-only: this class can send nothing but GET requests, and only to api.discogs.com.
    //
    // Lidarr's IHttpClient is not used because it does not allow the unique User-Agent Discogs requires.
    public class DiscogsTransport : IDiscogsTransport
    {
        private static readonly Uri BaseUri = new ("https://api.discogs.com/");
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

        private readonly IHttpProxySettingsProvider _proxySettingsProvider;
        private readonly ICreateManagedWebProxy _proxyFactory;
        private readonly ConcurrentDictionary<string, HttpClient> _clients = new ();

        public DiscogsTransport(IHttpProxySettingsProvider proxySettingsProvider, ICreateManagedWebProxy proxyFactory)
        {
            _proxySettingsProvider = proxySettingsProvider;
            _proxyFactory = proxyFactory;
        }

        internal static string UserAgent { get; } = BuildUserAgent();

        public HttpResponseMessage Get(string pathAndQuery, string token)
        {
            var uri = BuildUri(pathAndQuery);

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.discogs.v2.plaintext+json");
            request.Headers.TryAddWithoutValidation("Authorization", $"Discogs token={token}");

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

        private static Uri BuildUri(string pathAndQuery)
        {
            if (string.IsNullOrEmpty(pathAndQuery) || pathAndQuery[0] != '/' || pathAndQuery.StartsWith("//", StringComparison.Ordinal))
            {
                throw new ArgumentException("A path relative to the Discogs API root is required.", nameof(pathAndQuery));
            }

            return new Uri(BaseUri, pathAndQuery);
        }

        private static string BuildUserAgent()
        {
            var version = typeof(DiscogsTransport).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

            return $"LidarrListsForDiscogs/{version} +{DiscogsListsPlugin.RepositoryUrl}";
        }

        // Honours the proxy configured in Lidarr.
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
