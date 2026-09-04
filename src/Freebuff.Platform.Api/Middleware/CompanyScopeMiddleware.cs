using Freebuff.Platform.Infrastructure.CompanyScope;
using Microsoft.Extensions.Primitives;

namespace Freebuff.Platform.Api.Middleware;

/// <summary>
/// Resolves the effective company scope once per request (after authentication)
/// and stores it in HttpContext.Items for downstream controllers/services.
/// Stateless: no scope is persisted server-side; the header expresses intent and
/// the resolver intersects it with the user's permitted-company set, logging any
/// requested-but-unpermitted companies.
/// </summary>
public sealed class CompanyScopeMiddleware
{
    private readonly RequestDelegate _next;

    public CompanyScopeMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ICompanyScopeResolver resolver)
    {
        var header = StringValues.Empty;
        if (context.Request.Headers.TryGetValue(CompanyScopePolicy.HeaderName, out var value))
            header = value;

        context.Items[CompanyScopePolicy.ItemsKey] = await resolver.ResolveAsync(
            context.User,
            header.Count > 0 ? header[0] : null,
            context.RequestAborted);

        await _next(context);
    }
}
