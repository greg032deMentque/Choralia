using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Test.Fakes;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.Clients;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
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
using ChoraleBackEnd.ViewModels.Choirs;

namespace ChoraleBackEnd.Test.Services.Clients;

/// <summary>
/// Isolation entre clients et frontière rôle-client / rôle-espace (`10-D23`, lot 3) : le rôle
/// <c>ManagerClient</c> ouvre la porte de « Ma structure », il n'entre pas dans le
/// contenu d'une chorale, et il ne touche jamais aux plafonds — fixés par l'administration
/// générale seule.
/// </summary>
[TestFixture]
public sealed class ClientIsolationTests
{
    private const string ManagerClientUserId = "resp-client-1";

    private ChoraleDbContext _context = null!;
    private Guid _clientId;
    private Guid _otherClientId;
    private Guid _choirId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _clientId = ChoraleDbContext.NewIdGuid();
        _otherClientId = ChoraleDbContext.NewIdGuid();
        _choirId = ChoraleDbContext.NewIdGuid();

        _context.Users.Add(new User { Id = ManagerClientUserId, UserName = "rc@test.com", Email = "rc@test.com" });

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

        _context.Spaces.Add(new Space { Id = _choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = _clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir { Id = _choirId, ClientId = _clientId, Name = "Choir A", Status = ChoirStatusEnum.Published });

        // Le ResponsableClient n'est délibérément PAS membre de cette chorale : le rôle
        // client n'ouvre pas la porte du contenu (`10-D23`).

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task GetChoirsAsync_ClientManager_SeesOwnClientChoirs()
    {
        var page = await ClientServiceSut(ManagerClientUserId).GetChoirsAsync(_clientId, new PaginateViewModel());

        Assert.That(page.Items.Select(c => c.Id), Does.Contain(_choirId));
    }

    [Test]
    public void GetChoirsAsync_ForeignClient_Is404()
    {
        Assert.ThrowsAsync<KeyNotFoundException>(
            () => ClientServiceSut(ManagerClientUserId).GetChoirsAsync(_otherClientId, new PaginateViewModel()));
    }

    [Test]
    public async Task GetChoirsAsync_ArchivedChoir_DoesNotAppearInMyStructure()
    {
        // Migration 13 : avant elle, IsDeleted portait l'archivage et le filtre de requête
        // par défaut suffisait à l'exclure d'ici. Une chorale Archive garde désormais
        // IsDeleted = false — sans exclusion explicite sur Statut, elle réapparaîtrait.
        var choir = await _context.Choirs.FirstAsync(c => c.Id == _choirId);
        choir.Status = ChoirStatusEnum.Archived;
        await _context.SaveChangesAsync();

        var page = await ClientServiceSut(ManagerClientUserId).GetChoirsAsync(_clientId, new PaginateViewModel());

        Assert.That(page.Items.Select(c => c.Id), Does.Not.Contain(_choirId));
    }

    [Test]
    public void AddMemberAsync_ClientManagerWithoutSpaceRole_ThrowsForbidden()
    {
        // Le rôle client n'ouvre pas la porte du contenu : sans rôle Responsable SUR CETTE
        // chorale, l'appel est refusé — même si l'appelant est ResponsableClient du client
        // qui la possède.
        var exception = Assert.ThrowsAsync<CustomException>(
            () => ChoirServiceSut(ManagerClientUserId).AddMemberAsync(_choirId, "quelqu-un"));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public void UpdateLimitsAsync_ClientManager_ThrowsForbidden()
    {
        var exception = Assert.ThrowsAsync<CustomException>(
            () => ClientServiceSut(ManagerClientUserId).UpdateLimitsAsync(new UpdateClientLimitsViewModel
            {
                Id = _clientId,
                ChoirLimit = 10,
                MemberLimit = 500,
                StorageQuotaBytes = 2_000_000,
                MaxFileSizeBytes = 200_000
            }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task GetByIdAsync_ClientManager_ReadsOwnLimits()
    {
        var client = await ClientServiceSut(ManagerClientUserId).GetByIdAsync(_clientId);

        Assert.Multiple(() =>
        {
            Assert.That(client.ChoirLimit, Is.EqualTo(5));
            Assert.That(client.ChoirCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SuspendedClient_ClientManager_StillReadsButCannotWrite()
    {
        var client = await _context.Clients.FirstAsync(c => c.Id == _clientId);
        client.Status = ClientStatusEnum.Suspended;
        await _context.SaveChangesAsync();

        var read = await ClientServiceSut(ManagerClientUserId).GetByIdAsync(_clientId);
        Assert.That(read.Status, Is.EqualTo(ClientStatusEnum.Suspended));

        var exception = Assert.ThrowsAsync<CustomException>(
            () => ClientServiceSut(ManagerClientUserId).UpdateLimitsAsync(new UpdateClientLimitsViewModel
            {
                Id = _clientId,
                ChoirLimit = 10,
                MemberLimit = 500,
                StorageQuotaBytes = 2_000_000,
                MaxFileSizeBytes = 200_000
            }));
        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    private ClientService ClientServiceSut(string userId)
    {
        var sp = BuildServiceProvider(userId);
        return new ClientService(sp, new AuditLogService(sp), new ServiceLimitService(sp), new ClientRoleResolverService(_context));
    }

    private ChoirService ChoirServiceSut(string userId)
    {
        var sp = BuildServiceProvider(userId);
        return new ChoirService(
            sp,
            new AuditLogService(sp),
            new FakeServiceLimitService(),
            new MembershipService(sp),
            new ClientRoleResolverService(_context),
            new SpaceRoleResolverService(_context),
            new SectionService(sp));
    }

    private IServiceProvider BuildServiceProvider(string userId)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test"))
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

        return services.BuildServiceProvider();
    }
}
