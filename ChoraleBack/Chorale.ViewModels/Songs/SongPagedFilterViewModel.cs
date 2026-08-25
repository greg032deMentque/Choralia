using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.Songs;

public sealed class SongPagedFilterViewModel : PaginateViewModel
{
    public Guid? ChoirId { get; set; }
    public VoicePartEnum? VoicePart { get; set; }
    public SongStatusEnum? Status { get; set; }
    public SongPriorityEnum? Priority { get; set; }
}
