using System.ComponentModel.DataAnnotations;
using AutoMapper;
using ChoraleBackEnd.Common.Enums;

namespace ChoraleBackEnd.ViewModels.Choirs;

public sealed class ChoirViewModel
{
    public Guid? Id { get; set; }

    /// <summary>
    /// Client de rattachement, obligatoire a la creation (`10-D23`).
    /// </summary>
    [Required]
    public Guid ClientId { get; set; }

    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Lecture seule : la transition de statut passe exclusivement par
    /// <c>AdminChoirService.ChangeStatusAsync</c>, qui applique
    /// <c>ChoirStateHelper.IsTransitionAllowed</c>. Ignore en mapping retour (voir le profil
    /// ci-dessous) pour qu'un appel a <c>ChoirService.UpdateAsync</c> ne puisse jamais
    /// update le statut en contournant cette validation.
    /// </summary>
    public ChoirStatusEnum Status { get; set; }

    /// <summary>
    /// Email du compte qui devient chef de chœur (role Manager) a la creation — obligatoire
    /// pour Create, verifie en code dans <c>ChoirService.CreateAsync</c> et non via
    /// <c>[Required]</c> : ce DTO sert aussi de corps a Update, qui ne le lit jamais. Le
    /// compte doit deja exister (meme regle que <c>ClientService.AssignManagerAsync</c>) :
    /// le ResponsableClient createur ne devient jamais lui-meme membre de la chorale qu'il
    /// cree (`10-D23`).
    /// </summary>
    [EmailAddress]
    [MaxLength(256)]
    public string? ChoirMasterEmail { get; set; }
}

public sealed class ChoirViewModelMappingProfile : Profile
{
    public ChoirViewModelMappingProfile()
    {
        CreateMap<Data.Entities.Choir, ChoirViewModel>();

        CreateMap<ChoirViewModel, Data.Entities.Choir>()
            // ClientId n'est JAMAIS mappe depuis ce DTO : Update ne doit pas pouvoir
            // deplacer une chorale vers un autre client (10-D23). Create le pose
            // explicitement en code, apres verification d'appartenance — voir
            // ChoirService.CreateAsync.
            .ForMember(dest => dest.ClientId, opt => opt.Ignore())
            .ForMember(dest => dest.Status, opt => opt.Ignore())
            .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedByUserId, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.UpdatedByUserId, opt => opt.Ignore())
            .ForMember(dest => dest.Client, opt => opt.Ignore())
            .ForMember(dest => dest.Sections, opt => opt.Ignore())
            .ForMember(dest => dest.Members, opt => opt.Ignore())
            .ForMember(dest => dest.SongLists, opt => opt.Ignore())
            .ForMember(dest => dest.Events, opt => opt.Ignore());
    }
}
