using AutoMapper;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.Choirs;

public sealed class SectionMemberViewModel
{
    public Guid? Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid SectionId { get; set; }
    public string? UserFullName { get; set; }
    public string? UserEmail { get; set; }
}

public sealed class SectionMemberViewModelMappingProfile : Profile
{
    public SectionMemberViewModelMappingProfile()
    {
        CreateMap<SectionMember, SectionMemberViewModel>()
            .ForMember(dest => dest.UserFullName, opt => opt.MapFrom(src =>
                src.User != null
                    ? $"{src.User.Firstname} {src.User.Lastname}".Trim()
                    : null))
            .ForMember(dest => dest.UserEmail, opt => opt.MapFrom(src =>
                src.User != null ? src.User.Email : null));

        CreateMap<SectionMemberViewModel, SectionMember>()
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedByUserId, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedByUserId, opt => opt.Ignore())
            .ForMember(dest => dest.User, opt => opt.Ignore())
            .ForMember(dest => dest.Section, opt => opt.Ignore());
    }
}
