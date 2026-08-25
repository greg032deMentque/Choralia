using System.Security.Claims;
using AutoMapper;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels.AdminDashboard;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using ChoraleBackEnd.ViewModels.Choirs;

namespace ChoraleBackEnd.Test.Services.Dashboard;

/// <summary>
/// Indicateurs du tableau de bord d'administration generale (`10-D30`). Verifie que chaque
/// indicateur a une source reelle, exclut correctement le soft-delete, et ne divise jamais
/// par zero sur une base vide ou un plafond a 0.
/// </summary>
[TestFixture]
public sealed class AdminDashboardTests
{
    private const string AdminUserId = "admin-1";

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
    public async Task GetKpiAsync_EmptyDatabase_ReturnsZeroEverywhereWithoutException()
    {
        AdminDashboardKpiViewModel result = null!;

        Assert.DoesNotThrowAsync(async () => result = await Sut().GetKpiAsync());

        Assert.Multiple(() =>
        {
            Assert.That(result.Clients.Total, Is.EqualTo(0));
            Assert.That(result.Choirs.Total, Is.EqualTo(0));
            Assert.That(result.Users.Total, Is.EqualTo(0));
            Assert.That(result.InactiveChoirs.Count, Is.EqualTo(0));
            Assert.That(result.NotStartedClients.Count, Is.EqualTo(0));
            Assert.That(result.ClientsNearCap.Count, Is.EqualTo(0));
            Assert.That(result.TotalStorageBytes, Is.EqualTo(0));
            Assert.That(result.Songs.Total, Is.EqualTo(0));
            Assert.That(result.Songs.DuplicateGroups, Is.EqualTo(0));
            Assert.That(result.UpcomingEvents30Days, Is.EqualTo(0));
            Assert.That(result.EventsWithoutStructureAnomaly.Count, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task GetKpiAsync_ClientsByStatus_CorrectCountsAndArchivedExcludedFromActive()
    {
        CreateClient("Active 1", ClientStatusEnum.Active);
        CreateClient("Active 2", ClientStatusEnum.Active);
        CreateClient("Suspendu", ClientStatusEnum.Suspended);
        CreateClient("Archive", ClientStatusEnum.Archived);
        await _context.SaveChangesAsync();

        var result = await Sut().GetKpiAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Clients.Total, Is.EqualTo(4));
            Assert.That(result.Clients.Active, Is.EqualTo(2));
            Assert.That(result.Clients.Suspended, Is.EqualTo(1));
            Assert.That(result.Clients.Archived, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GetKpiAsync_ChoirsByStatus_CorrectCountsAndArchivedExcludedFromPublished()
    {
        var clientId = CreateClient("Client");
        CreateChoir("Draft", clientId, ChoirStatusEnum.Draft);
        CreateChoir("Published 1", clientId, ChoirStatusEnum.Published);
        CreateChoir("Published 2", clientId, ChoirStatusEnum.Published);
        CreateChoir("Annulee", clientId, ChoirStatusEnum.Cancelled);
        CreateChoir("Archivee", clientId, ChoirStatusEnum.Archived);
        await _context.SaveChangesAsync();

        var result = await Sut().GetKpiAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Choirs.Total, Is.EqualTo(5));
            Assert.That(result.Choirs.Draft, Is.EqualTo(1));
            Assert.That(result.Choirs.Published, Is.EqualTo(2));
            Assert.That(result.Choirs.Cancelled, Is.EqualTo(1));
            Assert.That(result.Choirs.Archived, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GetKpiAsync_SoftDeletedUser_NeverCountedEvenAmongInvitees()
    {
        CreateUser("active", isActive: true);
        CreateUser("invite-non-active", isActive: false, isGuest: true, emailConfirmed: false);
        CreateUser("supprime", isGuest: true, emailConfirmed: false, isDeleted: true);
        await _context.SaveChangesAsync();

        var result = await Sut().GetKpiAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Users.Total, Is.EqualTo(2));
            Assert.That(result.Users.Active, Is.EqualTo(1));
            Assert.That(result.Users.InactiveInvitees, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GetKpiAsync_ChoirInactiveFor30Days_DetectedInUtc_ChoirWithoutMemberExcluded()
    {
        var clientId = CreateClient("Client");

        var choirInactive = CreateChoir("Inactive", clientId, ChoirStatusEnum.Published);
        var memberInactive = CreateUser("member-inactive", lastActive: DateTime.UtcNow.AddDays(-40));
        CreateMember(choirInactive, memberInactive.Id);

        var choirActive = CreateChoir("Active", clientId, ChoirStatusEnum.Published);
        var memberActive = CreateUser("member-active", lastActive: DateTime.UtcNow.AddDays(-5));
        CreateMember(choirActive, memberActive.Id);

        // Chorale operationnelle sans aucun membre actif : n'a pas de mesure, ne doit pas etre
        // comptee ici (elle releve de « non demarre » cote client, pas de cette inactivite).
        CreateChoir("SansMembre", clientId, ChoirStatusEnum.Published);

        await _context.SaveChangesAsync();

        var result = await Sut().GetKpiAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.InactiveChoirs.Count, Is.EqualTo(1));
            Assert.That(result.InactiveChoirs.ChoirIds, Is.EquivalentTo(new[] { choirInactive }));
        });
    }

    [Test]
    public async Task GetKpiAsync_ClientWithoutChoirAndEmptyClient_CountedAsNotStarted_ClientWithSongExcluded()
    {
        var clientWithoutChoir = CreateClient("Sans choir");

        var emptyClient = CreateClient("Vide");
        CreateChoir("ChoraleVide", emptyClient, ChoirStatusEnum.Published);

        var activeClient = CreateClient("Demarre");
        var choirActive = CreateChoir("ChoraleAvecChant", activeClient, ChoirStatusEnum.Published);
        CreateSong(choirActive, "Un chant", "Un composer");

        await _context.SaveChangesAsync();

        var result = await Sut().GetKpiAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.NotStartedClients.Count, Is.EqualTo(2));
            Assert.That(result.NotStartedClients.ClientIds, Is.EquivalentTo(new[] { clientWithoutChoir, emptyClient }));
        });
    }

    [Test]
    public async Task GetKpiAsync_CapAtZero_ExcludedFromCalculation_NeverCountedNearCap()
    {
        // ChoirLimit = 0 alors que le client a des chorales : cette dimension doit etre
        // exclue du calcul, pas produire un taux de 100 % qui ferait remonter le client a tort.
        var clientWithZeroCap = CreateClient(
            "PlafondZero", limitChoirs: 0, limitMembers: 100, quotaStorage: 10_000_000, sizeMaxFile: 5_000_000);
        CreateChoir("Chorale1", clientWithZeroCap, ChoirStatusEnum.Published);
        CreateChoir("Chorale2", clientWithZeroCap, ChoirStatusEnum.Published);

        var clientNearMemberCap = CreateClient(
            "PresPlafondMembres", limitChoirs: 10, limitMembers: 5, quotaStorage: 10_000_000, sizeMaxFile: 5_000_000);
        var choirMembers = CreateChoir("ChoirMembers", clientNearMemberCap, ChoirStatusEnum.Published);
        for (var i = 0; i < 5; i++)
        {
            var member = CreateUser($"member-plafond-{i}");
            CreateMember(choirMembers, member.Id);
        }

        await _context.SaveChangesAsync();

        var result = await Sut().GetKpiAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.ClientsNearCap.ClientIds, Does.Not.Contain(clientWithZeroCap));
            Assert.That(result.ClientsNearCap.ClientIds, Contains.Item(clientNearMemberCap));
        });
    }

    [Test]
    public async Task GetKpiAsync_EventAttachedToTechnicalClient_SurfacedAsAnomaly()
    {
        var withoutStructureId = CreateClientTechnique();
        var eventId = CreateStandaloneEvent(withoutStructureId, DateTime.UtcNow.AddDays(5));
        await _context.SaveChangesAsync();

        var result = await Sut().GetKpiAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.EventsWithoutStructureAnomaly.Count, Is.EqualTo(1));
            Assert.That(result.EventsWithoutStructureAnomaly.EventIds, Is.EquivalentTo(new[] { eventId }));
        });
    }

    [Test]
    public async Task GetKpiAsync_DuplicateSongs_ConsistentWithSongKeyHelper()
    {
        var clientId = CreateClient("Client");
        var choirA = CreateChoir("ChoraleA", clientId, ChoirStatusEnum.Published);
        var choirB = CreateChoir("ChoraleB", clientId, ChoirStatusEnum.Published);

        CreateSong(choirA, "Ave Maria", "Gounod");
        CreateSong(choirB, "Ave Maria", "Gounod");
        CreateSong(choirA, "Chant Unique", "Composer Unique");

        await _context.SaveChangesAsync();

        var result = await Sut().GetKpiAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Songs.Total, Is.EqualTo(3));
            Assert.That(result.Songs.DuplicateGroups, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GetKpiAsync_UpcomingEvents30Days_OnlyPublishedWithinWindowIsCounted()
    {
        var withoutStructureId = CreateClientTechnique();

        CreateStandaloneEvent(withoutStructureId, DateTime.UtcNow.AddDays(10));
        CreateStandaloneEvent(withoutStructureId, DateTime.UtcNow.AddDays(40), EventStatusEnum.Published);
        CreateStandaloneEvent(withoutStructureId, DateTime.UtcNow.AddDays(10), EventStatusEnum.Draft);

        await _context.SaveChangesAsync();

        var result = await Sut().GetKpiAsync();

        Assert.That(result.UpcomingEvents30Days, Is.EqualTo(1));
    }

    private Guid CreateClient(
        string name, ClientStatusEnum status = ClientStatusEnum.Active,
        int limitChoirs = 10, int limitMembers = 100,
        long quotaStorage = 10_000_000, long sizeMaxFile = 5_000_000)
    {
        var clientId = ChoraleDbContext.NewIdGuid();
        _context.Clients.Add(new Client
        {
            Id = clientId,
            Name = name,
            Status = status,
            ChoirLimit = limitChoirs,
            MemberLimit = limitMembers,
            StorageQuotaBytes = quotaStorage,
            MaxFileSizeBytes = sizeMaxFile
        });
        return clientId;
    }

    /// <summary>
    /// Client technique seede en production par la migration AjouteClientSurEspace
    /// (Client.ClientTechnique.SansStructureId) : une vraie ligne Client existe pour ce
    /// GUID, sans quoi Espace.HasQueryFilter(!e.Client.IsDeleted) rendrait invisible tout
    /// evenement qui y est rattache, y compris l'anomalie que ce test verifie.
    /// </summary>
    private Guid CreateClientTechnique()
    {
        var id = Guid.Parse(Client.ClientTechnique.WithoutStructureId);
        _context.Clients.Add(new Client
        {
            Id = id, Name = "Sans structure", Status = ClientStatusEnum.Active
        });
        return id;
    }

    private Guid CreateChoir(string name, Guid clientId, ChoirStatusEnum status)
    {
        var choirId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = choirId, ClientId = clientId, Name = name, Status = status
        });
        return choirId;
    }

    private void CreateMember(Guid choirId, string userId, MemberStatusEnum status = MemberStatusEnum.Active)
    {
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ChoirId = choirId,
            SpaceId = choirId,
            UserId = userId,
            Status = status
        });
    }

    private User CreateUser(
        string id, bool isActive = true, bool isGuest = false,
        bool emailConfirmed = true, DateTime? lastActive = null, bool isDeleted = false)
    {
        var email = $"{id}@test.com";
        var user = new User
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Firstname = "Prenom",
            Lastname = "Nom",
            IsActive = isActive,
            IsGuestAccount = isGuest,
            EmailConfirmed = emailConfirmed,
            LastActive = lastActive,
            IsDeleted = isDeleted,
            CreatedAt = DateTime.UtcNow.AddDays(-100)
        };
        _context.Users.Add(user);
        return user;
    }

    private Guid CreateSong(Guid choirId, string title, string? composer)
    {
        var songId = ChoraleDbContext.NewIdGuid();
        _context.Songs.Add(new Song
        {
            Id = songId,
            ChoirId = choirId,
            Title = title,
            Composer = composer,
            Status = SongStatusEnum.Active
        });
        return songId;
    }

    private Guid CreateStandaloneEvent(
        Guid clientId, DateTime startDate, EventStatusEnum status = EventStatusEnum.Published)
    {
        var eventId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = eventId, SpaceType = SpaceTypeEnum.Event, ClientId = clientId });
        _context.Events.Add(new Event
        {
            Id = eventId,
            Title = "Event",
            Location = "Lieu",
            StartDate = startDate,
            Type = EventTypeEnum.Concert,
            Status = status,
            ChoirId = null
        });
        return eventId;
    }

    private AdminDashboardService Sut()
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, AdminUserId),
                     new Claim(ClaimTypes.Role, nameof(UserRoleEnum.Admin))], "Test"))
            }
        };

        var mapper = new MapperConfiguration(
            cfg => cfg.AddMaps(typeof(ChoirViewModel).Assembly),
            NullLoggerFactory.Instance).CreateMapper();

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton(mapper);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<ILogService>(new LogService(httpContextAccessor));
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>().AddEntityFrameworkStores<ChoraleDbContext>();

        var serviceProvider = services.BuildServiceProvider();
        return new AdminDashboardService(serviceProvider);
    }
}
