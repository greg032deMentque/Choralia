using System.Net;
using System.Security.Claims;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Services.UserServices;
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
using ChoraleBackEnd.ViewModels.Events;

namespace ChoraleBackEnd.Test.Services.Events;

/// <summary>
/// Resolution du client d'un evenement autonome depuis l'identite du createur, quand ni
/// chorale ni client ne sont fournis explicitement (`10-D23`).
/// </summary>
/// <remarks>
/// Decision produit : « la personne qui cree un evenement autonome est elle aussi un
/// client Â» â€” un evenement sans client n'existe pas. La valeur de repli
/// <c>ClientId ?? Guid.Empty</c> retiree ici reproduisait exactement le trou que ce lot
/// devait fermer : sous SQL Server, une violation de FK au premier INSERT ; si la FK venait
/// a manquer, un espace hors quota — le bug d'origine.
/// </remarks>
[TestFixture]
public sealed class EventClientAutonomeTests
{
    private const string CreatorUserId = "createur-1";

    private ChoraleDbContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);
        _context.Users.Add(new User { Id = CreatorUserId, UserName = "c@t.com", Email = "c@t.com", EmailConfirmed = true });
        _context.SaveChanges();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task CreateAsync_CreatorManagerOfASingleClient_ResolvesThatClient()
    {
        var clientId = AddClient(ClientStatusEnum.Active);
        AddClientMember(clientId);

        var result = await CreateService().CreateAsync(NewStandaloneEvent());

        var space = await _context.Spaces.AsNoTracking().FirstAsync(e => e.Id == result.Id);
        Assert.That(space.ClientId, Is.EqualTo(clientId));
    }

    [Test]
    public async Task CreateAsync_CreatorManagerOfTwoClients_ThrowsBadRequestWithoutPersistingAnything()
    {
        var clientA = AddClient(ClientStatusEnum.Active);
        var clientB = AddClient(ClientStatusEnum.Active);
        AddClientMember(clientA);
        AddClientMember(clientB);

        var sut = CreateService();
        var exception = Assert.ThrowsAsync<CustomException>(
            () => sut.CreateAsync(NewStandaloneEvent()));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await _context.Events.CountAsync(), Is.EqualTo(0));
        Assert.That(await _context.Spaces.CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task CreateAsync_CreatorWithoutAnyClient_ThrowsExplicitExceptionWithoutPersistingAnything()
    {
        var sut = CreateService();

        var exception = Assert.ThrowsAsync<CustomException>(
            () => sut.CreateAsync(NewStandaloneEvent()));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await _context.Events.CountAsync(), Is.EqualTo(0));
        Assert.That(await _context.Spaces.CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task CreateAsync_CreatorsClientSuspended_IsRejected()
    {
        var clientId = AddClient(ClientStatusEnum.Suspended);
        AddClientMember(clientId);

        var sut = CreateService();
        var exception = Assert.ThrowsAsync<CustomException>(
            () => sut.CreateAsync(NewStandaloneEvent()));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(await _context.Events.CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task CreateAsync_StandaloneEvent_SpaceClientIdIsNeverGuidEmpty()
    {
        var clientId = AddClient(ClientStatusEnum.Active);
        AddClientMember(clientId);

        var result = await CreateService().CreateAsync(NewStandaloneEvent());

        var space = await _context.Spaces.AsNoTracking().FirstAsync(e => e.Id == result.Id);
        Assert.That(space.ClientId, Is.Not.EqualTo(Guid.Empty));
    }

    private static EventViewModel NewStandaloneEvent() => new()
    {
        Title = "Event Autonome",
        StartDate = DateTime.UtcNow,
        Type = EventTypeEnum.Rehearsal,
        ChoirId = null
    };

    private Guid AddClient(ClientStatusEnum status)
    {
        var clientId = ChoraleDbContext.NewIdGuid();
        _context.Clients.Add(new Client { Id = clientId, Name = $"Client {clientId}", Status = status });
        _context.SaveChanges();
        return clientId;
    }

    private void AddClientMember(Guid clientId)
    {
        _context.ClientMembers.Add(new ClientMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ClientId = clientId,
            UserId = CreatorUserId,
            Role = UserRoleEnum.ClientManager
        });
        _context.SaveChanges();
    }

    private EventService CreateService()
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, CreatorUserId)], "Test"))
            }
        };

        var mapper = new MapperConfiguration(
            cfg => cfg.AddMaps(typeof(EventViewModel).Assembly),
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
        var authorizationService = new EventAuthorizationService(serviceProvider, new ChoirAuthorizationService(serviceProvider, new MembershipService(serviceProvider)));
        var auditLogService = new AuditLogService(serviceProvider);
        var guestAccountLifecycleService = new GuestAccountLifecycleService(serviceProvider, auditLogService);
        var clientRoleResolverService = new ClientRoleResolverService(_context);

        return new EventService(
            serviceProvider, authorizationService, guestAccountLifecycleService, clientRoleResolverService,
            new MembershipService(serviceProvider), new EventParticipationSeedingService(serviceProvider));
    }
}
