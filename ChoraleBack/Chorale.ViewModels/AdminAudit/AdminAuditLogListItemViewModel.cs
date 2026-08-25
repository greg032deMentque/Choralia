using AutoMapper;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.AdminAudit;

public sealed class AdminAuditLogListItemViewModel
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Nom affichable de l'acteur, enrichi apres pagination. Replie sur un libelle lisible
    /// quand l'acteur n'est plus resolvable (compte supprime, ou action systeme sans acteur) —
    /// le journal d'audit doit rester lisible meme quand l'entite qu'il trace a disparu.
    /// </summary>
    public string UserFullName { get; set; } = string.Empty;

    public string? UserEmail { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Detail { get; set; }
    public DateTime OccurredAt { get; set; }
}

public sealed class AdminAuditLogListItemViewModelMappingProfile : Profile
{
    public AdminAuditLogListItemViewModelMappingProfile()
    {
        CreateMap<AdminAuditLog, AdminAuditLogListItemViewModel>()
            .ForMember(dest => dest.UserFullName, opt => opt.Ignore())
            .ForMember(dest => dest.UserEmail, opt => opt.Ignore());
    }
}
