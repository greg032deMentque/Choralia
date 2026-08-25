using ChoraleBackEnd.ViewModels.Events;
using System.Net;
using System.Security.Claims;
using AutoMapper;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Services.UserServices;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.Test.Fakes;
using ChoraleBackEnd.Test.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Services.Events;

/// <summary>
/// Invitation d'un participant a un evenement (lot "invitation participant evenement").
/// </summary>
/// <remarks>
/// Avant ce lot, un evenement autonome (sans chorale porteuse) n'avait aucun moyen d'inviter
/// qui que ce soit depuis l'application. Ces tests protegent en particulier le cas d'consommation qui
/// motive le lot : l'invitation sur un evenement autonome doit fonctionner ET rester soumise
/// au plafond de membres de son propre client de rattachement (`Space.ClientId`, depuis la
/// migration 12), exactement comme une invitation de chorale.
///
/// Differences deliberees avec <c>ChoirMembersService.InviteAsync</c> : le role attribue est
/// <c>Participant</c> (jamais <c>Singer</c>), aucune voix ni pupitre n'est affecte, et la
/// <c>Presence</c> reste nulle a l'invitation â€” c'est au participant de repondre (RSVP), pas a
/// l'invitant de repondre pour lui.
/// </remarks>
[TestFixture]
public sealed class EventParticipantInvitationTests
{
    private const string OrganizerUserId = "organisateur-invit-1";
    private const string ParticipantSimpleUserId = "participant-invit-1";
    private const string ExistingUserId = "existant-invit-1";
    private const string ExistingUserEmail = "existant-invit@test.com";

    private ChoraleDbContext _context = null!;
    private Guid _clientId;
    private Guid _suspendedClientId;
    private Guid _attachedChoirId;
    private Guid _standaloneEventId;
    private Guid _attachedEventId;
    private Guid _suspendedClientEventId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _clientId = ChoraleDbContext.NewIdGuid();
        _suspendedClientId = ChoraleDbContext.NewIdGuid();
        _attachedChoirId = ChoraleDbContext.NewIdGuid();
        _standaloneEventId = ChoraleDbContext.NewIdGuid();
        _attachedEventId = ChoraleDbContext.NewIdGuid();
        _suspendedClientEventId = ChoraleDbContext.NewIdGuid();

        _context.Clients.Add(new Client
        {
            Id = _clientId, Name = "Client Autonome Test", Status = ClientStatusEnum.Active,
            ChoirLimit = 10, MemberLimit = 100, StorageQuotaBytes = 1_000_000, MaxFileSizeBytes = 500_000
        });
        _context.Clients.Add(new Client
        {
            Id = _suspendedClientId, Name = "Client Suspendu Test", Status = ClientStatusEnum.Suspended,
            ChoirLimit = 10, MemberLimit = 100, StorageQuotaBytes = 1_000_000, MaxFileSizeBytes = 500_000
        });

        _context.Users.Add(new User { Id = OrganizerUserId, UserName = "organisateur-invit@test.com", Email = "organisateur-invit@test.com" });
        _context.Users.Add(new User { Id = ParticipantSimpleUserId, UserName = "participant-invit@test.com", Email = "participant-invit@test.com" });
        // Compte invite existant (guest, jamais revendique) : simule quelqu'un deja invite
        // ailleurs (ex. une chorale) et pas encore connecte. UserInvitationService ne
        // reutilise QUE ce type de compte sans le dupliquer — un compte deja actif et
        // revendique reste hors de scope de cette invitation (voir UserInvitationService).
        // NormalizedEmail renseigne comme le ferait UserManager.CreateAsync : la collision
        // d'email se juge desormais sur cette colonne, pas sur Email brut.
        _context.Users.Add(new User
        {
            Id = ExistingUserId, UserName = ExistingUserEmail, Email = ExistingUserEmail,
            NormalizedUserName = ExistingUserEmail.ToUpperInvariant(),
            NormalizedEmail = ExistingUserEmail.ToUpperInvariant(),
            Firstname = "Existant", Lastname = string.Empty, IsGuestAccount = true, EmailConfirmed = false
        });

        // --- Event autonome : le cas d'consommation qui motive le lot -------------------------
        _context.Spaces.Add(new Space { Id = _standaloneEventId, SpaceType = SpaceTypeEnum.Event, ClientId = _clientId });
        _context.Events.Add(new Event
        {
            Id = _standaloneEventId, Title = "Concert Autonome", Location = "Salle",
            StartDate = DateTime.UtcNow.AddDays(7), Type = EventTypeEnum.Concert,
            Status = EventStatusEnum.Published, ChoirId = null
        });

        var standaloneOrganizer = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), UserId = OrganizerUserId, SpaceId = _standaloneEventId,
            ChoirId = null, Status = MemberStatusEnum.Active
        };
        _context.SpaceMembers.Add(standaloneOrganizer);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(), SpaceMemberId = standaloneOrganizer.Id, Role = UserRoleEnum.Organizer
        });

        var standaloneSimpleParticipant = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), UserId = ParticipantSimpleUserId, SpaceId = _standaloneEventId,
            ChoirId = null, Status = MemberStatusEnum.Active
        };
        _context.SpaceMembers.Add(standaloneSimpleParticipant);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(), SpaceMemberId = standaloneSimpleParticipant.Id, Role = UserRoleEnum.Participant
        });

        // --- Event rattache a une chorale : pour les tests Annule/Archive ---------------
        _context.Spaces.Add(new Space { Id = _attachedChoirId, SpaceType = SpaceTypeEnum.Choir, ClientId = _clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = _attachedChoirId, ClientId = _clientId, Name = "Choir Rattachee Test", Status = ChoirStatusEnum.Published
        });
        _context.Spaces.Add(new Space { Id = _attachedEventId, SpaceType = SpaceTypeEnum.Event, ClientId = _clientId });
        _context.Events.Add(new Event
        {
            Id = _attachedEventId, Title = "Concert Rattache", Location = "Salle",
            StartDate = DateTime.UtcNow.AddDays(7), Type = EventTypeEnum.Concert,
            Status = EventStatusEnum.Published, ChoirId = _attachedChoirId
        });

        var attachedOrganizer = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), UserId = OrganizerUserId, SpaceId = _attachedEventId,
            ChoirId = _attachedChoirId, Status = MemberStatusEnum.Active
        };
        _context.SpaceMembers.Add(attachedOrganizer);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(), SpaceMemberId = attachedOrganizer.Id, Role = UserRoleEnum.Organizer
        });

        // --- Event autonome rattache a un client suspendu -------------------------------
        _context.Spaces.Add(new Space { Id = _suspendedClientEventId, SpaceType = SpaceTypeEnum.Event, ClientId = _suspendedClientId });
        _context.Events.Add(new Event
        {
            Id = _suspendedClientEventId, Title = "Concert Client Suspendu", Location = "Salle",
            StartDate = DateTime.UtcNow.AddDays(7), Type = EventTypeEnum.Concert,
            Status = EventStatusEnum.Published, ChoirId = null
        });

        var suspendedClientOrganizer = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), UserId = OrganizerUserId, SpaceId = _suspendedClientEventId,
            ChoirId = null, Status = MemberStatusEnum.Active
        };
        _context.SpaceMembers.Add(suspendedClientOrganizer);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(), SpaceMemberId = suspendedClientOrganizer.Id, Role = UserRoleEnum.Organizer
        });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Test]
    public async Task InviteAsync_UnknownEmail_CreatesInvitedAccountWithoutVoicePartOrSectionNullPresence()
    {
        var (sut, _) = await BuildSutAsync(OrganizerUserId);

        var result = await sut.InviteAsync(new InviteEventParticipantViewModel
        {
            EventId = _standaloneEventId,
            Email = "inconnu-invit@test.com",
            Firstname = "Ana"
        });

        var member = await _context.SpaceMembers.AsNoTracking().FirstAsync(m => m.Id == result.Id);

        Assert.Multiple(() =>
        {
            Assert.That(member.Status, Is.EqualTo(MemberStatusEnum.Invited),
                "Un participant invite n'est pas actif : c'est la connexion qui le promeut.");
            Assert.That(member.Presence, Is.Null,
                "La presence remaining sans reponse tant que le participant n'a pas repondu lui-meme.");
            Assert.That(result.Roles, Is.EqualTo(new List<string> { nameof(UserRoleEnum.Participant) }));
        });

        var user = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == member.UserId);
        Assert.That(user.IsGuestAccount, Is.True);

        var hasASection = await _context.SectionMembers.AnyAsync(mp => mp.UserId == member.UserId);
        Assert.That(hasASection, Is.False, "Un participant d'evenement n'a ni voix ni pupitre.");
    }

    [Test]
    public async Task InviteAsync_ExistingUser_AttachedWithoutDuplicateAccount()
    {
        var (sut, _) = await BuildSutAsync(OrganizerUserId);

        // Compte deja invite ailleurs (guest, non confirme) : doit etre reutilise tel quel
        // pour cette nouvelle participation, sans jamais create un second compte.
        var result = await sut.InviteAsync(new InviteEventParticipantViewModel
        {
            EventId = _standaloneEventId,
            Email = ExistingUserEmail
        });

        Assert.That(result.UserId, Is.EqualTo(ExistingUserId));

        var accountCount = await _context.Users.CountAsync(u => u.Email == ExistingUserEmail);
        Assert.That(accountCount, Is.EqualTo(1), "Aucun second compte ne doit etre cree pour un email deja connu.");
    }

    [Test]
    public async Task InviteAsync_ParticipantAlreadyAttached_ThrowsConflictWithoutDuplicate()
    {
        var (sut, _) = await BuildSutAsync(OrganizerUserId);

        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), UserId = ExistingUserId, SpaceId = _standaloneEventId,
            ChoirId = null, Status = MemberStatusEnum.Active
        });
        await _context.SaveChangesAsync();

        var exception = Assert.ThrowsAsync<CustomException>(() => sut.InviteAsync(new InviteEventParticipantViewModel
        {
            EventId = _standaloneEventId,
            Email = ExistingUserEmail
        }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        var participationCount = await _context.SpaceMembers
            .CountAsync(m => m.UserId == ExistingUserId && m.SpaceId == _standaloneEventId);
        Assert.That(participationCount, Is.EqualTo(1), "Aucun doublon de SpaceMember ne doit etre cree.");
    }

    [Test]
    public async Task InviteAsync_BySimpleParticipant_ThrowsForbidden()
    {
        var (sut, _) = await BuildSutAsync(ParticipantSimpleUserId);

        var exception = Assert.ThrowsAsync<CustomException>(() => sut.InviteAsync(new InviteEventParticipantViewModel
        {
            EventId = _standaloneEventId,
            Email = "quelconque@test.com"
        }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task InviteAsync_StandaloneEvent_ClientCapReached_ThrowsConflictWithoutPartialEntity()
    {
        // Le plafond porte sur le client de rattachement de l'EVENEMENT lui-meme
        // (Espace.ClientId), alors qu'aucune chorale n'existe pour ce client : la preuve que
        // l'evenement autonome n'est pas exempte du plafond (`10-D23`).
        var client = await _context.Clients.FirstAsync(c => c.Id == _clientId);
        client.MemberLimit = 0;
        await _context.SaveChangesAsync();

        var (sut, _) = await BuildSutAsync(OrganizerUserId);

        const string notYetCreatedEmail = "plafond-invit@test.com";
        var exception = Assert.ThrowsAsync<CustomException>(() => sut.InviteAsync(new InviteEventParticipantViewModel
        {
            EventId = _standaloneEventId,
            Email = notYetCreatedEmail
        }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(exception.Message, Does.Contain("0"), "Le refus doit nommer la limite atteinte.");

        var accountCreated = await _context.Users.AnyAsync(u => u.Email == notYetCreatedEmail);
        Assert.That(accountCreated, Is.False, "Aucun compte ne doit etre cree quand le plafond est deja atteint.");
    }

    [Test]
    public async Task InviteAsync_SuspendedClient_ThrowsRejection()
    {
        var (sut, _) = await BuildSutAsync(OrganizerUserId);

        var exception = Assert.ThrowsAsync<CustomException>(() => sut.InviteAsync(new InviteEventParticipantViewModel
        {
            EventId = _suspendedClientEventId,
            Email = "quelconque-suspendu@test.com"
        }));

        Assert.That(exception, Is.Not.Null);
    }

    [Test]
    public async Task InviteAsync_EventAttachedToCancelledChoir_ThrowsConflict()
    {
        var choir = await _context.Choirs.FirstAsync(c => c.Id == _attachedChoirId);
        choir.Status = ChoirStatusEnum.Cancelled;
        await _context.SaveChangesAsync();

        var (sut, _) = await BuildSutAsync(OrganizerUserId);

        var exception = Assert.ThrowsAsync<CustomException>(() => sut.InviteAsync(new InviteEventParticipantViewModel
        {
            EventId = _attachedEventId,
            Email = "quelconque-annule@test.com"
        }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task InviteAsync_EventAttachedToArchivedChoir_ThrowsRejection()
    {
        var choir = await _context.Choirs.FirstAsync(c => c.Id == _attachedChoirId);
        choir.Status = ChoirStatusEnum.Archived;
        await _context.SaveChangesAsync();

        var (sut, _) = await BuildSutAsync(OrganizerUserId);

        Assert.ThrowsAsync<CustomException>(() => sut.InviteAsync(new InviteEventParticipantViewModel
        {
            EventId = _attachedEventId,
            Email = "quelconque-archive@test.com"
        }));
    }

    [Test]
    public async Task InviteAsync_SentEmail_CarriesEventSubjectNotChoir()
    {
        var (sut, fakeEmailService) = await BuildSutAsync(OrganizerUserId);

        await sut.InviteAsync(new InviteEventParticipantViewModel
        {
            EventId = _standaloneEventId,
            Email = "objet-invit@test.com"
        });

        Assert.That(fakeEmailService.SentEmails, Has.Count.EqualTo(1));
        Assert.That(fakeEmailService.SentEmails[0].Subject, Is.EqualTo("Invitation à rejoindre un événement"));
    }

    [Test]
    public async Task InviteAsync_ProducesAnAdminAuditLogRow()
    {
        var (sut, _) = await BuildSutAsync(OrganizerUserId);

        var before = await _context.AdminAuditLogs.CountAsync();

        var result = await sut.InviteAsync(new InviteEventParticipantViewModel
        {
            EventId = _standaloneEventId,
            Email = "audit-invit@test.com"
        });

        var after = await _context.AdminAuditLogs.CountAsync();
        Assert.That(after, Is.EqualTo(before + 1));

        var row = await _context.AdminAuditLogs.OrderByDescending(l => l.OccurredAt).FirstAsync();
        Assert.That(row.EntityType, Is.EqualTo(nameof(SpaceMember)));
        Assert.That(row.EntityId, Is.EqualTo(result.Id.ToString()));
    }

    private async Task<(EventParticipantService Sut, FakeEmailService EmailService)> BuildSutAsync(string userId)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId)], "Test"))
            }
        };

        var mapper = new MapperConfiguration(
            cfg => cfg.AddMaps(typeof(EventViewModel).Assembly),
            NullLoggerFactory.Instance).CreateMapper();

        var fakeEmailService = new FakeEmailService();

        var configuration = new ConfigurationManager();
        configuration["Frontend:BaseUrl"] = "http://localhost:4200";

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton(mapper);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<ILogService>(new LogService(httpContextAccessor));
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>()
            .AddEntityFrameworkStores<ChoraleDbContext>()
            .AddDefaultTokenProviders()
            .AddInvitationTokenProvider();

        var serviceProvider = services.BuildServiceProvider();
        await SeedSingerRoleAsync(serviceProvider);

        var authorizationService = new EventAuthorizationService(serviceProvider, new ChoirAuthorizationService(serviceProvider, new MembershipService(serviceProvider)));
        var userInvitationService = new UserInvitationService(serviceProvider, fakeEmailService);
        var serviceLimitService = new ServiceLimitService(serviceProvider);
        var membershipService = new MembershipService(serviceProvider);
        var auditLogService = new AuditLogService(serviceProvider);

        var sut = new EventParticipantService(
            serviceProvider, authorizationService, userInvitationService,
            serviceLimitService, membershipService, auditLogService);

        return (sut, fakeEmailService);
    }

    private static async Task SeedSingerRoleAsync(IServiceProvider serviceProvider)
    {
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(UserRoleEnum.Singer.ToString()))
            await roleManager.CreateAsync(new IdentityRole(UserRoleEnum.Singer.ToString()));
    }
}
