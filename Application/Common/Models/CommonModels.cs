namespace EAIOS.Api.Application.Common.Models;

/// <summary>Generic paginated result wrapper.</summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}

/// <summary>Standard API response wrapper { "data": T }.</summary>
public static class ApiResponse
{
    public static object Wrap<T>(T data) => new { data };
    public static object List<T>(IEnumerable<T> items, int total, int page, int pageSize) =>
        new { data = items, meta = new { total, page, pageSize, totalPages = (int)Math.Ceiling(total / (double)pageSize) } };
}

/// <summary>Access control evaluation result.</summary>
public sealed record PermissionCheckResult(
    bool Allowed,
    string Permission,
    string EvaluatedBy,  // "rbac", "abac", "resource-policy"
    string? DenyReason = null);
