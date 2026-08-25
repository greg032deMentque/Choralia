using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.AdminUsers;
using System.Security.Claims;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Test.Fakes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.Services.UserServices;
using ChoraleBackEnd.ViewModels.Events;

namespace ChoraleBackEnd.Test.Services.Users;

[TestFixture]
public sealed class AdminUserListTests
{
    private const string AdminId = "admin-courant";

    private ChoraleDbContext _context = null!;
    private AdminUserService _sut = null!;
    private AdminUserQueryService _querySut = null!;
    private RoleManager<IdentityRole> _roleManager = null!;
    private UserManager<User> _userManager = null!;

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
                    [new Claim(ClaimTypes.NameIdentifier, AdminId)], "Test"))
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
            .AddEntityFrameworkStores<ChoraleDbContext>()
            .AddDefaultTokenProviders();

        var serviceProvider = services.BuildServiceProvider();
        _roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        _userManager = serviceProvider.GetRequiredService<UserManager<User>>();
        await _roleManager.CreateAsync(new IdentityRole(UserRoleEnum.Admin.ToString()));

        var auditLogService = new AuditLogService(serviceProvider);
        var fakeAccountService = new FakeAccountService();
        var fakeEmailService = new FakeEmailService();
        _querySut = new AdminUserQueryService(serviceProvider, new SectionVoicePartLookupService(_context));
        _sut = new AdminUserService(serviceProvider, auditLogService, fakeAccountService, fakeEmailService, _querySut);

        CreateUser(AdminId, "Admin", "Courant", $"{AdminId}@test.com");
        await _context.SaveChangesAsync();
        await _userManager.AddToRoleAsync((await _context.Users.FindAsync(AdminId))!, UserRoleEnum.Admin.ToString());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Test]
    public async Task GetChoirUsersPagedAsync_ChoirMember_AppearsWithOwnRoles()
    {
        var clientId = ChoraleDbContext.NewIdGuid();
        var choirId = CreateChoir(clientId, "Chorale des Alpes");
        var user = CreateUser("membre-1", "Alice", "Martin", "alice@test.com");
        var member = AddMemberChoir(user.Id, choirId);
        await _context.SaveChangesAsync();

        var result = await _querySut.GetChoirUsersPagedAsync(new AdminChoirUsersPagedFilterViewModel());

        var row = result.Items.Single(i => i.Id == member.Id);
        Assert.Multiple(() =>
        {
            Assert.That(row.Firstname, Is.EqualTo("Alice"));
            Assert.That(row.ChoirName, Is.EqualTo("Chorale des Alpes"));
            Assert.That(row.Roles, Is.EqualTo(new List<string> { UserRoleEnum.Singer.ToString() }));
        });
    }

    [Test]
    public async Task GetEventUsersPagedAsync_EventParticipant_AppearsWithOwnRole()
    {
        var clientId = ChoraleDbContext.NewIdGuid();
        var choirId = CreateChoir(clientId, "Chorale porteuse");
        var eventId = CreateEvent(choirId, "Concert de Noël", DateTime.UtcNow.AddDays(10));
        var user = CreateUser("participant-1", "Paul", "Durand", "paul@test.com");
        var member = AddMemberEvent(user.Id, eventId, choirId);
        await _context.SaveChangesAsync();

        var result = await _querySut.GetEventUsersPagedAsync(new AdminEventUsersPagedFilterViewModel());

        var row = result.Items.Single(i => i.Id == member.Id);
        Assert.Multiple(() =>
        {
            Assert.That(row.EventTitle, Is.EqualTo("Concert de Noël"));
            Assert.That(row.ChoirName, Is.EqualTo("Chorale porteuse"));
            Assert.That(row.Role, Is.EqualTo(UserRoleEnum.Participant.ToString()));
        });
    }

    [Test]
    public async Task GetPagedAsync_AdminUser_AppearsInAdminsTab()
    {
        var result = await _querySut.GetPagedAsync(new AdminUsersPagedFilterViewModel());

        Assert.That(result.Items.Any(i => i.Id == AdminId), Is.True);
    }

    [Test]
    public async Task GetPagedAsync_SortRequestOnAllowedColumn_EffectivelyChangesResultOrder()
    {
        CreateUser("zed-admin", "Zed", "Zorro", "zed@test.com");
        CreateUser("aaa-admin", "Aaa", "Aaronson", "aaa@test.com");
        await _context.SaveChangesAsync();
        await _userManager.AddToRoleAsync((await _context.Users.FindAsync("zed-admin"))!, UserRoleEnum.Admin.ToString());
        await _userManager.AddToRoleAsync((await _context.Users.FindAsync("aaa-admin"))!, UserRoleEnum.Admin.ToString());

        var parDefaut = await _querySut.GetPagedAsync(new AdminUsersPagedFilterViewModel { PageSize = 100 });
        var parEmailDesc = await _querySut.GetPagedAsync(
            new AdminUsersPagedFilterViewModel { PageSize = 100, SortActive = "Email", SortDirection = "desc" });

        var orderParDefaut = parDefaut.Items.Select(i => i.Id).ToList();
        var orderParEmailDesc = parEmailDesc.Items.Select(i => i.Id).ToList();

        Assert.That(orderParEmailDesc, Is.Not.EqualTo(orderParDefaut),
            "Un SortActive different du tri par defaut doit changer l'ordre des resultats.");
        Assert.That(orderParEmailDesc.First(), Is.EqualTo("zed-admin"),
            "zed@test.com doit arriver en tete d'un tri Email descendant.");
    }

    [Test]
    public async Task GetPagedAsync_NoSortActive_IdenticalToHistoricalDefaultSort()
    {
        CreateUser("zed-admin", "Zed", "Zorro", "zed@test.com");
        CreateUser("aaa-admin", "Aaa", "Aaronson", "aaa@test.com");
        await _context.SaveChangesAsync();
        await _userManager.AddToRoleAsync((await _context.Users.FindAsync("zed-admin"))!, UserRoleEnum.Admin.ToString());
        await _userManager.AddToRoleAsync((await _context.Users.FindAsync("aaa-admin"))!, UserRoleEnum.Admin.ToString());

        var result = await _querySut.GetPagedAsync(new AdminUsersPagedFilterViewModel { PageSize = 100 });

        // Tri par defaut historique : Lastname puis Firstname (voir AdminUserService.GetPagedAsync).
        var expectedLastnames = result.Items
            .OrderBy(i => i.Lastname).ThenBy(i => i.Firstname).ThenBy(i => i.Id)
            .Select(i => i.Id)
            .ToList();

        Assert.That(result.Items.Select(i => i.Id).ToList(), Is.EqualTo(expectedLastnames));
    }

    [Test]
    public async Task GetPagedAsync_IsActiveFilter_ReturnsOnlyMatchingAccounts()
    {
        var active = CreateUser("admin-active", "Active", "Un", "admin-active@test.com", isActive: true);
        var inactive = CreateUser("admin-inactive", "Inactive", "Deux", "admin-inactive@test.com", isActive: false);
        await _context.SaveChangesAsync();
        await _userManager.AddToRoleAsync(active, UserRoleEnum.Admin.ToString());
        await _userManager.AddToRoleAsync(inactive, UserRoleEnum.Admin.ToString());

        var activeOnly = await _querySut.GetPagedAsync(new AdminUsersPagedFilterViewModel { PageSize = 100, IsActive = true });
        var inactiveOnly = await _querySut.GetPagedAsync(new AdminUsersPagedFilterViewModel { PageSize = 100, IsActive = false });
        var allUsers = await _querySut.GetPagedAsync(new AdminUsersPagedFilterViewModel { PageSize = 100 });

        Assert.Multiple(() =>
        {
            Assert.That(activeOnly.Items.Select(i => i.Id), Does.Contain(active.Id));
            Assert.That(activeOnly.Items.Select(i => i.Id), Does.Not.Contain(inactive.Id));
            Assert.That(inactiveOnly.Items.Select(i => i.Id), Does.Contain(inactive.Id));
            Assert.That(inactiveOnly.Items.Select(i => i.Id), Does.Not.Contain(active.Id));
            Assert.That(allUsers.Items.Select(i => i.Id), Does.Contain(active.Id));
            Assert.That(allUsers.Items.Select(i => i.Id), Does.Contain(inactive.Id));
            Assert.That(allUsers.TotalCount, Is.EqualTo(activeOnly.TotalCount + inactiveOnly.TotalCount),
                "Sans filtre, le total doit couvrir exactement les deux sous-ensembles filtrés — non-régression.");
        });
    }

    [Test]
    public async Task GetUnattachedUsersPagedAsync_UserWithNothingAtAll_AppearsInTab4()
    {
        var orphanUser = CreateUser("orphelin-1", "Orphelin", "Test", "orphelin@test.com");
        await _context.SaveChangesAsync();

        var result = await _querySut.GetUnattachedUsersPagedAsync(new AdminUsersPagedFilterViewModel());

        Assert.That(result.Items.Any(i => i.Id == orphanUser.Id), Is.True);
    }

    [Test]
    public async Task GetUnattachedUsersPagedAsync_IsGuestAccountFilter_ReturnsOnlyMatchingAccounts()
    {
        var invitedUser = CreateUser("orph-invite", "Invite", "Un", "orph-invite@test.com",
            isGuestAccount: true, emailConfirmed: false);
        var normalUser = CreateUser("orph-normal", "Normal", "Deux", "orph-normal@test.com", isGuestAccount: false);
        await _context.SaveChangesAsync();

        var invitedOnly = await _querySut.GetUnattachedUsersPagedAsync(
            new AdminUsersPagedFilterViewModel { PageSize = 100, IsGuestAccount = true });
        var normalOnly = await _querySut.GetUnattachedUsersPagedAsync(
            new AdminUsersPagedFilterViewModel { PageSize = 100, IsGuestAccount = false });
        var allUsers = await _querySut.GetUnattachedUsersPagedAsync(new AdminUsersPagedFilterViewModel { PageSize = 100 });

        Assert.Multiple(() =>
        {
            Assert.That(invitedOnly.Items.Select(i => i.Id), Does.Contain(invitedUser.Id));
            Assert.That(invitedOnly.Items.Select(i => i.Id), Does.Not.Contain(normalUser.Id));
            Assert.That(normalOnly.Items.Select(i => i.Id), Does.Contain(normalUser.Id));
            Assert.That(normalOnly.Items.Select(i => i.Id), Does.Not.Contain(invitedUser.Id));
            Assert.That(allUsers.Items.Select(i => i.Id), Does.Contain(invitedUser.Id));
            Assert.That(allUsers.Items.Select(i => i.Id), Does.Contain(normalUser.Id));
        });
    }

    [Test]
    public async Task GetUnattachedUsersPagedAsync_IsActiveAndIsGuestAccountFiltersCombined_Accumulate()
    {
        var activeInvitedUser = CreateUser("orph-invite-active", "InviteActif", "A", "orph-ia@test.com",
            isActive: true, isGuestAccount: true, emailConfirmed: false);
        var inactiveInvitedUser = CreateUser("orph-invite-inactive", "InviteInactif", "B", "orph-ii@test.com",
            isActive: false, isGuestAccount: true, emailConfirmed: false);
        var activeNormalUser = CreateUser("orph-normal-active", "NormalActif", "C", "orph-na@test.com",
            isActive: true, isGuestAccount: false);
        await _context.SaveChangesAsync();

        var result = await _querySut.GetUnattachedUsersPagedAsync(
            new AdminUsersPagedFilterViewModel { PageSize = 100, IsActive = true, IsGuestAccount = true });

        var ids = result.Items.Select(i => i.Id).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(ids, Does.Contain(activeInvitedUser.Id));
            Assert.That(ids, Does.Not.Contain(inactiveInvitedUser.Id));
            Assert.That(ids, Does.Not.Contain(activeNormalUser.Id));
        });
    }

    [Test]
    public async Task GetUnattachedUsersPagedAsync_IsActiveFilter_TotalCountReflectsFilterNotOverallTotal()
    {
        for (var i = 0; i < 3; i++)
            CreateUser($"orph-active-{i}", "Active", $"N{i}", $"orph-active-{i}@test.com", isActive: true);
        for (var i = 0; i < 2; i++)
            CreateUser($"orph-inactive-{i}", "Inactive", $"N{i}", $"orph-inactive-{i}@test.com", isActive: false);
        await _context.SaveChangesAsync();

        var result = await _querySut.GetUnattachedUsersPagedAsync(
            new AdminUsersPagedFilterViewModel { PageSize = 2, IsActive = true });

        Assert.Multiple(() =>
        {
            Assert.That(result.TotalCount, Is.EqualTo(3),
                "TotalCount doit refleter le sous-ensemble filtre (3 actifs), pas le total des orphelins (5).");
            Assert.That(result.Items, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task OnePersonInTwoChoirsAndOneEvent_ProducesThreeRowsAndOneProfile()
    {
        var clientId = ChoraleDbContext.NewIdGuid();
        var choir1 = CreateChoir(clientId, "Choir 1");
        var choir2 = CreateChoir(clientId, "Choir 2");
        var eventId = CreateEvent(null, "Concert autonome", DateTime.UtcNow.AddDays(5));
        var user = CreateUser("multi-1", "Multi", "Casquette", "multi@test.com");

        AddMemberChoir(user.Id, choir1);
        AddMemberChoir(user.Id, choir2);
        AddMemberEvent(user.Id, eventId, null);
        await _context.SaveChangesAsync();

        var choirUsers = await _querySut.GetChoirUsersPagedAsync(new AdminChoirUsersPagedFilterViewModel { PageSize = 100 });
        var eventUsers = await _querySut.GetEventUsersPagedAsync(new AdminEventUsersPagedFilterViewModel { PageSize = 100 });
        var profile = await _querySut.GetUserDetailAsync(user.Id);

        Assert.Multiple(() =>
        {
            Assert.That(choirUsers.Items.Count(i => i.UserId == user.Id), Is.EqualTo(2));
            Assert.That(eventUsers.Items.Count(i => i.UserId == user.Id), Is.EqualTo(1));
            Assert.That(profile.Choirs, Has.Count.EqualTo(2));
            Assert.That(profile.Events, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task ClientManagerWithoutAnySpace_AppearsInTab4WithClientAttachment()
    {
        var clientId = ChoraleDbContext.NewIdGuid();
        CreateClient(clientId, "Client Alpha");
        var manager = CreateUser("resp-client-1", "Resp", "Client", "resp-client@test.com");
        _context.ClientMembers.Add(new ClientMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ClientId = clientId,
            UserId = manager.Id,
            Role = UserRoleEnum.ClientManager,
            IsDeleted = false
        });
        await _context.SaveChangesAsync();

        var result = await _querySut.GetUnattachedUsersPagedAsync(new AdminUsersPagedFilterViewModel());

        var row = result.Items.Single(i => i.Id == manager.Id);
        Assert.Multiple(() =>
        {
            Assert.That(row.ClientName, Is.EqualTo("Client Alpha"));
            Assert.That(row.ClientRole, Is.EqualTo(UserRoleEnum.ClientManager.ToString()));
        });
    }

    [Test]
    public async Task ChoirAttachmentSoftDeleted_ProducesNoGhostRow()
    {
        var clientId = ChoraleDbContext.NewIdGuid();
        var choirId = CreateChoir(clientId, "Chorale supprimée");
        var user = CreateUser("member-supprime-1", "Ancien", "Membre", "previous@test.com");
        AddMemberChoir(user.Id, choirId);
        await _context.SaveChangesAsync();

        var choir = await _context.Choirs.FirstAsync(c => c.Id == choirId);
        choir.IsDeleted = true;
        await _context.SaveChangesAsync();

        var result = await _querySut.GetChoirUsersPagedAsync(new AdminChoirUsersPagedFilterViewModel { PageSize = 100 });

        Assert.That(result.Items.Any(i => i.UserId == user.Id), Is.False);
    }

    [Test]
    public async Task EventAttachmentSoftDeleted_ProducesNoGhostRow()
    {
        var eventId = CreateEvent(null, "Événement supprimé", DateTime.UtcNow.AddDays(1));
        var user = CreateUser("participant-supprime-1", "Ancien", "Participant", "previous-part@test.com");
        AddMemberEvent(user.Id, eventId, null);
        await _context.SaveChangesAsync();

        var evt = await _context.Events.FirstAsync(e => e.Id == eventId);
        evt.IsDeleted = true;
        await _context.SaveChangesAsync();

        var result = await _querySut.GetEventUsersPagedAsync(new AdminEventUsersPagedFilterViewModel { PageSize = 100 });

        Assert.That(result.Items.Any(i => i.UserId == user.Id), Is.False);
    }

    [Test]
    public async Task InvitedMemberNeverLoggedIn_AppearsInTab4()
    {
        var invitedUser = CreateUser("invite-jamais-connecte", "Invite", "Jamais", "invite-jamais@test.com",
            isGuestAccount: true, emailConfirmed: false);
        await _context.SaveChangesAsync();

        var result = await _querySut.GetUnattachedUsersPagedAsync(new AdminUsersPagedFilterViewModel());

        Assert.Multiple(() =>
        {
            Assert.That(result.Items.Any(i => i.Id == invitedUser.Id), Is.True);
            Assert.That(result.Items.Single(i => i.Id == invitedUser.Id).IsGuestAccount, Is.True);
        });
    }

    [Test]
    public async Task TwoAccountsWithSameEmail_AppearAsTwoDistinctRowsNeverMerged()
    {
        var email = "doublon@test.com";
        var account1 = new User
        {
            Id = "doublon-1",
            UserName = "doublon-1@test.com",
            NormalizedUserName = "DOUBLON-1@TEST.COM",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Firstname = "Premier",
            Lastname = "Doublon",
            IsActive = true
        };
        var account2 = new User
        {
            Id = "doublon-2",
            UserName = "doublon-2@test.com",
            NormalizedUserName = "DOUBLON-2@TEST.COM",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Firstname = "Second",
            Lastname = "Doublon",
            IsActive = true
        };
        _context.Users.AddRange(account1, account2);
        await _context.SaveChangesAsync();

        var result = await _querySut.GetUnattachedUsersPagedAsync(new AdminUsersPagedFilterViewModel { PageSize = 100 });

        var rows = result.Items.Where(i => i.Email == email).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows.Select(l => l.Id).Distinct().Count(), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task StandaloneEventWithoutOwningChoir_DisplaysWithoutExceptionAndWithoutChoir()
    {
        var eventId = CreateEvent(null, "Concert indépendant", DateTime.UtcNow.AddDays(3));
        var user = CreateUser("participant-autonome", "Auto", "Nome", "autonome@test.com");
        AddMemberEvent(user.Id, eventId, null);
        await _context.SaveChangesAsync();

        var result = await _querySut.GetEventUsersPagedAsync(new AdminEventUsersPagedFilterViewModel { PageSize = 100 });

        var row = result.Items.Single(i => i.UserId == user.Id);
        Assert.Multiple(() =>
        {
            Assert.That(row.ChoirId, Is.Null);
            Assert.That(row.ChoirName, Is.Null);
        });
    }

    [Test]
    public async Task MemberManagerAndSectionLeaderOnSameChoir_OneRowTwoRoles()
    {
        var clientId = ChoraleDbContext.NewIdGuid();
        var choirId = CreateChoir(clientId, "Chorale cumulée");
        var user = CreateUser("cumul-1", "Cumul", "Role", "cumul@test.com");
        var member = AddMemberChoir(user.Id, choirId);

        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceMemberId = member.Id,
            Role = UserRoleEnum.Manager
        });

        var sectionId = ChoraleDbContext.NewIdGuid();
        _context.Sections.Add(new Section
        {
            Id = sectionId,
            ChoirId = choirId,
            VoicePart = VoicePartEnum.Tenor,
            SectionLeaderId = user.Id
        });
        await _context.SaveChangesAsync();

        var result = await _querySut.GetChoirUsersPagedAsync(new AdminChoirUsersPagedFilterViewModel { PageSize = 100 });

        var rows = result.Items.Where(i => i.UserId == user.Id).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Roles, Has.Count.EqualTo(2));
            Assert.That(rows[0].Roles, Does.Contain(UserRoleEnum.Manager.ToString()));
            Assert.That(rows[0].Roles, Does.Contain(UserRoleEnum.SectionLeader.ToString()));
        });
    }

    /// <summary>
    /// Complétude de l'onglet « chorales » a l'echelle d'une page entiere : chaque ligne porte
    /// son propre UserId, sa propre chorale et son propre pupitre, sans melange d'une ligne a
    /// l'autre. Les tests unitaires voisins verifient UNE ligne a la fois — un defaut de
    /// jointure qui decale les colonnes entre lignes ne s'y voit pas.
    /// </summary>
    [Test]
    public async Task GetChoirUsersPagedAsync_FiftyMembersAcrossTwoChoirs_ReturnsEveryRowCompleteAndCorrect()
    {
        const int memberCount = 50;

        var clientId = ChoraleDbContext.NewIdGuid();
        var choirA = CreateChoir(clientId, "Chorale A");
        var choirB = CreateChoir(clientId, "Chorale B");

        var sectionA = ChoraleDbContext.NewIdGuid();
        _context.Sections.Add(new Section { Id = sectionA, ChoirId = choirA, VoicePart = VoicePartEnum.Alto });

        var expected = new Dictionary<Guid, (string UserId, Guid ChoirId, string ChoirName)>();

        for (var i = 0; i < memberCount; i++)
        {
            var userId = $"membre-echelle-{i}";
            CreateUser(userId, $"Prenom{i}", $"Nom{i}", $"{userId}@test.com");

            var inChoirA = i % 2 == 0;
            var choirId = inChoirA ? choirA : choirB;
            var member = AddMemberChoir(userId, choirId);

            if (inChoirA)
                _context.SectionMembers.Add(new SectionMember
                {
                    Id = ChoraleDbContext.NewIdGuid(),
                    UserId = userId,
                    SectionId = sectionA
                });

            expected[member.Id] = (userId, choirId, inChoirA ? "Chorale A" : "Chorale B");
        }

        await _context.SaveChangesAsync();

        var result = await _querySut.GetChoirUsersPagedAsync(
            new AdminChoirUsersPagedFilterViewModel { PageSize = memberCount });

        Assert.Multiple(() =>
        {
            Assert.That(result.TotalCount, Is.EqualTo(memberCount));
            Assert.That(result.Items, Has.Count.EqualTo(memberCount));
        });

        foreach (var item in result.Items)
        {
            Assert.That(expected.ContainsKey(item.Id), Is.True);
            var (userId, choirId, choirName) = expected[item.Id];

            Assert.Multiple(() =>
            {
                Assert.That(item.UserId, Is.EqualTo(userId));
                Assert.That(item.ChoirId, Is.EqualTo(choirId));
                Assert.That(item.ChoirName, Is.EqualTo(choirName));
                Assert.That(item.Firstname, Is.Not.Empty);
                Assert.That(item.Roles, Is.Not.Empty);
            });
        }

        Assert.That(
            result.Items.Where(i => i.ChoirId == choirA).All(i => i.PrimaryVoicePart == VoicePartEnum.Alto),
            Is.True,
            "Le pupitre est resolu chorale par chorale : aucun membre de la chorale B ne doit heriter du pupitre de la chorale A.");
    }

    [Test]
    public async Task GetChoirUsersPagedAsync_ChoirIdsFilter_OnlyMembersOfThoseChoirs()
    {
        var clientId = ChoraleDbContext.NewIdGuid();
        var choirA = CreateChoir(clientId, "Chorale A");
        var choirB = CreateChoir(clientId, "Chorale B");
        var userA = CreateUser("filtre-choirA", "Alice", "A", "alice-choira@test.com");
        var userB = CreateUser("filtre-choirB", "Bob", "B", "bob-choirb@test.com");
        AddMemberChoir(userA.Id, choirA);
        AddMemberChoir(userB.Id, choirB);
        await _context.SaveChangesAsync();

        var result = await _querySut.GetChoirUsersPagedAsync(
            new AdminChoirUsersPagedFilterViewModel { ChoirIds = [choirA], PageSize = 50 });

        Assert.Multiple(() =>
        {
            Assert.That(result.Items.Select(i => i.UserId), Does.Contain(userA.Id));
            Assert.That(result.Items.Select(i => i.UserId), Does.Not.Contain(userB.Id));
        });
    }

    [Test]
    public async Task GetChoirUsersPagedAsync_ChoirIdsWithNonExistentIdentifier_IgnoredWithoutException()
    {
        var clientId = ChoraleDbContext.NewIdGuid();
        var choirA = CreateChoir(clientId, "Chorale existante");
        var userA = CreateUser("filtre-choir-existant", "Alice", "A", "alice-existant@test.com");
        AddMemberChoir(userA.Id, choirA);
        await _context.SaveChangesAsync();

        PagedListViewModel<AdminChoirUserListItemViewModel>? result = null;
        Assert.DoesNotThrowAsync(async () => result = await _querySut.GetChoirUsersPagedAsync(
            new AdminChoirUsersPagedFilterViewModel { ChoirIds = [choirA, Guid.NewGuid()], PageSize = 50 }));

        Assert.That(result!.Items.Select(i => i.UserId), Is.EqualTo(new[] { userA.Id }));
    }

    [Test]
    public async Task GetChoirUsersPagedAsync_ChoirIdsEmptyListProvided_ReturnsNoResults()
    {
        // Decision explicite, meme regle que ClientService.GetPagedAsync/ClientIds : une liste
        // presente mais vide designe « ces identifiants precis », qui n'existent pas — zero
        // result, jamais un repli sur la liste complete.
        var clientId = ChoraleDbContext.NewIdGuid();
        var choirA = CreateChoir(clientId, "Chorale A");
        AddMemberChoir(CreateUser("filtre-choir-vide", "Alice", "A", "alice-vide@test.com").Id, choirA);
        await _context.SaveChangesAsync();

        var result = await _querySut.GetChoirUsersPagedAsync(
            new AdminChoirUsersPagedFilterViewModel { ChoirIds = [], PageSize = 50 });

        Assert.That(result.TotalCount, Is.EqualTo(0));
    }

    [Test]
    public void GetChoirUsersPagedAsync_ChoirIdsAboveTheLimit_IsExplicitlyRejected()
    {
        var tooMany = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToList();

        var exception = Assert.ThrowsAsync<ChoraleBackEnd.Common.Exceptions.CustomException>(() =>
            _querySut.GetChoirUsersPagedAsync(new AdminChoirUsersPagedFilterViewModel { ChoirIds = tooMany }));

        Assert.That(exception!.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task GetEventUsersPagedAsync_EventIdsFilter_OnlyParticipantsOfThoseEvents()
    {
        var eventA = CreateEvent(null, "Concert A", DateTime.UtcNow.AddDays(1));
        var eventB = CreateEvent(null, "Concert B", DateTime.UtcNow.AddDays(2));
        var userA = CreateUser("filtre-eventA", "Alice", "A", "alice-eventa@test.com");
        var userB = CreateUser("filtre-eventB", "Bob", "B", "bob-eventb@test.com");
        AddMemberEvent(userA.Id, eventA, null);
        AddMemberEvent(userB.Id, eventB, null);
        await _context.SaveChangesAsync();

        var result = await _querySut.GetEventUsersPagedAsync(
            new AdminEventUsersPagedFilterViewModel { EventIds = [eventA], PageSize = 50 });

        Assert.Multiple(() =>
        {
            Assert.That(result.Items.Select(i => i.UserId), Does.Contain(userA.Id));
            Assert.That(result.Items.Select(i => i.UserId), Does.Not.Contain(userB.Id));
        });
    }

    [Test]
    public async Task GetEventUsersPagedAsync_EventIdsWithNonExistentIdentifier_IgnoredWithoutException()
    {
        var eventA = CreateEvent(null, "Concert existant", DateTime.UtcNow.AddDays(1));
        var userA = CreateUser("filtre-event-existant", "Alice", "A", "alice-event-existant@test.com");
        AddMemberEvent(userA.Id, eventA, null);
        await _context.SaveChangesAsync();

        PagedListViewModel<AdminEventUserListItemViewModel>? result = null;
        Assert.DoesNotThrowAsync(async () => result = await _querySut.GetEventUsersPagedAsync(
            new AdminEventUsersPagedFilterViewModel { EventIds = [eventA, Guid.NewGuid()], PageSize = 50 }));

        Assert.That(result!.Items.Select(i => i.UserId), Is.EqualTo(new[] { userA.Id }));
    }

    [Test]
    public async Task GetEventUsersPagedAsync_EventIdsEmptyListProvided_ReturnsNoResults()
    {
        var eventA = CreateEvent(null, "Concert A", DateTime.UtcNow.AddDays(1));
        AddMemberEvent(CreateUser("filtre-event-vide", "Alice", "A", "alice-event-vide@test.com").Id, eventA, null);
        await _context.SaveChangesAsync();

        var result = await _querySut.GetEventUsersPagedAsync(
            new AdminEventUsersPagedFilterViewModel { EventIds = [], PageSize = 50 });

        Assert.That(result.TotalCount, Is.EqualTo(0));
    }

    [Test]
    public void GetEventUsersPagedAsync_EventIdsAboveTheLimit_IsExplicitlyRejected()
    {
        var tooMany = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToList();

        var exception = Assert.ThrowsAsync<ChoraleBackEnd.Common.Exceptions.CustomException>(() =>
            _querySut.GetEventUsersPagedAsync(new AdminEventUsersPagedFilterViewModel { EventIds = tooMany }));

        Assert.That(exception!.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.BadRequest));
    }

    private User CreateUser(
        string id, string firstname, string lastname, string email,
        bool isActive = true, bool isGuestAccount = false, bool emailConfirmed = true)
    {
        var user = new User
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Firstname = firstname,
            Lastname = lastname,
            IsActive = isActive,
            IsGuestAccount = isGuestAccount,
            EmailConfirmed = emailConfirmed,
            CreatedAt = DateTime.UtcNow
        };
        _context.Users.Add(user);
        return user;
    }

    private Guid CreateChoir(Guid clientId, string name)
    {
        EnsureClientExists(clientId);
        var choirId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir { Id = choirId, ClientId = clientId, Name = name, Status = ChoirStatusEnum.Published });
        return choirId;
    }

    private Guid CreateEvent(Guid? choirId, string title, DateTime startDate)
    {
        var eventId = ChoraleDbContext.NewIdGuid();
        var spaceClientId = ChoraleDbContext.NewIdGuid();
        EnsureClientExists(spaceClientId);
        _context.Spaces.Add(new Space { Id = eventId, SpaceType = SpaceTypeEnum.Event, ClientId = spaceClientId });
        _context.Events.Add(new Event
        {
            Id = eventId,
            Title = title,
            StartDate = startDate,
            Type = EventTypeEnum.Concert,
            Location = "Salle des fêtes",
            Status = EventStatusEnum.Published,
            ChoirId = choirId
        });
        return eventId;
    }

    private void CreateClient(Guid clientId, string name)
    {
        _context.Clients.Add(new Client
        {
            Id = clientId,
            Name = name,
            Status = ClientStatusEnum.Active
        });
    }

    /// <summary>
    /// Espace.HasQueryFilter(!e.Client.IsDeleted) exige une ligne Client reelle et Active
    /// pour tout ClientId rattache a un Espace : sans elle, la jointure requise ne
    /// remonte rien et la chorale/l'evenement entier devient invisible. Idempotent : ce
    /// fichier reutilise parfois le meme clientId pour plusieurs chorales d'un seul test
    /// (`UnePersonneDansDeuxChoralesEtUnEvenement...`).
    /// </summary>
    private void EnsureClientExists(Guid clientId)
    {
        if (_context.ChangeTracker.Entries<Client>().Any(e => e.Entity.Id == clientId))
            return;
        _context.Clients.Add(new Client { Id = clientId, Name = $"Client {clientId}", Status = ClientStatusEnum.Active });
    }

    private SpaceMember AddMemberChoir(string userId, Guid choirId, MemberStatusEnum status = MemberStatusEnum.Active)
    {
        var member = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = userId,
            SpaceId = choirId,
            ChoirId = choirId,
            Status = status
        };
        _context.SpaceMembers.Add(member);
        return member;
    }

    private SpaceMember AddMemberEvent(
        string userId, Guid eventId, Guid? owningChoirId,
        MemberStatusEnum status = MemberStatusEnum.Active, AttendanceEnum presence = AttendanceEnum.NoReply)
    {
        var member = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = userId,
            SpaceId = eventId,
            ChoirId = owningChoirId,
            Status = status,
            Presence = presence
        };
        _context.SpaceMembers.Add(member);

        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceMemberId = member.Id,
            Role = UserRoleEnum.Participant
        });

        return member;
    }
}
