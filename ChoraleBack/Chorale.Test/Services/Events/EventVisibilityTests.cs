using System;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Services.UserServices;
using ChoraleBackEnd.Test.Fakes;
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
using ChoraleBackEnd.ViewModels.Events;
using ChoraleBackEnd.ViewModels.Choirs;

namespace ChoraleBackEnd.Test.Services.Events;

/// <summary>
/// Visibilite des events selon leur statut (`04` § Event : un draftEvent est
/// invisible des membres, un archive est masque).
/// </summary>
/// <remarks>
/// Constate en exercant l'API : `GetPagedAsync` n'appliquait AUCUN filtre de statut. Les
/// draftEvents d'un responsable etaient servis a toute la chorale — et ce trou masquait un
/// second defaut, le front ne publiant pas encore, tous les events restaient Draft
/// et n'etaient visibles QUE grace a lui. Corriger le filtre sans livrer la publication
/// cote front aurait fait disparaitre tous les events : les deux sont partis ensemble,
/// et ce test fige le contrat.
/// </remarks>
[TestFixture]
public sealed class EventVisibilityTests
{
    private const string MemberUserId = "member-1";
    private const string ManagerUserId = "responsable-1";

    private ChoraleDbContext _context = null!;
    private Guid _choirId;
    private Guid _clientId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _choirId = ChoraleDbContext.NewIdGuid();
        _clientId = ChoraleDbContext.NewIdGuid();

        _context.Users.Add(new User { Id = MemberUserId, UserName = "m@t.com", Email = "m@t.com" });
        _context.Users.Add(new User { Id = ManagerUserId, UserName = "r@t.com", Email = "r@t.com" });
        _context.Clients.Add(new Client { Id = _clientId, Name = "Client", Status = ClientStatusEnum.Active });
        _context.Spaces.Add(new Space { Id = _choirId, ClientId = _clientId, SpaceType = SpaceTypeEnum.Choir });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = _choirId, ClientId = _clientId, Name = "Choir", Status = ChoirStatusEnum.Published
        });

        foreach (var (userId, role) in new[]
        {
            (MemberUserId, (UserRoleEnum?)null),
            (ManagerUserId, UserRoleEnum.Manager)
        })
        {
            var member = new SpaceMember
            {
                Id = ChoraleDbContext.NewIdGuid(),
                UserId = userId,
                ChoirId = _choirId,
                SpaceId = _choirId,
                Status = MemberStatusEnum.Active
            };
            _context.SpaceMembers.Add(member);
            if (role is { } r)
                _context.SpaceMemberRoles.Add(new SpaceMemberRole
                {
                    Id = ChoraleDbContext.NewIdGuid(), SpaceMemberId = member.Id, Role = r
                });
        }

        AddEvent("Publié", EventStatusEnum.Published);
        AddEvent("Draft secret", EventStatusEnum.Draft, ManagerUserId);
        AddEvent("Annulé", EventStatusEnum.Cancelled);
        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task GetPaged_Member_DoesNotSeeDrafts()
    {
        var result = await Sut(MemberUserId).GetPagedAsync(
            new EventPagedFilterViewModel { Page = 1, PageSize = 50 });

        Assert.Multiple(() =>
        {
            Assert.That(result.TotalCount, Is.EqualTo(2),
                "Publié et Annulé restent visibles — un annulé n'est pas supprimé (`04`).");
            Assert.That(result.Items.TrueForAll(e => e.Title != "Draft secret"), Is.True);
        });
    }

    [Test]
    public async Task GetPaged_Manager_SeesOwnDrafts()
    {
        var result = await Sut(ManagerUserId).GetPagedAsync(
            new EventPagedFilterViewModel { Page = 1, PageSize = 50 });

        Assert.That(result.TotalCount, Is.EqualTo(3));
    }

    [Test]
    public async Task GetById_Member_DraftNotFoundNotForbidden()
    {
        var draftEvent = await _context.Events.AsNoTracking()
            .FirstAsync(e => e.Status == EventStatusEnum.Draft);

        // Introuvable et non Interdit : un 403 revelerait l'existence d'un contenu non
        // publie (`02` § Regles de visibilite).
        Assert.ThrowsAsync<KeyNotFoundException>(() => Sut(MemberUserId).GetByIdAsync(draftEvent.Id));
    }

    [Test]
    public async Task GetById_Manager_ReachesOwnDraft()
    {
        var draftEvent = await _context.Events.AsNoTracking()
            .FirstAsync(e => e.Status == EventStatusEnum.Draft);

        var result = await Sut(ManagerUserId).GetByIdAsync(draftEvent.Id);

        Assert.That(result.Status, Is.EqualTo(EventStatusEnum.Draft));
    }

    private void AddEvent(string title, EventStatusEnum status, string? creator = null)
    {
        var id = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = id, ClientId = _clientId, SpaceType = SpaceTypeEnum.Event });
        _context.Events.Add(new Event
        {
            Id = id,
            ChoirId = _choirId,
            Title = title,
            Location = "Salle",
            Status = status,
            Type = EventTypeEnum.Rehearsal,
            StartDate = DateTime.UtcNow.AddDays(7),
            CreatedByUserId = creator
        });

        // Les deux comptes participent a chaque evenement : c'est bien le STATUT qui doit
        // trancher la visibilite, pas l'appartenance.
        foreach (var userId in new[] { MemberUserId, ManagerUserId })
            _context.SpaceMembers.Add(new SpaceMember
            {
                Id = ChoraleDbContext.NewIdGuid(),
                UserId = userId,
                ChoirId = _choirId,
                SpaceId = id,
                Status = MemberStatusEnum.Active
            });
    }

    private EventService Sut(string userId)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId)], "Test"))
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
        services.AddSingleton<IEmailService>(new FakeEmailService());
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>().AddEntityFrameworkStores<ChoraleDbContext>();

        var sp = services.BuildServiceProvider();
        var authorization = new EventAuthorizationService(sp, new ChoirAuthorizationService(sp, new MembershipService(sp)));
        var audit = new AuditLogService(sp);
        return new EventService(
            sp, authorization, new GuestAccountLifecycleService(sp, audit), new ClientRoleResolverService(_context),
            new MembershipService(sp), new EventParticipationSeedingService(sp));
    }
}
