using CorePlasma.Common.Enums;

namespace CorePlasma.Common.Models;

/// <summary>
/// Base query parameters for paged lists.
/// </summary>
public class PagedRequest
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 200;

    private int _page = 1;
    private int _pageSize = DefaultPageSize;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? DefaultPageSize : Math.Min(value, MaxPageSize);
    }

    public string? Keyword { get; set; }

    public string? SortBy { get; set; }

    public SortDirection SortDirection { get; set; } = SortDirection.Asc;
}
