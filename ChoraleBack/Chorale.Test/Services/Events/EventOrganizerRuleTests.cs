using System.Net;
using System.Security.Claims;
using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Services.UserServices;
using ChoraleBackEnd.Test.Fakes;
using ChoraleBackEnd.Test.TestSupport;
using ChoraleBackEnd.ViewModels.ChoirMembers;
using ChoraleBackEnd.ViewModels.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Services.Events;

/// <summary>
/// D39 (`10-decisions.md`) : le rôle Organizer n'est affecté qu'à un événement autonome
/// (<c>ChoirId</c> nul). Un événement rattaché à une chorale est déjà géré par les
/// <c>Manager</c> de cette chorale — y affecter en plus un Organizer créerait deux chemins
/// d'autorité concurrents sur le même espace, sans règle pour les départager.
/// </summary>
/// <remarks>
/// PrÃ©cision produit (juillet 2026) : le rattachement d'un Ã©vÃ©nement Ã  une chorale se
/// décide exclusivement à sa création et ne peut plus jamais changer ensuite — ni pour
/// rattacher un événement autonome, ni pour le déplacer vers une autre chorale. La règle
/// D39 en devient une invariante stable : un Organizer affecté à la création d'un
/// Ã©vÃ©nement autonome ne peut plus Ãªtre invalidÃ© rÃ©troactivement par un rattachement
/// ultÃ©rieur, puisque ce rattachement n'existe plus comme chemin possible.
/// </remarks>
[TestFixture]
public sealed class EventOrganizerRuleTests
{
    private const string ManagerUserId = "manager-1";
    private const string MemberUserId = "membre-1";

    private ChoraleDbContext _context = null!;
    private IMapper _mapper = null!;
    private Guid _clientId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _mapper = new MapperConfiguration(
            cfg => cfg.AddMaps(typeof(EventViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

        var roleManager = BuildServiceProvider().GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(UserRoleEnum.Singer.ToString()))
            await roleManager.CreateAsync(new IdentityRole(UserRoleEnum.Singer.ToString()));

        _context.Users.Add(new User
        {
            Id = ManagerUserId, UserName = "manager@t.com", NormalizedUserName = "MANAGER@T.COM",
            Email = "manager@t.com", NormalizedEmail = "MANAGER@T.COM", EmailConfirmed = true
        });
        _context.Users.Add(new User
        {
            Id = MemberUserId, UserName = "membre@t.com", NormalizedUserName = "MEMBRE@T.COM",
            Email = "membre@t.com", NormalizedEmail = "MEMBRE@T.COM", EmailConfirmed = true
        });

        _clientId = ChoraleDbContext.NewIdGuid();
        _context.Clients.Add(new Client
        {
            Id = _clientId, Name = "Client Test", Status = ClientStatusEnum.Active,
            ChoirLimit = 10, MemberLimit = 100, StorageQuotaBytes = 1_000_000, MaxFileSizeBytes = 500_000
        });
        _context.ClientMembers.Add(new ClientMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ClientId = _clientId, UserId = ManagerUserId,
            Role = UserRoleEnum.ClientManager
        });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    // --- Garde d'affectation (EventAuthorizationService.EnsureOrganizerAssignable) ----------

    [Test]
    public void EnsureOrganizerAssignable_StandaloneEvent_DoesNotThrow()
        => Assert.DoesNotThrow(() => CreateAuthorizationService().EnsureOrganizerAssignable(null));

    [Test]
    public void EnsureOrganizerAssignable_AttachedEvent_ThrowsConflictAndNamesTheRule()
    {
        var choirId = ChoraleDbContext.NewIdGuid();

        var ex = Assert.Throws<CustomException>(
            () => CreateAuthorizationService().EnsureOrganizerAssignable(choirId));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(ex.FrontMessage, Does.Contain("organisateur"),
                "Le message doit nommer explicitement le rôle refusé.");
            Assert.That(ex.FrontMessage, Does.Contain("autonome"),
                "Le message doit nommer explicitement la règle (réservé aux événements autonomes).");
        });
    }

    // --- Création d'un événement (EventService.CreateAsync) ----------------------------------

    [Test]
    public async Task CreateAsync_StandaloneEvent_CreatorBecomesOrganizer()
    {
        var result = await CreateEventService().CreateAsync(new EventViewModel
        {
            Title = "Répétition",
            StartDate = DateTime.UtcNow.AddDays(1),
            Type = EventTypeEnum.Rehearsal,
            ChoirId = null
        });

        var isOrganizer = await _context.SpaceMemberRoles
            .AnyAsync(r => r.SpaceMember.SpaceId == result.Id
                        && r.SpaceMember.UserId == ManagerUserId
                        && r.Role == UserRoleEnum.Organizer);

        Assert.That(isOrganizer, Is.True);
    }

    [Test]
    public async Task CreateAsync_AttachedEvent_CreatorDoesNotBecomeOrganizer()
    {
        var choirId = await CreateChoirWithManagerAsync(ChoirStatusEnum.Published);

        var result = await CreateEventService().CreateAsync(new EventViewModel
        {
            Title = "Concert",
            StartDate = DateTime.UtcNow.AddDays(30),
            Type = EventTypeEnum.Concert,
            ChoirId = choirId
        });

        var hasRoleOnTheEvent = await _context.SpaceMemberRoles
            .AnyAsync(r => r.SpaceMember.SpaceId == result.Id && r.SpaceMember.UserId == ManagerUserId);

        Assert.That(hasRoleOnTheEvent, Is.False,
            "Le créateur d'un événement de chorale ne doit recevoir aucun rôle sur l'événement : "
            + "il le gère déjà via son rôle Manager sur la chorale porteuse (D39).");
    }

    // --- Non-régression : le Manager de la chorale porteuse gère l'événement rattaché -------

    [Test]
    public async Task Manager_ManagesAttachedEvent_InvitesParticipantWithoutBeingOrganizer()
    {
        var choirId = await CreateChoirWithManagerAsync(ChoirStatusEnum.Published);
        var evt = await CreateEventService().CreateAsync(new EventViewModel
        {
            Title = "Concert",
            StartDate = DateTime.UtcNow.AddDays(30),
            Type = EventTypeEnum.Concert,
            ChoirId = choirId
        });

        var participant = await CreateEventParticipantService().InviteAsync(new InviteEventParticipantViewModel
        {
            EventId = evt.Id!.Value,
            Email = "invite@t.com",
            Firstname = "Invité"
        });

        Assert.That(participant, Is.Not.Null);

        var invitedRole = await _context.SpaceMemberRoles
            .Where(r => r.SpaceMember.SpaceId == evt.Id && r.SpaceMember.UserId != ManagerUserId)
            .Select(r => r.Role)
            .FirstAsync();

        Assert.That(invitedRole, Is.EqualTo(UserRoleEnum.Participant),
            "Le Manager gère l'invitation sans être Organizer : on ferme un path d'autorité, "
            + "on ne doit pas en fermer deux.");
    }

    [Test]
    public async Task Manager_ManagesAttachedEvent_ChangesStatus()
    {
        var choirId = await CreateChoirWithManagerAsync(ChoirStatusEnum.Published);
        var eventService = CreateEventService();
        var evt = await eventService.CreateAsync(new EventViewModel
        {
            Title = "Concert",
            StartDate = DateTime.UtcNow.AddDays(30),
            Type = EventTypeEnum.Concert,
            Location = "Salle des fêtes",
            ChoirId = choirId
        });

        var result = await eventService.ChangeStatusAsync(evt.Id!.Value, EventStatusEnum.Published);

        Assert.That(result.Status, Is.EqualTo(EventStatusEnum.Published));
    }

    // --- Rattachement figé : ChoirId immuable après création ---------------------------------

    [Test]
    public async Task UpdateAsync_AttachingStandaloneEventToChoir_Rejected()
    {
        var eventService = CreateEventService();
        var evt = await eventService.CreateAsync(new EventViewModel
        {
            Title = "Répétition",
            StartDate = DateTime.UtcNow.AddDays(1),
            Type = EventTypeEnum.Rehearsal,
            ChoirId = null
        });
        var choirId = await CreateChoirWithManagerAsync(ChoirStatusEnum.Published);

        var ex = Assert.ThrowsAsync<CustomException>(() => eventService.UpdateAsync(new EventViewModel
        {
            Id = evt.Id,
            Title = evt.Title,
            StartDate = evt.StartDate,
            Type = evt.Type,
            ChoirId = choirId
        }));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var reloadedEvent = await _context.Events.AsNoTracking().FirstAsync(e => e.Id == evt.Id);
        Assert.That(reloadedEvent.ChoirId, Is.Null,
            "Un événement autonome ne doit jamais pouvoir être rattaché à une chorale après coup.");
    }

    [Test]
    public async Task UpdateAsync_ChangingChoirIdOfAttachedEvent_Rejected()
    {
        var choirA = await CreateChoirWithManagerAsync(ChoirStatusEnum.Published);
        var eventService = CreateEventService();
        var evt = await eventService.CreateAsync(new EventViewModel
        {
            Title = "Concert",
            StartDate = DateTime.UtcNow.AddDays(30),
            Type = EventTypeEnum.Concert,
            ChoirId = choirA
        });

        var choirB = await CreateChoirWithManagerAsync(ChoirStatusEnum.Published);

        var ex = Assert.ThrowsAsync<CustomException>(() => eventService.UpdateAsync(new EventViewModel
        {
            Id = evt.Id,
            Title = evt.Title,
            StartDate = evt.StartDate,
            Type = evt.Type,
            ChoirId = choirB
        }));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var reloadedEvent = await _context.Events.AsNoTracking().FirstAsync(e => e.Id == evt.Id);
        Assert.That(reloadedEvent.ChoirId, Is.EqualTo(choirA),
            "Un événement de chorale ne doit jamais pouvoir changer de chorale porteuse.");
    }

    [Test]
    public void MappingProfile_IgnoresChoirId_EvenWithoutServiceGuardBeingReached()
    {
        // Defense en profondeur : meme sans passer par EventService.UpdateAsync (donc sans
        // que la garde EventStateHelper.IsChoirIdChangeRequested soit sollicitee), le profil
        // AutoMapper seul ne doit jamais laisser un ChoirId du corps de requete ecraser
        // celui deja porte par l'entite — voir CreateMap<EventViewModel, Event>() dans
        // EventViewModel.cs.
        var mapper = new MapperConfiguration(
            cfg => cfg.AddMaps(typeof(EventViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

        var currentChoirId = ChoraleDbContext.NewIdGuid();
        var evt = new Event
        {
            Id = ChoraleDbContext.NewIdGuid(),
            Title = "Concert",
            StartDate = DateTime.UtcNow,
            Type = EventTypeEnum.Concert,
            ChoirId = currentChoirId
        };

        mapper.Map(new EventViewModel
        {
            Title = "Concert Renommé",
            StartDate = evt.StartDate,
            Type = EventTypeEnum.Concert,
            ChoirId = ChoraleDbContext.NewIdGuid()
        }, evt);

        Assert.Multiple(() =>
        {
            Assert.That(evt.ChoirId, Is.EqualTo(currentChoirId),
                "ChoirId ne doit jamais être écrasé par le mapping, même sans garde de service.");
            Assert.That(evt.Title, Is.EqualTo("Concert Renommé"),
                "Les autres champs restent bien mappés.");
        });
    }

    [Test]
    public async Task UpdateAsync_OtherFields_StayModifiable()
    {
        var eventService = CreateEventService();
        var evt = await eventService.CreateAsync(new EventViewModel
        {
            Title = "Répétition",
            StartDate = DateTime.UtcNow.AddDays(1),
            Type = EventTypeEnum.Rehearsal,
            ChoirId = null
        });

        var result = await eventService.UpdateAsync(new EventViewModel
        {
            Id = evt.Id,
            Title = "Répétition Renommée",
            StartDate = evt.StartDate.AddDays(1),
            Type = EventTypeEnum.Concert,
            Location = "Salle A",
            ChoirId = null
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Title, Is.EqualTo("Répétition Renommée"));
            Assert.That(result.Type, Is.EqualTo(EventTypeEnum.Concert));
            Assert.That(result.Location, Is.EqualTo("Salle A"));
            Assert.That(result.ChoirId, Is.Null);
        });
    }

    // --- Non-régression : les autres rôles ne sont pas concernés par cette garde ------------

    [Test]
    public async Task ChangeRoleAsync_Manager_NotAffectedByTheGuard()
    {
        var choirId = await CreateChoirWithManagerAsync(ChoirStatusEnum.Published);
        var targetId = await AddSpaceMemberChoirAsync(choirId, MemberUserId);

        var result = await CreateChoirMembersService().ChangeRoleAsync(choirId, new ChangeMemberRoleViewModel
        {
            Id = targetId,
            Role = UserRoleEnum.Manager
        });

        Assert.That(result, Is.Not.Null);
        var isManager = await _context.SpaceMemberRoles
            .AnyAsync(r => r.SpaceMemberId == targetId && r.Role == UserRoleEnum.Manager);
        Assert.That(isManager, Is.True);
    }

    [Test]
    public async Task ChangeRoleAsync_Singer_NotAffectedByTheGuard()
    {
        var choirId = await CreateChoirWithManagerAsync(ChoirStatusEnum.Published);
        var targetId = await AddSpaceMemberChoirAsync(choirId, MemberUserId);

        Assert.DoesNotThrowAsync(() => CreateChoirMembersService().ChangeRoleAsync(choirId, new ChangeMemberRoleViewModel
        {
            Id = targetId,
            Role = UserRoleEnum.Singer
        }));
    }

    // SectionLeader n'est pas rejoué ici : ChoirMembersService.ChangeRoleAsync n'a jamais eu
    // de branche Organizer dans son switch (default => "Rôle non supporté", inchangé par ce
    // lot) et ce lot ne modifie pas ChoirMembersService — le risque de régression introduit
    // ici y est nul. Le couvrir exigerait un pupitre et une appartenance de pupitre sans
    // rapport avec la règle D39 testée dans ce fichier.

    // --- Fabriques -----------------------------------------------------------------------------

    private async Task<Guid> CreateChoirWithManagerAsync(ChoirStatusEnum status)
    {
        var choirId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = _clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = choirId, ClientId = _clientId, Name = $"Choir {choirId}", Status = status
        });

        var member = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = choirId, SpaceId = choirId,
            UserId = ManagerUserId, Status = MemberStatusEnum.Active
        };
        _context.SpaceMembers.Add(member);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(), SpaceMemberId = member.Id, Role = UserRoleEnum.Manager
        });

        await _context.SaveChangesAsync();
        return choirId;
    }

    private async Task<Guid> AddSpaceMemberChoirAsync(Guid choirId, string userId)
    {
        var member = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = choirId, SpaceId = choirId,
            UserId = userId, Status = MemberStatusEnum.Active
        };
        _context.SpaceMembers.Add(member);
        await _context.SaveChangesAsync();
        return member.Id;
    }

    /// <summary>
    /// Reconstruit tout le conteneur DI — <c>IHttpContextAccessor</c> compris — a chaque
    /// appel, comme les autres suites d'evenements (<c>EventAttachmentTests</c>,
    /// <c>EventClientAutonomeTests</c>) : <see cref="HttpContextAccessor.HttpContext"/> est
    /// porte par un <c>AsyncLocal</c> STATIQUE. Le poser dans <c>[SetUp]</c> (une invocation
    /// async distincte de celle du corps de test sous NUnit) ne survit pas jusqu'au test —
    /// <c>_currentUserId</c> resout alors a <c>null</c> et chaque service leve "Non
    /// authentifié" ou un refus d'ecriture muet. Le poser ICI, appele depuis le corps meme
    /// du test, le garde dans le bon flot d'execution.
    /// </summary>
    private IServiceProvider BuildServiceProvider()
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, ManagerUserId)], "Test"))
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton(_mapper);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Frontend:BaseUrl"] = "http://localhost:4200" })
            .Build());
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<ILogService>(new LogService(httpContextAccessor));
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>()
            .AddEntityFrameworkStores<ChoraleDbContext>()
            .AddDefaultTokenProviders()
            .AddInvitationTokenProvider();

        return services.BuildServiceProvider();
    }

    private EventAuthorizationService CreateAuthorizationService()
    {
        var sp = BuildServiceProvider();
        return new EventAuthorizationService(sp, new ChoirAuthorizationService(sp, new MembershipService(sp)));
    }

    private EventService CreateEventService()
    {
        var sp = BuildServiceProvider();
        var authorizationService = new EventAuthorizationService(sp, new ChoirAuthorizationService(sp, new MembershipService(sp)));
        var auditLogService = new AuditLogService(sp);
        var guestAccountLifecycleService = new GuestAccountLifecycleService(sp, auditLogService);
        var clientRoleResolverService = new ClientRoleResolverService(_context);
        return new EventService(
            sp, authorizationService, guestAccountLifecycleService,
            clientRoleResolverService, new MembershipService(sp), new EventParticipationSeedingService(sp));
    }

    private EventParticipantService CreateEventParticipantService()
    {
        var sp = BuildServiceProvider();
        var authorizationService = new EventAuthorizationService(sp, new ChoirAuthorizationService(sp, new MembershipService(sp)));
        var userInvitationService = new UserInvitationService(sp, new FakeEmailService());
        return new EventParticipantService(
            sp, authorizationService, userInvitationService,
            new FakeServiceLimitService(), new MembershipService(sp), new AuditLogService(sp));
    }

    private ChoirMembersService CreateChoirMembersService()
    {
        var sp = BuildServiceProvider();
        var sectionService = new SectionService(sp);
        var auditLogService = new AuditLogService(sp);
        return new ChoirMembersService(
            sp, sectionService, auditLogService,
            new FakeServiceLimitService(), new MembershipService(sp),
            new UserInvitationService(sp, new FakeEmailService()), new MemberEnrollmentService(sp),
            new SectionVoicePartLookupService(_context));
    }
}
