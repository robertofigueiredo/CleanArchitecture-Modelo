using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Application.Abstractions.Pagination;

/// <summary>
/// Centralizes the count/skip/take/project shape every paginated query handler needs, so that
/// logic isn't re-implemented per handler. See docs/engineering/API_RESPONSE_GUIDELINES.md for
/// the full rationale.
/// </summary>
public static class PaginationQueryableExtensions
{
    /// <summary>
    /// Executes <paramref name="source"/> as a paginated, projected query: one <c>CountAsync</c>
    /// against the full filtered set, then (only if there are any records) one
    /// <c>Skip().Take().Select().ToListAsync()</c> for the requested page. Never materializes the
    /// full result set in memory.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="source"/> has no explicit <c>OrderBy</c>/<c>OrderByDescending</c>.
    /// Pagination over an unordered query can return duplicate/skipped rows across pages —
    /// every paginated query must supply a deterministic order (a unique/stable key as the
    /// final tiebreaker) before calling this method.
    /// </exception>
    public static async Task<PagedResult<TResult>> ToPagedResultAsync<TSource, TResult>(
        this IQueryable<TSource> source,
        PaginationRequest pagination,
        Expression<Func<TSource, TResult>> selector,
        CancellationToken cancellationToken)
    {
        if (!IsOrdered(source))
        {
            throw new InvalidOperationException(
                $"Paginated query for '{typeof(TSource).Name}' has no OrderBy/OrderByDescending. " +
                "Pagination requires a deterministic order — add one before calling ToPagedResultAsync.");
        }

        int totalRecords = await source.CountAsync(cancellationToken);

        if (totalRecords == 0)
        {
            return PagedResult<TResult>.Create([], pagination, totalRecords: 0);
        }

        List<TResult> items = await source
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .Select(selector)
            .ToListAsync(cancellationToken);

        return PagedResult<TResult>.Create(items, pagination, totalRecords);
    }

    private static bool IsOrdered(IQueryable source)
    {
        Expression expression = source.Expression;

        while (expression is MethodCallExpression methodCall)
        {
            if (methodCall.Method.Name is nameof(Queryable.OrderBy) or nameof(Queryable.OrderByDescending))
            {
                return true;
            }

            if (methodCall.Arguments.Count == 0)
            {
                break;
            }

            expression = methodCall.Arguments[0];
        }

        return false;
    }
}
