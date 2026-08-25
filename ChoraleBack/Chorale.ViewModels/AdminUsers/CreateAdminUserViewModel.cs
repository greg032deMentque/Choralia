using System.ComponentModel.DataAnnotations;
using AutoMapper;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.AdminUsers;

public sealed class CreateAdminUserViewModel
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Firstname { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Lastname { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    [RegularExpression(
        @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z0-9]).+$",
        ErrorMessage = "Le mot de passe doit contenir au moins une majuscule, une minuscule, un chiffre et un caractère spécial.")]
    public string Password { get; set; } = string.Empty;
}

public sealed class CreateAdminUserViewModelMappingProfile : Profile
{
    public CreateAdminUserViewModelMappingProfile()
    {
        CreateMap<CreateAdminUserViewModel, User>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.UserName, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedByUserId, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedByUserId, opt => opt.Ignore());
    }
}
