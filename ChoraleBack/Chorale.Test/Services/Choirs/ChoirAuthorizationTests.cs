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
using NUnit.Framework;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels.Choirs;

namespace ChoraleBackEnd.Test.Services.Choirs;

/// <summary>
/// Autorisation de creation et de management des membres d'une chorale (`10-D23`). Depuis ce
/// lot, ce n'est plus l'administration generale qui cree les chorales : c'est le
/// ResponsableClient, scope a son propre client. La verification vit dans le SERVICE, pas
/// seulement dans la policy HTTP — plus large par construction (voir
/// <c>AuthorizationPolicies.AdminOrClientManager</c>) — pour qu'un appel direct ne la
/// contourne pas.
/// </summary>
[TestFixture]
public sealed class ChoirAuthorizationTests
{
    private const string ManagerClientUserId = "responsable-client-1";
    private const string ManagerChoirUserId = "responsable-choir-1";
    private const string SectionLeaderUserId = "chef-section-1";
    private const string OtherMemberUserId = "autre-membre-1";

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

        // NormalizedEmail/NormalizedUserName requis pour que UserManager.FindByEmailAsync
        // resolve ces comptes (ChoirService.CreateAsync et ChoirMasterService.AssignAsync
        // resolvent desormais le chef de chœur par email) — meme piège deja documente dans
        // ClientManagersTests.cs sur un utilisateur seede directement en base.
        _context.Users.Add(new User
        {
            Id = ManagerClientUserId, UserName = "rc@t.com", Email = "rc@t.com",
            NormalizedEmail = "RC@T.COM", NormalizedUserName = "RC@T.COM", EmailConfirmed = true
        });
        _context.Users.Add(new User
        {
            Id = ManagerChoirUserId, UserName = "resp@t.com", Email = "resp@t.com",
            NormalizedEmail = "RESP@T.COM", NormalizedUserName = "RESP@T.COM", EmailConfirmed = true
        });
        _context.Users.Add(new User
        {
            Id = SectionLeaderUserId, UserName = "chef@t.com", Email = "chef@t.com",
            NormalizedEmail = "CHEF@T.COM", NormalizedUserName = "CHEF@T.COM", EmailConfirmed = true
        });
        _context.Users.Add(new User
        {
            Id = OtherMemberUserId, UserName = "autre@t.com", Email = "autre@t.com",
            NormalizedEmail = "AUTRE@T.COM", NormalizedUserName = "AUTRE@T.COM", EmailConfirmed = true
        });

        _context.Clients.Add(new Client
        {
            Id = _clientId, Name = "Client", Status = ClientStatusEnum.Active,
            ChoirLimit = 10, MemberLimit = 100,
            StorageQuotaBytes = 1_000_000, MaxFileSizeBytes = 100_000
        });
        _context.ClientMembers.Add(new ClientMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ClientId = _clientId,
            UserId = ManagerClientUserId, Role = UserRoleEnum.ClientManager
        });

        _context.Spaces.Add(new Space { Id = _choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = _clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = _choirId, ClientId = _clientId, Name = "Choir", Status = ChoirStatusEnum.Published
        });

        var memberManager = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId, SpaceId = _choirId,
            UserId = ManagerChoirUserId, Status = MemberStatusEnum.Active
        };
        _context.SpaceMembers.Add(memberManager);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(), SpaceMemberId = memberManager.Id, Role = UserRoleEnum.Manager
        });

        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId, SpaceId = _choirId,
            UserId = SectionLeaderUserId, Status = MemberStatusEnum.Active
        });
        _context.Sections.Add(new Section
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId, VoicePart = VoicePartEnum.Soprano,
            SectionLeaderId = SectionLeaderUserId
        });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task CreateAsync_ClientManager_CreatesChoirInOwnClient()
    {
        var sut = CreateService(ManagerClientUserId);

        var result = await sut.CreateAsync(new ChoirViewModel
        {
            ClientId = _clientId, Name = "Nouvelle Choir", ChoirMasterEmail = "rc@t.com"
        });

        Assert.That(result.ClientId, Is.EqualTo(_clientId));
    }

    [Test]
    public async Task CreateAsync_InAnotherClient_ThrowsForbidden()
    {
        var otherClientId = ChoraleDbContext.NewIdGuid();
        _context.Clients.Add(new Client
        {
            Id = otherClientId, Name = "Autre Client", Status = ClientStatusEnum.Active,
            ChoirLimit = 10, MemberLimit = 100
        });
        await _context.SaveChangesAsync();

        var sut = CreateService(ManagerClientUserId);

        var exception = Assert.ThrowsAsync<CustomException>(
            () => sut.CreateAsync(new ChoirViewModel
            {
                ClientId = otherClientId, Name = "Choir Volee", ChoirMasterEmail = "rc@t.com"
            }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task CreateAsync_AboveChoirLimitCap_ThrowsConflict409NamingTheLimit()
    {
        var client = await _context.Clients.FirstAsync(c => c.Id == _clientId);
        client.ChoirLimit = 1;
        await _context.SaveChangesAsync();

        var sut = CreateService(ManagerClientUserId);

        var exception = Assert.ThrowsAsync<CustomException>(
            () => sut.CreateAsync(new ChoirViewModel
            {
                ClientId = _clientId, Name = "Choir En Trop", ChoirMasterEmail = "rc@t.com"
            }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(exception.Message, Does.Contain("1"));
    }

    [Test]
    public async Task CreateAsync_InSuspendedClient_ThrowsForbidden()
    {
        var client = await _context.Clients.FirstAsync(c => c.Id == _clientId);
        client.Status = ClientStatusEnum.Suspended;
        await _context.SaveChangesAsync();

        var sut = CreateService(ManagerClientUserId);

        var exception = Assert.ThrowsAsync<CustomException>(
            () => sut.CreateAsync(new ChoirViewModel
            {
                ClientId = _clientId, Name = "Choir Suspendue", ChoirMasterEmail = "rc@t.com"
            }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AddMemberAsync_ChoirManager_IsAllowed()
    {
        var sut = CreateService(ManagerChoirUserId);

        Assert.DoesNotThrowAsync(() => sut.AddMemberAsync(_choirId, OtherMemberUserId));
    }

    [Test]
    public void AddMemberAsync_SectionLeader_ThrowsForbidden()
    {
        var sut = CreateService(SectionLeaderUserId);

        var exception = Assert.ThrowsAsync<CustomException>(
            () => sut.AddMemberAsync(_choirId, OtherMemberUserId));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task DeleteAsync_ClientManager_DeletesChoirFromOwnClient()
    {
        var sut = CreateService(ManagerClientUserId);

        await sut.DeleteAsync(_choirId);

        var choir = await _context.Choirs.IgnoreQueryFilters().FirstAsync(c => c.Id == _choirId);
        Assert.That(choir.IsDeleted, Is.True, "Soft-delete attendu, jamais de suppression physique.");
    }

    [Test]
    public async Task DeleteAsync_ChoirOfAnotherClient_ThrowsForbidden()
    {
        var (otherClientId, otherChoirId) = await AddOtherClientAndChoirAsync();

        var sut = CreateService(ManagerClientUserId);

        var exception = Assert.ThrowsAsync<CustomException>(() => sut.DeleteAsync(otherChoirId));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        var choir = await _context.Choirs.FirstAsync(c => c.Id == otherChoirId);
        Assert.That(choir.IsDeleted, Is.False);
        _ = otherClientId;
    }

    [Test]
    public async Task UpdateAsync_ChoirOfAnotherClientWithOwnClientIdInBody_ThrowsForbidden()
    {
        // Le point de securite verifie ici : le client s'evalue depuis la chorale VISEE
        // (chargee en base), jamais depuis ce que le corps de la requete pretend. Sans
        // cette garantie, ResponsableClientUserId pourrait update la chorale d'un autre
        // client en falsifiant simplement ClientId dans le corps.
        var (_, otherChoirId) = await AddOtherClientAndChoirAsync();

        var sut = CreateService(ManagerClientUserId);

        var exception = Assert.ThrowsAsync<CustomException>(() => sut.UpdateAsync(new ChoirViewModel
        {
            Id = otherChoirId,
            ClientId = _clientId, // son PROPRE client, declare frauduleusement
            Name = "Choir Renommee Frauduleusement"
        }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        var choir = await _context.Choirs.FirstAsync(c => c.Id == otherChoirId);
        Assert.That(choir.Name, Is.Not.EqualTo("Choir Renommee Frauduleusement"));
    }

    [Test]
    public async Task DeleteAsync_Admin_DeletesAnyChoir()
    {
        var sut = CreateService(userId: "admin-1", isAdmin: true);

        await sut.DeleteAsync(_choirId);

        var choir = await _context.Choirs.IgnoreQueryFilters().FirstAsync(c => c.Id == _choirId);
        Assert.That(choir.IsDeleted, Is.True);
    }

    [Test]
    public async Task DeleteAsync_FreesUpClientChoirLimitCap()
    {
        var client = await _context.Clients.FirstAsync(c => c.Id == _clientId);
        client.ChoirLimit = 1;
        await _context.SaveChangesAsync();

        var sut = CreateService(ManagerClientUserId);

        // Le seul emplacement du plafond est deja occupe par _choirId : une creation
        // supplementaire est refusee tant qu'il n'est pas libere.
        var exception = Assert.ThrowsAsync<CustomException>(
            () => sut.CreateAsync(new ChoirViewModel
            {
                ClientId = _clientId, Name = "Refusee Avant Suppression", ChoirMasterEmail = "rc@t.com"
            }));
        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        await sut.DeleteAsync(_choirId);

        // Chorale soft-deleted : exclue par le HasQueryFilter de ChoirConfiguration, donc
        // du decompte du plafond. La creation redevient possible.
        Assert.DoesNotThrowAsync(
            () => sut.CreateAsync(new ChoirViewModel
            {
                ClientId = _clientId, Name = "Autorisee Apres Suppression", ChoirMasterEmail = "rc@t.com"
            }));
    }

    private async Task<(Guid ClientId, Guid ChoirId)> AddOtherClientAndChoirAsync()
    {
        var otherClientId = ChoraleDbContext.NewIdGuid();
        var otherChoirId = ChoraleDbContext.NewIdGuid();

        _context.Clients.Add(new Client
        {
            Id = otherClientId, Name = "Autre Client", Status = ClientStatusEnum.Active,
            ChoirLimit = 10, MemberLimit = 100
        });
        _context.Spaces.Add(new Space { Id = otherChoirId, SpaceType = SpaceTypeEnum.Choir, ClientId = otherClientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = otherChoirId, ClientId = otherClientId, Name = "Choir De L'Autre Client", Status = ChoirStatusEnum.Published
        });
        await _context.SaveChangesAsync();

        return (otherClientId, otherChoirId);
    }

    private ChoirService CreateService(string userId, bool isAdmin = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        if (isAdmin)
            claims.Add(new Claim(ClaimTypes.Role, UserRoleEnum.Admin.ToString()));

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
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
        services.AddIdentity<User, IdentityRole>().AddEntityFrameworkStores<ChoraleDbContext>();

        var serviceProvider = services.BuildServiceProvider();
        var auditLogService = new AuditLogService(serviceProvider);
        var serviceLimitService = new ServiceLimitService(serviceProvider);
        var membershipService = new MembershipService(serviceProvider);
        var clientRoleResolverService = new ClientRoleResolverService(_context);
        var spaceRoleResolverService = new SpaceRoleResolverService(_context);

        return new ChoirService(
            serviceProvider, auditLogService, serviceLimitService, membershipService,
            clientRoleResolverService, spaceRoleResolverService, new SectionService(serviceProvider));
    }
}
