namespace EAIOS.Api.Application.Common.Models;

/// <summary>
/// Wrapper de réponse API standardisé.
/// </summary>
public sealed record ApiResponse<T>(T Data, ApiMeta Meta);

public sealed record ApiMeta(long Timestamp = 0)
{
    public static ApiMeta Now => new(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}

public static class ApiResponse
{
    public static ApiResponse<T> Wrap<T>(T data) => new(data, ApiMeta.Now);

    public static PagedApiResponse<T> List<T>(IReadOnlyList<T> items, int total, int page, int pageSize) =>
        new(items, new PagedMeta(page, pageSize, total, (int)Math.Ceiling((double)total / pageSize)),
            new ApiMeta(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
}

public sealed record PagedApiResponse<T>(IReadOnlyList<T> Data, PagedMeta Pagination, ApiMeta Meta);

public sealed record PagedMeta(int Page, int PageSize, int TotalCount, int TotalPages)
{
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}

/// <summary>
/// Résultat paginé renvoyé par les repositories.
/// </summary>
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items      { get; }
    public int              Page       { get; }
    public int              PageSize   { get; }
    public int              TotalCount { get; }
    public int              TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

    public PagedResult(IReadOnlyList<T> items, int page, int pageSize, int total)
    {
        Items      = items;
        Page       = page;
        PageSize   = pageSize;
        TotalCount = total;
    }
}
