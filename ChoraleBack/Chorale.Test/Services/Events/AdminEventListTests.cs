using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.ViewModels.AdminEvents;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
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

namespace ChoraleBackEnd.Test.Services.Events;

/// <summary>
/// Liste transverse des événements pour l'administration générale (`10-D23`, lot 3) : un
/// événement autonome (sans chorale porteuse) doit être listable sans exception, son état
/// effectif se calcule toujours depuis les dates (jamais un champ stocké), et un événement
/// rattaché au client technique créé par la migration <c>AjouteClientSurSpace</c> doit être
/// signalé comme anomalie à traiter.
/// </summary>
[TestFixture]
public sealed class AdminEventListTests
{
    private const string AdminUserId = "admin-1";

    private ChoraleDbContext _context = null!;
    private Guid _clientId;
    private Guid _standaloneEventId;
    private Guid _anomalyEventId;
    private Guid _deletedEventId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _clientId = ChoraleDbContext.NewIdGuid();
        var clientTechniqueId = Guid.Parse(Client.ClientTechnique.WithoutStructureId);

        _context.Users.Add(new User { Id = AdminUserId, UserName = "admin@test.com", Email = "admin@test.com" });
        _context.Clients.Add(new Client { Id = _clientId, Name = "Client Test", Status = ClientStatusEnum.Active });
        _context.Clients.Add(new Client
        {
            Id = clientTechniqueId, Name = "Événements sans structure — à rattacher", Status = ClientStatusEnum.Suspended
        });

        // Event autonome normal, publie, date passee : doit se lister sans exception,
        // sans chorale porteuse, et son etat effectif doit se lire "Finished" (calcule).
        _standaloneEventId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = _standaloneEventId, SpaceType = SpaceTypeEnum.Event, ClientId = _clientId });
        _context.Events.Add(new Event
        {
            Id = _standaloneEventId,
            Title = "Concert autonome",
            ChoirId = null,
            StartDate = DateTime.UtcNow.AddDays(-30),
            Type = EventTypeEnum.Concert,
            Location = "Salle",
            Status = EventStatusEnum.Published
        });

        // Event rattache au client technique : anomalie a remonter.
        _anomalyEventId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = _anomalyEventId, SpaceType = SpaceTypeEnum.Event, ClientId = clientTechniqueId });
        _context.Events.Add(new Event
        {
            Id = _anomalyEventId,
            Title = "Event orphelin",
            ChoirId = null,
            StartDate = DateTime.UtcNow.AddDays(5),
            Type = EventTypeEnum.Other,
            Location = "Inconnu",
            Status = EventStatusEnum.Published
        });

        // Event soft-delete : ne doit jamais resurgir, meme cote administration.
        _deletedEventId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space
        {
            Id = _deletedEventId, SpaceType = SpaceTypeEnum.Event, ClientId = _clientId, IsDeleted = true
        });
        _context.Events.Add(new Event
        {
            Id = _deletedEventId,
            Title = "Event supprime",
            ChoirId = null,
            StartDate = DateTime.UtcNow.AddDays(1),
            Type = EventTypeEnum.Concert,
            Location = "Salle",
            Status = EventStatusEnum.Published,
            IsDeleted = true
        });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task GetPagedAsync_StandaloneEvent_ListsWithoutExceptionAndWithoutOwningChoir()
    {
        var page = await Sut().GetPagedAsync(new AdminEventsPagedFilterViewModel { PageSize = 50 });

        var item = page.Items.Single(e => e.Id == _standaloneEventId);
        Assert.Multiple(() =>
        {
            Assert.That(item.ChoirId, Is.Null);
            Assert.That(item.ChoirName, Is.Null);
        });
    }

    [Test]
    public async Task GetPagedAsync_PublishedEventPastDate_EffectiveStateComputedAsFinished()
    {
        var page = await Sut().GetPagedAsync(new AdminEventsPagedFilterViewModel { PageSize = 50 });

        var item = page.Items.Single(e => e.Id == _standaloneEventId);
        Assert.Multiple(() =>
        {
            Assert.That(item.Status, Is.EqualTo(EventStatusEnum.Published), "Le statut stocke ne change jamais tout seul.");
            Assert.That(item.EffectiveState, Is.EqualTo(EventEffectiveStateEnum.Finished));
        });
    }

    [Test]
    public async Task GetPagedAsync_EventAttachedToTechnicalClient_FlaggedAsAnomaly()
    {
        var page = await Sut().GetPagedAsync(new AdminEventsPagedFilterViewModel { PageSize = 50 });

        var anomalyItem = page.Items.Single(e => e.Id == _anomalyEventId);
        var normalItem = page.Items.Single(e => e.Id == _standaloneEventId);

        Assert.Multiple(() =>
        {
            Assert.That(anomalyItem.IsTechnicalClientAnomaly, Is.True);
            Assert.That(normalItem.IsTechnicalClientAnomaly, Is.False);
        });
    }

    [Test]
    public async Task GetPagedAsync_EventSoftDeleted_AbsentFromList()
    {
        var page = await Sut().GetPagedAsync(new AdminEventsPagedFilterViewModel { PageSize = 50 });

        Assert.That(page.Items.Any(e => e.Id == _deletedEventId), Is.False);
    }

    private AdminEventService Sut()
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

        return new AdminEventService(services.BuildServiceProvider());
    }
}
