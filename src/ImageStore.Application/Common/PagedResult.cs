namespace ImageStore.Application.Common;

/// <summary>A single page of results plus the information needed to render pagination.</summary>
/// <param name="Items">The items on the requested page.</param>
/// <param name="Page">The 1-based page number.</param>
/// <param name="PageSize">The maximum number of items per page.</param>
/// <param name="TotalCount">The total number of items across all pages.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    /// <summary>The total number of pages (0 when there are no items).</summary>
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
