using System.Linq.Expressions;

namespace Freebuff.Platform.Infrastructure.CompanyScope;

public static class CompanyScopeQueryExtensions
{
    /// <summary>
    /// Applies the resolved company scope to a tenant-scoped query:
    ///  - no scope context (background jobs, seed) or unconstrained scope → unchanged
    ///  - constrained scope → rows must belong to one of the effective company ids
    /// Callers pass the <see cref="ResolvedCompanyScope"/> produced for the request.
    /// </summary>
    public static IQueryable<TEntity> InEffectiveCompanyScope<TEntity>(
        this IQueryable<TEntity> query,
        ResolvedCompanyScope? scope,
        Expression<Func<TEntity, Guid>> companyIdSelector)
    {
        if (scope is null || !scope.IsConstrained)
            return query;

        var ids = new List<Guid>(scope.EffectiveCompanyIds ?? Array.Empty<Guid>());
        if (ids.Count == 0)
            return query.Where(_ => false);

        var param = companyIdSelector.Parameters[0];
        var body = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Contains),
            new[] { typeof(Guid) },
            Expression.Constant(ids),
            companyIdSelector.Body);
        var lambda = Expression.Lambda<Func<TEntity, bool>>(body, param);
        return query.Where(lambda);
    }
}
