using System;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Test.Fakes;
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
/// Changement de statut d'une chorale par le <c>ManagerClient</c> de son client
/// (<c>ClientService.ChangeChoirStatusAsync</c>, `10-D23`). Réutilise strictement
/// <c>ChoirStateHelper.IsTransitionAllowed</c> — la table de transitions elle-même est déjà
/// couverte en détail par <c>ChoirStatusTests</c> ; cette suite ne revalide que l'isolation
/// client (IDOR) et les garde-fous propres à ce point d'entrée.
/// </summary>
[TestFixture]
public sealed class ClientChoirStatusTests
{
    private const string ManagerClientUserId = "resp-client-1";

    private ChoraleDbContext _context = null!;
    private Guid _clientId;
    private Guid _otherClientId;
    private Guid _choirId;
    private Guid _choirOtherClientId;

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
        _choirOtherClientId = ChoraleDbContext.NewIdGuid();

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
        _context.Choirs.Add(new Choir
        {
            Id = _choirId, ClientId = _clientId, Name = "Choir A", Status = ChoirStatusEnum.Published
        });

        _context.Spaces.Add(new Space { Id = _choirOtherClientId, SpaceType = SpaceTypeEnum.Choir, ClientId = _otherClientId });
        _context.Choirs.Add(new Choir
        {
            Id = _choirOtherClientId, ClientId = _otherClientId, Name = "Choir B", Status = ChoirStatusEnum.Published
        });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task ChangeChoirStatusAsync_AllowedTransition_IsApplied()
    {
        await Sut(ManagerClientUserId).ChangeChoirStatusAsync(_clientId, _choirId, ChoirStatusEnum.Cancelled);

        var choir = await _context.Choirs.FirstAsync(c => c.Id == _choirId);
        Assert.That(choir.Status, Is.EqualTo(ChoirStatusEnum.Cancelled));
    }

    [Test]
    public void ChangeChoirStatusAsync_ForbiddenTransition_ThrowsConflict()
    {
        // Publie -> Draft n'est pas autorisé, même table que l'administration générale.
        var ex = Assert.ThrowsAsync<CustomException>(
            () => Sut(ManagerClientUserId).ChangeChoirStatusAsync(_clientId, _choirId, ChoirStatusEnum.Draft));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public void ChangeChoirStatusAsync_ForeignClient_Is404()
    {
        Assert.ThrowsAsync<KeyNotFoundException>(
            () => Sut(ManagerClientUserId).ChangeChoirStatusAsync(
                _otherClientId, _choirOtherClientId, ChoirStatusEnum.Cancelled));
    }

    [Test]
    public void ChangeChoirStatusAsync_ChoirIdBelongsToAnotherClient_Is404()
    {
        // Deuxième barrière IDOR : clientId légitime (ManagerClientUserId en est responsable),
        // mais choirId appartient à _otherClientId — refusé même si la policy HTTP a laissé
        // passer la route sur la seule base du clientId.
        Assert.ThrowsAsync<KeyNotFoundException>(
            () => Sut(ManagerClientUserId).ChangeChoirStatusAsync(
                _clientId, _choirOtherClientId, ChoirStatusEnum.Cancelled));
    }

    [Test]
    public async Task ChangeChoirStatusAsync_ArchivedToPublished_AboveLimit_IsRejectedWithQuantifiedImpact()
    {
        // Deuxième chorale du client, pour occuper la seule place restante une fois le
        // plafond abaissé à 1 — même scénario que ChoirStatusTests (administration générale),
        // rejoué ici avec le point d'entrée ManagerClient.
        var otherChoirId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = otherChoirId, SpaceType = SpaceTypeEnum.Choir, ClientId = _clientId });
        _context.Choirs.Add(new Choir
        {
            Id = otherChoirId, ClientId = _clientId, Name = "Autre Choir", Status = ChoirStatusEnum.Published
        });

        var sut = SutWithRealLimits(ManagerClientUserId);
        await sut.ChangeChoirStatusAsync(_clientId, _choirId, ChoirStatusEnum.Archived);

        var client = await _context.Clients.FirstAsync(c => c.Id == _clientId);
        client.ChoirLimit = 1;
        await _context.SaveChangesAsync();

        var ex = Assert.ThrowsAsync<CustomException>(
            () => sut.ChangeChoirStatusAsync(_clientId, _choirId, ChoirStatusEnum.Published));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(ex.Message, Does.Contain("1"), "Le refus doit chiffrer l'impact, pas échouer en silence.");
        });
    }

    private ClientService Sut(string userId)
    {
        var sp = BuildServiceProvider(userId);
        return new ClientService(
            sp, new AuditLogService(sp), new FakeServiceLimitService(), new ClientRoleResolverService(_context));
    }

    private ClientService SutWithRealLimits(string userId)
    {
        var sp = BuildServiceProvider(userId);
        return new ClientService(
            sp, new AuditLogService(sp), new ServiceLimitService(sp), new ClientRoleResolverService(_context));
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
