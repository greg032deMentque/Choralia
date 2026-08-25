using System.Security.Claims;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Test.Fakes;
using ChoraleBackEnd.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels.Choirs;

namespace ChoraleBackEnd.Test.Services.Choirs;

/// <summary>
/// Visibilite de la liste des chorales selon l'identite authentifiee (`10-D23`). Avant ce
/// lot, `GetPaged` ne portait aucune restriction de role au niveau du controleur ; le
/// filtrage au niveau service ignorait par ailleurs le role ResponsableClient, qui ne voyait
/// donc que les chorales dont il etait personnellement membre.
/// </summary>
[TestFixture]
public sealed class ChoirVisibilityTests
{
    private const string AdminUserId = "admin-1";
    private const string ManagerClientUserId = "responsable-client-1";
    private const string SingerUserId = "chanteur-1";

    private ChoraleDbContext _context = null!;
    private Guid _clientA;
    private Guid _clientB;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _clientA = ChoraleDbContext.NewIdGuid();
        _clientB = ChoraleDbContext.NewIdGuid();

        _context.Users.Add(new User { Id = AdminUserId, UserName = "admin@t.com", Email = "admin@t.com" });
        _context.Users.Add(new User { Id = ManagerClientUserId, UserName = "rc@t.com", Email = "rc@t.com" });
        _context.Users.Add(new User { Id = SingerUserId, UserName = "ch@t.com", Email = "ch@t.com" });

        _context.Clients.Add(new Client { Id = _clientA, Name = "Client A", Status = ClientStatusEnum.Active });
        _context.Clients.Add(new Client { Id = _clientB, Name = "Client B", Status = ClientStatusEnum.Active });

        _context.ClientMembers.Add(new ClientMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ClientId = _clientA,
            UserId = ManagerClientUserId, Role = UserRoleEnum.ClientManager
        });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task GetPagedAsync_Singer_ReturnsOnlyOwnChoirs()
    {
        var ownChoir = AddChoir(_clientA, "Choir Du Singer");
        AddMemberActive(ownChoir, SingerUserId);
        AddChoir(_clientA, "Autre Choir Du Meme Client");
        await _context.SaveChangesAsync();

        var result = await CreateService(SingerUserId).GetPagedAsync(new PaginateViewModel());

        Assert.That(result.Items.Select(c => c.Id), Is.EquivalentTo(new[] { ownChoir }));
    }

    [Test]
    public async Task GetPagedAsync_Admin_ReturnsAllChoirsAcrossAllClients()
    {
        AddChoir(_clientA, "Choir A");
        AddChoir(_clientB, "Choir B");
        await _context.SaveChangesAsync();

        var result = await CreateService(AdminUserId, isAdmin: true).GetPagedAsync(new PaginateViewModel());

        Assert.That(result.TotalCount, Is.EqualTo(2));
    }

    [Test]
    public async Task GetPagedAsync_ClientManager_ReturnsOnlyOwnClientChoirs()
    {
        var ownClientChoir = AddChoir(_clientA, "Choir Client A");
        AddChoir(_clientB, "Choir Client B");
        await _context.SaveChangesAsync();

        var result = await CreateService(ManagerClientUserId).GetPagedAsync(new PaginateViewModel());

        Assert.That(result.Items.Select(c => c.Id), Is.EquivalentTo(new[] { ownClientChoir }));
    }

    [Test]
    public async Task GetPagedAsync_SingerOfMultipleChoirs_ReturnsExactlyOwnOnes()
    {
        var firstChoir = AddChoir(_clientA, "Premiere Choir");
        var secondChoir = AddChoir(_clientB, "Seconde Choir");
        AddChoir(_clientA, "Choir Sans Rapport");
        AddMemberActive(firstChoir, SingerUserId);
        AddMemberActive(secondChoir, SingerUserId);
        await _context.SaveChangesAsync();

        var result = await CreateService(SingerUserId).GetPagedAsync(new PaginateViewModel());

        Assert.That(result.Items.Select(c => c.Id), Is.EquivalentTo(new[] { firstChoir, secondChoir }));
    }

    [Test]
    public async Task GetPagedAsync_ChoirSoftDeleted_NeverListedEvenForAdmin()
    {
        var deletedChoir = AddChoir(_clientA, "Choir Supprimee");
        await _context.SaveChangesAsync();

        var choir = await _context.Choirs.FirstAsync(c => c.Id == deletedChoir);
        choir.IsDeleted = true;
        await _context.SaveChangesAsync();

        var result = await CreateService(AdminUserId, isAdmin: true).GetPagedAsync(new PaginateViewModel());

        Assert.That(result.Items.Select(c => c.Id), Does.Not.Contain(deletedChoir));
    }

    [Test]
    public async Task GetPagedAsync_ChoirOfASuspendedClient_ListedForAdminAbsentForMember()
    {
        var choir = AddChoir(_clientA, "Choir Client Suspendu");
        AddMemberActive(choir, SingerUserId);
        await _context.SaveChangesAsync();

        var client = await _context.Clients.FirstAsync(c => c.Id == _clientA);
        client.Status = ClientStatusEnum.Suspended;
        await _context.SaveChangesAsync();

        var adminResult = await CreateService(AdminUserId, isAdmin: true).GetPagedAsync(new PaginateViewModel());
        var memberResult = await CreateService(SingerUserId).GetPagedAsync(new PaginateViewModel());

        Assert.Multiple(() =>
        {
            Assert.That(adminResult.Items.Select(c => c.Id), Does.Contain(choir));
            Assert.That(memberResult.Items.Select(c => c.Id), Does.Not.Contain(choir));
        });
    }

    [Test]
    public async Task GetPagedAsync_Pagination_IsDeterministicAcrossTwoPages()
    {
        for (var i = 0; i < 5; i++)
            AddChoir(_clientA, $"Choir {i:D2}");
        await _context.SaveChangesAsync();

        var sut = CreateService(AdminUserId, isAdmin: true);

        var page1 = await sut.GetPagedAsync(new PaginateViewModel { Page = 1, PageSize = 2 });
        var page2 = await sut.GetPagedAsync(new PaginateViewModel { Page = 2, PageSize = 2 });

        Assert.That(page1.Items.Select(c => c.Id).Intersect(page2.Items.Select(c => c.Id)), Is.Empty,
            "Sans tri stable, Skip/Take peut renvoyer la meme ligne sur deux pages ou en sauter une.");
    }

    private Guid AddChoir(Guid clientId, string name)
    {
        var choirId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = choirId, ClientId = clientId, Name = name, Status = ChoirStatusEnum.Published
        });
        return choirId;
    }

    private void AddMemberActive(Guid choirId, string userId)
    {
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = choirId, SpaceId = choirId,
            UserId = userId, Status = MemberStatusEnum.Active
        });
    }

    private ChoirService CreateService(string userId, bool isAdmin = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        if (isAdmin)
            claims.Add(new Claim(ClaimTypes.Role, UserRoleEnum.Admin.ToString()));

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        };

        var mapper = new MapperConfiguration(cfg => cfg.AddMaps(typeof(ChoirViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton(mapper);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<ILogService>(new LogService(httpContextAccessor));
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>().AddEntityFrameworkStores<ChoraleDbContext>();

        var serviceProvider = services.BuildServiceProvider();
        var auditLogService = new AuditLogService(serviceProvider);
        var membershipService = new MembershipService(serviceProvider);
        var clientRoleResolverService = new ClientRoleResolverService(_context);
        var spaceRoleResolverService = new SpaceRoleResolverService(_context);

        return new ChoirService(
            serviceProvider, auditLogService, new FakeServiceLimitService(), membershipService,
            clientRoleResolverService, spaceRoleResolverService, new SectionService(serviceProvider));
    }
}
