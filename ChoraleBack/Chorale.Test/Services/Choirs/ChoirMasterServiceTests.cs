using System.Net;
using System.Security.Claims;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ChoraleBackEnd.Test.Fakes;
using NUnit.Framework;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.ChoirMembers;
using ChoraleBackEnd.ViewModels.Choirs;

namespace ChoraleBackEnd.Test.Services.Choirs;

/// <summary>
/// <see cref="ChoirMasterService"/> — depanne/gere le chef de chœur d'une chorale EXISTANTE
/// pour le compte d'un ResponsableClient qui n'est pas necessairement membre de cette chorale
/// (a la difference de <c>ChoirMembersService</c>, qui exige une appartenance active).
/// </summary>
[TestFixture]
public sealed class ChoirMasterServiceTests
{
    private const string ManagerClientUserId = "responsable-client-1";
    private const string OtherManagerClientUserId = "responsable-client-2";
    private const string ExistingManagerUserId = "chef-choeur-existant-1";
    private const string ActiveMemberUserId = "membre-actif-1";
    private const string ArchivedMemberUserId = "membre-archive-1";
    private const string SectionLeaderUserId = "chef-pupitre-1";
    private const string NewUserId = "nouveau-compte-1";

    private ChoraleDbContext _context = null!;
    private Guid _clientId;
    private Guid _otherClientId;
    private Guid _choirId;
    private Guid _sectionId;
    private Guid _existingManagerMemberId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _clientId = ChoraleDbContext.NewIdGuid();
        _otherClientId = ChoraleDbContext.NewIdGuid();
        _choirId = ChoraleDbContext.NewIdGuid();
        _sectionId = ChoraleDbContext.NewIdGuid();

        _context.Users.Add(Seed(ManagerClientUserId, "rc@t.com"));
        _context.Users.Add(Seed(OtherManagerClientUserId, "autre-rc@t.com"));
        _context.Users.Add(Seed(ExistingManagerUserId, "chef-existant@t.com"));
        _context.Users.Add(Seed(ActiveMemberUserId, "actif@t.com"));
        _context.Users.Add(Seed(ArchivedMemberUserId, "archive@t.com"));
        _context.Users.Add(Seed(SectionLeaderUserId, "pupitre@t.com"));
        _context.Users.Add(Seed(NewUserId, "nouveau@t.com"));

        _context.Clients.Add(new Client
        {
            Id = _clientId, Name = "Client", Status = ClientStatusEnum.Active,
            ChoirLimit = 10, MemberLimit = 100,
            StorageQuotaBytes = 1_000_000, MaxFileSizeBytes = 100_000
        });
        _context.Clients.Add(new Client
        {
            Id = _otherClientId, Name = "Autre Client", Status = ClientStatusEnum.Active,
            ChoirLimit = 10, MemberLimit = 100
        });
        _context.ClientMembers.Add(new ClientMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ClientId = _clientId,
            UserId = ManagerClientUserId, Role = UserRoleEnum.ClientManager
        });
        _context.ClientMembers.Add(new ClientMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ClientId = _otherClientId,
            UserId = OtherManagerClientUserId, Role = UserRoleEnum.ClientManager
        });

        _context.Spaces.Add(new Space { Id = _choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = _clientId });
        _context.Choirs.Add(new Choir
        {
            Id = _choirId, ClientId = _clientId, Name = "Choir", Status = ChoirStatusEnum.Published
        });

        var existingManager = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId, SpaceId = _choirId,
            UserId = ExistingManagerUserId, Status = MemberStatusEnum.Active
        };
        _existingManagerMemberId = existingManager.Id;
        _context.SpaceMembers.Add(existingManager);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(), SpaceMemberId = existingManager.Id, Role = UserRoleEnum.Manager
        });

        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId, SpaceId = _choirId,
            UserId = ActiveMemberUserId, Status = MemberStatusEnum.Active
        });

        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId, SpaceId = _choirId,
            UserId = ArchivedMemberUserId, Status = MemberStatusEnum.Archived, IsDeleted = true
        });

        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId, SpaceId = _choirId,
            UserId = SectionLeaderUserId, Status = MemberStatusEnum.Active
        });
        _context.Sections.Add(new Section
        {
            Id = _sectionId, ChoirId = _choirId, VoicePart = VoicePartEnum.Soprano,
            SectionLeaderId = SectionLeaderUserId
        });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    private static User Seed(string id, string email) => new()
    {
        Id = id, UserName = email, Email = email,
        NormalizedEmail = email.ToUpperInvariant(), NormalizedUserName = email.ToUpperInvariant(),
        EmailConfirmed = true
    };

    // ---- Autorisation ----------------------------------------------------

    [Test]
    public void GetPagedAsync_ClientManagerOfAnotherClient_ThrowsForbidden()
    {
        var sut = CreateService(OtherManagerClientUserId);

        var exception = Assert.ThrowsAsync<CustomException>(
            () => sut.GetPagedAsync(_choirId, new PaginateViewModel()));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public void AssignAsync_UserWithoutClientRole_ThrowsForbidden()
    {
        var sut = CreateService(ActiveMemberUserId);

        var exception = Assert.ThrowsAsync<CustomException>(() => sut.AssignAsync(
            _choirId, new AssignChoirMasterViewModel { Email = "nouveau@t.com" }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AssignAsync_Admin_IsAllowedWithoutBeingClientManager()
    {
        var sut = CreateService("admin-1", isAdmin: true);

        var result = await sut.AssignAsync(_choirId, new AssignChoirMasterViewModel { Email = "nouveau@t.com" });

        Assert.That(result.UserId, Is.EqualTo(NewUserId));
    }

    // ---- AssignAsync -------------------------------------------------------

    [Test]
    public async Task AssignAsync_AccountNotYetMember_CreatesSpaceMemberAndManagerRole()
    {
        var sut = CreateService(ManagerClientUserId);

        var result = await sut.AssignAsync(_choirId, new AssignChoirMasterViewModel { Email = "nouveau@t.com" });

        Assert.That(result.Roles, Does.Contain(nameof(UserRoleEnum.Manager)));

        var member = await _context.SpaceMembers.FirstAsync(m => m.ChoirId == _choirId && m.UserId == NewUserId);
        Assert.That(member.Status, Is.EqualTo(MemberStatusEnum.Active));
    }

    [Test]
    public async Task AssignAsync_AlreadyActiveMemberWithoutRole_AddsOnlyTheRole()
    {
        var sut = CreateService(ManagerClientUserId);

        await sut.AssignAsync(_choirId, new AssignChoirMasterViewModel { Email = "actif@t.com" });

        var member = await _context.SpaceMembers.FirstAsync(m => m.ChoirId == _choirId && m.UserId == ActiveMemberUserId);
        var hasRole = await _context.SpaceMemberRoles
            .AnyAsync(r => r.SpaceMemberId == member.Id && r.Role == UserRoleEnum.Manager);

        Assert.That(hasRole, Is.True);
        Assert.That(member.Status, Is.EqualTo(MemberStatusEnum.Active));
    }

    [Test]
    public async Task AssignAsync_ArchivedMember_ReactivatesAndAddsTheRole()
    {
        // Decision produit tranchee en Phase 2 : reactiver plutot que refuser (409) — designer
        // explicitement un chef de chœur exprime l'intention qu'il gere la chorale des
        // maintenant, y compris s'il l'avait quittee.
        var sut = CreateService(ManagerClientUserId);

        var result = await sut.AssignAsync(_choirId, new AssignChoirMasterViewModel { Email = "archive@t.com" });

        Assert.That(result.Roles, Does.Contain(nameof(UserRoleEnum.Manager)));

        var member = await _context.SpaceMembers
            .IgnoreQueryFilters()
            .FirstAsync(m => m.ChoirId == _choirId && m.UserId == ArchivedMemberUserId);
        Assert.That(member.Status, Is.EqualTo(MemberStatusEnum.Active));
        Assert.That(member.IsDeleted, Is.False);
    }

    [Test]
    public void AssignAsync_AlreadyChoirManager_IsIdempotent()
    {
        var sut = CreateService(ManagerClientUserId);

        Assert.DoesNotThrowAsync(() => sut.AssignAsync(
            _choirId, new AssignChoirMasterViewModel { Email = "chef-existant@t.com" }));
    }

    [Test]
    public void AssignAsync_UnknownEmail_ThrowsNotFound()
    {
        var sut = CreateService(ManagerClientUserId);

        Assert.ThrowsAsync<KeyNotFoundException>(() => sut.AssignAsync(
            _choirId, new AssignChoirMasterViewModel { Email = "jamais-inscrit@t.com" }));
    }

    [Test]
    public async Task AssignAsync_MemberLimitReached_ThrowsConflict409()
    {
        var client = await _context.Clients.FirstAsync(c => c.Id == _clientId);
        // 4 membres distincts deja actifs (chef existant, actif, pupitre) + 1 archive non
        // compte : le plafond est deja atteint pour un 4e membre distinct.
        client.MemberLimit = 3;
        await _context.SaveChangesAsync();

        var sut = CreateService(ManagerClientUserId, withRealLimits: true);

        var exception = Assert.ThrowsAsync<CustomException>(() => sut.AssignAsync(
            _choirId, new AssignChoirMasterViewModel { Email = "nouveau@t.com" }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task AssignAsync_ArchivedChoir_ThrowsConflict409()
    {
        var choir = await _context.Choirs.FirstAsync(c => c.Id == _choirId);
        choir.Status = ChoirStatusEnum.Archived;
        await _context.SaveChangesAsync();

        var sut = CreateService(ManagerClientUserId);

        var exception = Assert.ThrowsAsync<CustomException>(() => sut.AssignAsync(
            _choirId, new AssignChoirMasterViewModel { Email = "nouveau@t.com" }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    // ---- RevokeAsync ---------------------------------------------------------

    [Test]
    public async Task RevokeAsync_ManagerWithAnotherActiveManager_RemovesOnlyTheRole()
    {
        // Ajoute un second manager pour ne pas declencher la garde "dernier manager".
        var secondManager = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId, SpaceId = _choirId,
            UserId = NewUserId, Status = MemberStatusEnum.Active
        };
        _context.SpaceMembers.Add(secondManager);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(), SpaceMemberId = secondManager.Id, Role = UserRoleEnum.Manager
        });
        await _context.SaveChangesAsync();

        var sut = CreateService(ManagerClientUserId);

        await sut.RevokeAsync(_choirId, ExistingManagerUserId);

        var stillHasRole = await _context.SpaceMemberRoles
            .AnyAsync(r => r.SpaceMemberId == _existingManagerMemberId && r.Role == UserRoleEnum.Manager);
        Assert.That(stillHasRole, Is.False);

        var member = await _context.SpaceMembers.FirstAsync(m => m.Id == _existingManagerMemberId);
        Assert.That(member.Status, Is.EqualTo(MemberStatusEnum.Active), "Retirer le role ne retire pas le membre.");
    }

    [Test]
    public void RevokeAsync_LastManager_ThrowsConflict409()
    {
        var sut = CreateService(ManagerClientUserId);

        var exception = Assert.ThrowsAsync<CustomException>(
            () => sut.RevokeAsync(_choirId, ExistingManagerUserId));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task RevokeAsync_ManagerIsAlsoSectionLeader_ThrowsBadRequest()
    {
        // SectionLeaderUserId doit lui-meme porter le role Manager pour que la garde
        // "chef de pupitre" de RevokeAsync soit celle qui s'applique (elle est verifiee AVANT
        // la garde "dernier manager", donc pas besoin d'un second manager ici).
        var sectionLeaderMember = await _context.SpaceMembers
            .FirstAsync(m => m.ChoirId == _choirId && m.UserId == SectionLeaderUserId);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(), SpaceMemberId = sectionLeaderMember.Id, Role = UserRoleEnum.Manager
        });
        await _context.SaveChangesAsync();

        var sut = CreateService(ManagerClientUserId);

        var exception = Assert.ThrowsAsync<CustomException>(
            () => sut.RevokeAsync(_choirId, SectionLeaderUserId));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public void RevokeAsync_NonExistentMember_ThrowsNotFound()
    {
        var sut = CreateService(ManagerClientUserId);

        Assert.ThrowsAsync<KeyNotFoundException>(() => sut.RevokeAsync(_choirId, "jamais-vu"));
    }

    [Test]
    public void RevokeAsync_MemberIsNotChoirManager_ThrowsNotFound()
    {
        var sut = CreateService(ManagerClientUserId);

        Assert.ThrowsAsync<KeyNotFoundException>(() => sut.RevokeAsync(_choirId, ActiveMemberUserId));
    }

    // ---- GetPagedAsync ---------------------------------------------------

    [Test]
    public async Task GetPagedAsync_ListsOnlyActiveManagers()
    {
        var sut = CreateService(ManagerClientUserId);

        var page = await sut.GetPagedAsync(_choirId, new PaginateViewModel());

        Assert.That(page.Items.Select(i => i.UserId), Does.Contain(ExistingManagerUserId));
        Assert.That(page.Items.Select(i => i.UserId), Does.Not.Contain(ActiveMemberUserId));
        Assert.That(page.Items.Select(i => i.UserId), Does.Not.Contain(ArchivedMemberUserId));
    }

    [Test]
    public async Task GetPagedAsync_FilterByEmail_ReturnsOnlyMatchingRows()
    {
        // Defaut corrige : pagination.Filter etait ignore, la recherche du front etait
        // silencieusement inoperante. Deux managers actifs, filtre cible un seul des deux.
        await AddSecondManagerAsync();

        var sut = CreateService(ManagerClientUserId);

        var page = await sut.GetPagedAsync(_choirId, new PaginateViewModel { Filter = "chef-existant" });

        Assert.That(page.Items.Select(i => i.UserId), Does.Contain(ExistingManagerUserId));
        Assert.That(page.Items.Select(i => i.UserId), Does.Not.Contain(NewUserId));
        Assert.That(page.TotalCount, Is.EqualTo(1));
    }

    [Test]
    public async Task GetPagedAsync_EmptyOrBlankFilter_ReturnsAllManagers()
    {
        await AddSecondManagerAsync();

        var sut = CreateService(ManagerClientUserId);

        var page = await sut.GetPagedAsync(_choirId, new PaginateViewModel { Filter = "   " });

        Assert.That(page.Items.Select(i => i.UserId), Does.Contain(ExistingManagerUserId));
        Assert.That(page.Items.Select(i => i.UserId), Does.Contain(NewUserId));
        Assert.That(page.TotalCount, Is.EqualTo(2));
    }

    private async Task AddSecondManagerAsync()
    {
        var otherManager = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId, SpaceId = _choirId,
            UserId = NewUserId, Status = MemberStatusEnum.Active
        };
        _context.SpaceMembers.Add(otherManager);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(), SpaceMemberId = otherManager.Id, Role = UserRoleEnum.Manager
        });
        await _context.SaveChangesAsync();
    }

    private ChoirMasterService CreateService(string userId, bool isAdmin = false, bool withRealLimits = false)
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
        var serviceLimitService = withRealLimits
            ? (IServiceLimitService)new ServiceLimitService(serviceProvider)
            : new FakeServiceLimitService();

        return new ChoirMasterService(
            serviceProvider,
            auditLogService,
            serviceLimitService,
            new ClientRoleResolverService(_context),
            new SectionService(serviceProvider),
            new MembershipService(serviceProvider));
    }
}
