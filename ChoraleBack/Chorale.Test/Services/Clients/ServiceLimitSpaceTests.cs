using System.Net;
using System.Security.Claims;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using ChoraleBackEnd.Data.Entities;
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
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.ViewModels.Choirs;

namespace ChoraleBackEnd.Test.Services.Clients;

/// <summary>
/// Depuis qu'<see cref="Space"/> porte son propre <c>ClientId</c>, le chemin de resolution
/// des plafonds est unique pour une chorale ET un evenement (`10-D23`). Avant cette
/// evolution, un evenement autonome (sans chorale porteuse) echappait entierement aux
/// plafonds de son client — le trou que ces tests ferment.
/// </summary>
[TestFixture]
public sealed class ServiceLimitSpaceTests
{
    private ChoraleDbContext _context = null!;
    private ServiceLimitService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);
        _sut = CreateService();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task UploadFile_StandaloneEvent_ResolvesClientViaSpaceClientId()
    {
        var clientId = AddClient(quotaStorage: 1000, sizeMaxFile: 400);
        var eventId = AddStandaloneSpaceEvent(clientId);

        Assert.DoesNotThrowAsync(() => _sut.EnsureCanUploadFileAsync(eventId, 300));
    }

    [Test]
    public async Task UploadFile_EventAttachedToChoir_SameClientAsChoir()
    {
        var clientId = AddClient(quotaStorage: 500, sizeMaxFile: 400);
        var choirId = AddChoir(clientId);
        // L'evenement herite du client de sa chorale porteuse : son propre Espace.ClientId
        // doit donc etre identique — c'est EventService qui garantit cette egalite a la
        // creation (voir ChargerChoraleAsync), reproduite ici directement sur l'espace.
        var eventId = AddAttachedSpaceEvent(clientId, choirId);

        Assert.DoesNotThrowAsync(() => _sut.EnsureCanUploadFileAsync(eventId, 300));
        Assert.DoesNotThrowAsync(() => _sut.EnsureCanUploadFileAsync(choirId, 100));
    }

    [Test]
    public async Task UploadFile_StandaloneEvent_OwnClientDistinctFromAnotherChoirsClient()
    {
        var eventClientId = AddClient(quotaStorage: 1000, sizeMaxFile: 400);
        var otherClientId = AddClient(quotaStorage: 50, sizeMaxFile: 10);
        AddChoir(otherClientId);
        var eventId = AddStandaloneSpaceEvent(eventClientId);

        // Le plafond restrictif de l'autre client n'a aucun effet ici : l'evenement resout
        // son PROPRE client, pas celui d'une chorale sans rapport.
        Assert.DoesNotThrowAsync(() => _sut.EnsureCanUploadFileAsync(eventId, 300));
    }

    [Test]
    public void UploadFile_SuspendedClient_WriteRejectedOnEventAndOnChoir()
    {
        var clientId = AddClient(quotaStorage: 1000, sizeMaxFile: 400, status: ClientStatusEnum.Suspended);
        var choirId = AddChoir(clientId);
        var eventId = AddStandaloneSpaceEvent(clientId);

        var exceptionEvent = Assert.ThrowsAsync<CustomException>(
            () => _sut.EnsureCanUploadFileAsync(eventId, 100));
        var exceptionChoir = Assert.ThrowsAsync<CustomException>(
            () => _sut.EnsureCanUploadFileAsync(choirId, 100));

        Assert.Multiple(() =>
        {
            Assert.That(exceptionEvent!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(exceptionChoir!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        });
    }

    [Test]
    public void UploadFile_StandaloneEvent_StorageQuotaExceeded_ThrowsConflict409()
    {
        // MaxFileSize volontairement large : c'est le quota AGREGE qui doit trancher
        // ici, pas la taille unitaire (deja couverte par le test suivant).
        var clientId = AddClient(quotaStorage: 1000, sizeMaxFile: 2000);
        var eventId = AddStandaloneSpaceEvent(clientId);

        // Sans la resolution via Espace.ClientId, cet appel passait silencieusement : le
        // depot d'un evenement autonome n'etait rattache a aucun client, donc a aucun quota.
        var exception = Assert.ThrowsAsync<CustomException>(
            () => _sut.EnsureCanUploadFileAsync(eventId, 1500));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(exception.Message, Does.Contain("Quota"));
    }

    [Test]
    public void UploadFile_StandaloneEvent_MaxFileSizeApplied()
    {
        var clientId = AddClient(quotaStorage: 1000, sizeMaxFile: 400);
        var eventId = AddStandaloneSpaceEvent(clientId);

        var exception = Assert.ThrowsAsync<CustomException>(
            () => _sut.EnsureCanUploadFileAsync(eventId, 500));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
    }

    private Guid AddClient(
        long quotaStorage, long sizeMaxFile, ClientStatusEnum status = ClientStatusEnum.Active)
    {
        var clientId = ChoraleDbContext.NewIdGuid();
        _context.Clients.Add(new Client
        {
            Id = clientId,
            Name = $"Client {clientId}",
            Status = status,
            ChoirLimit = 10,
            MemberLimit = 100,
            StorageQuotaBytes = quotaStorage,
            MaxFileSizeBytes = sizeMaxFile
        });
        _context.SaveChanges();
        return clientId;
    }

    private Guid AddChoir(Guid clientId)
    {
        var choirId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = choirId, ClientId = clientId, Name = $"Choir {choirId}", Status = ChoirStatusEnum.Published
        });
        _context.SaveChanges();
        return choirId;
    }

    private Guid AddStandaloneSpaceEvent(Guid clientId)
        => AddSpaceEvent(clientId, choirId: null);

    private Guid AddAttachedSpaceEvent(Guid clientId, Guid choirId)
        => AddSpaceEvent(clientId, choirId);

    private Guid AddSpaceEvent(Guid clientId, Guid? choirId)
    {
        var eventId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space
        {
            Id = eventId, SpaceType = SpaceTypeEnum.Event, ClientId = clientId
        });
        _context.Events.Add(new Event
        {
            Id = eventId,
            Title = choirId is null ? "Event Autonome" : "Event Rattache",
            StartDate = DateTime.UtcNow,
            Type = EventTypeEnum.Concert,
            Location = "Salle",
            ChoirId = choirId
        });
        _context.SaveChanges();
        return eventId;
    }

    private ServiceLimitService CreateService()
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "user-1")], "Test"))
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

        return new ServiceLimitService(services.BuildServiceProvider());
    }
}
