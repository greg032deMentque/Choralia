using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.Instructions;

public sealed class InstructionViewModel
{
    public Guid? Id { get; set; }
    public Guid SongId { get; set; }
    public VoicePartEnum? VoicePart { get; set; }
    public string? Title { get; set; }
    public string Content { get; set; } = string.Empty;
    public InstructionStatusEnum Status { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string AuthorUserId { get; set; } = string.Empty;
    public string? AuthorName { get; set; }
}

public sealed class InstructionViewModelMappingProfile : Profile
{
    public InstructionViewModelMappingProfile()
    {
        CreateMap<Instruction, InstructionViewModel>()
            .ForMember(dest => dest.AuthorName, opt => opt.MapFrom(src =>
                src.Author != null
                    ? $"{src.Author.Firstname} {src.Author.Lastname}".Trim()
                    : null));
    }
}
