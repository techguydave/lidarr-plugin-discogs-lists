using System;
using System.Net;

namespace Lidarr.Plugin.DiscogsLists.Api
{
    public class DiscogsApiException : Exception
    {
        public DiscogsApiException(string message, HttpStatusCode? statusCode = null, Exception innerException = null)
            : base(message, innerException)
        {
            StatusCode = statusCode;
        }

        public HttpStatusCode? StatusCode { get; }
    }

    public class DiscogsRateLimitException : DiscogsApiException
    {
        public DiscogsRateLimitException(TimeSpan retryAfter)
            : base("Discogs rate limit reached. Lidarr will try again later.", HttpStatusCode.TooManyRequests)
        {
            RetryAfter = retryAfter;
        }

        public TimeSpan RetryAfter { get; }
    }
}
