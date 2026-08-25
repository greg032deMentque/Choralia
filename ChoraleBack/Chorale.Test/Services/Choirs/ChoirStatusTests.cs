using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Helpers;
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
/// Statut métier explicite d'une chorale (migration 13) : <c>ChoirStateHelper</c>,
/// <c>AdminChoirService.ChangeStatusAsync</c> et les règles de visibilité portées par
/// <c>MembershipService</c>.
/// </summary>
[TestFixture]
public sealed class ChoirStatusTests
{
    private const string AdminUserId = "admin-1";
    private const string CreatorUserId = "createur-1";
    private const string ManagerUserId = "responsable-1";
    private const string MemberSimpleUserId = "member-simple-1";

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

        _context.Users.Add(new User { Id = AdminUserId, UserName = "admin@t.com", Email = "admin@t.com" });
        _context.Users.Add(new User { Id = CreatorUserId, UserName = "createur@t.com", Email = "createur@t.com" });
        _context.Users.Add(new User { Id = ManagerUserId, UserName = "resp@t.com", Email = "resp@t.com" });
        _context.Users.Add(new User { Id = MemberSimpleUserId, UserName = "member@t.com", Email = "member@t.com" });

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
            Id = _choirId,
            ClientId = _clientId,
            Name = "Choir Test",
            Status = ChoirStatusEnum.Published,
            CreatedByUserId = CreatorUserId
        });

        var memberManager = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = ManagerUserId,
            ChoirId = _choirId,
            SpaceId = _choirId,
            Status = MemberStatusEnum.Active
        };
        _context.SpaceMembers.Add(memberManager);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceMemberId = memberManager.Id,
            Role = UserRoleEnum.Manager
        });

        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = MemberSimpleUserId,
            ChoirId = _choirId,
            SpaceId = _choirId,
            Status = MemberStatusEnum.Active
        });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    // --- ChoirStateHelper : table de transitions --------------------------------------

    [TestCase(ChoirStatusEnum.Draft, ChoirStatusEnum.Published)]
    [TestCase(ChoirStatusEnum.Draft, ChoirStatusEnum.Archived)]
    [TestCase(ChoirStatusEnum.Published, ChoirStatusEnum.Cancelled)]
    [TestCase(ChoirStatusEnum.Published, ChoirStatusEnum.Archived)]
    [TestCase(ChoirStatusEnum.Cancelled, ChoirStatusEnum.Published)]
    [TestCase(ChoirStatusEnum.Cancelled, ChoirStatusEnum.Archived)]
    [TestCase(ChoirStatusEnum.Archived, ChoirStatusEnum.Published)]
    public void TransitionsAllowed(ChoirStatusEnum from, ChoirStatusEnum to)
        => Assert.That(ChoirStateHelper.IsTransitionAllowed(from, to), Is.True);

    [TestCase(ChoirStatusEnum.Published, ChoirStatusEnum.Draft)]
    [TestCase(ChoirStatusEnum.Cancelled, ChoirStatusEnum.Draft)]
    [TestCase(ChoirStatusEnum.Archived, ChoirStatusEnum.Draft)]
    [TestCase(ChoirStatusEnum.Archived, ChoirStatusEnum.Cancelled)]
    [TestCase(ChoirStatusEnum.Draft, ChoirStatusEnum.Cancelled)]
    public void ForbiddenTransitions(ChoirStatusEnum from, ChoirStatusEnum to)
        => Assert.That(ChoirStateHelper.IsTransitionAllowed(from, to), Is.False);

    [Test]
    public async Task ChangeStatusAsync_ForbiddenTransition_RejectsWithoutExposingTechnicalIdentifier()
    {
        // Publie -> Draft n'est pas autorise.
        var ex = Assert.ThrowsAsync<CustomException>(
            () => CreateAdminChoirService().ChangeStatusAsync(_choirId, ChoirStatusEnum.Draft));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(ex.Message, Does.Not.Contain(nameof(ChoirStatusEnum.Published)));
            Assert.That(ex.Message, Does.Not.Contain(nameof(ChoirStatusEnum.Draft)));
        });
    }

    // --- Archivage / suppression : IsDeleted et Statut sont desormais independants ------

    [Test]
    public async Task ChangeStatusAsync_ToArchived_IsDeletedStaysFalse()
    {
        await CreateAdminChoirService().ChangeStatusAsync(_choirId, ChoirStatusEnum.Archived);

        var choir = await _context.Choirs.FirstAsync(c => c.Id == _choirId);
        Assert.Multiple(() =>
        {
            Assert.That(choir.Status, Is.EqualTo(ChoirStatusEnum.Archived));
            Assert.That(choir.IsDeleted, Is.False, "Archive n'est plus delete.");
        });
    }

    [Test]
    public async Task DeleteAsync_MarksIsDeleted_WithoutTouchingStatus()
    {
        await CreateChoirService(AdminUserId, isAdmin: true).DeleteAsync(_choirId);

        var choir = await _context.Choirs.IgnoreQueryFilters().FirstAsync(c => c.Id == _choirId);
        Assert.Multiple(() =>
        {
            Assert.That(choir.IsDeleted, Is.True);
            Assert.That(choir.Status, Is.EqualTo(ChoirStatusEnum.Published),
                "La suppression ne doit pas réécrire le statut métier.");
        });
    }

    // --- Visibilite selon le statut, scope par MembershipService --------------------

    [Test]
    public async Task Archived_InvisibleToSimpleMember_ButVisibleToAdmin()
    {
        var choir = await _context.Choirs.FirstAsync(c => c.Id == _choirId);
        choir.Status = ChoirStatusEnum.Archived;
        await _context.SaveChangesAsync();

        var visibleToMember = await CreateMembershipService(MemberSimpleUserId).IsMemberActiveAsync(_choirId);
        var visibleToAdmin = await CreateMembershipService(AdminUserId, isAdmin: true).IsMemberActiveAsync(_choirId);

        Assert.Multiple(() =>
        {
            Assert.That(visibleToMember, Is.False);
            Assert.That(visibleToAdmin, Is.True);
        });
    }

    [Test]
    public async Task Draft_VisibleToCreatorAndManager_InvisibleToSimpleMember()
    {
        var choir = await _context.Choirs.FirstAsync(c => c.Id == _choirId);
        choir.Status = ChoirStatusEnum.Draft;
        await _context.SaveChangesAsync();

        var visibleToCreator = await CreateMembershipService(CreatorUserId).IsMemberActiveAsync(_choirId);
        var visibleToManager = await CreateMembershipService(ManagerUserId).IsMemberActiveAsync(_choirId);
        var visibleToSimpleMember = await CreateMembershipService(MemberSimpleUserId).IsMemberActiveAsync(_choirId);

        Assert.Multiple(() =>
        {
            Assert.That(visibleToCreator, Is.True,
                "Le créateur voit sa chorale en cours de création même s'il n'en est pas membre.");
            Assert.That(visibleToManager, Is.True);
            Assert.That(visibleToSimpleMember, Is.False);
        });
    }

    [Test]
    public async Task Cancelled_IsReadable_ButAllContentWriteIsRejected()
    {
        var choir = await _context.Choirs.FirstAsync(c => c.Id == _choirId);
        choir.Status = ChoirStatusEnum.Cancelled;
        await _context.SaveChangesAsync();

        var membership = CreateMembershipService(MemberSimpleUserId);
        var isReadable = await membership.IsMemberActiveAsync(_choirId);
        var canWrite = await membership.CanWriteAsync(_choirId);

        Assert.Multiple(() =>
        {
            Assert.That(isReadable, Is.True,
                "Une chorale annulée remaining visible — seule son écriture se ferme.");
            Assert.That(canWrite, Is.False);
        });

        var ex = Assert.ThrowsAsync<CustomException>(() => membership.EnsureCanWriteAsync(_choirId));
        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    // --- Reactivation et plafond de service ---------------------------------------------

    [Test]
    public async Task ChangeStatusAsync_ArchivedToPublished_ImpactQuantifiedIfCapExceeded()
    {
        // Deuxieme chorale du client, pour occuper la seule place restante une fois le
        // plafond abaisse a 1.
        var otherChoirId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = otherChoirId, SpaceType = SpaceTypeEnum.Choir, ClientId = _clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = otherChoirId, ClientId = _clientId, Name = "Autre Choir", Status = ChoirStatusEnum.Published
        });

        var sut = CreateAdminChoirService();
        await sut.ChangeStatusAsync(_choirId, ChoirStatusEnum.Archived);

        var client = await _context.Clients.FirstAsync(c => c.Id == _clientId);
        client.ChoirLimit = 1;
        await _context.SaveChangesAsync();

        var ex = Assert.ThrowsAsync<CustomException>(
            () => sut.ChangeStatusAsync(_choirId, ChoirStatusEnum.Published));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(ex.Message, Does.Contain("1"), "Le refus doit chiffrer l'impact, pas échouer en silence.");
        });
    }

    [Test]
    public async Task Archived_NeverCountsTowardChoirLimit()
    {
        var serviceLimitService = CreateServiceLimitService();
        var before = await serviceLimitService.GetUsageAsync(_clientId);
        Assert.That(before.Choirs, Is.EqualTo(1));

        await CreateAdminChoirService().ChangeStatusAsync(_choirId, ChoirStatusEnum.Archived);

        var after = await serviceLimitService.GetUsageAsync(_clientId);
        Assert.That(after.Choirs, Is.EqualTo(0));
    }

    private AdminChoirService CreateAdminChoirService()
    {
        var sp = BuildServiceProvider(AdminUserId, isAdmin: true);
        return new AdminChoirService(sp, new AuditLogService(sp), new ServiceLimitService(sp));
    }

    private MembershipService CreateMembershipService(string userId, bool isAdmin = false)
        => new(BuildServiceProvider(userId, isAdmin));

    private ServiceLimitService CreateServiceLimitService()
        => new(BuildServiceProvider(AdminUserId, isAdmin: true));

    private ChoirService CreateChoirService(string userId, bool isAdmin = false)
    {
        var sp = BuildServiceProvider(userId, isAdmin);
        return new ChoirService(
            sp,
            new AuditLogService(sp),
            new ServiceLimitService(sp),
            new MembershipService(sp),
            new ClientRoleResolverService(_context),
            new SpaceRoleResolverService(_context),
            new SectionService(sp));
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
