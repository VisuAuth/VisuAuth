namespace VisuAuth.Abstractions.Common;

/// <summary>
/// A page of results plus the metadata needed to render pagination controls.
/// </summary>
public sealed record PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }

    public required int Total { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }

    public int TotalPages => PageSize == 0 ? 0 : (Total + PageSize - 1) / PageSize;

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;

    public static PagedResult<T> Empty(int page = 1, int pageSize = 25) => new()
    {
        Items = [],
        Total = 0,
        Page = page,
        PageSize = pageSize,
    };
}
