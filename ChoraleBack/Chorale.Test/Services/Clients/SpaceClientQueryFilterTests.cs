using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Test.Fakes;
using ChoraleBackEnd.ViewModels.AdminChoirs;
using ChoraleBackEnd.ViewModels.AdminEvents;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using ChoraleBackEnd.ViewModels.Events;
using ChoraleBackEnd.ViewModels.Choirs;

namespace ChoraleBackEnd.Test.Services.Clients;

/// <summary>
/// SpaceConfiguration.HasQueryFilter(!e.Client.IsDeleted) : correction de l'avertissement EF
/// <c>PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning</c> constate sur la
/// relation Client/Espace (migration 12, <c>AjouteClientSurSpace</c>, qui a rendu
/// <c>Space.ClientId</c> obligatoire). Avant ce filtre, un client supprime laissait ses
/// espaces — donc les chorales et events qui en dependent — pleinement visibles.
/// </summary>
/// <remarks>
/// La cascade est le point le moins evident : <c>ChoirConfiguration</c> et
/// <c>EventConfiguration</c> filtrent deja sur <c>!Space.IsDeleted</c>. Comme cette
/// expression force EF a joindre l'entite <c>Space</c>, le filtre desormais defini sur
/// <c>Space</c> (donc sur son client) s'applique automatiquement partout ou une Chorale ou
/// un Event est interroge — sans qu'aucune des deux configurations n'ait ete modifiee.
/// C'est ce qui ferme le trou (aucune ligne orpheline), et c'est aussi ce qui a du etre
/// corrige a part sur <c>AdminChoirService</c>/<c>AdminEventService</c> : ces deux
/// services doivent au contraire continuer a voir ce qu'un client supprime laisse derriere
/// lui (`10-D23`), et s'appuyaient sur le filtre par defaut sans le savoir.
/// </remarks>
[TestFixture]
public sealed class SpaceClientQueryFilterTests
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
        _context.Users.Add(new User { Id = AdminUserId, UserName = "admin@test.com", Email = "admin@test.com" });
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task Space_DeletedClient_InvisibleFromDirectRead()
    {
        var clientId = CreateClient(isDeleted: true);
        var choirId = CreateSpaceChoir(clientId, "Choir");
        await _context.SaveChangesAsync();

        var space = await _context.Spaces.AsNoTracking().FirstOrDefaultAsync(e => e.Id == choirId);

        Assert.That(space, Is.Null);
    }

    [Test]
    public async Task Space_ActiveClient_StaysVisible_NoRegression()
    {
        var clientId = CreateClient(isDeleted: false);
        var choirId = CreateSpaceChoir(clientId, "Choir");
        await _context.SaveChangesAsync();

        var space = await _context.Spaces.AsNoTracking().FirstOrDefaultAsync(e => e.Id == choirId);

        Assert.That(space, Is.Not.Null);
    }

    [Test]
    public async Task Choir_DeletedClient_NoOrphanRow_DirectRead()
    {
        var clientId = CreateClient(isDeleted: true);
        CreateSpaceChoir(clientId, "Choir Orpheline");
        await _context.SaveChangesAsync();

        var count = await _context.Choirs.CountAsync();

        Assert.That(count, Is.EqualTo(0),
            "Chorale herite du filtre d'Espace via `!c.Space.IsDeleted` (jointure forcee) : " +
            "la cascade doit aussi exclure une chorale dont le client est supprime.");
    }

    [Test]
    public async Task Event_DeletedClient_NoOrphanRow_DirectRead()
    {
        var clientId = CreateClient(isDeleted: true);
        CreateSpaceEvent(clientId, "Concert Orphelin");
        await _context.SaveChangesAsync();

        var count = await _context.Events.CountAsync();

        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public async Task ChoirService_BusinessPath_DoesNotReturnChoirWhoseClientIsDeleted()
    {
        var clientId = CreateClient(isDeleted: true);
        CreateSpaceChoir(clientId, "Choir Client Supprime");
        await _context.SaveChangesAsync();

        // isAdmin: true delibere : le role Admin sur le point d'entry METIER (ChoirService)
        // ne doit pas suffire a voir le contenu d'un client supprime — seule l'administration
        // generale dediee (AdminChoirService, teste plus bas) le peut.
        var result = await CreateChoirService(AdminUserId, isAdmin: true).GetPagedAsync(new PaginateViewModel());

        Assert.That(result.TotalCount, Is.EqualTo(0));
    }

    [Test]
    public async Task AdminChoirService_KeepsAccessToChoirWhoseClientIsDeleted()
    {
        var clientId = CreateClient(isDeleted: true);
        var choirId = CreateSpaceChoir(clientId, "Choir Client Supprime");
        await _context.SaveChangesAsync();

        var result = await CreateAdminChoirService()
            .GetPagedAsync(new AdminChoirsPagedFilterViewModel { PageSize = 50 });

        Assert.That(result.Items.Select(c => c.Id), Does.Contain(choirId));
    }

    [Test]
    public async Task AdminChoirService_DoesNotLoseOwnExclusionWhenChoirIsDeleted_NoRegression()
    {
        // Non-regression : seul le cas "client supprime" est concerne par cette correction.
        // Une chorale reellement supprimee (son propre IsDeleted) doit continuer a rester
        // invisible, y compris de l'administration generale.
        var clientId = CreateClient(isDeleted: false);
        var choirId = CreateSpaceChoir(clientId, "Choir Supprimee");
        await _context.SaveChangesAsync();
        var choir = await _context.Choirs.FirstAsync(c => c.Id == choirId);
        choir.IsDeleted = true;
        await _context.SaveChangesAsync();

        var result = await CreateAdminChoirService()
            .GetPagedAsync(new AdminChoirsPagedFilterViewModel { PageSize = 50 });

        Assert.That(result.Items.Select(c => c.Id), Does.Not.Contain(choirId));
    }

    [Test]
    public async Task AdminEventService_KeepsAccessToEventWhoseClientIsDeleted()
    {
        var clientId = CreateClient(isDeleted: true);
        var eventId = CreateSpaceEvent(clientId, "Concert Client Supprime");
        await _context.SaveChangesAsync();

        var result = await CreateAdminEventService()
            .GetPagedAsync(new AdminEventsPagedFilterViewModel { PageSize = 50 });

        Assert.That(result.Items.Select(e => e.Id), Does.Contain(eventId));
    }

    [Test]
    public async Task ServiceLimitService_DeletedClient_GetUsageThrowsKeyNotFoundException()
    {
        // ChargerClientAsync s'appuie sur le filtre propre a Client (independant de cette
        // correction) : verifie que la garde tient toujours pour un client dont des chorales
        // n'auraient pas ete nettoyees.
        var clientId = CreateClient(isDeleted: true);
        CreateSpaceChoir(clientId, "Choir Fantome");
        await _context.SaveChangesAsync();

        Assert.ThrowsAsync<KeyNotFoundException>(
            () => CreateServiceLimitService().GetUsageAsync(clientId));
    }

    [Test]
    public async Task ServiceLimitService_CountChoirs_DoesNotCountChoirWhoseClientIsDeleted()
    {
        // Verifie directement le mecanisme de comptage sous-jacent de
        // ServiceLimitService.CountChoirsAsync (meme forme de requete) : une chorale dont
        // le client est supprime ne doit jamais etre comptee, y compris si ce comptage etait
        // un jour interroge par un autre chemin que GetConsommationAsync.
        var clientId = CreateClient(isDeleted: true);
        CreateSpaceChoir(clientId, "Choir Fantome");
        await _context.SaveChangesAsync();

        var count = await _context.Choirs.CountAsync(c => c.ClientId == clientId);

        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public async Task ServiceLimitService_CountChoirs_CountsNormallyForActiveClient_NoRegression()
    {
        var clientId = CreateClient(isDeleted: false, choirLimit: 5);
        CreateSpaceChoir(clientId, "Choir Normale");
        await _context.SaveChangesAsync();

        var usage = await CreateServiceLimitService().GetUsageAsync(clientId);

        Assert.That(usage.Choirs, Is.EqualTo(1));
    }

    private Guid CreateClient(bool isDeleted, int choirLimit = 10)
    {
        var clientId = ChoraleDbContext.NewIdGuid();
        _context.Clients.Add(new Client
        {
            Id = clientId,
            Name = $"Client {clientId}",
            Status = ClientStatusEnum.Active,
            IsDeleted = isDeleted,
            ChoirLimit = choirLimit,
            MemberLimit = 100,
            StorageQuotaBytes = 1_000_000,
            MaxFileSizeBytes = 500_000
        });
        return clientId;
    }

    private Guid CreateSpaceChoir(Guid clientId, string name)
    {
        var choirId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = choirId, ClientId = clientId, Name = name, Status = ChoirStatusEnum.Published
        });
        return choirId;
    }

    private Guid CreateSpaceEvent(Guid clientId, string title)
    {
        var eventId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = eventId, SpaceType = SpaceTypeEnum.Event, ClientId = clientId });
        _context.Events.Add(new Event
        {
            Id = eventId,
            Title = title,
            StartDate = DateTime.UtcNow.AddDays(5),
            Type = EventTypeEnum.Concert,
            Location = "Salle",
            Status = EventStatusEnum.Published,
            ChoirId = null
        });
        return eventId;
    }

    private ChoirService CreateChoirService(string userId, bool isAdmin = false)
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

    private AdminChoirService CreateAdminChoirService()
    {
        var serviceProvider = CreateServiceProviderAdmin(typeof(ChoirViewModel));
        var auditLogService = new AuditLogService(serviceProvider);

        return new AdminChoirService(serviceProvider, auditLogService, new FakeServiceLimitService());
    }

    private AdminEventService CreateAdminEventService()
        => new(CreateServiceProviderAdmin(typeof(EventViewModel)));

    private ServiceLimitService CreateServiceLimitService()
        => new(CreateServiceProviderAdmin(typeof(ChoirViewModel)));

    private IServiceProvider CreateServiceProviderAdmin(Type mapperAssemblyMarker)
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
            cfg => cfg.AddMaps(mapperAssemblyMarker.Assembly),
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
