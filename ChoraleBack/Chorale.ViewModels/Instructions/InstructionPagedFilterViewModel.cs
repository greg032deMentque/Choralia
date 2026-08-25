using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.Instructions;

public sealed class InstructionPagedFilterViewModel : PaginateViewModel
{
    public Guid? SongId { get; set; }
    public VoicePartEnum? VoicePart { get; set; }
    public InstructionStatusEnum? Status { get; set; }
}
