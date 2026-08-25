using System.Net;
using System.Security.Claims;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.ViewModels.SongLists;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.ChoirServices;

namespace ChoraleBackEnd.Test.Services.SongLists;

[TestFixture]
public sealed class SongListServiceTests
{
    private const string ManagerUserId = "responsable-1";
    private const string SectionLeaderUserId = "chef-pupitre-1";
    private const string ExternalMemberUserId = "membre-externe-1";

    private ChoraleDbContext _context = null!;
    private SongListService _sut = null!;
    private Guid _choirId;
    private Guid _otherChoirId;
    private Guid _sectionId;
    private Guid _songId;
    private Guid _songId2;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _choirId = ChoraleDbContext.NewIdGuid();
        _otherChoirId = ChoraleDbContext.NewIdGuid();
        _sectionId = ChoraleDbContext.NewIdGuid();
        _songId = ChoraleDbContext.NewIdGuid();
        _songId2 = ChoraleDbContext.NewIdGuid();

        _context.Users.Add(new User { Id = ManagerUserId, UserName = "responsable@test.com", Email = "responsable@test.com" });
        _context.Users.Add(new User { Id = SectionLeaderUserId, UserName = "chef@test.com", Email = "chef@test.com" });
        _context.Users.Add(new User { Id = ExternalMemberUserId, UserName = "externe@test.com", Email = "externe@test.com" });
        // Une chorale sans client actif ne confère plus aucun accès (IMembershipService).
        var clientId = ChoraleDbContext.NewIdGuid();
        _context.Clients.Add(new Client
        {
            Id = clientId, Name = $"Client {clientId}", Status = ClientStatusEnum.Active
        });
        _context.Spaces.Add(new Space { Id = _choirId, ClientId = clientId, SpaceType = SpaceTypeEnum.Choir });
        _context.Spaces.Add(new Space { Id = _otherChoirId, ClientId = clientId, SpaceType = SpaceTypeEnum.Choir });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = _choirId, ClientId = clientId, Name = "Choir Test", Status = ChoirStatusEnum.Published
        });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = _otherChoirId, ClientId = clientId, Name = "Autre Choir", Status = ChoirStatusEnum.Published
        });
        _context.Songs.Add(new Song { Id = _songId, ChoirId = _choirId, Title = "Chant Test", Status = SongStatusEnum.Active });
        _context.Songs.Add(new Song { Id = _songId2, ChoirId = _choirId, Title = "Chant Test 2", Status = SongStatusEnum.Active });
        _context.Sections.Add(new Section { Id = _sectionId, ChoirId = _choirId, VoicePart = VoicePartEnum.Soprano, SectionLeaderId = SectionLeaderUserId });

        var memberChoir = new SpaceMember { Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId, SpaceId = _choirId, UserId = ManagerUserId, Status = MemberStatusEnum.Active };
        _context.SpaceMembers.Add(memberChoir);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceMemberId = memberChoir.Id,
            Role = UserRoleEnum.Manager
        });

        // En production, un SectionLeader est necessairement deja SpaceMember de sa chorale
        // (ChoirMembersService.AssignChefPupitreRoleAsync ne promeut qu'un membre existant).
        // MembershipService.EstMembreActifAsync (desormais cable sur l'ecriture via
        // EnsureCanWriteAsync) exige cette appartenance : sans elle, Pupitre.SectionLeaderId
        // seul ne suffit plus a ecrire.
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId, SpaceId = _choirId, UserId = SectionLeaderUserId, Status = MemberStatusEnum.Active
        });

        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ChoirId = _otherChoirId,
            SpaceId = _otherChoirId,
            UserId = ExternalMemberUserId,
            Status = MemberStatusEnum.Active
        });

        await _context.SaveChangesAsync();

        _sut = CreateServiceForUser(ManagerUserId);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Test]
    public void CreateAsync_UserNotMemberOfTargetedChoir_ThrowsForbidden()
    {
        var service = CreateServiceForUser(ExternalMemberUserId);
        var model = new SongListViewModel { Name = "SongList Test", ChoirId = _choirId, Type = SongListTypeEnum.Free };

        var exception = Assert.ThrowsAsync<CustomException>(() => service.CreateAsync(model));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task CreateAsync_UserMemberOfTargetedChoir_CreatesTheSongList()
    {
        var model = new SongListViewModel { Name = "SongList Test", ChoirId = _choirId, Type = SongListTypeEnum.Free };

        var result = await _sut.CreateAsync(model);

        Assert.That(await _context.SongLists.CountAsync(d => d.Id == result.Id), Is.EqualTo(1));
    }

    [Test]
    public async Task CreateAsync_OwnerUserIdSuppliedByClient_IsIgnoredAndReplacedByCurrentUser()
    {
        var model = new SongListViewModel
        {
            Name = "SongList Test",
            ChoirId = _choirId,
            Type = SongListTypeEnum.Free,
            OwnerUserId = ExternalMemberUserId
        };

        var result = await _sut.CreateAsync(model);
        var songList = await _context.SongLists.FirstAsync(d => d.Id == result.Id);

        Assert.That(songList.OwnerUserId, Is.EqualTo(ManagerUserId));
    }

    [Test]
    public async Task DeleteAsync_UserIsNeitherCreatorNorAdminNorSectionLeader_ThrowsForbidden()
    {
        var songList = await CreateSongListAsync(SongListTypeEnum.Free, SongListStatusEnum.Draft);
        var service = CreateServiceForUser(SectionLeaderUserId);

        var exception = Assert.ThrowsAsync<CustomException>(() => service.DeleteAsync(songList.Id));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task DeleteAsync_UserCreator_DeletesTheSongList()
    {
        var songList = await CreateSongListAsync(SongListTypeEnum.Free, SongListStatusEnum.Draft);

        await _sut.DeleteAsync(songList.Id);

        var deletedSongList = await _context.SongLists.IgnoreQueryFilters().FirstAsync(d => d.Id == songList.Id);
        Assert.That(deletedSongList.IsDeleted, Is.True);
    }

    [Test]
    public async Task AddSongAsync_PublishedList_ThrowsCustomExceptionConflict()
    {
        var songList = await CreatePublishedSongListAsync(SongListTypeEnum.Free);

        var exception = Assert.ThrowsAsync<CustomException>(
            () => _sut.AddSongAsync(songList.Id, new AddSongViewModel { SongId = _songId, Position = 0 }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task RemoveSongAsync_PublishedList_ThrowsCustomExceptionConflict()
    {
        var songList = await CreatePublishedSongListAsync(SongListTypeEnum.Free);
        _context.SongListSongs.Add(new SongListSong { Id = ChoraleDbContext.NewIdGuid(), SongListId = songList.Id, SongId = _songId, Position = 0 });
        await _context.SaveChangesAsync();

        var exception = Assert.ThrowsAsync<CustomException>(
            () => _sut.RemoveSongAsync(songList.Id, _songId));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task PublishAsync_DraftSongList_MovesToPublished()
    {
        var songList = await CreateSongListAsync(SongListTypeEnum.Free, SongListStatusEnum.Draft);

        var result = await _sut.PublishAsync(songList.Id);

        Assert.That(result.Status, Is.EqualTo(SongListStatusEnum.Published));
    }

    [Test]
    public async Task PublishAsync_AlreadyPublishedSongList_ThrowsCustomExceptionConflict()
    {
        var songList = await CreatePublishedSongListAsync(SongListTypeEnum.Free);

        var exception = Assert.ThrowsAsync<CustomException>(() => _sut.PublishAsync(songList.Id));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task PublishAsync_FreeTypeBySectionLeader_ThrowsForbidden()
    {
        var songList = await CreateSongListAsync(SongListTypeEnum.Free, SongListStatusEnum.Draft);
        var service = CreateServiceForUser(SectionLeaderUserId);

        var exception = Assert.ThrowsAsync<CustomException>(() => service.PublishAsync(songList.Id));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task PublishAsync_SectionTypeByConcernedSectionLeader_Works()
    {
        var songList = new SongList
        {
            Id = ChoraleDbContext.NewIdGuid(),
            Name = "Liste Section",
            SectionId = _sectionId,
            Type = SongListTypeEnum.Section,
            Status = SongListStatusEnum.Draft,
            CreatedById = SectionLeaderUserId,
            OwnerUserId = SectionLeaderUserId
        };
        _context.SongLists.Add(songList);
        await _context.SaveChangesAsync();

        var service = CreateServiceForUser(SectionLeaderUserId);

        var result = await service.PublishAsync(songList.Id);

        Assert.That(result.Status, Is.EqualTo(SongListStatusEnum.Published));
    }

    [Test]
    public async Task RevertToDraftAsync_PublishedSongList_AllowsAddingSongAgain()
    {
        var songList = await CreatePublishedSongListAsync(SongListTypeEnum.Free);

        await _sut.RevertToDraftAsync(songList.Id);
        var result = await _sut.AddSongAsync(songList.Id, new AddSongViewModel { SongId = _songId, Position = 0 });

        Assert.That(result.Songs.Any(c => c.SongId == _songId), Is.True);
    }

    [Test]
    public async Task ReorderSongsAsync_DraftSongListWithSameComposition_UpdatesPositionSequentially()
    {
        var songList = await CreateSongListAsync(SongListTypeEnum.Free, SongListStatusEnum.Draft);
        _context.SongListSongs.Add(new SongListSong { Id = ChoraleDbContext.NewIdGuid(), SongListId = songList.Id, SongId = _songId, Position = 0 });
        _context.SongListSongs.Add(new SongListSong { Id = ChoraleDbContext.NewIdGuid(), SongListId = songList.Id, SongId = _songId2, Position = 1 });
        await _context.SaveChangesAsync();

        var result = await _sut.ReorderSongsAsync(songList.Id,
            new ReorderSongsViewModel { SongIds = [_songId2, _songId] });

        Assert.That(result.Songs.First(c => c.SongId == _songId2).Position, Is.EqualTo(0));
        Assert.That(result.Songs.First(c => c.SongId == _songId).Position, Is.EqualTo(1));
    }

    [Test]
    public async Task ReorderSongsAsync_PublishedSongList_ThrowsCustomExceptionConflict()
    {
        var songList = await CreatePublishedSongListAsync(SongListTypeEnum.Free);
        _context.SongListSongs.Add(new SongListSong { Id = ChoraleDbContext.NewIdGuid(), SongListId = songList.Id, SongId = _songId, Position = 0 });
        await _context.SaveChangesAsync();

        var exception = Assert.ThrowsAsync<CustomException>(
            () => _sut.ReorderSongsAsync(songList.Id, new ReorderSongsViewModel { SongIds = [_songId] }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task ReorderSongsAsync_SongIdsSetDoesNotMatchComposition_ThrowsCustomExceptionBadRequest()
    {
        var songList = await CreateSongListAsync(SongListTypeEnum.Free, SongListStatusEnum.Draft);
        _context.SongListSongs.Add(new SongListSong { Id = ChoraleDbContext.NewIdGuid(), SongListId = songList.Id, SongId = _songId, Position = 0 });
        await _context.SaveChangesAsync();

        var exception = Assert.ThrowsAsync<CustomException>(
            () => _sut.ReorderSongsAsync(songList.Id,
                new ReorderSongsViewModel { SongIds = [_songId, _songId2] }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    private async Task<SongList> CreateSongListAsync(SongListTypeEnum type, SongListStatusEnum status)
    {
        var songList = new SongList
        {
            Id = ChoraleDbContext.NewIdGuid(),
            Name = "Liste Test",
            ChoirId = _choirId,
            Type = type,
            Status = status,
            CreatedById = ManagerUserId,
            OwnerUserId = ManagerUserId
        };
        _context.SongLists.Add(songList);
        await _context.SaveChangesAsync();
        return songList;
    }

    private async Task<SongList> CreatePublishedSongListAsync(SongListTypeEnum type)
        => await CreateSongListAsync(type, SongListStatusEnum.Published);

    private SongListService CreateServiceForUser(string userId)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId)], "Test"))
            }
        };

        var mapper = new MapperConfiguration(cfg => cfg.AddMaps(typeof(SongListViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton(mapper);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<ILogService>(new LogService(httpContextAccessor));
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>()
            .AddEntityFrameworkStores<ChoraleDbContext>();

        var serviceProvider = services.BuildServiceProvider();
        return new SongListService(serviceProvider, new MembershipService(serviceProvider), new ChoirAuthorizationService(serviceProvider, new MembershipService(serviceProvider)));
    }
}
