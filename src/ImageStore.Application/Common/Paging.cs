namespace ImageStore.Application.Common;

/// <summary>Base class for query-string objects that carry pagination parameters.</summary>
public class PagedQuery
{
    /// <summary>1-based page number (default 1).</summary>
    public int? Page { get; set; }

    /// <summary>Items per page (default and maximum depend on the endpoint).</summary>
    public int? PageSize { get; set; }
}

/// <summary>Shared paging validation and normalization for list endpoints.</summary>
public static class Paging
{
    public const int DefaultPageSize = 12;
    public const int MaxPageSize = 100;

    public static void Validate(int? page, int? pageSize, ValidationErrors errors, int maxPageSize = MaxPageSize)
    {
        if (page is < 1)
        {
            errors.Add("page", "Page must be 1 or greater.");
        }

        if (pageSize is < 1 || pageSize > maxPageSize)
        {
            errors.Add("pageSize", $"Page size must be between 1 and {maxPageSize}.");
        }
    }

    public static (int Page, int PageSize) Normalize(int? page, int? pageSize, int defaultPageSize = DefaultPageSize) =>
        (page ?? 1, pageSize ?? defaultPageSize);
}
