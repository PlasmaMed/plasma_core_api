using System.ComponentModel.DataAnnotations;
using CorePlasma.Common.Models;

namespace CorePlasma.API.Integration;

public sealed class IntegrationReadQuery
{
    [Range(1, 1_000_000)]
    public int Page { get; set; } = 1;

    [Range(1, PagedRequest.MaxPageSize)]
    public int PageSize { get; set; } = PagedRequest.DefaultPageSize;

    [StringLength(200)]
    public string? Keyword { get; set; }

    public PagedRequest ToPagedRequest() => new() { Page = Page, PageSize = PageSize, Keyword = Keyword };
}
