using System;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
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
/// Réactivation d'un client suspendu (`10-D23`, lot 3). Contrairement à une suspension, une
/// réactivation peut faire ressurgir une consommation qui dépasse un plafond abaissé pendant
/// la suspension : le refus doit être explicite et chiffré, sans jamais amputer l'existant.
/// </summary>
[TestFixture]
public sealed class ClientReactivationTests
{
    private const string MemberUserId = "member-1";

    private ChoraleDbContext _context = null!;
    private Guid _clientId;
    private Guid _choirA;
    private Guid _choirB;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _clientId = ChoraleDbContext.NewIdGuid();
        _choirA = ChoraleDbContext.NewIdGuid();
        _choirB = ChoraleDbContext.NewIdGuid();

        _context.Users.Add(new User { Id = MemberUserId, UserName = "m@test.com", Email = "m@test.com" });
        _context.Clients.Add(new Client
        {
            Id = _clientId,
            Name = "Client Test",
            Status = ClientStatusEnum.Suspended,
            ChoirLimit = 5,
            MemberLimit = 250,
            StorageQuotaBytes = 1_000_000,
            MaxFileSizeBytes = 100_000
        });

        foreach (var choirId in new[] { _choirA, _choirB })
        {
            _context.Spaces.Add(new Space { Id = choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = _clientId });
            _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
            {
                Id = choirId, ClientId = _clientId, Name = $"Choir {choirId}", Status = ChoirStatusEnum.Published
            });
            _context.SpaceMembers.Add(new SpaceMember
            {
                Id = ChoraleDbContext.NewIdGuid(),
                UserId = MemberUserId,
                ChoirId = choirId,
                SpaceId = choirId,
                Status = MemberStatusEnum.Active
            });
        }

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task SuspendedClient_NominalReactivation_BecomesActive()
    {
        await Sut().ReactivateAsync(_clientId);

        var client = await _context.Clients.AsNoTracking().FirstAsync(c => c.Id == _clientId);
        Assert.That(client.Status, Is.EqualTo(ClientStatusEnum.Active));
    }

    [Test]
    public async Task UsageExceedsLoweredCap_ReactivationRejected_ExistingDataIntact()
    {
        var client = await _context.Clients.FirstAsync(c => c.Id == _clientId);
        client.ChoirLimit = 1; // deux chorales existent deja : le plafond a ete abaisse en dessous
        await _context.SaveChangesAsync();

        var exception = Assert.ThrowsAsync<CustomException>(() => Sut().ReactivateAsync(_clientId));
        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(exception.FrontMessage, Does.Contain("choirs"));

        // L'existant n'est jamais ampute par un refus de reactivation.
        var choirCount = await _context.Choirs.CountAsync(c => c.ClientId == _clientId);
        Assert.That(choirCount, Is.EqualTo(2));

        var reloadedClient = await _context.Clients.AsNoTracking().FirstAsync(c => c.Id == _clientId);
        Assert.That(reloadedClient.Status, Is.EqualTo(ClientStatusEnum.Suspended),
            "Un refus de reactivation ne doit pas laisser le client dans un etat intermediaire.");
    }

    [Test]
    public async Task CapAdjustedThenReactivation_Succeeds()
    {
        var client = await _context.Clients.FirstAsync(c => c.Id == _clientId);
        client.ChoirLimit = 1;
        await _context.SaveChangesAsync();

        Assert.ThrowsAsync<CustomException>(() => Sut().ReactivateAsync(_clientId));

        client.ChoirLimit = 5;
        await _context.SaveChangesAsync();

        await Sut().ReactivateAsync(_clientId);

        var clientFinal = await _context.Clients.AsNoTracking().FirstAsync(c => c.Id == _clientId);
        Assert.That(clientFinal.Status, Is.EqualTo(ClientStatusEnum.Active));
    }

    [Test]
    public async Task ArchivedClient_NeverReactivates()
    {
        var client = await _context.Clients.FirstAsync(c => c.Id == _clientId);
        client.Status = ClientStatusEnum.Archived;
        await _context.SaveChangesAsync();

        var exception = Assert.ThrowsAsync<CustomException>(() => Sut().ReactivateAsync(_clientId));
        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task AfterReactivation_RolesBecomeResolvableAgainOnAllChoirs()
    {
        await Sut().ReactivateAsync(_clientId);

        var resolver = new SpaceRoleResolverService(_context);
        var roles = await resolver.ResolveRolesAsync(MemberUserId);

        Assert.Multiple(() =>
        {
            Assert.That(roles.ContainsKey(_choirA), Is.True);
            Assert.That(roles.ContainsKey(_choirB), Is.True);
        });
    }

    private ClientService Sut()
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "admin-1"),
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

        var sp = services.BuildServiceProvider();
        return new ClientService(
            sp,
            new AuditLogService(sp),
            new ServiceLimitService(sp),
            new ClientRoleResolverService(_context));
    }
}
