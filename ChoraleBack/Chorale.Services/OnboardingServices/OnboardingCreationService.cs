using System.Net;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.ViewModels.Onboarding;
using Microsoft.EntityFrameworkCore;
using ChoraleBackEnd.ViewModels.Events;
using ChoraleBackEnd.ViewModels.Choirs;

namespace ChoraleBackEnd.Services.OnboardingServices;

/// <summary>
/// Creation d'un premier espace en auto-service (lot 6, `10-Q22`) : depuis le chantier
/// d'administration, l'administrateur ne cree plus de chorale — sans ce service, plus rien ne
/// peut en create hors du seed de demonstration.
/// </summary>
/// <remarks>
/// Le palier Client est masque au createur individuel : sans "structure" renseignee, un
/// Client est cree en silence, nomme d'apres la chorale/l'evenement. Le mot "Client" n'apparait
/// jamais dans un message destine a l'utilisateur — on dit "structure". L'unicite du nom de
/// client a ete levee : chaque appel cree systematiquement un NOUVEAU client, sans chercher a
/// en reutiliser un homonyme.
/// </remarks>
public interface IOnboardingCreationService
{
    Task<ChoirViewModel> CreateChoirAsync(CreateChoirViewModel model, CancellationToken ct = default);
    Task<EventViewModel> CreateEventAsync(CreateEventViewModel model, CancellationToken ct = default);
}

public sealed class OnboardingCreationService : BaseService, IOnboardingCreationService
{
    private readonly IJoinCodeService _joinCodeService;

    public OnboardingCreationService(
        IServiceProvider serviceProvider, IJoinCodeService joinCodeService)
        : base(serviceProvider)
    {
        _joinCodeService = joinCodeService;
    }

    public async Task<ChoirViewModel> CreateChoirAsync(CreateChoirViewModel model, CancellationToken ct = default)
    {
        var currentUserId = await EnsureCreatorEmailConfirmeAsync(ct);

        var client = new Client
        {
            Id = ChoraleDbContext.NewIdGuid(),
            Name = string.IsNullOrWhiteSpace(model.Structure) ? model.Name : model.Structure.Trim(),
            Status = ClientStatusEnum.Active,
            ChoirLimit = Client.DefaultLimits.Choirs,
            MemberLimit = Client.DefaultLimits.Members,
            StorageQuotaBytes = Client.DefaultLimits.StorageOctets,
            MaxFileSizeBytes = Client.DefaultLimits.FileSizeBytes,
            IsDeleted = false
        };

        var choir = new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ClientId = client.Id,
            Name = model.Name,
            Description = model.Description,
            // Publie des la creation, comme ChoirController.Create (`10-Q22`) : une chorale
            // sans membre invisible d'elle-meme casserait le parcours d'auto-service qui vient
            // justement de l'amorcer.
            Status = ChoirStatusEnum.Published,
            IsDeleted = false
        };

        var space = new Space
        {
            Id = choir.Id,
            SpaceType = SpaceTypeEnum.Choir,
            ClientId = client.Id,
            EndDate = null,
            IsDeleted = false
        };

        var spaceMember = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = currentUserId,
            ChoirId = choir.Id,
            SpaceId = choir.Id,
            Status = MemberStatusEnum.Active,
            IsDeleted = false
        };

        _context.Clients.Add(client);
        _context.Choirs.Add(choir);
        _context.Spaces.Add(space);

        foreach (var voicePart in Enum.GetValues<VoicePartEnum>())
        {
            _context.Sections.Add(new Section
            {
                Id = ChoraleDbContext.NewIdGuid(),
                ChoirId = choir.Id,
                VoicePart = voicePart
            });
        }

        _context.ClientMembers.Add(new ClientMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ClientId = client.Id,
            UserId = currentUserId,
            Role = UserRoleEnum.ClientManager,
            IsDeleted = false
        });

        _context.SpaceMembers.Add(spaceMember);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceMemberId = spaceMember.Id,
            Role = UserRoleEnum.Manager
        });

        // Active des la creation, a la difference d'un espace existant (decision produit) :
        // une chorale sans membre n'a rien a proteger, et sans lui l'amorcage serait bloque
        // sur la saisie d'emails un par un.
        _context.SpaceJoinCodes.Add(
            _joinCodeService.CreateActiveCodeForNewSpaceWithoutSave(choir.Id));

        await _context.SaveChangesAsync(ct);
        return _mapper.Map<ChoirViewModel>(choir);
    }

    public async Task<EventViewModel> CreateEventAsync(CreateEventViewModel model, CancellationToken ct = default)
    {
        var currentUserId = await EnsureCreatorEmailConfirmeAsync(ct);

        var client = new Client
        {
            Id = ChoraleDbContext.NewIdGuid(),
            Name = string.IsNullOrWhiteSpace(model.Structure) ? model.Title : model.Structure.Trim(),
            Status = ClientStatusEnum.Active,
            ChoirLimit = Client.DefaultLimits.Choirs,
            MemberLimit = Client.DefaultLimits.Members,
            StorageQuotaBytes = Client.DefaultLimits.StorageOctets,
            MaxFileSizeBytes = Client.DefaultLimits.FileSizeBytes,
            IsDeleted = false
        };

        var evt = new Event
        {
            Id = ChoraleDbContext.NewIdGuid(),
            Title = model.Title,
            Description = model.Description,
            StartDate = model.StartDate,
            EndDate = model.EndDate,
            Type = model.Type,
            Location = model.Location ?? string.Empty,
            Status = EventStatusEnum.Draft,
            ChoirId = null,
            IsDeleted = false
        };

        // Espace.ClientId est obligatoire (migration 12) : jamais Guid.Empty, y compris pour
        // un evenement autonome — il herite ici du client fraichement cree, jamais laisse vide.
        _context.Clients.Add(client);
        _context.Events.Add(evt);
        _context.Spaces.Add(new Space
        {
            Id = evt.Id,
            SpaceType = SpaceTypeEnum.Event,
            ClientId = client.Id,
            EndDate = evt.EndDate ?? evt.StartDate,
            IsDeleted = false
        });

        _context.ClientMembers.Add(new ClientMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ClientId = client.Id,
            UserId = currentUserId,
            Role = UserRoleEnum.ClientManager,
            IsDeleted = false
        });

        var organizer = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = currentUserId,
            SpaceId = evt.Id,
            ChoirId = null,
            Status = MemberStatusEnum.Active,
            Presence = AttendanceEnum.NoReply,
            IsDeleted = false
        };
        _context.SpaceMembers.Add(organizer);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceMemberId = organizer.Id,
            Role = UserRoleEnum.Organizer
        });

        await _context.SaveChangesAsync(ct);
        return _mapper.Map<EventViewModel>(evt);
    }

    /// <summary>
    /// Bloquant pour CREER un espace (a la difference de REJOINDRE, non bloquant) : un compte
    /// non verifie doit couter une ligne et rien d'autre — ni client, ni espace, ni pupitres,
    /// ni quota (decision produit). Le controle intervient AVANT tout <c>Add</c> : aucune
    /// entite partielle ne peut donc atteindre le contexte si l'email n'est pas confirme.
    /// </summary>
    private async Task<string> EnsureCreatorEmailConfirmeAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_currentUserId))
            throw new CustomException(HttpStatusCode.Unauthorized, "Non authentifié.");

        var emailConfirme = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == _currentUserId)
            .Select(u => (bool?)u.EmailConfirmed)
            .FirstOrDefaultAsync(ct);

        if (emailConfirme != true)
            throw new CustomException(HttpStatusCode.Forbidden,
                "Vérifiez votre adresse email avant de créer une chorale ou un événement.");

        return _currentUserId;
    }
}
