namespace VisuAuth.Abstractions.Common;

/// <summary>
/// A page of results plus the metadata needed to render forward (cursor-based)
/// pagination controls.
/// </summary>
/// <remarks>
/// <para>
/// Pagination is <b>cursor-based and forward-only</b>. A backend returns an
/// opaque <see cref="NextCursor"/> that the caller passes back (via
/// <c>UserFilter.Cursor</c> / <c>AuditFilter.Cursor</c>) to fetch the page that
/// follows; the cursor is meaningless to the caller and must be treated as a
/// black box. This matches stores whose only paging primitive is a forward
/// continuation token — most importantly Microsoft Graph's
/// <c>@odata.nextLink</c> — while EF-backed stores encode an offset into the
/// same shape.
/// </para>
/// <para>
/// There is deliberately no page number, total-page count, or "previous"
/// cursor: a forward continuation token can't express any of those. The admin
/// UI renders "previous" via the browser's history (each page push carries the
/// cursor in the URL). <see cref="TotalCount"/> is optional — EF stores fill it
/// from a cheap <c>COUNT</c>, cursor-only backends (Graph) leave it
/// <see langword="null"/>.
/// </para>
/// </remarks>
/// <typeparam name="T">Element type of the page.</typeparam>
public sealed record PagedResult<T>
{
    /// <summary>The items on this page.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>
    /// Opaque token for fetching the next page, or <see langword="null"/> when
    /// this is the last page. Pass it back unchanged in the filter's
    /// <c>Cursor</c> property — never parse or construct it.
    /// </summary>
    public string? NextCursor { get; init; }

    /// <summary>
    /// Total number of matching rows when the backend can supply it cheaply
    /// (EF stores do); <see langword="null"/> for cursor-only backends such as
    /// Microsoft Graph, which don't return a count alongside a page.
    /// </summary>
    public int? TotalCount { get; init; }

    /// <summary>True when another page follows (i.e. a cursor was returned).</summary>
    public bool HasMore => NextCursor is not null;

    /// <summary>Returns an empty page (no items, no cursor, no count).</summary>
    public static PagedResult<T> Empty() => new()
    {
        Items = [],
        NextCursor = null,
        TotalCount = null,
    };
}
