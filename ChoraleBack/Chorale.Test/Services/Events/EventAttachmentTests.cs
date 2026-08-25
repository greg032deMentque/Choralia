using System.Net;
using System.Security.Claims;
using AutoMapper;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Services.UserServices;
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
using ChoraleBackEnd.ViewModels.Events;

namespace ChoraleBackEnd.Test.Services.Events;

/// <summary>
/// Defaut 2 (migration 13) : avant ce statut, une chorale <c>Archive</c> posait
/// <c>IsDeleted</c>, ce qui bloquait indirectement tout rattachement par violation de FK. Le
/// nouveau mecanisme de statut a retire cet effet de bord sans qu'aucun garde explicite ne
/// prenne le relais — un evenement pouvait se rattacher a une chorale <c>Archive</c> ou
/// <c>Annule</c>. Ce fichier fige : seule une chorale <c>Publie</c> peut recevoir un
/// evenement, et un evenement deja rattache perd le droit de modification des que sa chorale
/// n'est plus <c>Publie</c>/<c>Draft</c> (Defaut 1, meme mecanisme qu'ailleurs).
/// </summary>
[TestFixture]
public sealed class EventAttachmentTests
{
    private const string ManagerUserId = "responsable-1";

    private ChoraleDbContext _context = null!;
    private Guid _clientId;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _clientId = ChoraleDbContext.NewIdGuid();
        _context.Users.Add(new User { Id = ManagerUserId, UserName = "responsable@t.com", Email = "responsable@t.com", EmailConfirmed = true });
        _context.Clients.Add(new Client
        {
            Id = _clientId, Name = "Client Test", Status = ClientStatusEnum.Active,
            ChoirLimit = 10, MemberLimit = 100, StorageQuotaBytes = 1_000_000, MaxFileSizeBytes = 500_000
        });
        _context.SaveChanges();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    private Guid CreateChoirWithManager(ChoirStatusEnum status)
    {
        var choirId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = _clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = choirId, ClientId = _clientId, Name = "Choir Test", Status = status
        });

        var member = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = choirId, SpaceId = choirId,
            UserId = ManagerUserId, Status = MemberStatusEnum.Active
        };
        _context.SpaceMembers.Add(member);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(), SpaceMemberId = member.Id, Role = UserRoleEnum.Manager
        });

        _context.SaveChanges();
        return choirId;
    }

    private static EventViewModel NewEvent(Guid choirId) => new()
    {
        Title = "Concert",
        StartDate = DateTime.UtcNow.AddDays(30),
        Type = EventTypeEnum.Concert,
        ChoirId = choirId
    };

    // --- Rattachement (CreateAsync) : seule une chorale Publie est acceptee --------------

    [Test]
    public async Task CreateAsync_AttachmentToPublishedChoir_Succeeds()
    {
        var choirId = CreateChoirWithManager(ChoirStatusEnum.Published);

        var result = await CreateEventService().CreateAsync(NewEvent(choirId));

        Assert.That(result.ChoirId, Is.EqualTo(choirId));
    }

    [TestCase(ChoirStatusEnum.Archived)]
    [TestCase(ChoirStatusEnum.Cancelled)]
    [TestCase(ChoirStatusEnum.Draft)]
    public async Task CreateAsync_AttachmentToNonPublishedChoir_RejectsWithoutExposingTechnicalStatus(ChoirStatusEnum status)
    {
        var choirId = CreateChoirWithManager(status);

        var ex = Assert.ThrowsAsync<CustomException>(
            () => CreateEventService().CreateAsync(NewEvent(choirId)));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(ex.Message, Does.Not.Contain(status.ToString()),
                "Le message ne doit pas exposer l'identifiant technique du statut au client.");
        });

        Assert.That(await _context.Events.CountAsync(), Is.EqualTo(0),
            "Un assignment refuse ne doit rien persister.");
    }

    // --- Modification d'un evenement deja rattache : suit le statut ACTUEL de la chorale --

    [Test]
    public async Task UpdateAsync_EventAttachedToChoirThatBecameArchived_Rejected()
    {
        var choirId = CreateChoirWithManager(ChoirStatusEnum.Published);
        var evt = await CreateEventService().CreateAsync(NewEvent(choirId));

        var choir = await _context.Choirs.FirstAsync(c => c.Id == choirId);
        choir.Status = ChoirStatusEnum.Archived;
        await _context.SaveChangesAsync();

        var ex = Assert.ThrowsAsync<CustomException>(() => CreateEventService().UpdateAsync(new EventViewModel
        {
            Id = evt.Id,
            Title = "Title Modifie Apres Archivage",
            StartDate = evt.StartDate,
            Type = EventTypeEnum.Concert,
            ChoirId = choirId
        }));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        var reloadedEvent = await _context.Events.AsNoTracking().FirstAsync(e => e.Id == evt.Id);
        Assert.That(reloadedEvent.Title, Is.EqualTo("Concert"),
            "Decision documentee : une chorale Archive ferme aussi la modification d'un evenement deja rattache, pas seulement son assignment initial.");
    }

    // --- Fabriques -------------------------------------------------------------------------

    private EventService CreateEventService()
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, ManagerUserId) };
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
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

        var sp = services.BuildServiceProvider();
        var auditLogService = new AuditLogService(sp);
        return new EventService(
            sp, new EventAuthorizationService(sp, new ChoirAuthorizationService(sp, new MembershipService(sp))), new GuestAccountLifecycleService(sp, auditLogService),
            new ClientRoleResolverService(_context), new MembershipService(sp),
            new EventParticipationSeedingService(sp));
    }
}
