using System.Net;
using System.Security.Claims;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ClientServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.OnboardingServices;
using ChoraleBackEnd.ViewModels.Events;

namespace ChoraleBackEnd.Test.Services.Onboarding;

/// <summary>
/// Code de rattachement d'espace (lot 6). Le test le plus important est
/// <see cref="ResolveActiveSpaceByCodeAsync_AllFailureReasons_SameCodeAndSameMessage"/> :
/// c'est lui qui garantit qu'aucune des sept raisons d'echec ne devient un oracle
/// d'enumeration.
/// </summary>
[TestFixture]
public sealed class JoinCodeTests
{
    private const string ManagerId = "responsable-1";

    private ChoraleDbContext _context = null!;
    private IServiceProvider _serviceProvider = null!;
    private JoinCodeService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        var mapper = new MapperConfiguration(
            cfg => cfg.AddMaps(typeof(EventViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, ManagerId)], "Test"))
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton(mapper);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<ILogService>(new LogService(httpContextAccessor));
        services.AddMemoryCache();
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>()
            .AddEntityFrameworkStores<ChoraleDbContext>();

        _serviceProvider = services.BuildServiceProvider();
        _sut = CreateServiceWithFreshCounter();

        _context.Users.Add(new User { Id = ManagerId, UserName = $"{ManagerId}@test.com", Email = $"{ManagerId}@test.com" });
        _context.SaveChanges();
    }

    /// <summary>
    /// Un nouveau <see cref="IMemoryCache"/> a chaque appel : le limiteur de tentatives (5 /
    /// 15 min, decision produit) partage sinon son compteur entre TOUS les cases d'un meme test
    /// parametre, qui deviendrait faussement bloque des le 6e cases d'echec deliberement
    /// provoque.
    /// </summary>
    private JoinCodeService CreateServiceWithFreshCounter()
    {
        var spaceRoleResolverService = new SpaceRoleResolverService(_context);
        var serviceLimitService = new ServiceLimitService(_serviceProvider);
        return new JoinCodeService(
            _serviceProvider, spaceRoleResolverService, serviceLimitService, new MemoryCache(new MemoryCacheOptions()));
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Test]
    public async Task PreviewAsync_ValidCode_ReturnsNameAndTypeOnly()
    {
        var (choirId, _) = await CreateChoirWithManagerAsync();
        var generated = await _sut.GenerateOrRotateAsync(choirId);

        var preview = await _sut.PreviewAsync(generated.Code);

        Assert.Multiple(() =>
        {
            Assert.That(preview.Name, Is.EqualTo("Choir Test"));
            Assert.That(preview.SpaceType, Is.EqualTo(SpaceTypeEnum.Choir));
        });
    }

    [Test]
    public async Task ResolveActiveSpaceByCodeAsync_AllFailureReasons_SameCodeAndSameMessage()
    {
        const string expectedMessage = "Code inconnu ou expiré.";

        foreach (var codeToTry in await BuildFailureCasesAsync())
        {
            // Service frais par cases : le limiteur de tentatives ne doit pas interferer avec
            // ce test, qui provoque deliberement plus d'echecs que le seuil.
            var service = CreateServiceWithFreshCounter();
            var exception = Assert.ThrowsAsync<CustomException>(
                () => service.ResolveActiveSpaceByCodeAsync(codeToTry));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), $"cases: {codeToTry}");
                Assert.That(exception.FrontMessage, Is.EqualTo(expectedMessage), $"cases: {codeToTry}");
            });
        }
    }

    [Test]
    public async Task GenerateOrRotateAsync_GeneratedCode_ContainsNoAmbiguousCharacter()
    {
        var (choirId, _) = await CreateChoirWithManagerAsync();

        var generated = await _sut.GenerateOrRotateAsync(choirId);

        var ambiguousCharacters = new[] { '0', 'O', '1', 'I', 'L' };
        Assert.That(generated.Code!.ToUpperInvariant().Any(c => ambiguousCharacters.Contains(c)), Is.False);
    }

    [Test]
    public async Task GenerateOrRotateAsync_Rotation_OldCodeImmediatelyStopsWorking()
    {
        var (choirId, _) = await CreateChoirWithManagerAsync();
        var previous = await _sut.GenerateOrRotateAsync(choirId);

        await _sut.GenerateOrRotateAsync(choirId);

        var exception = Assert.ThrowsAsync<CustomException>(
            () => _sut.ResolveActiveSpaceByCodeAsync(previous.Code));
        Assert.That(exception!.FrontMessage, Is.EqualTo("Code inconnu ou expiré."));
    }

    [Test]
    public async Task GenerateOrRotateAsync_OnlyOneActiveCodePerSpace()
    {
        var (choirId, _) = await CreateChoirWithManagerAsync();

        await _sut.GenerateOrRotateAsync(choirId);
        await _sut.GenerateOrRotateAsync(choirId);
        await _sut.GenerateOrRotateAsync(choirId);

        var activeCount = await _context.SpaceJoinCodes
            .CountAsync(c => c.SpaceId == choirId && c.IsActive);
        Assert.That(activeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task GenerateOrRotateAsync_DurationAbove90Days_IsRejected()
    {
        var (choirId, _) = await CreateChoirWithManagerAsync();

        var exception = Assert.ThrowsAsync<CustomException>(
            () => _sut.GenerateOrRotateAsync(choirId, durationDays: 91));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task GetActiveAsync_ExistingSpaceWithoutCode_IsInactiveByDefault()
    {
        var (choirId, _) = await CreateChoirWithManagerAsync();

        var state = await _sut.GetActiveAsync(choirId);

        Assert.That(state.IsActive, Is.False);
    }

    private async Task<List<string?>> BuildFailureCasesAsync()
    {
        var cases = new List<string?> { null, "XXXX-XXXX", "" };

        // Expire.
        var (expiredChoirId, _) = await CreateChoirWithManagerAsync("expiree-1");
        var expiredCode = await _sut.GenerateOrRotateAsync(expiredChoirId);
        var expiredRow = await _context.SpaceJoinCodes.FirstAsync(c => c.Code == expiredCode.Code);
        expiredRow.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        await _context.SaveChangesAsync();
        cases.Add(expiredCode.Code);

        // Revoque (desactive).
        var (revokedChoirId, _) = await CreateChoirWithManagerAsync("revoquee-1");
        var revokedCode = await _sut.GenerateOrRotateAsync(revokedChoirId);
        await _sut.DeactivateAsync(revokedChoirId);
        cases.Add(revokedCode.Code);

        // Space plein.
        var (fullChoirId, fullClientId) = await CreateChoirWithManagerAsync("pleine-1", limitMembers: 0);
        var fullCode = await _sut.GenerateOrRotateAsync(fullChoirId);
        cases.Add(fullCode.Code);

        // Client suspendu.
        var (suspendedChoirId, suspendedClientId) = await CreateChoirWithManagerAsync("suspendue-1");
        var suspendedCode = await _sut.GenerateOrRotateAsync(suspendedChoirId);
        var client = await _context.Clients.FirstAsync(c => c.Id == suspendedClientId);
        client.Status = ClientStatusEnum.Suspended;
        await _context.SaveChangesAsync();
        cases.Add(suspendedCode.Code);

        // Choir non Publie.
        var (draftChoirId, _) = await CreateChoirWithManagerAsync("brouillon-1");
        var draftCode = await _sut.GenerateOrRotateAsync(draftChoirId);
        var choir = await _context.Choirs.FirstAsync(c => c.Id == draftChoirId);
        choir.Status = ChoirStatusEnum.Draft;
        await _context.SaveChangesAsync();
        cases.Add(draftCode.Code);

        // Space supprime.
        var (deletedChoirId, _) = await CreateChoirWithManagerAsync("supprimee-1");
        var deletedCode = await _sut.GenerateOrRotateAsync(deletedChoirId);
        var deletedSpace = await _context.Spaces.FirstAsync(e => e.Id == deletedChoirId);
        deletedSpace.IsDeleted = true;
        await _context.SaveChangesAsync();
        cases.Add(deletedCode.Code);

        return cases;
    }

    private async Task<(Guid ChoirId, Guid ClientId)> CreateChoirWithManagerAsync(
        string suffix = "1", int limitMembers = 250)
    {
        var clientId = ChoraleDbContext.NewIdGuid();
        _context.Clients.Add(new Client
        {
            Id = clientId,
            Name = $"Client {suffix}",
            Status = ClientStatusEnum.Active,
            ChoirLimit = 5,
            MemberLimit = limitMembers,
            StorageQuotaBytes = 1_000_000,
            MaxFileSizeBytes = 100_000,
            IsDeleted = false
        });

        var choirId = ChoraleDbContext.NewIdGuid();
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = choirId,
            ClientId = clientId,
            Name = "Choir Test",
            Status = ChoirStatusEnum.Published,
            IsDeleted = false
        });
        _context.Spaces.Add(new Space
        {
            Id = choirId,
            SpaceType = SpaceTypeEnum.Choir,
            ClientId = clientId,
            IsDeleted = false
        });

        var member = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = ManagerId,
            ChoirId = choirId,
            SpaceId = choirId,
            Status = MemberStatusEnum.Active,
            IsDeleted = false
        };
        _context.SpaceMembers.Add(member);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceMemberId = member.Id,
            Role = UserRoleEnum.Manager
        });

        await _context.SaveChangesAsync();
        return (choirId, clientId);
    }
}
