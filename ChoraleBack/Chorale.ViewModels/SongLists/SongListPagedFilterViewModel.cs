namespace ChoraleBackEnd.ViewModels.SongLists;

public sealed class SongListPagedFilterViewModel : PaginateViewModel
{
    public Guid? EventId { get; set; }
}
