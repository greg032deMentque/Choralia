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
using ChoraleBackEnd.ViewModels.SongLists;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Services.SongLists;

/// <summary>
/// Modification et cycle de publication d'une liste de chants. Complete
/// <see cref="SongListServiceTests"/>, qui porte la creation et la composition.
/// </summary>
/// <remarks>
/// Le point sensible est <c>EnsureTypeMatchesStoredScope</c> : depuis que les cles de
/// rattachement ne sont plus mappables en update (voir <c>SongListViewModelMappingProfile</c>
/// et <c>EntityMappingGuardTests</c>), la validation du corps de requete ne suffit plus — elle
/// raisonne sur des cles qui ne seront pas ecrites. Sans ce controle, poser
/// <c>Type = Event</c> sur une liste sans evenement stocke produit une ligne incoherente que
/// rien ne rattrape ensuite.
/// </remarks>
[TestFixture]
public sealed class SongListLifecycleTests
{
    private const string ManagerUserId = "manager-1";
    private const string SectionLeaderUserId = "section-leader-1";
    private const string PlainMemberUserId = "plain-member-1";

    private ChoraleDbContext _context = null!;
    private Guid _choirId;
    private Guid _sectionId;
    private Guid _eventId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _choirId = ChoraleDbContext.NewIdGuid();
        _sectionId = ChoraleDbContext.NewIdGuid();
        _eventId = ChoraleDbContext.NewIdGuid();
        var clientId = ChoraleDbContext.NewIdGuid();

        foreach (var userId in new[] { ManagerUserId, SectionLeaderUserId, PlainMemberUserId })
            _context.Users.Add(new User { Id = userId, UserName = $"{userId}@test.com", Email = $"{userId}@test.com" });

        _context.Clients.Add(new Client { Id = clientId, Name = "Client Test", Status = ClientStatusEnum.Active });
        _context.Spaces.Add(new Space { Id = _choirId, ClientId = clientId, SpaceType = SpaceTypeEnum.Choir });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = _choirId, ClientId = clientId, Name = "Choir Test", Status = ChoirStatusEnum.Published
        });

        _context.Spaces.Add(new Space { Id = _eventId, ClientId = clientId, SpaceType = SpaceTypeEnum.Event });
        _context.Events.Add(new Event
        {
            Id = _eventId,
            Title = "Concert",
            StartDate = DateTime.UtcNow.AddDays(10),
            Type = EventTypeEnum.Concert,
            Location = "Salle",
            Status = EventStatusEnum.Published,
            ChoirId = _choirId
        });

        _context.Sections.Add(new Section
        {
            Id = _sectionId, ChoirId = _choirId, VoicePart = VoicePartEnum.Soprano, SectionLeaderId = SectionLeaderUserId
        });

        var managerMembership = AddChoirMembership(ManagerUserId);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(), SpaceMemberId = managerMembership.Id, Role = UserRoleEnum.Manager
        });
        AddChoirMembership(SectionLeaderUserId);
        AddChoirMembership(PlainMemberUserId);

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    // ---------- EnsureTypeMatchesStoredScope ----------

    [Test]
    public async Task UpdateAsync_TypeEventOnAListWithoutStoredEvent_ThrowsBadRequest()
    {
        var id = await AddSongListAsync(SongListTypeEnum.Free, choirId: _choirId);

        var exception = Assert.ThrowsAsync<CustomException>(
            () => Sut(ManagerUserId).UpdateAsync(new SongListViewModel
            {
                Id = id,
                Name = "Renommee",
                Type = SongListTypeEnum.Event,
                EventId = _eventId,
                ChoirId = _choirId
            }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task UpdateAsync_TypeOtherThanEventOnAListAttachedToAnEvent_ThrowsBadRequest()
    {
        var id = await AddSongListAsync(SongListTypeEnum.Event, eventId: _eventId);

        var exception = Assert.ThrowsAsync<CustomException>(
            () => Sut(ManagerUserId).UpdateAsync(new SongListViewModel
            {
                Id = id,
                Name = "Renommee",
                Type = SongListTypeEnum.Free,
                ChoirId = _choirId
            }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    // ---------- UpdateAsync ----------

    [Test]
    public void UpdateAsync_WithoutId_ThrowsBadRequest()
    {
        var exception = Assert.ThrowsAsync<CustomException>(
            () => Sut(ManagerUserId).UpdateAsync(new SongListViewModel
            {
                Name = "Sans id", Type = SongListTypeEnum.Free, ChoirId = _choirId
            }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public void UpdateAsync_UnknownList_ThrowsNotFound()
        => Assert.ThrowsAsync<KeyNotFoundException>(
            () => Sut(ManagerUserId).UpdateAsync(new SongListViewModel
            {
                Id = ChoraleDbContext.NewIdGuid(),
                Name = "Inconnue",
                Type = SongListTypeEnum.Free,
                ChoirId = _choirId
            }));

    /// <summary>
    /// Verrouille aussi le mapping : <c>ChoirId</c> venu du corps de la requete ne doit jamais
    /// repointer une liste existante — les gardes d'autorisation se sont executees sur la
    /// valeur STOCKEE, un remappage les contournerait apres coup.
    /// </summary>
    [Test]
    public async Task UpdateAsync_Nominal_UpdatesTheLabelsAndNeverRepointsTheChoir()
    {
        var otherChoirId = await AddSecondChoirAsync();
        var id = await AddSongListAsync(SongListTypeEnum.Free, choirId: _choirId);

        await Sut(ManagerUserId).UpdateAsync(new SongListViewModel
        {
            Id = id,
            Name = "Nouveau nom",
            Description = "Nouvelle description",
            Type = SongListTypeEnum.Free,
            ChoirId = otherChoirId
        });

        var stored = await _context.SongLists.AsNoTracking().FirstAsync(d => d.Id == id);
        Assert.Multiple(() =>
        {
            Assert.That(stored.Name, Is.EqualTo("Nouveau nom"));
            Assert.That(stored.Description, Is.EqualTo("Nouvelle description"));
            Assert.That(stored.ChoirId, Is.EqualTo(_choirId));
        });
    }

    [Test]
    public async Task UpdateAsync_NeitherCreatorNorSectionLeaderNorAdmin_ThrowsForbidden()
    {
        var id = await AddSongListAsync(SongListTypeEnum.Free, choirId: _choirId);

        var exception = Assert.ThrowsAsync<CustomException>(
            () => Sut(PlainMemberUserId).UpdateAsync(new SongListViewModel
            {
                Id = id, Name = "Detournee", Type = SongListTypeEnum.Free, ChoirId = _choirId
            }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    // ---------- ArchiveAsync ----------

    [TestCase(SongListStatusEnum.Draft)]
    [TestCase(SongListStatusEnum.Published)]
    public async Task ArchiveAsync_AnyNonArchivedStatus_MovesToArchived(SongListStatusEnum status)
    {
        var id = await AddSongListAsync(SongListTypeEnum.Free, choirId: _choirId, status: status);

        var result = await Sut(ManagerUserId).ArchiveAsync(id);

        Assert.That(result.Status, Is.EqualTo(SongListStatusEnum.Archived));
    }

    [Test]
    public async Task ArchiveAsync_AlreadyArchived_ThrowsConflict()
    {
        var id = await AddSongListAsync(
            SongListTypeEnum.Free, choirId: _choirId, status: SongListStatusEnum.Archived);

        var exception = Assert.ThrowsAsync<CustomException>(() => Sut(ManagerUserId).ArchiveAsync(id));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public void ArchiveAsync_UnknownList_ThrowsNotFound()
        => Assert.ThrowsAsync<KeyNotFoundException>(
            () => Sut(ManagerUserId).ArchiveAsync(ChoraleDbContext.NewIdGuid()));

    // ---------- EnsurePublicationRightsAsync ----------

    [Test]
    public async Task ArchiveAsync_PlainMemberEvenIfCreator_ThrowsForbidden()
    {
        var id = await AddSongListAsync(
            SongListTypeEnum.Free, choirId: _choirId, createdById: PlainMemberUserId);

        var exception = Assert.ThrowsAsync<CustomException>(() => Sut(PlainMemberUserId).ArchiveAsync(id));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
            "Creer une liste ne donne pas le droit de la publier ou de l'archiver : ce droit est celui du chef de choeur.");
    }

    [Test]
    public async Task PublishAsync_PlainMember_ThrowsForbidden()
    {
        var id = await AddSongListAsync(
            SongListTypeEnum.Free, choirId: _choirId, createdById: PlainMemberUserId);

        var exception = Assert.ThrowsAsync<CustomException>(() => Sut(PlainMemberUserId).PublishAsync(id));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task PublishAsync_SectionListBySectionLeader_Succeeds()
    {
        var id = await AddSongListAsync(
            SongListTypeEnum.Section, sectionId: _sectionId, createdById: SectionLeaderUserId);

        var result = await Sut(SectionLeaderUserId).PublishAsync(id);

        Assert.That(result.Status, Is.EqualTo(SongListStatusEnum.Published),
            "Le chef du pupitre concerne gere la publication de SA liste de pupitre — et d'elle seule.");
    }

    [Test]
    public async Task PublishAsync_ChoirListBySectionLeader_ThrowsForbidden()
    {
        var id = await AddSongListAsync(
            SongListTypeEnum.Free, choirId: _choirId, createdById: SectionLeaderUserId);

        var exception = Assert.ThrowsAsync<CustomException>(() => Sut(SectionLeaderUserId).PublishAsync(id));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
            "Le droit du chef de pupitre est borne aux listes de type Section rattachees a SON pupitre.");
    }

    [Test]
    public async Task PublishAsync_AlreadyPublished_ThrowsConflict()
    {
        var id = await AddSongListAsync(
            SongListTypeEnum.Free, choirId: _choirId, status: SongListStatusEnum.Published);

        var exception = Assert.ThrowsAsync<CustomException>(() => Sut(ManagerUserId).PublishAsync(id));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    // ---------- Montage ----------

    private SpaceMember AddChoirMembership(string userId)
    {
        var membership = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ChoirId = _choirId,
            SpaceId = _choirId,
            UserId = userId,
            Status = MemberStatusEnum.Active
        };
        _context.SpaceMembers.Add(membership);
        return membership;
    }

    private async Task<Guid> AddSecondChoirAsync()
    {
        var clientId = ChoraleDbContext.NewIdGuid();
        var choirId = ChoraleDbContext.NewIdGuid();
        _context.Clients.Add(new Client { Id = clientId, Name = "Client 2", Status = ClientStatusEnum.Active });
        _context.Spaces.Add(new Space { Id = choirId, ClientId = clientId, SpaceType = SpaceTypeEnum.Choir });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = choirId, ClientId = clientId, Name = "Autre Choir", Status = ChoirStatusEnum.Published
        });
        await _context.SaveChangesAsync();
        return choirId;
    }

    private async Task<Guid> AddSongListAsync(
        SongListTypeEnum type,
        Guid? choirId = null,
        Guid? sectionId = null,
        Guid? eventId = null,
        SongListStatusEnum status = SongListStatusEnum.Draft,
        string createdById = ManagerUserId)
    {
        var id = ChoraleDbContext.NewIdGuid();
        _context.SongLists.Add(new SongList
        {
            Id = id,
            Name = "Liste Test",
            Type = type,
            Status = status,
            ChoirId = choirId,
            SectionId = sectionId,
            EventId = eventId,
            OwnerUserId = createdById,
            CreatedById = createdById
        });
        await _context.SaveChangesAsync();
        return id;
    }

    private SongListService Sut(string userId)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test"))
            }
        };

        var mapper = new MapperConfiguration(
            cfg => cfg.AddMaps(typeof(SongListViewModel).Assembly),
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
        var membershipService = new MembershipService(serviceProvider);

        return new SongListService(
            serviceProvider,
            membershipService,
            new ChoirAuthorizationService(serviceProvider, membershipService));
    }
}
