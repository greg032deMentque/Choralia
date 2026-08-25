using System.Security.Claims;
using AutoMapper;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.Services.UserServices;
using ChoraleBackEnd.Test.Fakes;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.Technical;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using ChoraleBackEnd.ViewModels.Auth;

namespace ChoraleBackEnd.Test.Services.Auth;

/// <summary>
/// Contrat de <c>GET /api/auth/Me</c> : couverture de <c>SpaceRoles</c> et
/// <c>ClientRoles</c>.
/// </summary>
[TestFixture]
public sealed class AuthenticatedUserContratTests
{
    private ChoraleDbContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task Nominal_ChoirMemberAndEventParticipant_TwoSpaceRolesEntries()
    {
        const string userId = "user-nominal";
        await CreateUserAsync(userId);
        var clientId = await CreateClientAsync();
        var choirId = await CreateChoirAsync(clientId, "Choir A");
        await AddSpaceMemberAsync(userId, choirId, MemberStatusEnum.Active, choirId);
        var eventId = await CreateEventAsync(clientId, "Concert", null);
        await AddSpaceMemberAsync(userId, eventId, MemberStatusEnum.Active);

        var result = await CreateAccountService(userId).GetCurrentUserAsync();

        Assert.That(result.SpaceRoles, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            var choir = result.SpaceRoles.Single(e => e.SpaceId == choirId);
            Assert.That(choir.SpaceType, Is.EqualTo(SpaceTypeEnum.Choir));
            Assert.That(choir.Roles, Does.Contain("Singer"));

            var evt = result.SpaceRoles.Single(e => e.SpaceId == eventId);
            Assert.That(evt.SpaceType, Is.EqualTo(SpaceTypeEnum.Event));
            Assert.That(evt.Roles, Does.Contain("Participant"));
        });
    }

    [Test]
    public async Task Event_ChoirIdReflectsTheAttachment()
    {
        const string userId = "user-events";
        await CreateUserAsync(userId);
        var clientId = await CreateClientAsync();
        var choirId = await CreateChoirAsync(clientId, "Choir Mere");

        var attachedEventId = await CreateEventAsync(clientId, "Concert Rattache", choirId);
        await AddSpaceMemberAsync(userId, attachedEventId, MemberStatusEnum.Active);

        var standaloneEventId = await CreateEventAsync(clientId, "Concert Autonome", null);
        await AddSpaceMemberAsync(userId, standaloneEventId, MemberStatusEnum.Active);

        var result = await CreateAccountService(userId).GetCurrentUserAsync();

        Assert.Multiple(() =>
        {
            var attached = result.SpaceRoles.Single(e => e.SpaceId == attachedEventId);
            Assert.That(attached.ChoirId, Is.EqualTo(choirId));

            var standalone = result.SpaceRoles.Single(e => e.SpaceId == standaloneEventId);
            Assert.That(standalone.ChoirId, Is.Null);
        });
    }

    [Test]
    public async Task ManagerClient_ObtainsOneClientRolesEntry()
    {
        const string userId = "user-responsable-client";
        await CreateUserAsync(userId);
        var clientId = await CreateClientAsync();
        _context.ClientMembers.Add(new ClientMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ClientId = clientId,
            UserId = userId,
            Role = UserRoleEnum.ClientManager
        });
        await _context.SaveChangesAsync();

        var result = await CreateAccountService(userId).GetCurrentUserAsync();

        Assert.That(result.ClientRoles, Has.Count.EqualTo(1));
        var entry = result.ClientRoles[0];
        Assert.Multiple(() =>
        {
            Assert.That(entry.ClientId, Is.EqualTo(clientId));
            Assert.That(entry.Roles, Does.Contain("ClientManager"));
        });
    }

    [Test]
    public async Task UnattachedClient_ClientRolesIsEmptyNeverNull()
    {
        const string userId = "user-sans-client";
        await CreateUserAsync(userId);
        var clientId = await CreateClientAsync();
        var choirId = await CreateChoirAsync(clientId, "Choir");
        await AddSpaceMemberAsync(userId, choirId, MemberStatusEnum.Active, choirId);

        var result = await CreateAccountService(userId).GetCurrentUserAsync();

        Assert.That(result.ClientRoles, Is.Not.Null);
        Assert.That(result.ClientRoles, Is.Empty);
    }

    [TestCase(ClientStatusEnum.Suspended)]
    [TestCase(ClientStatusEnum.Archived)]
    public async Task SpaceWhoseClientIsNotActive_AbsentFromSpaceRoles(ClientStatusEnum clientStatus)
    {
        const string userId = "user-client-non-active";
        await CreateUserAsync(userId);
        var clientId = await CreateClientAsync(clientStatus);
        var choirId = await CreateChoirAsync(clientId, "Choir");
        await AddSpaceMemberAsync(userId, choirId, MemberStatusEnum.Active, choirId);

        var result = await CreateAccountService(userId).GetCurrentUserAsync();

        Assert.That(result.SpaceRoles, Is.Empty);
    }

    [Test]
    public async Task SpaceSoftDeleted_AbsentFromSpaceRoles()
    {
        const string userId = "user-space-supprime";
        await CreateUserAsync(userId);
        var clientId = await CreateClientAsync();
        var choirId = await CreateChoirAsync(clientId, "Choir");
        await AddSpaceMemberAsync(userId, choirId, MemberStatusEnum.Active, choirId);

        var space = await _context.Spaces.FirstAsync(e => e.Id == choirId);
        space.IsDeleted = true;
        await _context.SaveChangesAsync();

        var result = await CreateAccountService(userId).GetCurrentUserAsync();

        Assert.That(result.SpaceRoles, Is.Empty);
    }

    [TestCase(MemberStatusEnum.Invited)]
    [TestCase(MemberStatusEnum.Inactive)]
    [TestCase(MemberStatusEnum.Archived)]
    public async Task SpaceMemberNonActive_AbsentFromSpaceRoles(MemberStatusEnum memberStatus)
    {
        const string userId = "user-member-non-active";
        await CreateUserAsync(userId);
        var clientId = await CreateClientAsync();
        var choirId = await CreateChoirAsync(clientId, "Choir");
        await AddSpaceMemberAsync(userId, choirId, memberStatus, choirId);

        var result = await CreateAccountService(userId).GetCurrentUserAsync();

        Assert.That(result.SpaceRoles, Is.Empty);
    }

    [Test]
    public async Task WithoutAnyAttachment_EmptyListsAndNoException()
    {
        const string userId = "user-isole";
        await CreateUserAsync(userId);

        AuthenticatedUserViewModel? result = null;
        Assert.DoesNotThrowAsync(async () =>
            result = await CreateAccountService(userId).GetCurrentUserAsync());

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.SpaceRoles, Is.Not.Null);
            Assert.That(result.SpaceRoles, Is.Empty);
            Assert.That(result.ClientRoles, Is.Not.Null);
            Assert.That(result.ClientRoles, Is.Empty);
        });
    }

    [Test]
    public async Task AccumulatedRolesOnSameSpace_OneEntryWithBothRoles()
    {
        const string userId = "user-cumul";
        await CreateUserAsync(userId);
        var clientId = await CreateClientAsync();
        var choirId = await CreateChoirAsync(clientId, "Choir Cumul");
        var spaceMemberId = await AddSpaceMemberAsync(userId, choirId, MemberStatusEnum.Active, choirId);
        await AddRoleSpaceAsync(spaceMemberId, UserRoleEnum.Manager);

        _context.Sections.Add(new Section
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ChoirId = choirId,
            VoicePart = VoicePartEnum.Alto,
            SectionLeaderId = userId
        });
        await _context.SaveChangesAsync();

        var result = await CreateAccountService(userId).GetCurrentUserAsync();

        Assert.That(result.SpaceRoles, Has.Count.EqualTo(1));
        var entry = result.SpaceRoles[0];
        Assert.Multiple(() =>
        {
            Assert.That(entry.Roles, Does.Contain("Manager"));
            Assert.That(entry.Roles, Does.Contain("SectionLeader"));
        });
    }

    /// <summary>
    /// Le fournisseur EF Core InMemory n'emet aucun evenement par execution de requete
    /// (seulement a la premiere compilation d'une forme de requete) : un vrai N+1 y est
    /// indetectable par comptage de requetes. La garantie "requetes groupees" reste donc
    /// structurelle (revue de code : <c>BuildSpaceRolesAsync</c> n'appelle <c>_context</c>
    /// qu'en dehors de toute boucle sur les espaces). Ce test fige ce qu'une regression vers
    /// une boucle par espace casserait en premier : des entrees manquantes a l'echelle.
    /// </summary>
    [Test]
    public async Task TwentySpaces_ProducesCompleteResultWithoutPerSpaceDegradation()
    {
        const string userId = "user-vingt-spaces";
        await CreateUserAsync(userId);
        var clientId = await CreateClientAsync();

        var spaceIds = new List<Guid>();
        for (var i = 0; i < 20; i++)
        {
            var choirId = await CreateChoirAsync(clientId, $"Choir {i}");
            await AddSpaceMemberAsync(userId, choirId, MemberStatusEnum.Active, choirId);
            spaceIds.Add(choirId);
        }

        var result = await CreateAccountService(userId).GetCurrentUserAsync();

        Assert.That(result.SpaceRoles, Has.Count.EqualTo(20));
        Assert.That(result.SpaceRoles.Select(e => e.SpaceId), Is.EquivalentTo(spaceIds));
    }

    private async Task CreateUserAsync(string userId)
    {
        _context.Users.Add(new User
        {
            Id = userId,
            UserName = $"{userId}@test.com",
            Email = $"{userId}@test.com"
        });
        await _context.SaveChangesAsync();
    }

    private async Task<Guid> CreateClientAsync(ClientStatusEnum status = ClientStatusEnum.Active)
    {
        var clientId = ChoraleDbContext.NewIdGuid();
        _context.Clients.Add(new Client { Id = clientId, Name = $"Client {clientId}", Status = status });
        await _context.SaveChangesAsync();
        return clientId;
    }

    private async Task<Guid> CreateChoirAsync(Guid clientId, string name)
    {
        var choirId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir { Id = choirId, ClientId = clientId, Name = name, Status = ChoirStatusEnum.Published });
        await _context.SaveChangesAsync();
        return choirId;
    }

    private async Task<Guid> CreateEventAsync(Guid clientId, string title, Guid? choirId)
    {
        var eventId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = eventId, SpaceType = SpaceTypeEnum.Event, ClientId = clientId });
        _context.Events.Add(new Event
        {
            Id = eventId,
            Title = title,
            StartDate = DateTime.UtcNow.AddDays(7),
            Type = EventTypeEnum.Concert,
            Location = "Salle des fetes",
            Status = EventStatusEnum.Draft,
            ChoirId = choirId
        });
        await _context.SaveChangesAsync();
        return eventId;
    }

    private async Task<Guid> AddSpaceMemberAsync(
        string userId, Guid spaceId, MemberStatusEnum status, Guid? choirIdFk = null)
    {
        var spaceMemberId = ChoraleDbContext.NewIdGuid();
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = spaceMemberId,
            UserId = userId,
            SpaceId = spaceId,
            ChoirId = choirIdFk,
            Status = status
        });
        await _context.SaveChangesAsync();
        return spaceMemberId;
    }

    private async Task AddRoleSpaceAsync(Guid spaceMemberId, UserRoleEnum role)
    {
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceMemberId = spaceMemberId,
            Role = role
        });
        await _context.SaveChangesAsync();
    }

    private AccountService CreateAccountService(string userId)
    {
        var mapper = new MapperConfiguration(cfg => { }, NullLoggerFactory.Instance).CreateMapper();

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId)], "Test"))
            }
        };

        var configuration = new ConfigurationManager();
        configuration["JWTToken:Secret"] =
            "test-secret-key-64-characters-minimum-for-hmacsha512-signing-xxxxxxxxxxxx";
        configuration["JWTToken:Issuer"] = "choir-test";
        configuration["JWTToken:Audience"] = "choir-test";
        configuration["JWTToken:ExpiresInMinutes"] = "60";

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton(mapper);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<ILogService>(new LogService(httpContextAccessor));
        services.AddSingleton<IEmailService>(new FakeEmailService());
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>()
            .AddEntityFrameworkStores<ChoraleDbContext>()
            .AddDefaultTokenProviders();

        var serviceProvider = services.BuildServiceProvider();

        var jwtGeneratorService = new JwtGeneratorService(serviceProvider);
        var userRoleDataService = new UserRoleDataService(serviceProvider);
        var spaceRoleResolverService = new SpaceRoleResolverService(_context);
        var sectionVoicePartLookupService = new SectionVoicePartLookupService(_context);

        return new AccountService(
            serviceProvider,
            jwtGeneratorService,
            userRoleDataService,
            spaceRoleResolverService,
            sectionVoicePartLookupService,
            serviceProvider.GetRequiredService<IEmailService>());
    }

    private async Task AddSectionMemberAsync(string userId, Guid choirId, VoicePartEnum voicePart)
    {
        var sectionId = ChoraleDbContext.NewIdGuid();
        _context.Sections.Add(new Section
        {
            Id = sectionId,
            ChoirId = choirId,
            VoicePart = voicePart
        });
        _context.SectionMembers.Add(new SectionMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = userId,
            SectionId = sectionId
        });
        await _context.SaveChangesAsync();
    }

    [Test]
    public async Task MemberWithSection_PrimaryVoicePartReflectsSectionVoicePart()
    {
        const string userId = "user-avec-pupitre";
        await CreateUserAsync(userId);
        var clientId = await CreateClientAsync();
        var choirId = await CreateChoirAsync(clientId, "Choir Pupitre");
        await AddSpaceMemberAsync(userId, choirId, MemberStatusEnum.Active, choirId);
        await AddSectionMemberAsync(userId, choirId, VoicePartEnum.Tenor);

        var result = await CreateAccountService(userId).GetCurrentUserAsync();

        var entry = result.SpaceRoles.Single(e => e.SpaceId == choirId);
        Assert.That(entry.PrimaryVoicePart, Is.EqualTo(VoicePartEnum.Tenor));
    }

    [Test]
    public async Task MemberWithoutSection_PrimaryVoicePartIsNullNotAlto()
    {
        const string userId = "user-sans-pupitre";
        await CreateUserAsync(userId);
        var clientId = await CreateClientAsync();
        var choirId = await CreateChoirAsync(clientId, "Choir Sans Pupitre");
        await AddSpaceMemberAsync(userId, choirId, MemberStatusEnum.Active, choirId);

        var result = await CreateAccountService(userId).GetCurrentUserAsync();

        var entry = result.SpaceRoles.Single(e => e.SpaceId == choirId);
        Assert.That(entry.PrimaryVoicePart, Is.Null);
    }

    [Test]
    public async Task EventSpace_PrimaryVoicePartIsNullEvenWithSectionInParentChoir()
    {
        const string userId = "user-event-avec-pupitre";
        await CreateUserAsync(userId);
        var clientId = await CreateClientAsync();
        var choirId = await CreateChoirAsync(clientId, "Choir Mere Event");
        await AddSpaceMemberAsync(userId, choirId, MemberStatusEnum.Active, choirId);
        await AddSectionMemberAsync(userId, choirId, VoicePartEnum.Bass);

        var eventId = await CreateEventAsync(clientId, "Concert", choirId);
        await AddSpaceMemberAsync(userId, eventId, MemberStatusEnum.Active);

        var result = await CreateAccountService(userId).GetCurrentUserAsync();

        var eventEntry = result.SpaceRoles.Single(e => e.SpaceId == eventId);
        Assert.That(eventEntry.PrimaryVoicePart, Is.Null);
    }

    [Test]
    public async Task TwoMembersSameChoir_EachSeesOnlyTheirOwnVoicePart()
    {
        const string userIdA = "user-voix-a";
        const string userIdB = "user-voix-b";
        await CreateUserAsync(userIdA);
        await CreateUserAsync(userIdB);
        var clientId = await CreateClientAsync();
        var choirId = await CreateChoirAsync(clientId, "Choir Isolation Voix");
        await AddSpaceMemberAsync(userIdA, choirId, MemberStatusEnum.Active, choirId);
        await AddSpaceMemberAsync(userIdB, choirId, MemberStatusEnum.Active, choirId);
        await AddSectionMemberAsync(userIdA, choirId, VoicePartEnum.Soprano);
        await AddSectionMemberAsync(userIdB, choirId, VoicePartEnum.Bass);

        var resultA = await CreateAccountService(userIdA).GetCurrentUserAsync();
        var resultB = await CreateAccountService(userIdB).GetCurrentUserAsync();

        Assert.Multiple(() =>
        {
            Assert.That(resultA.SpaceRoles.Single(e => e.SpaceId == choirId).PrimaryVoicePart,
                Is.EqualTo(VoicePartEnum.Soprano));
            Assert.That(resultB.SpaceRoles.Single(e => e.SpaceId == choirId).PrimaryVoicePart,
                Is.EqualTo(VoicePartEnum.Bass));
        });
    }
}
