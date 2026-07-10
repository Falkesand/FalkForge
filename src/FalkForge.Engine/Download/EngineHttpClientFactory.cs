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
        // Redirect policy (deliberate, reviewed):
        //  - Cross-host redirects stay ALLOWED. Real download hosts legitimately redirect to
        //    CDNs (S3, Azure Blob, Fastly); pinning redirects to the original host would break
        //    normal payload downloads. The residual blind-GET SSRF a hostile redirect enables
        //    is bounded: every downloaded byte is SHA-256/signature-verified downstream, so a
        //    redirected response can never be installed or executed.
        //  - https -> http downgrade redirects are BLOCKED by .NET itself: SocketsHttpHandler's
        //    RedirectHandler refuses to follow a redirect from a secure (https) request URI to
        //    an insecure (http) location (dotnet/runtime RedirectHandler.RequestNeedsRedirect),
        //    and initial URLs are already https-only (PayloadDownloader scheme check, BDL025 +
        //    UpdateChecker for the feed). Do not add a second, redundant scheme gate here.
        //  - .NET also strips the Authorization header on any redirect, so credentials cannot
        //    leak to a redirect target.
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
