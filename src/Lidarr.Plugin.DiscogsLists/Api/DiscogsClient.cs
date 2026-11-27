using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Lidarr.Plugin.DiscogsLists.Api.Models;
using Newtonsoft.Json;

namespace Lidarr.Plugin.DiscogsLists.Api
{
    public interface IDiscogsClient
    {
        string GetUsername(string token);
        List<DiscogsFolder> GetFolders(string token, string username);
        IEnumerable<DiscogsRelease> GetFolderReleases(string token, string username, int folderId);
        IEnumerable<DiscogsRelease> GetWants(string token, string username);
        IEnumerable<DiscogsUserList> GetUserLists(string token, string username);
        IEnumerable<DiscogsListItem> GetListItems(string token, int listId);
    }

    public class DiscogsClient : IDiscogsClient
    {
        internal const int PageSize = 100;
        internal const int MaxPages = 1000;

        // Visible ASCII only, so a mistyped token can never inject headers or end up in an error message.
        private static readonly Regex TokenPattern = new (@"^[\x21-\x7E]+$", RegexOptions.Compiled);

        private readonly IDiscogsTransport _transport;
        private readonly IDiscogsThrottle _throttle;

        public DiscogsClient(IDiscogsTransport transport, IDiscogsThrottle throttle)
        {
            _transport = transport;
            _throttle = throttle;
        }

        public string GetUsername(string token)
        {
            var identity = Fetch<DiscogsIdentity>(token, "/oauth/identity");

            if (string.IsNullOrWhiteSpace(identity.Username))
            {
                throw new DiscogsApiException("Discogs did not return a username for this token.");
            }

            return identity.Username;
        }

        public List<DiscogsFolder> GetFolders(string token, string username)
        {
            return Fetch<DiscogsFoldersResponse>(token, $"{UserPath(username)}/collection/folders").Folders ?? new List<DiscogsFolder>();
        }

        public IEnumerable<DiscogsRelease> GetFolderReleases(string token, string username, int folderId)
        {
            var path = string.Create(CultureInfo.InvariantCulture, $"{UserPath(username)}/collection/folders/{folderId}/releases");

            return FetchPaged<DiscogsCollectionPage, DiscogsRelease>(token, path, p => p.Releases);
        }

        public IEnumerable<DiscogsRelease> GetWants(string token, string username)
        {
            return FetchPaged<DiscogsWantlistPage, DiscogsRelease>(token, $"{UserPath(username)}/wants", p => p.Wants);
        }

        public IEnumerable<DiscogsUserList> GetUserLists(string token, string username)
        {
            return FetchPaged<DiscogsUserListsPage, DiscogsUserList>(token, $"{UserPath(username)}/lists", p => p.Lists);
        }

        public IEnumerable<DiscogsListItem> GetListItems(string token, int listId)
        {
            var path = string.Create(CultureInfo.InvariantCulture, $"/lists/{listId}");

            return FetchPaged<DiscogsListPage, DiscogsListItem>(token, path, p => p.Items);
        }

        private static string UserPath(string username) => $"/users/{Uri.EscapeDataString(username)}";

        // Page numbers are built here rather than following the URLs in the response, keeping the host fixed.
        private IEnumerable<TItem> FetchPaged<TPage, TItem>(string token, string path, Func<TPage, List<TItem>> items)
            where TPage : DiscogsPage
        {
            for (var page = 1; page <= MaxPages; page++)
            {
                var query = string.Create(CultureInfo.InvariantCulture, $"?page={page}&per_page={PageSize}");
                var response = Fetch<TPage>(token, path + query);
                var pageItems = items(response);

                foreach (var item in pageItems ?? Enumerable.Empty<TItem>())
                {
                    yield return item;
                }

                if (response.Pagination == null || page >= response.Pagination.Pages)
                {
                    yield break;
                }
            }
        }

        private T Fetch<T>(string token, string pathAndQuery)
        {
            token = token?.Trim();

            if (string.IsNullOrEmpty(token) || !TokenPattern.IsMatch(token))
            {
                throw new DiscogsApiException("The Discogs personal access token is empty or contains invalid characters.", HttpStatusCode.Unauthorized);
            }

            _throttle.WaitForSlot();

            try
            {
                using var response = _transport.Get(pathAndQuery, token);

                ReportBudget(response);
                EnsureSuccess(response);

                var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                return JsonConvert.DeserializeObject<T>(body) ?? throw new DiscogsApiException("Discogs returned an empty response.");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                throw new DiscogsApiException($"Unable to reach Discogs: {ex.Message}", null, ex);
            }
            catch (JsonException ex)
            {
                throw new DiscogsApiException("Discogs returned a response that could not be read.", null, ex);
            }
        }

        private void ReportBudget(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("X-Discogs-Ratelimit-Remaining", out var values) &&
                int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var remaining))
            {
                _throttle.ReportRemaining(remaining);
            }
        }

        private void EnsureSuccess(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var status = response.StatusCode;

            if (status == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? DiscogsThrottle.Window;
                _throttle.Pause(retryAfter);

                throw new DiscogsRateLimitException(retryAfter);
            }

            var message = status switch
            {
                HttpStatusCode.Unauthorized => "Discogs rejected the personal access token.",
                HttpStatusCode.Forbidden => "The Discogs account for this token is not allowed to view this resource.",
                HttpStatusCode.NotFound => "Discogs could not find the requested resource.",
                _ => $"Discogs returned HTTP {(int)status}."
            };

            throw new DiscogsApiException(message, status);
        }
    }
}
