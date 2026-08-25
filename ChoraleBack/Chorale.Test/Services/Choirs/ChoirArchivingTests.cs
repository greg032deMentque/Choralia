using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
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

namespace ChoraleBackEnd.Test.Services.Choirs;

/// <summary>
/// Archivage/réactivation d'une chorale par l'administration générale (`10-D23`, lot 3,
/// puis migration 13).
/// </summary>
/// <remarks>
/// Comportement corrigé par la migration 13 : le lot 3 avait réutilisé <c>IsDeleted</c>
/// comme mécanisme d'archivage, faute de champ <c>Status</c> sur <c>Choir</c> — ce qui
/// confondait « archivée » (réversible, contenu conservé) et « supprimée » (irréversible).
/// Ces tests vérifiaient donc jusqu'ici que l'archivage posait <c>IsDeleted = true</c> ; ils
/// vérifient désormais l'inverse, qui est le cœur de cette migration : archive une chorale
/// ne doit plus jamais toucher <c>IsDeleted</c>, seul <c>Status</c> change.
/// </remarks>
[TestFixture]
public sealed class ChoirArchivingTests
{
    private const string AdminUserId = "admin-1";
    private const string MemberUserId = "member-1";
    private const string FormerMemberUserId = "previous-member";

    private ChoraleDbContext _context = null!;
    private Guid _clientId;
    private Guid _choirId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _clientId = ChoraleDbContext.NewIdGuid();
        _choirId = ChoraleDbContext.NewIdGuid();

        _context.Users.Add(new User { Id = AdminUserId, UserName = "admin@test.com", Email = "admin@test.com" });
        _context.Users.Add(new User { Id = MemberUserId, UserName = "m@test.com", Email = "m@test.com" });
        _context.Users.Add(new User { Id = FormerMemberUserId, UserName = "am@test.com", Email = "am@test.com" });

        _context.Clients.Add(new Client
        {
            Id = _clientId,
            Name = "Client Test",
            Status = ClientStatusEnum.Active,
            ChoirLimit = 5,
            MemberLimit = 250,
            StorageQuotaBytes = 1_000_000,
            MaxFileSizeBytes = 100_000
        });

        _context.Spaces.Add(new Space { Id = _choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = _clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = _choirId, ClientId = _clientId, Name = "Choir Test", Status = ChoirStatusEnum.Published
        });

        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = MemberUserId,
            ChoirId = _choirId,
            SpaceId = _choirId,
            Status = MemberStatusEnum.Active
        });

        // Membre deja archive : ne doit pas entrer dans les compteurs d'impact.
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = FormerMemberUserId,
            ChoirId = _choirId,
            SpaceId = _choirId,
            Status = MemberStatusEnum.Archived,
            IsDeleted = true
        });

        _context.Songs.Add(new Song
        {
            Id = ChoraleDbContext.NewIdGuid(), Title = "Chant active", ChoirId = _choirId, Status = SongStatusEnum.Active
        });

        // Chant deja supprime logiquement : ne doit pas entrer dans les compteurs d'impact.
        _context.Songs.Add(new Song
        {
            Id = ChoraleDbContext.NewIdGuid(), Title = "Chant supprime", ChoirId = _choirId,
            Status = SongStatusEnum.Active, IsDeleted = true
        });

        var eventId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = eventId, SpaceType = SpaceTypeEnum.Event, ClientId = _clientId });
        _context.Events.Add(new Event
        {
            Id = eventId,
            Title = "Concert",
            ChoirId = _choirId,
            StartDate = DateTime.UtcNow.AddDays(10),
            Type = EventTypeEnum.Concert,
            Location = "Église"
        });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task ChangeStatusAsync_ToArchived_DoesNotTouchIsDeleted()
    {
        // Coeur de la migration 13 : archive n'est plus delete. Avant cette correction,
        // ce test vérifiait l'inverse (IsDeleted = true sur la chorale ET l'espace).
        await Sut().ChangeStatusAsync(_choirId, ChoirStatusEnum.Archived);

        var choir = await _context.Choirs.IgnoreQueryFilters().FirstAsync(c => c.Id == _choirId);
        var space = await _context.Spaces.FirstAsync(e => e.Id == _choirId);

        Assert.Multiple(() =>
        {
            Assert.That(choir.Status, Is.EqualTo(ChoirStatusEnum.Archived));
            Assert.That(choir.IsDeleted, Is.False);
            Assert.That(space.IsDeleted, Is.False);
        });
    }

    [Test]
    public async Task ChangeStatusAsync_ToArchived_FreesUpClientChoirLimitSlot()
    {
        var serviceLimitService = new ServiceLimitService(BuildServiceProvider(AdminUserId, isAdmin: true));
        var before = await serviceLimitService.GetUsageAsync(_clientId);
        Assert.That(before.Choirs, Is.EqualTo(1));

        await Sut().ChangeStatusAsync(_choirId, ChoirStatusEnum.Archived);

        var after = await serviceLimitService.GetUsageAsync(_clientId);
        Assert.That(after.Choirs, Is.EqualTo(0));
    }

    [Test]
    public async Task ChangeStatusAsync_ToArchived_ContentKeptButInvisibleToMembers()
    {
        var membershipBefore = new MembershipService(BuildServiceProvider(MemberUserId));
        Assert.That(await membershipBefore.IsMemberActiveAsync(_choirId), Is.True);

        await Sut().ChangeStatusAsync(_choirId, ChoirStatusEnum.Archived);

        var membershipAfter = new MembershipService(BuildServiceProvider(MemberUserId));
        Assert.That(await membershipAfter.IsMemberActiveAsync(_choirId), Is.False);

        var songExists = await _context.Songs
            .IgnoreQueryFilters()
            .AnyAsync(c => c.ChoirId == _choirId && c.Title == "Chant active" && !c.IsDeleted);
        Assert.That(songExists, Is.True, "Le contenu doit rester en base, seulement devenir inaccessible.");
    }

    [Test]
    public async Task ChangeStatusAsync_ToArchived_MembersKeepInactiveMembership()
    {
        await Sut().ChangeStatusAsync(_choirId, ChoirStatusEnum.Archived);

        var member = await _context.SpaceMembers
            .FirstAsync(m => m.UserId == MemberUserId && m.ChoirId == _choirId);
        Assert.Multiple(() =>
        {
            Assert.That(member.IsDeleted, Is.False, "Le assignment du membre n'est pas touché par l'archivage de la chorale.");
            Assert.That(member.Status, Is.EqualTo(MemberStatusEnum.Active));
        });

        var membership = new MembershipService(BuildServiceProvider(MemberUserId));
        Assert.That(await membership.IsMemberActiveAsync(_choirId), Is.False,
            "Le assignment exists toujours mais n'ouvre plus l'accès tant que la chorale est archivée.");
    }

    [Test]
    public async Task ChangeStatusAsync_ArchivedToPublished_ContentRestoredIdentically()
    {
        await Sut().ChangeStatusAsync(_choirId, ChoirStatusEnum.Archived);
        await Sut().ChangeStatusAsync(_choirId, ChoirStatusEnum.Published);

        var choir = await _context.Choirs.FirstAsync(c => c.Id == _choirId);
        Assert.That(choir.Status, Is.EqualTo(ChoirStatusEnum.Published));

        var membership = new MembershipService(BuildServiceProvider(MemberUserId));
        Assert.That(await membership.IsMemberActiveAsync(_choirId), Is.True);

        var songCount = await _context.Songs.CountAsync(c => c.ChoirId == _choirId);
        Assert.That(songCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ChangeStatusAsync_ToArchived_LastChoirOfClient_ClientKeptNotArchived()
    {
        await Sut().ChangeStatusAsync(_choirId, ChoirStatusEnum.Archived);

        var client = await _context.Clients.AsNoTracking().FirstAsync(c => c.Id == _clientId);
        Assert.Multiple(() =>
        {
            Assert.That(client.IsDeleted, Is.False);
            Assert.That(client.Status, Is.EqualTo(ClientStatusEnum.Active));
        });
    }

    [Test]
    public async Task GetArchiveImpactAsync_ExactCounters_ExcludingAlreadySoftDeletedEntities()
    {
        var impact = await Sut().GetArchiveImpactAsync(_choirId);

        Assert.Multiple(() =>
        {
            Assert.That(impact.MemberCount, Is.EqualTo(1));
            Assert.That(impact.SongCount, Is.EqualTo(1));
            Assert.That(impact.EventCount, Is.EqualTo(1));
        });
    }

    private AdminChoirService Sut()
    {
        var sp = BuildServiceProvider(AdminUserId, isAdmin: true);
        return new AdminChoirService(sp, new AuditLogService(sp), new ServiceLimitService(sp));
    }

    private IServiceProvider BuildServiceProvider(string userId, bool isAdmin = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        if (isAdmin) claims.Add(new Claim(ClaimTypes.Role, nameof(UserRoleEnum.Admin)));

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
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
