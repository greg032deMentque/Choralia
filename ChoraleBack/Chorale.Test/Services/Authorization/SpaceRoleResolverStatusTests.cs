using System.Security.Claims;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.UserServices;
using ChoraleBackEnd.Test.Fakes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels.Auth;

namespace ChoraleBackEnd.Test.Services.Authorization;

/// <summary>
/// Correction ciblee : <see cref="SpaceRoleResolverService"/> ne filtrait pas sur
/// <see cref="MemberStatusEnum.Active"/>, contrairement a <see cref="MembershipService"/>.
/// Un membre Invite, Inactive ou Archive conservait donc ses roles pour TOUTES les policies
/// scopees d'ecriture. Ce fichier fige le comportement corrige au niveau du resolveur.
/// </summary>
[TestFixture]
public sealed class SpaceRoleResolverStatusTests
{
    private const string Password = "MotDePasse!123";
    private const string UserId = "user-1";

    private ChoraleDbContext _context = null!;
    private SpaceRoleResolverService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);
        _sut = new SpaceRoleResolverService(_context);
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task Member_Active_with_Manager_role_resolves_that_role()
    {
        var choirId = await CreateChoirWithActiveClientAsync();
        await AddMemberAsync(UserId, choirId, MemberStatusEnum.Active, UserRoleEnum.Manager);

        var roles = await _sut.ResolveRolesAsync(UserId, [choirId]);

        Assert.That(roles.TryGetValue(choirId, out var spaceRoles), Is.True);
        Assert.That(spaceRoles, Does.Contain(UserRoleEnum.Manager));
    }

    [TestCase(MemberStatusEnum.Archived)]
    [TestCase(MemberStatusEnum.Inactive)]
    public async Task Member_NonActive_resolves_no_role_even_with_SpaceMemberRole(MemberStatusEnum status)
    {
        // C'est le defaut corrige : avant le filtre Statut, cette appartenance conservait son
        // role Responsable indefiniment, y compris archivee ou desactivee.
        var choirId = await CreateChoirWithActiveClientAsync();
        await AddMemberAsync(UserId, choirId, status, UserRoleEnum.Manager);

        var roles = await _sut.ResolveRolesAsync(UserId, [choirId]);

        Assert.That(roles, Is.Empty);
    }

    [Test]
    public async Task Manager_Archived_ChoirA_Active_ChoirB_keeps_rights_only_on_B()
    {
        var choirAId = await CreateChoirWithActiveClientAsync();
        var choirBId = await CreateChoirWithActiveClientAsync();
        await AddMemberAsync(UserId, choirAId, MemberStatusEnum.Archived, UserRoleEnum.Manager);
        await AddMemberAsync(UserId, choirBId, MemberStatusEnum.Active, UserRoleEnum.Manager);

        var roles = await _sut.ResolveRolesAsync(UserId, [choirAId, choirBId]);

        Assert.Multiple(() =>
        {
            Assert.That(roles.ContainsKey(choirAId), Is.False);
            Assert.That(roles.TryGetValue(choirBId, out var rolesB), Is.True);
            Assert.That(rolesB, Does.Contain(UserRoleEnum.Manager));
        });
    }

    [TestCase(ClientStatusEnum.Suspended)]
    [TestCase(ClientStatusEnum.Archived)]
    public async Task Member_Active_but_client_NonActive_still_resolves_no_role(ClientStatusEnum clientStatus)
    {
        // Non-regression : ce filtre existait deja avant la correction (`10-D23`) et doit
        // continuer a s'appliquer independamment du filtre Statut ajoute ici.
        var choirId = await CreateChoirWithActiveClientAsync(clientStatus);
        await AddMemberAsync(UserId, choirId, MemberStatusEnum.Active, UserRoleEnum.Manager);

        var roles = await _sut.ResolveRolesAsync(UserId, [choirId]);

        Assert.That(roles, Is.Empty);
    }

    [Test]
    public async Task Login_InvitedMember_IsPromotedToActiveAndRolesBecomeResolvable()
    {
        // Cas identifie lors de l'analyse d'impact (point 2 de la correction ciblee) : un
        // SpaceMember peut etre Invite avant la premiere connexion. Le filtre Statut ajoute
        // au resolveur ne doit pas casser ce parcours : AccountService promeut Invite -> Active
        // ET sauvegarde AVANT toute resolution de roles (generation du JWT), donc aucun
        // chemin legitime n'a besoin de resolve un role pour une appartenance encore Invite.
        var choirId = await CreateChoirWithActiveClientAsync();
        var spaceMemberId = await AddMemberAsync(UserId, choirId, MemberStatusEnum.Invited, UserRoleEnum.Manager);

        var (accountService, userManager) = CreateAccountService();
        var user = new User
        {
            Id = UserId,
            UserName = "invite@test.com",
            Email = "invite@test.com",
            IsActive = true,
            EmailConfirmed = true
        };
        var creation = await userManager.CreateAsync(user, Password);
        if (!creation.Succeeded)
            throw new InvalidOperationException(string.Join(";", creation.Errors.Select(e => e.Description)));

        var tokens = await accountService.Login(new LoginViewModel { Email = user.Email, Password = Password });

        Assert.That(tokens, Is.Not.Null);

        var memberInDb = await _context.SpaceMembers.FirstAsync(m => m.Id == spaceMemberId);
        Assert.That(memberInDb.Status, Is.EqualTo(MemberStatusEnum.Active));

        var roles = await _sut.ResolveRolesAsync(UserId, [choirId]);
        Assert.That(roles.TryGetValue(choirId, out var spaceRoles), Is.True);
        Assert.That(spaceRoles, Does.Contain(UserRoleEnum.Manager));
    }

    [Test]
    public async Task Space_deleted_no_longer_resolves_any_role()
    {
        // SpaceConfiguration ne filtre que `!Client.IsDeleted`, jamais l'espace lui-meme :
        // sans exclusion explicite ici, l'ex-Responsable d'une chorale supprimee gardait son
        // role effectif et passait toutes les policies scopees de cet espace.
        var choirId = await CreateChoirWithActiveClientAsync();
        await AddMemberAsync(UserId, choirId, MemberStatusEnum.Active, UserRoleEnum.Manager);

        var space = await _context.Spaces.FirstAsync(e => e.Id == choirId);
        space.IsDeleted = true;
        await _context.SaveChangesAsync();

        var roles = await _sut.ResolveRolesAsync(UserId, [choirId]);

        Assert.That(roles.ContainsKey(choirId), Is.False);
    }

    private async Task<Guid> CreateChoirWithActiveClientAsync(ClientStatusEnum clientStatus = ClientStatusEnum.Active)
    {
        var clientId = ChoraleDbContext.NewIdGuid();
        var choirId = ChoraleDbContext.NewIdGuid();
        _context.Clients.Add(new Client { Id = clientId, Name = $"Client {clientId}", Status = clientStatus });
        _context.Spaces.Add(new Space { Id = choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir { Id = choirId, ClientId = clientId, Name = $"Choir {choirId}", Status = ChoirStatusEnum.Published });
        await _context.SaveChangesAsync();
        return choirId;
    }

    private async Task<Guid> AddMemberAsync(
        string userId, Guid choirId, MemberStatusEnum status, UserRoleEnum? role = null)
    {
        var spaceMember = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = userId,
            ChoirId = choirId,
            SpaceId = choirId,
            Status = status
        };
        _context.SpaceMembers.Add(spaceMember);

        if (role.HasValue)
            _context.SpaceMemberRoles.Add(new SpaceMemberRole
            {
                Id = ChoraleDbContext.NewIdGuid(),
                SpaceMemberId = spaceMember.Id,
                Role = role.Value
            });

        await _context.SaveChangesAsync();
        return spaceMember.Id;
    }

    private (AccountService AccountService, UserManager<User> UserManager) CreateAccountService()
    {
        var mapper = new MapperConfiguration(cfg => { }, NullLoggerFactory.Instance).CreateMapper();

        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };

        var configuration = new ConfigurationManager();
        configuration["JWTToken:Secret"] =
            "test-secret-key-64-characters-minimum-for-hmacsha512-signing-xxxxxxxxxxxx";
        configuration["JWTToken:Issuer"] = "choir-test";
        configuration["JWTToken:Audience"] = "choir-test";
        configuration["JWTToken:ExpiresInMinutes"] = "60";

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton(mapper);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<ILogService>(new LogService(httpContextAccessor));
        services.AddSingleton<IEmailService>(new FakeEmailService());
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>()
            .AddEntityFrameworkStores<ChoraleDbContext>()
            .AddDefaultTokenProviders();

        var serviceProvider = services.BuildServiceProvider();

        var jwtGeneratorService = new JwtGeneratorService(serviceProvider);
        var userRoleDataService = new UserRoleDataService(serviceProvider);
        var spaceRoleResolverService = new SpaceRoleResolverService(_context);

        var sectionVoicePartLookupService = new SectionVoicePartLookupService(_context);

        var accountService = new AccountService(
            serviceProvider,
            jwtGeneratorService,
            userRoleDataService,
            spaceRoleResolverService,
            sectionVoicePartLookupService,
            serviceProvider.GetRequiredService<IEmailService>());

        return (accountService, serviceProvider.GetRequiredService<UserManager<User>>());
    }
}
