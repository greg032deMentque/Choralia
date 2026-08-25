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

[TestFixture]
public sealed class EventServiceTests
{
    private const string UserId = "membre-1";

    private ChoraleDbContext _context = null!;
    private EventService _sut = null!;
    private Guid _clientId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        var mapper = new MapperConfiguration(cfg => cfg.AddMaps(typeof(EventViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, UserId)], "Test"))
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton(mapper);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<ILogService>(new LogService(httpContextAccessor));
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>()
            .AddEntityFrameworkStores<ChoraleDbContext>();

        var serviceProvider = services.BuildServiceProvider();
        var authorizationService = new EventAuthorizationService(serviceProvider, new ChoirAuthorizationService(serviceProvider, new MembershipService(serviceProvider)));
        var auditLogService = new AuditLogService(serviceProvider);
        var guestAccountLifecycleService = new GuestAccountLifecycleService(serviceProvider, auditLogService);
        var clientRoleResolverService = new ClientRoleResolverService(_context);
        _sut = new EventService(
            serviceProvider, authorizationService, guestAccountLifecycleService, clientRoleResolverService,
            new MembershipService(serviceProvider), new EventParticipationSeedingService(serviceProvider));

        // EmailConfirmed obligatoire : creer un espace est bloque pour un compte non verifie
        // (lot 6). Sans ce drapeau la fixture ne teste pas le scenario nominal.
        _context.Users.Add(new User
        {
            Id = UserId, UserName = "membre@test.com", Email = "membre@test.com", EmailConfirmed = true
        });

        // Decision produit (10-D23) : la personne qui cree un evenement autonome est
        // elle-meme un client. UserId est ResponsableClient d'un unique client, ce qui rend
        // resoluble le ClientId d'un evenement autonome cree sans ChoirId ni ClientId
        // explicite (voir CreateAsync_SansChorale_CreeOrganisateur).
        _clientId = ChoraleDbContext.NewIdGuid();
        _context.Clients.Add(new Client { Id = _clientId, Name = "Client Test", Status = ClientStatusEnum.Active });
        _context.ClientMembers.Add(new ClientMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ClientId = _clientId, UserId = UserId, Role = UserRoleEnum.ClientManager
        });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Test]
    public async Task UpdateAsync_WithoutRoleOnChoirlessEvent_ThrowsForbidden()
    {
        var evt = await CreateChoirlessEventAsync();
        var model = new EventViewModel
        {
            Id = evt.Id,
            Title = "Nouveau Title",
            StartDate = DateTime.UtcNow,
            Type = EventTypeEnum.Concert,
            ChoirId = null
        };

        var exception = Assert.ThrowsAsync<CustomException>(() => _sut.UpdateAsync(model));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task DeleteAsync_WithoutRoleOnChoirlessEvent_ThrowsForbidden()
    {
        var evt = await CreateChoirlessEventAsync();

        var exception = Assert.ThrowsAsync<CustomException>(() => _sut.DeleteAsync(evt.Id));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task UpdateAsync_OrganizerEventSansChoir_Allowed()
    {
        var evt = await CreateChoirlessEventAsync();
        await AddOrganizerAsync(evt.Id);

        var model = new EventViewModel
        {
            Id = evt.Id,
            Title = "Nouveau Title",
            StartDate = DateTime.UtcNow,
            Type = EventTypeEnum.Concert,
            ChoirId = null
        };

        var result = await _sut.UpdateAsync(model);

        Assert.That(result.Title, Is.EqualTo("Nouveau Title"));
    }

    [Test]
    public async Task DeleteAsync_OrganizerEventSansChoir_Allowed()
    {
        var evt = await CreateChoirlessEventAsync();
        await AddOrganizerAsync(evt.Id);

        Assert.DoesNotThrowAsync(() => _sut.DeleteAsync(evt.Id));
    }

    [Test]
    public async Task GetPagedAsync_UserWithoutMembership_ReturnsEmptyList()
    {
        await CreateChoirlessEventAsync();

        var result = await _sut.GetPagedAsync(new EventPagedFilterViewModel());

        Assert.That(result.Items, Is.Empty);
        Assert.That(result.TotalCount, Is.EqualTo(0));
    }

    [Test]
    public async Task GetPagedAsync_UserMember_ReturnsOwnEvent()
    {
        var evt = await CreateChoirlessEventAsync();
        await AddOrganizerAsync(evt.Id);

        var result = await _sut.GetPagedAsync(new EventPagedFilterViewModel());

        Assert.That(result.TotalCount, Is.EqualTo(1));
        Assert.That(result.Items[0].Id, Is.EqualTo(evt.Id));
    }

    [Test]
    public async Task GetPagedAsync_ChoirIdFilterWithoutChoirMembership_ThrowsForbidden()
    {
        var choirId = ChoraleDbContext.NewIdGuid();
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir { Id = choirId, Name = "Choir Test", Status = ChoirStatusEnum.Published });
        await _context.SaveChangesAsync();

        var filter = new EventPagedFilterViewModel { ChoirId = choirId };

        var exception = Assert.ThrowsAsync<CustomException>(() => _sut.GetPagedAsync(filter));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task GetByIdAsync_NonMemberOfTheEvent_ThrowsForbidden()
    {
        var evt = await CreateChoirlessEventAsync();

        var exception = Assert.ThrowsAsync<CustomException>(() => _sut.GetByIdAsync(evt.Id));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task GetByIdAsync_OrganizerEvent_Allowed()
    {
        var evt = await CreateChoirlessEventAsync();
        await AddOrganizerAsync(evt.Id);

        var result = await _sut.GetByIdAsync(evt.Id);

        Assert.That(result.Id, Is.EqualTo(evt.Id));
    }

    [Test]
    public async Task UpdateAsync_WithoutChoirIdInBody_KeepsExistingChoir()
    {
        var choirId = ChoraleDbContext.NewIdGuid();
        // ChoirConfiguration.HasQueryFilter exige un Espace non supprime rattache (jointure
        // implicite sur c.Espace.IsDeleted) : sans cette ligne, la chorale reste invisible de
        // toute requete EF, y compris pour MembershipService.
        _context.Spaces.Add(new Space { Id = choirId, ClientId = _clientId, SpaceType = SpaceTypeEnum.Choir });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir { Id = choirId, ClientId = _clientId, Name = "Choir Test", Status = ChoirStatusEnum.Published });
        var evt = await CreateEventWithChoirAsync(choirId);
        await AddOrganizerAsync(evt.Id);

        // En production, CreateAsync affecte ChoirId sur le SpaceMember de l'organisateur
        // quand l'evenement est rattache a une chorale : l'ecriture (desormais cablee via
        // EnsureCanWriteAsync) exige cette appartenance. AjouterOrganisateurAsync est
        // partagee avec les scenarios d'evenement autonome (ChoirId null) : on complete
        // ici l'appartenance propre a ce test plutot que de update ce helper commun.
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = UserId,
            ChoirId = choirId,
            SpaceId = choirId,
            Status = MemberStatusEnum.Active
        });
        await _context.SaveChangesAsync();

        var model = new EventViewModel
        {
            Id = evt.Id,
            Title = "Title Mis A Jour",
            StartDate = evt.StartDate,
            Type = EventTypeEnum.Concert,
            ChoirId = null
        };

        await _sut.UpdateAsync(model);

        var updatedEvent = await _context.Events.AsNoTracking().FirstAsync(e => e.Id == evt.Id);
        Assert.That(updatedEvent.ChoirId, Is.EqualTo(choirId));
        Assert.That(updatedEvent.Title, Is.EqualTo("Title Mis A Jour"));
    }

    [Test]
    public async Task CreateAsync_EmailNotConfirmed_ThrowsForbidden()
    {
        // Modele strictement identique a CreateAsync_SansChoir_... qui, lui, aboutit : seul
        // EmailConfirmed change, donc le refus ne peut venir que de la regle du lot 6.
        var creator = await _context.Users.FirstAsync(u => u.Id == UserId);
        creator.EmailConfirmed = false;
        await _context.SaveChangesAsync();

        var model = new EventViewModel
        {
            Title = "Repetition Ponctuelle",
            StartDate = DateTime.UtcNow,
            Type = EventTypeEnum.Rehearsal,
            ChoirId = null
        };

        var exception = Assert.ThrowsAsync<CustomException>(() => _sut.CreateAsync(model));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(exception.Message, Does.Contain("adresse email"));
        Assert.That(await _context.Spaces.AnyAsync(), Is.False);
    }

    [Test]
    public async Task CreateAsync_WithoutChoir_CreatesOrganizerAndResolvesCreatorsClient()
    {
        var model = new EventViewModel
        {
            Title = "Repetition Ponctuelle",
            StartDate = DateTime.UtcNow,
            Type = EventTypeEnum.Rehearsal,
            ChoirId = null
        };

        var result = await _sut.CreateAsync(model);

        // Ni ChoirId ni ClientId fournis : le client se resout depuis l'unique
        // rattachement ResponsableClient du createur (10-D23). Guid.Empty ne doit jamais
        // atteindre Espace.ClientId — c'etait exactement le trou que ce lot devait fermer.
        var space = await _context.Spaces.AsNoTracking().FirstAsync(e => e.Id == result.Id);
        Assert.That(space.ClientId, Is.EqualTo(_clientId));
        Assert.That(space.ClientId, Is.Not.EqualTo(Guid.Empty));

        var member = await _context.SpaceMembers
            .FirstOrDefaultAsync(m => m.SpaceId == result.Id && m.UserId == UserId);

        Assert.That(member, Is.Not.Null);
        Assert.That(member!.ChoirId, Is.Null);
        Assert.That(member.Status, Is.EqualTo(MemberStatusEnum.Active));

        var isOrganizer = await _context.SpaceMemberRoles
            .AnyAsync(r => r.SpaceMemberId == member.Id && r.Role == UserRoleEnum.Organizer);
        Assert.That(isOrganizer, Is.True);
    }

    [Test]
    public async Task CloseAsync_FinishedEvent_SetsClosedAt()
    {
        var evt = await CreateEventFinishedAsync();
        await AddOrganizerAsync(evt.Id);

        var result = await _sut.CloseAsync(evt.Id);

        Assert.That(result.ClosedAt, Is.Not.Null);
    }

    [Test]
    public async Task CloseAsync_NonFinishedEvent_ThrowsBadRequest()
    {
        var evt = await CreateFutureEventAsync();
        await AddOrganizerAsync(evt.Id);

        var exception = Assert.ThrowsAsync<CustomException>(() => _sut.CloseAsync(evt.Id));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CloseAsync_AlreadyClosed_ThrowsConflict()
    {
        var evt = await CreateEventFinishedAsync();
        await AddOrganizerAsync(evt.Id);
        await _sut.CloseAsync(evt.Id);

        var exception = Assert.ThrowsAsync<CustomException>(() => _sut.CloseAsync(evt.Id));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    private async Task<Event> CreateFutureEventAsync()
    {
        var evt = new Event
        {
            Id = ChoraleDbContext.NewIdGuid(),
            Title = "Event Futur",
            StartDate = DateTime.UtcNow.AddDays(1),
            Type = EventTypeEnum.Concert,
            ChoirId = null
        };
        _context.Spaces.Add(new Space { Id = evt.Id, ClientId = _clientId, SpaceType = SpaceTypeEnum.Event });
        _context.Events.Add(evt);
        await _context.SaveChangesAsync();
        return evt;
    }

    private async Task<Event> CreateEventFinishedAsync()
    {
        var evt = new Event
        {
            Id = ChoraleDbContext.NewIdGuid(),
            Title = "Event Finished",
            StartDate = DateTime.UtcNow.AddDays(-2),
            EndDate = DateTime.UtcNow.AddDays(-1),
            Type = EventTypeEnum.Concert,
            ChoirId = null
        };
        _context.Spaces.Add(new Space { Id = evt.Id, ClientId = _clientId, SpaceType = SpaceTypeEnum.Event });
        _context.Events.Add(evt);
        await _context.SaveChangesAsync();
        return evt;
    }

    private async Task<Event> CreateChoirlessEventAsync()
    {
        var evt = new Event
        {
            Id = ChoraleDbContext.NewIdGuid(),
            Title = "Event Sans Choir",
            StartDate = DateTime.UtcNow,
            Type = EventTypeEnum.Concert,
            ChoirId = null
        };
        _context.Spaces.Add(new Space { Id = evt.Id, ClientId = _clientId, SpaceType = SpaceTypeEnum.Event });
        _context.Events.Add(evt);
        await _context.SaveChangesAsync();
        return evt;
    }

    private async Task<Event> CreateEventWithChoirAsync(Guid choirId)
    {
        var evt = new Event
        {
            Id = ChoraleDbContext.NewIdGuid(),
            Title = "Event Avec Choir",
            StartDate = DateTime.UtcNow,
            Type = EventTypeEnum.Concert,
            ChoirId = choirId
        };
        _context.Spaces.Add(new Space { Id = evt.Id, ClientId = _clientId, SpaceType = SpaceTypeEnum.Event });
        _context.Events.Add(evt);
        await _context.SaveChangesAsync();
        return evt;
    }

    private async Task AddOrganizerAsync(Guid eventId)
    {
        var member = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = UserId,
            SpaceId = eventId,
            ChoirId = null,
            Status = MemberStatusEnum.Active,
            Presence = AttendanceEnum.NoReply
        };
        _context.SpaceMembers.Add(member);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceMemberId = member.Id,
            Role = UserRoleEnum.Organizer
        });
        await _context.SaveChangesAsync();
    }
}
