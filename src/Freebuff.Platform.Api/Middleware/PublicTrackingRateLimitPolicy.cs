using System.Threading.RateLimiting;

namespace Freebuff.Platform.Api.Middleware;

/// <summary>
/// Rate limiting for the unauthenticated public tracking surface. The token
/// space is unguessable (128+ bits), but a limiter is still required so the
/// endpoint can't be scraped or probed at machine speed. Two independent
/// limiters are chained (either rejecting ⇒ the request is rejected):
///   - per-IP:    rapid sequential token-guessing bursts (each guess carries a
///                DIFFERENT token, so a token-keyed limiter alone would never
///                engage — the IP is what's constant across a guessing burst);
///   - per-token: a valid, shared/scraped link can't be hammered.
/// Both are backed by one PartitionedRateLimiter cache (a single limiter
/// instance per key, held for the app lifetime) so budgets survive across
/// requests. Non-public paths get a no-op partition — internal endpoints are
/// untouched. Registered as the app-wide GlobalLimiter; the token is parsed
/// from the URL path so the limiter does not depend on routing metadata.
/// </summary>
public sealed class PublicTrackingRateLimitPolicy
{
    private const string PublicPrefix = "/api/v1/public/trips";

    public PartitionedRateLimiter<HttpContext> Limiter { get; }

    public PublicTrackingRateLimitPolicy(FixedWindowRateLimiterOptions options)
    {
        static bool IsPublic(HttpContext ctx) => ctx.Request.Path.StartsWithSegments(PublicPrefix);

        static string IpOf(HttpContext ctx)
            => ctx.Connection.RemoteIpAddress?.ToString() ?? "loopback";

        static string? TokenOf(HttpContext ctx)
        {
            // /api/v1/public/trips/{token}[/photos/{file}] → token at index 4.
            var segments = ctx.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments is { Length: >= 5 } ? segments[4] : null;
        }

        var perIp = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            IsPublic(ctx)
                ? RateLimitPartition.GetFixedWindowLimiter($"ip:{IpOf(ctx)}", _ => options)
                : RateLimitPartition.GetNoLimiter<string>("internal"));

        var perToken = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            IsPublic(ctx)
                ? RateLimitPartition.GetFixedWindowLimiter(TokenOf(ctx) ?? $"ip:{IpOf(ctx)}", _ => options)
                : RateLimitPartition.GetNoLimiter<string>("internal"));

        Limiter = PartitionedRateLimiter.CreateChained(perIp, perToken);
    }
}