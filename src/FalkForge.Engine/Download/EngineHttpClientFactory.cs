namespace FalkForge.Engine.Download;

using System.Net.Http;

/// <summary>
/// Constructs <see cref="HttpClient"/> instances configured for FalkForge installer
/// downloads. Centralizes the redirect cap and other cross-cutting policies so that
/// every production download path (payloads, update feed, delta artifacts) shares
/// the same defenses against misconfigured CDNs and runaway redirect loops.
/// </summary>
internal static class EngineHttpClientFactory
{
    /// <summary>
    /// Default User-Agent header value sent on every engine HTTP request.
    /// </summary>
    public const string UserAgent = "FalkForge-Engine/1.0";

    /// <summary>
    /// Maximum number of automatic redirects honored per request. The .NET default
    /// is 50; an installer that legitimately needs more than 5 hops is almost
    /// certainly hitting a misconfigured CDN or a redirect loop, and the right
    /// answer is to fail loud.
    /// </summary>
    public const int DefaultMaxRedirects = 5;

    /// <summary>
    /// Creates an <see cref="HttpClient"/> with the engine-standard redirect cap
    /// and User-Agent applied. Caller owns the returned instance and must dispose it.
    /// </summary>
    public static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            MaxAutomaticRedirections = DefaultMaxRedirects,
            AllowAutoRedirect = true
        };
        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }
}
