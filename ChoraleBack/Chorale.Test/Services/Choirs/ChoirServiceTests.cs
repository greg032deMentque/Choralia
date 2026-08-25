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
using ChoraleBackEnd.ViewModels.Choirs;

namespace ChoraleBackEnd.Test.Services.Choirs;

[TestFixture]
public sealed class ChoirServiceTests
{
    private const string AdminUserId = "admin-1";
    private const string MemberUserId = "membre-1";
    private const string SectionLeaderUserId = "chef-pupitre-1";

    private ChoraleDbContext _context = null!;
    private ChoirService _sut = null!;
    private Guid _choirId;
    private Guid _sectionId;
    private Guid _memberChoirId;
    private Guid _clientId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _choirId = ChoraleDbContext.NewIdGuid();
        _sectionId = ChoraleDbContext.NewIdGuid();
        _clientId = ChoraleDbContext.NewIdGuid();

        // NormalizedEmail/NormalizedUserName requis pour que UserManager.FindByEmailAsync
        // resolve ces comptes (ChoirService.CreateAsync resout desormais le chef de chœur
        // par email) — piège deja documente dans ClientManagersTests.cs.
        _context.Users.Add(new User
        {
            Id = AdminUserId, UserName = "admin@test.com", Email = "admin@test.com",
            NormalizedEmail = "ADMIN@TEST.COM", NormalizedUserName = "ADMIN@TEST.COM",
            EmailConfirmed = true
        });
        _context.Users.Add(new User
        {
            Id = MemberUserId, UserName = "membre@test.com", Email = "membre@test.com",
            NormalizedEmail = "MEMBRE@TEST.COM", NormalizedUserName = "MEMBRE@TEST.COM"
        });
        _context.Users.Add(new User
        {
            Id = SectionLeaderUserId, UserName = "chef@test.com", Email = "chef@test.com",
            NormalizedEmail = "CHEF@TEST.COM", NormalizedUserName = "CHEF@TEST.COM"
        });
        _context.Clients.Add(new Client { Id = _clientId, Name = "Client Test", Status = ClientStatusEnum.Active });
        _context.Spaces.Add(new Space { Id = _choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = _clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir { Id = _choirId, ClientId = _clientId, Name = "Chorale Test", Status = ChoirStatusEnum.Published });
        _context.Sections.Add(new Section { Id = _sectionId, ChoirId = _choirId, VoicePart = VoicePartEnum.Soprano, SectionLeaderId = SectionLeaderUserId });

        var memberChoir = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ChoirId = _choirId,
            SpaceId = _choirId,
            UserId = MemberUserId,
            Status = MemberStatusEnum.Active
        };
        _memberChoirId = memberChoir.Id;
        _context.SpaceMembers.Add(memberChoir);

        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ChoirId = _choirId,
            SpaceId = _choirId,
            UserId = SectionLeaderUserId,
            Status = MemberStatusEnum.Active
        });

        // AdminUserId agit ici comme Responsable de la chorale : RemoveMemberAsync est
        // desormais reserve au Responsable (`10-D23`), l'Admin n'ayant plus de bypass
        // d'ecriture. Le nom de la constante designe l'acteur historique du test, pas son
        // role effectif.
        var memberManager = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ChoirId = _choirId,
            SpaceId = _choirId,
            UserId = AdminUserId,
            Status = MemberStatusEnum.Active
        };
        _context.SpaceMembers.Add(memberManager);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceMemberId = memberManager.Id,
            Role = UserRoleEnum.Manager
        });

        await _context.SaveChangesAsync();

        _sut = CreateServiceForUser(AdminUserId);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Test]
    public async Task RemoveMemberAsync_ExistingMember_SoftDeletesWithoutPhysicalDeletion()
    {
        await _sut.RemoveMemberAsync(_choirId, MemberUserId);

        var member = await _context.SpaceMembers
            .IgnoreQueryFilters()
            .FirstAsync(m => m.Id == _memberChoirId);
        Assert.That(member.IsDeleted, Is.True);
        Assert.That(member.Status, Is.EqualTo(MemberStatusEnum.Archived));

        var visibleByDefault = await _context.SpaceMembers
            .AnyAsync(m => m.Id == _memberChoirId);
        Assert.That(visibleByDefault, Is.False);
    }

    [Test]
    public async Task RemoveMemberAsync_ExistingMember_RecordsAnAuditEntry()
    {
        await _sut.RemoveMemberAsync(_choirId, MemberUserId);

        var auditEntry = await _context.AdminAuditLogs
            .AnyAsync(a => a.EntityId == _memberChoirId.ToString() && a.EntityType == nameof(SpaceMember));
        Assert.That(auditEntry, Is.True);
    }

    [Test]
    public async Task RemoveMemberAsync_ArchivedManagerOfThisChoir_ThrowsForbidden()
    {
        // Defaut corrige : EnsureManagerChoirAsync s'appuie exclusivement sur
        // SpaceRoleResolverService (aucun autre garde-fou ici, a la difference de
        // SongService qui passe aussi par IMembershipService). Avant le filtre Statut du
        // resolveur, un Responsable archive conservait ce role indefiniment et pouvait donc
        // toujours retirer des membres de la chorale qui l'a archive.
        var manager = await _context.SpaceMembers
            .FirstAsync(m => m.UserId == AdminUserId && m.ChoirId == _choirId);
        manager.Status = MemberStatusEnum.Archived;
        await _context.SaveChangesAsync();

        var exception = Assert.ThrowsAsync<CustomException>(
            () => _sut.RemoveMemberAsync(_choirId, MemberUserId));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public void RemoveMemberAsync_MemberIsSectionLeader_ThrowsBadRequest()
    {
        var exception = Assert.ThrowsAsync<CustomException>(
            () => _sut.RemoveMemberAsync(_choirId, SectionLeaderUserId));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task RemoveMemberAsync_MemberIsSectionLeader_DoesNotModifyTheMember()
    {
        Assert.ThrowsAsync<CustomException>(
            () => _sut.RemoveMemberAsync(_choirId, SectionLeaderUserId));

        var member = await _context.SpaceMembers
            .FirstAsync(m => m.ChoirId == _choirId && m.UserId == SectionLeaderUserId);
        Assert.That(member.IsDeleted, Is.False);
    }

    [Test]
    public async Task RemoveMemberAsync_LastManager_ThrowsConflict409()
    {
        // AdminUserId est l'unique Manager de _choirId dans cette fixture (voir SetUp) : son
        // propre retrait recreerait l'impasse "chorale sans chef de chœur" corrigee par ce lot.
        var exception = Assert.ThrowsAsync<CustomException>(
            () => _sut.RemoveMemberAsync(_choirId, AdminUserId));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        var member = await _context.SpaceMembers
            .FirstAsync(m => m.ChoirId == _choirId && m.UserId == AdminUserId);
        Assert.That(member.IsDeleted, Is.False);
    }

    [Test]
    public async Task CreateAsync_EmailNotConfirmed_ThrowsForbidden()
    {
        // Regle du lot 6 : un compte non verifie ne produit ni chorale, ni pupitres, ni
        // quota. Le reste du modele est volontairement valide â€” createur ResponsableClient
        // du client vise, plafond permissif (FakeServiceLimitService) — pour que seul
        // EmailConfirmed puisse expliquer le refus. Sans ce test, retirer le controle ne
        // ferait echouer aucune suite : la regle etait jusqu'ici totalement decouverte.
        var creator = await _context.Users.FirstAsync(u => u.Id == AdminUserId);
        creator.EmailConfirmed = false;

        _context.ClientMembers.Add(new ClientMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ClientId = _clientId,
            UserId = AdminUserId,
            Role = UserRoleEnum.ClientManager
        });
        await _context.SaveChangesAsync();

        var model = new ChoirViewModel { Name = "Nouvelle Chorale", ClientId = _clientId };

        var exception = Assert.ThrowsAsync<CustomException>(() => _sut.CreateAsync(model));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(exception.Message, Does.Contain("adresse email"));
        Assert.That(await _context.Choirs.AnyAsync(c => c.Name == "Nouvelle Chorale"), Is.False);
    }

    [Test]
    public async Task CreateAsync_BootstrapsFirstChoirManager_CreatesActiveSpaceMemberAndManagerRole()
    {
        // Defaut corrige par ce lot : sans amorcage, une chorale creee par un ResponsableClient
        // n'avait ni SpaceMember ni SpaceMemberRole — aucune des 3 portes d'entree existantes
        // (ChoirMembersController, ChoirController.AddMember, SpaceJoinCodeController) ne
        // pouvait alors la peupler, puisque toutes exigent une appartenance active prealable.
        _context.ClientMembers.Add(new ClientMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ClientId = _clientId,
            UserId = AdminUserId, Role = UserRoleEnum.ClientManager
        });
        await _context.SaveChangesAsync();

        var model = new ChoirViewModel
        {
            Name = "Chorale Avec Chef", ClientId = _clientId, ChoirMasterEmail = "membre@test.com"
        };

        var created = await _sut.CreateAsync(model);

        var member = await _context.SpaceMembers
            .FirstAsync(m => m.ChoirId == created.Id && m.UserId == MemberUserId);
        Assert.That(member.Status, Is.EqualTo(MemberStatusEnum.Active));

        var role = await _context.SpaceMemberRoles
            .AnyAsync(r => r.SpaceMemberId == member.Id && r.Role == UserRoleEnum.Manager);
        Assert.That(role, Is.True);

        // Le ResponsableClient createur (AdminUserId) ne devient jamais lui-meme membre de la
        // chorale qu'il cree (`10-D23`) : seul le compte designe en ChoirMasterEmail l'est.
        var creatorIsMember = await _context.SpaceMembers
            .AnyAsync(m => m.ChoirId == created.Id && m.UserId == AdminUserId);
        Assert.That(creatorIsMember, Is.False);
    }

    [Test]
    public async Task CreateAsync_MissingChoirMasterEmail_ThrowsBadRequest()
    {
        _context.ClientMembers.Add(new ClientMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ClientId = _clientId,
            UserId = AdminUserId, Role = UserRoleEnum.ClientManager
        });
        await _context.SaveChangesAsync();

        var model = new ChoirViewModel { Name = "Chorale Sans Chef", ClientId = _clientId };

        var exception = Assert.ThrowsAsync<CustomException>(() => _sut.CreateAsync(model));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await _context.Choirs.AnyAsync(c => c.Name == "Chorale Sans Chef"), Is.False);
    }

    [Test]
    public async Task CreateAsync_UnknownChoirMasterEmail_ThrowsNotFound()
    {
        _context.ClientMembers.Add(new ClientMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ClientId = _clientId,
            UserId = AdminUserId, Role = UserRoleEnum.ClientManager
        });
        await _context.SaveChangesAsync();

        var model = new ChoirViewModel
        {
            Name = "Chorale Chef Inconnu", ClientId = _clientId, ChoirMasterEmail = "inconnu@test.com"
        };

        Assert.ThrowsAsync<KeyNotFoundException>(() => _sut.CreateAsync(model));
    }

    [Test]
    public async Task CreateAsync_MemberLimitReached_ThrowsConflict409()
    {
        const string newUserId = "nouveau-1";
        _context.Users.Add(new User
        {
            Id = newUserId, UserName = "nouveau@test.com", Email = "nouveau@test.com",
            NormalizedEmail = "NOUVEAU@TEST.COM", NormalizedUserName = "NOUVEAU@TEST.COM"
        });
        _context.ClientMembers.Add(new ClientMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ClientId = _clientId,
            UserId = AdminUserId, Role = UserRoleEnum.ClientManager
        });

        var client = await _context.Clients.FirstAsync(c => c.Id == _clientId);
        client.ChoirLimit = 10;
        // 3 membres distincts deja actifs sur _choirId (Membre, ChefDePupitre, Admin-manager) :
        // le plafond est deja atteint, une 4e personne distincte doit etre refusee.
        client.MemberLimit = 3;
        await _context.SaveChangesAsync();

        var sut = CreateServiceForUserWithRealLimits(AdminUserId);
        var model = new ChoirViewModel
        {
            Name = "Chorale Plafond Membres", ClientId = _clientId, ChoirMasterEmail = "nouveau@test.com"
        };

        var exception = Assert.ThrowsAsync<CustomException>(() => sut.CreateAsync(model));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await _context.Choirs.AnyAsync(c => c.Name == "Chorale Plafond Membres"), Is.False);
    }

    private ChoirService CreateServiceForUser(string userId)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId)], "Test"))
            }
        };

        var mapper = new MapperConfiguration(cfg => cfg.AddMaps(typeof(ChoirViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

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
        var auditLogService = new AuditLogService(serviceProvider);
        return new ChoirService(
            serviceProvider,
            auditLogService,
            new FakeServiceLimitService(),
            new MembershipService(serviceProvider),
            new ClientRoleResolverService(_context),
            new SpaceRoleResolverService(_context),
            new SectionService(serviceProvider));
    }

    /// <summary>
    /// Meme construction que <see cref="CreateServiceForUser"/>, mais avec le VRAI
    /// <see cref="ServiceLimitService"/> plutot que <see cref="FakeServiceLimitService"/> —
    /// necessaire pour les tests qui verifient un refus de plafond (choir/membre).
    /// </summary>
    private ChoirService CreateServiceForUserWithRealLimits(string userId)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId)], "Test"))
            }
        };

        var mapper = new MapperConfiguration(cfg => cfg.AddMaps(typeof(ChoirViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

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
        var auditLogService = new AuditLogService(serviceProvider);
        return new ChoirService(
            serviceProvider,
            auditLogService,
            new ServiceLimitService(serviceProvider),
            new MembershipService(serviceProvider),
            new ClientRoleResolverService(_context),
            new SpaceRoleResolverService(_context),
            new SectionService(serviceProvider));
    }
}
