using System.Security.Claims;
using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Services.Events;

/// <summary>
/// Regle unique du service (`04` § Membre/Event) : « un membre actif d'une chorale est
/// participant des evenements publies a venir de cette chorale ». Une regression y est
/// silencieuse — il manque des participants, aucune erreur n'est levee.
/// </summary>
/// <remarks>
/// Le service <b>ne sauvegarde pas</b> : il empile ses ecritures dans le change tracker et
/// c'est son appelant qui valide (<c>EventService.ChangeStatusAsync</c> appelle
/// <c>SeedForPublishedEventAsync</c> puis <c>SaveChangesAsync</c>, pour que la publication et
/// le peuplement soient atomiques). Les tests reproduisent cet enchainement via
/// <c>SeedAndSaveAsync</c> — sans le <c>SaveChangesAsync</c>, ils testeraient un etat que
/// personne n'observe.
/// </remarks>
[TestFixture]
public sealed class EventParticipationSeedingTests
{
    private const string ActiveMemberUserId = "member-active";
    private const string SecondMemberUserId = "member-second";
    private const string OtherChoirUserId = "member-other-choir";

    private ChoraleDbContext _context = null!;
    private Guid _clientId;
    private Guid _choirId;
    private Guid _otherChoirId;
    private Guid _eventId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _clientId = ChoraleDbContext.NewIdGuid();
        _choirId = ChoraleDbContext.NewIdGuid();
        _otherChoirId = ChoraleDbContext.NewIdGuid();

        _context.Clients.Add(new Client { Id = _clientId, Name = "Client Test", Status = ClientStatusEnum.Active });

        foreach (var (choirId, name) in new[] { (_choirId, "Choir A"), (_otherChoirId, "Choir B") })
        {
            _context.Spaces.Add(new Space { Id = choirId, ClientId = _clientId, SpaceType = SpaceTypeEnum.Choir });
            _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
            {
                Id = choirId, ClientId = _clientId, Name = name, Status = ChoirStatusEnum.Published
            });
        }

        foreach (var userId in new[] { ActiveMemberUserId, SecondMemberUserId, OtherChoirUserId })
            _context.Users.Add(new User { Id = userId, UserName = $"{userId}@test.com", Email = $"{userId}@test.com" });

        _eventId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = _eventId, ClientId = _clientId, SpaceType = SpaceTypeEnum.Event });
        _context.Events.Add(new Event
        {
            Id = _eventId,
            Title = "Concert",
            StartDate = DateTime.UtcNow.AddDays(7),
            Type = EventTypeEnum.Concert,
            Location = "Salle",
            Status = EventStatusEnum.Published,
            ChoirId = _choirId
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
    public async Task SeedForPublishedEventAsync_ActiveChoirMember_BecomesParticipantWithoutAnswer()
    {
        AddChoirMembership(ActiveMemberUserId, MemberStatusEnum.Active);
        await _context.SaveChangesAsync();

        await SeedAndSaveAsync();

        var participation = await _context.SpaceMembers.AsNoTracking()
            .SingleAsync(m => m.SpaceId == _eventId && m.UserId == ActiveMemberUserId);
        var role = await _context.SpaceMemberRoles.AsNoTracking()
            .SingleAsync(r => r.SpaceMemberId == participation.Id);

        Assert.Multiple(() =>
        {
            Assert.That(participation.ChoirId, Is.EqualTo(_choirId),
                "La participation garde la trace de la chorale porteuse, pas seulement de l'espace evenement.");
            Assert.That(participation.Status, Is.EqualTo(MemberStatusEnum.Active));
            Assert.That(participation.Presence, Is.EqualTo(AttendanceEnum.NoReply),
                "Le peuplement inscrit, il ne repond pas a la place du membre.");
            Assert.That(role.Role, Is.EqualTo(UserRoleEnum.Participant));
        });
    }

    [TestCase(MemberStatusEnum.Invited)]
    [TestCase(MemberStatusEnum.Inactive)]
    [TestCase(MemberStatusEnum.Archived)]
    public async Task SeedForPublishedEventAsync_MembershipNotActive_IsNotSeeded(MemberStatusEnum status)
    {
        AddChoirMembership(ActiveMemberUserId, status);
        await _context.SaveChangesAsync();

        await SeedAndSaveAsync();

        Assert.That(await IsParticipantAsync(ActiveMemberUserId), Is.False,
            "Une invitation non acceptee, une mise en sommeil ou un archivage ne valent pas participation automatique.");
    }

    [Test]
    public async Task SeedForPublishedEventAsync_MemberOfAnotherChoir_IsNotSeeded()
    {
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceId = _otherChoirId,
            ChoirId = _otherChoirId,
            UserId = OtherChoirUserId,
            Status = MemberStatusEnum.Active
        });
        await _context.SaveChangesAsync();

        await SeedAndSaveAsync();

        Assert.That(await IsParticipantAsync(OtherChoirUserId), Is.False);
    }

    /// <summary>
    /// Le predicat de lecture exige <c>ChoirId == SpaceId == choirId</c> : une ligne dont
    /// l'espace est un evenement n'est pas une appartenance a la chorale, meme si elle en
    /// porte le <c>ChoirId</c>. Sans cette double condition, un ancien participant serait
    /// re-seme comme s'il etait membre.
    /// </summary>
    [Test]
    public async Task SeedForPublishedEventAsync_ParticipationOfAnotherEvent_IsNotAChoirMembership()
    {
        var otherEventId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = otherEventId, ClientId = _clientId, SpaceType = SpaceTypeEnum.Event });
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceId = otherEventId,
            ChoirId = _choirId,
            UserId = OtherChoirUserId,
            Status = MemberStatusEnum.Active
        });
        await _context.SaveChangesAsync();

        await SeedAndSaveAsync();

        Assert.That(await IsParticipantAsync(OtherChoirUserId), Is.False);
    }

    /// <summary>
    /// Retirer quelqu'un d'un evenement doit tenir. Sans le <c>IgnoreQueryFilters()</c> du
    /// service, la ligne soft-deleted est invisible, le peuplement la croit absente et en cree
    /// une seconde : le retrait est annule au prochain passage, sans aucune trace.
    /// </summary>
    [Test]
    public async Task SeedForPublishedEventAsync_ParticipantRemovedManually_IsNeverResurrected()
    {
        AddChoirMembership(ActiveMemberUserId, MemberStatusEnum.Active);
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceId = _eventId,
            ChoirId = _choirId,
            UserId = ActiveMemberUserId,
            Status = MemberStatusEnum.Active,
            IsDeleted = true
        });
        await _context.SaveChangesAsync();

        await SeedAndSaveAsync();

        var rows = await _context.SpaceMembers.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.SpaceId == _eventId && m.UserId == ActiveMemberUserId)
            .ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].IsDeleted, Is.True);
        });
    }

    [Test]
    public async Task SeedForPublishedEventAsync_CalledTwice_CreatesNoDuplicate()
    {
        AddChoirMembership(ActiveMemberUserId, MemberStatusEnum.Active);
        await _context.SaveChangesAsync();

        await SeedAndSaveAsync();
        await SeedAndSaveAsync();

        Assert.That(
            await _context.SpaceMembers.AsNoTracking()
                .CountAsync(m => m.SpaceId == _eventId && m.UserId == ActiveMemberUserId),
            Is.EqualTo(1));
    }

    [Test]
    public async Task SeedForPublishedEventAsync_SeveralActiveMembers_AllBecomeParticipants()
    {
        AddChoirMembership(ActiveMemberUserId, MemberStatusEnum.Active);
        AddChoirMembership(SecondMemberUserId, MemberStatusEnum.Active);
        await _context.SaveChangesAsync();

        await SeedAndSaveAsync();

        Assert.That(
            await _context.SpaceMembers.AsNoTracking().CountAsync(m => m.SpaceId == _eventId),
            Is.EqualTo(2));
    }

    [Test]
    public async Task SeedForPublishedEventAsync_ChoirWithoutActiveMember_WritesNothing()
    {
        await SeedAndSaveAsync();

        Assert.That(await _context.SpaceMembers.IgnoreQueryFilters().AnyAsync(), Is.False);
    }

    // ---------- Montage ----------

    /// <summary>
    /// Reproduit l'enchainement de <c>EventService.ChangeStatusAsync</c> : le service empile
    /// ses ecritures, l'appelant valide.
    /// </summary>
    private async Task SeedAndSaveAsync()
    {
        await Sut().SeedForPublishedEventAsync(_eventId, _choirId);
        await _context.SaveChangesAsync();
    }

    private void AddChoirMembership(string userId, MemberStatusEnum status)
        => _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceId = _choirId,
            ChoirId = _choirId,
            UserId = userId,
            Status = status
        });

    private Task<bool> IsParticipantAsync(string userId)
        => _context.SpaceMembers.AsNoTracking().AnyAsync(m => m.SpaceId == _eventId && m.UserId == userId);

    private EventParticipationSeedingService Sut()
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "seeder")], "Test"))
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

        return new EventParticipationSeedingService(services.BuildServiceProvider());
    }
}
