using System.Security.Claims;
using AutoMapper;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.Clients;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using ChoraleBackEnd.ViewModels.Choirs;

namespace ChoraleBackEnd.Test.Services.Clients;

/// <summary>
/// A2 : <c>ClientController</c> exposait la designation et le retrait d'un responsable
/// (`POST`/`DELETE {clientId}/Managers`) sans jamais permettre de les LISTER — l'ecran ne
/// pouvait donc afficher personne, et le retrait (qui exige un <c>userId</c>) etait
/// inutilisable en pratique. Ce fichier verifie <see cref="IClientService.GetManagersAsync"/>.
/// </summary>
[TestFixture]
public sealed class ClientManagersTests
{
    private const string ManagerClientUserId = "resp-client-liste-1";
    private const string OtherManagerClientUserId = "resp-client-liste-2";

    private ChoraleDbContext _context = null!;
    private Guid _clientId;
    private Guid _otherClientId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _clientId = ChoraleDbContext.NewIdGuid();
        _otherClientId = ChoraleDbContext.NewIdGuid();

        _context.Users.Add(new User { Id = ManagerClientUserId, UserName = "rc1@t.com", Email = "rc1@t.com", Firstname = "Alice", Lastname = "Martin" });
        _context.Users.Add(new User { Id = OtherManagerClientUserId, UserName = "rc2@t.com", Email = "rc2@t.com", Firstname = "Bruno", Lastname = "Petit" });

        _context.Clients.Add(new Client
        {
            Id = _clientId, Name = "Client A", Status = ClientStatusEnum.Active,
            ChoirLimit = 5, MemberLimit = 250, StorageQuotaBytes = 1_000_000, MaxFileSizeBytes = 100_000
        });
        _context.Clients.Add(new Client
        {
            Id = _otherClientId, Name = "Client B", Status = ClientStatusEnum.Active,
            ChoirLimit = 5, MemberLimit = 250, StorageQuotaBytes = 1_000_000, MaxFileSizeBytes = 100_000
        });

        _context.ClientMembers.Add(new ClientMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ClientId = _clientId,
            UserId = ManagerClientUserId,
            Role = UserRoleEnum.ClientManager
        });

        // Responsable d'un AUTRE client : ne doit jamais apparaitre dans la liste de _clientId.
        _context.ClientMembers.Add(new ClientMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ClientId = _otherClientId,
            UserId = OtherManagerClientUserId,
            Role = UserRoleEnum.ClientManager
        });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task GetManagersAsync_Nominal_ListsClientManagers()
    {
        var page = await ClientServiceSut(ManagerClientUserId).GetManagersAsync(_clientId, new PaginateViewModel());

        Assert.That(page.Items.Select(r => r.UserId), Does.Contain(ManagerClientUserId));
        Assert.That(page.Items.Select(r => r.UserId), Does.Not.Contain(OtherManagerClientUserId));
    }

    [Test]
    public void GetManagersAsync_ManagerOfAnotherClient_Is404()
    {
        Assert.ThrowsAsync<KeyNotFoundException>(
            () => ClientServiceSut(OtherManagerClientUserId).GetManagersAsync(_clientId, new PaginateViewModel()));
    }

    [Test]
    public async Task GetManagersAsync_SoftDeletedManager_IsAbsentFromList()
    {
        var member = await _context.ClientMembers.FirstAsync(m => m.ClientId == _clientId && m.UserId == ManagerClientUserId);
        member.IsDeleted = true;
        await _context.SaveChangesAsync();

        // Interroge via un compte Admin : le responsable soft-delete ne pourrait de toute
        // facon plus s'authentifier lui-meme dans un scenario reel. Seul le filtre de
        // requete (HasQueryFilter sur ClientMember) est ici sous test.
        var page = await ClientServiceAdminSut().GetManagersAsync(_clientId, new PaginateViewModel());
        Assert.That(page.Items, Is.Empty);
    }

    [Test]
    public async Task GetManagersAsync_ClientWithoutManager_EmptyListNeverNull()
    {
        var clientWithoutManager = ChoraleDbContext.NewIdGuid();
        _context.Clients.Add(new Client
        {
            Id = clientWithoutManager, Name = "Client Sans Manager", Status = ClientStatusEnum.Active,
            ChoirLimit = 5, MemberLimit = 250, StorageQuotaBytes = 1_000_000, MaxFileSizeBytes = 100_000
        });
        await _context.SaveChangesAsync();

        var page = await ClientServiceAdminSut().GetManagersAsync(clientWithoutManager, new PaginateViewModel());

        Assert.That(page.Items, Is.Not.Null);
        Assert.That(page.Items, Is.Empty);
        Assert.That(page.TotalCount, Is.EqualTo(0));
    }

    [Test]
    public async Task AssignThenList_NewManagerAppears_RemoveThenList_ItDisappears()
    {
        const string newUserEmail = "nouveau-resp@t.com";
        const string newUserId = "nouveau-resp-id";
        _context.Users.Add(new User
        {
            Id = newUserId, UserName = newUserEmail, Email = newUserEmail,
            NormalizedEmail = newUserEmail.ToUpperInvariant(),
            NormalizedUserName = newUserEmail.ToUpperInvariant(),
            Firstname = "Claire", Lastname = "Durand"
        });
        await _context.SaveChangesAsync();

        var service = ClientServiceAdminSut();

        await service.AssignManagerAsync(_clientId, new AssignClientManagerViewModel { Email = newUserEmail });

        var afterAssignment = await service.GetManagersAsync(_clientId, new PaginateViewModel());
        Assert.That(afterAssignment.Items.Select(r => r.UserId), Does.Contain(newUserId));

        await service.RemoveManagerAsync(_clientId, newUserId);

        var afterRemoval = await service.GetManagersAsync(_clientId, new PaginateViewModel());
        Assert.That(afterRemoval.Items.Select(r => r.UserId), Does.Not.Contain(newUserId));
    }

    [Test]
    public async Task GetManagersAsync_FilterByName_ReturnsOnlyMatchingRows()
    {
        // Defaut corrige : pagination.Filter etait ignore, la recherche du front etait
        // silencieusement inoperante. Deux responsables du meme client, filtre cible un seul.
        await AddOtherManagerAsync();

        var page = await ClientServiceSut(ManagerClientUserId)
            .GetManagersAsync(_clientId, new PaginateViewModel { Filter = "Martin" });

        Assert.That(page.Items.Select(r => r.UserId), Does.Contain(ManagerClientUserId));
        Assert.That(page.Items.Select(r => r.UserId), Does.Not.Contain("resp-client-liste-3"));
        Assert.That(page.TotalCount, Is.EqualTo(1));
    }

    [Test]
    public async Task GetManagersAsync_EmptyOrBlankFilter_ReturnsAllManagers()
    {
        await AddOtherManagerAsync();

        var page = await ClientServiceSut(ManagerClientUserId)
            .GetManagersAsync(_clientId, new PaginateViewModel { Filter = "   " });

        Assert.That(page.Items.Select(r => r.UserId), Does.Contain(ManagerClientUserId));
        Assert.That(page.Items.Select(r => r.UserId), Does.Contain("resp-client-liste-3"));
        Assert.That(page.TotalCount, Is.EqualTo(2));
    }

    private async Task AddOtherManagerAsync()
    {
        const string otherManagerId = "resp-client-liste-3";
        _context.Users.Add(new User
        {
            Id = otherManagerId, UserName = "rc3@t.com", Email = "rc3@t.com",
            Firstname = "Claude", Lastname = "Dupont"
        });
        _context.ClientMembers.Add(new ClientMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ClientId = _clientId,
            UserId = otherManagerId, Role = UserRoleEnum.ClientManager
        });
        await _context.SaveChangesAsync();
    }

    private ClientService ClientServiceSut(string userId)
    {
        var sp = BuildServiceProvider(userId);
        return new ClientService(sp, new AuditLogService(sp), new ServiceLimitService(sp), new ClientRoleResolverService(_context));
    }

    private ClientService ClientServiceAdminSut()
    {
        var sp = BuildServiceProvider("admin-liste-responsables", isAdmin: true);
        return new ClientService(sp, new AuditLogService(sp), new ServiceLimitService(sp), new ClientRoleResolverService(_context));
    }

    private IServiceProvider BuildServiceProvider(string userId, bool isAdmin = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        if (isAdmin) claims.Add(new Claim(ClaimTypes.Role, UserRoleEnum.Admin.ToString()));

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
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

        return services.BuildServiceProvider();
    }
}
