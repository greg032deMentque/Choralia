using System.ComponentModel.DataAnnotations;

namespace ChoraleBackEnd.ViewModels;

public class PaginateViewModel
{
    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;

    public string? SortDirection { get; set; }
    public string? SortActive { get; set; }
    public string? Filter { get; set; }

    public int Offset => (Page - 1) * PageSize;
}
