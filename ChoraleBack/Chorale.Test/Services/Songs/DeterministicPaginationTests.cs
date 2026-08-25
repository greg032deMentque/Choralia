using System.Security.Claims;
using AutoMapper;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Services.UserServices;
using ChoraleBackEnd.Test.Fakes;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.SongLists;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.ChoirServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using ChoraleBackEnd.ViewModels.Songs;

namespace ChoraleBackEnd.Test.Services.Songs;

/// <summary>
/// Defaut de correction (pupitre C2) : <c>SongService.GetPagedAsync</c>,
/// <c>SongService.GetPagedByChoirAsync</c>, <c>SongListService.GetPagedAsync</c> et
/// <c>ChoirMembersService.GetPagedAsync</c> n'avaient aucun <c>OrderBy</c> — sur des lignes
/// de meme valeur de tri, deux pages consecutives pouvaient se recouvrir ou perdre des lignes.
/// Verifie que ce n'est plus le cas.
/// </summary>
/// <remarks>
/// Le non-chevauchement de deux pages consecutives EST la preuve du determinisme : si l'ordre
/// variait d'un appel a l'autre, les pages 1 et 2 se recouvriraient. Inutile d'y ajouter un test
/// d'idempotence « deux appels identiques rendent le meme ordre » — il serait plus faible.
/// </remarks>
[TestFixture]
public sealed class DeterministicPaginationTests
{
    private const string AdminUserId = "admin-1";
    private const string UserUserId = "user-1";

    private ChoraleDbContext _context = null!;
    private Guid _clientId;
    private Guid _choirId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _context.Users.Add(new User { Id = AdminUserId, UserName = "admin@test.com", Email = "admin@test.com" });
        _context.Users.Add(new User { Id = UserUserId, UserName = "user@test.com", Email = "user@test.com" });

        _clientId = ChoraleDbContext.NewIdGuid();
        _context.Clients.Add(new Client
        {
            Id = _clientId, Name = "Client Test", Status = ClientStatusEnum.Active,
            ChoirLimit = 10, MemberLimit = 100, StorageQuotaBytes = 1_000_000, MaxFileSizeBytes = 500_000
        });

        _choirId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = _choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = _clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = _choirId, ClientId = _clientId, Name = "Choir Test", Status = ChoirStatusEnum.Published
        });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task SongService_GetPagedAsync_SameTitleValues_TwoConsecutivePagesDoNotOverlapOrLoseAnyRow()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            var songId = ChoraleDbContext.NewIdGuid();
            _context.Songs.Add(new Song
            {
                Id = songId, ChoirId = _choirId, Title = "Meme Title", Status = SongStatusEnum.Active
            });
            ids.Add(songId);
        }
        await _context.SaveChangesAsync();

        var sut = SutSong();
        var page1 = await sut.GetPagedAsync(new SongPagedFilterViewModel { Page = 1, PageSize = 3 });
        var page2 = await sut.GetPagedAsync(new SongPagedFilterViewModel { Page = 2, PageSize = 3 });

        var idsPage1 = page1.Items.Select(i => i.Id).ToList();
        var idsPage2 = page2.Items.Select(i => i.Id).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(idsPage1, Has.Count.EqualTo(3));
            Assert.That(idsPage2, Has.Count.EqualTo(3));
            Assert.That(idsPage1.Intersect(idsPage2), Is.Empty);
            Assert.That(idsPage1.Concat(idsPage2).Distinct().Count(), Is.EqualTo(6));
            Assert.That(idsPage1.Concat(idsPage2).ToHashSet(), Is.EquivalentTo(ids));
        });
    }

    [Test]
    public async Task SongService_GetPagedByChoirAsync_SameTitleValues_TwoConsecutivePagesDoNotOverlapOrLoseAnyRow()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            var songId = ChoraleDbContext.NewIdGuid();
            _context.Songs.Add(new Song
            {
                Id = songId, ChoirId = _choirId, Title = "Meme Title", Status = SongStatusEnum.Active
            });
            ids.Add(songId);
        }
        await _context.SaveChangesAsync();

        var sut = SutSong();
        var page1 = await sut.GetPagedByChoirAsync(
            new SongByChoirFilterViewModel { ChoirId = _choirId, Page = 1, PageSize = 3 });
        var page2 = await sut.GetPagedByChoirAsync(
            new SongByChoirFilterViewModel { ChoirId = _choirId, Page = 2, PageSize = 3 });

        var idsPage1 = page1.Items.Select(i => i.Id).ToList();
        var idsPage2 = page2.Items.Select(i => i.Id).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(idsPage1, Has.Count.EqualTo(3));
            Assert.That(idsPage2, Has.Count.EqualTo(3));
            Assert.That(idsPage1.Intersect(idsPage2), Is.Empty);
            Assert.That(idsPage1.Concat(idsPage2).Distinct().Count(), Is.EqualTo(6));
            Assert.That(idsPage1.Concat(idsPage2).ToHashSet(), Is.EquivalentTo(ids));
        });
    }

    [Test]
    public async Task SongListService_GetPagedAsync_SameNameValues_TwoConsecutivePagesDoNotOverlapOrLoseAnyRow()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            var id = ChoraleDbContext.NewIdGuid();
            _context.SongLists.Add(new SongList
            {
                Id = id, Name = "Meme Nom", ChoirId = _choirId, Type = SongListTypeEnum.Free,
                Status = SongListStatusEnum.Draft, OwnerUserId = AdminUserId, CreatedById = AdminUserId
            });
            ids.Add(id);
        }
        await _context.SaveChangesAsync();

        var sut = SutSongList();
        var page1 = await sut.GetPagedAsync(new SongListPagedFilterViewModel { Page = 1, PageSize = 3 });
        var page2 = await sut.GetPagedAsync(new SongListPagedFilterViewModel { Page = 2, PageSize = 3 });

        var idsPage1 = page1.Items.Select(i => i.Id!.Value).ToList();
        var idsPage2 = page2.Items.Select(i => i.Id!.Value).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(idsPage1, Has.Count.EqualTo(3));
            Assert.That(idsPage2, Has.Count.EqualTo(3));
            Assert.That(idsPage1.Intersect(idsPage2), Is.Empty);
            Assert.That(idsPage1.Concat(idsPage2).ToHashSet(), Is.EquivalentTo(ids));
        });
    }

    [Test]
    public async Task ChoirMembersService_GetPagedAsync_SameNameAndFirstNameValues_TwoConsecutivePagesDoNotOverlapOrLoseAnyRow()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            var userId = $"member-{i}";
            var email = $"{userId}@test.com";
            _context.Users.Add(new User
            {
                Id = userId, UserName = email, Email = email, Firstname = "Meme", Lastname = "Meme"
            });

            var memberId = ChoraleDbContext.NewIdGuid();
            _context.SpaceMembers.Add(new SpaceMember
            {
                Id = memberId, ChoirId = _choirId, SpaceId = _choirId, UserId = userId, Status = MemberStatusEnum.Active
            });
            ids.Add(memberId);
        }
        await _context.SaveChangesAsync();

        var sut = SutMembers();
        var page1 = await sut.GetPagedAsync(_choirId, new PaginateViewModel { Page = 1, PageSize = 3 });
        var page2 = await sut.GetPagedAsync(_choirId, new PaginateViewModel { Page = 2, PageSize = 3 });

        var idsPage1 = page1.Items.Select(i => i.Id).ToList();
        var idsPage2 = page2.Items.Select(i => i.Id).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(idsPage1, Has.Count.EqualTo(3));
            Assert.That(idsPage2, Has.Count.EqualTo(3));
            Assert.That(idsPage1.Intersect(idsPage2), Is.Empty);
            Assert.That(idsPage1.Concat(idsPage2).ToHashSet(), Is.EquivalentTo(ids));
        });
    }


    private IServiceProvider BuildServiceProvider(bool admin)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, admin ? AdminUserId : UserUserId) };
        if (admin) claims.Add(new Claim(ClaimTypes.Role, nameof(UserRoleEnum.Admin)));

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        };

        var mapper = new MapperConfiguration(
            cfg => cfg.AddMaps(typeof(SongViewModel).Assembly),
            NullLoggerFactory.Instance).CreateMapper();

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton(mapper);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<ILogService>(new LogService(httpContextAccessor));
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>().AddEntityFrameworkStores<ChoraleDbContext>();

        return services.BuildServiceProvider();
    }

    private SongService SutSong()
    {
        var serviceProvider = BuildServiceProvider(admin: true);
        return new SongService(
            serviceProvider,
            new MembershipService(serviceProvider),
            new ChoirAuthorizationService(serviceProvider, new MembershipService(serviceProvider)));
    }

    private SongListService SutSongList()
    {
        var serviceProvider = BuildServiceProvider(admin: true);
        return new SongListService(serviceProvider, new MembershipService(serviceProvider), new ChoirAuthorizationService(serviceProvider, new MembershipService(serviceProvider)));
    }

    private ChoirMembersService SutMembers()
    {
        var serviceProvider = BuildServiceProvider(admin: true);
        var auditLogService = new AuditLogService(serviceProvider);
        return new ChoirMembersService(
            serviceProvider,
            new SectionService(serviceProvider),
            auditLogService,
            new FakeServiceLimitService(),
            new MembershipService(serviceProvider),
            new UserInvitationService(serviceProvider, new FakeEmailService()),
            new MemberEnrollmentService(serviceProvider),
            new SectionVoicePartLookupService(_context));
    }
}
