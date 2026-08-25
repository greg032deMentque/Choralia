using System.Net;
using System.Security.Claims;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Test.Fakes;
using ChoraleBackEnd.ViewModels.Onboarding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.OnboardingServices;
using ChoraleBackEnd.ViewModels.Events;
using ChoraleBackEnd.ViewModels.Choirs;

namespace ChoraleBackEnd.Test.Services.Onboarding;

/// <summary>
/// Creation d'un premier espace en auto-service (lot 6) : depuis le chantier
/// d'administration, l'administrateur ne cree plus de chorale — ce service (et lui seul,
/// desormais) debloque l'amorcage.
/// </summary>
[TestFixture]
public sealed class AutoCreationServiceTests
{
    private ChoraleDbContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Test]
    public async Task CreateChoirAsync_WithoutStructure_CreatesASilentClientAndTheCreatorManager()
    {
        var creatorId = await CreateUserAsync("createur-1@test.com", emailConfirmed: true);
        var service = CreateService(creatorId);

        var choir = await service.CreateChoirAsync(new CreateChoirViewModel { Name = "Chorale des Alpes" });

        var space = await _context.Spaces.AsNoTracking().SingleAsync(e => e.Id == choir.Id);
        var client = await _context.Clients.AsNoTracking().SingleAsync(c => c.Id == space.ClientId);
        var sections = await _context.Sections.AsNoTracking().Where(p => p.ChoirId == choir.Id).ToListAsync();
        var clientMember = await _context.ClientMembers.AsNoTracking()
            .SingleAsync(m => m.UserId == creatorId && m.ClientId == client.Id);
        var spaceMember = await _context.SpaceMembers.AsNoTracking()
            .SingleAsync(m => m.UserId == creatorId && m.SpaceId == choir.Id);
        var roleManager = await _context.SpaceMemberRoles.AsNoTracking()
            .AnyAsync(r => r.SpaceMemberId == spaceMember.Id && r.Role == UserRoleEnum.Manager);
        var codeActive = await _context.SpaceJoinCodes.AsNoTracking()
            .SingleOrDefaultAsync(c => c.SpaceId == choir.Id && c.IsActive);

        Assert.Multiple(() =>
        {
            Assert.That(client.Name, Is.EqualTo("Chorale des Alpes"));
            Assert.That(clientMember.Role, Is.EqualTo(UserRoleEnum.ClientManager));
            Assert.That(roleManager, Is.True);
            Assert.That(sections, Has.Count.EqualTo(4));
            Assert.That(choir.Status, Is.EqualTo(ChoirStatusEnum.Published));
            Assert.That(codeActive, Is.Not.Null);
        });
    }

    [Test]
    public async Task CreateChoirAsync_WithNamedStructure_ClientCarriesThatName()
    {
        var creatorId = await CreateUserAsync("createur-2@test.com", emailConfirmed: true);
        var service = CreateService(creatorId);

        var choir = await service.CreateChoirAsync(
            new CreateChoirViewModel { Name = "Choir Saint-Jean", Structure = "Paroisse Saint-Jean" });

        var space = await _context.Spaces.AsNoTracking().SingleAsync(e => e.Id == choir.Id);
        var client = await _context.Clients.AsNoTracking().SingleAsync(c => c.Id == space.ClientId);

        Assert.That(client.Name, Is.EqualTo("Paroisse Saint-Jean"));
    }

    [Test]
    public async Task CreateChoirAsync_UnverifiedAccount_RejectedWithoutPartialEntity()
    {
        var creatorId = await CreateUserAsync("non-verifie@test.com", emailConfirmed: false);
        var service = CreateService(creatorId);

        var exception = Assert.ThrowsAsync<CustomException>(
            () => service.CreateChoirAsync(new CreateChoirViewModel { Name = "Chorale Refusée" }));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(_context.Clients.Any(), Is.False);
            Assert.That(_context.Spaces.Any(), Is.False);
            Assert.That(_context.Choirs.Any(), Is.False);
            Assert.That(_context.Sections.Any(), Is.False);
        });
    }

    [Test]
    public async Task CreateChoirAsync_TwoCreatorsSameStructureName_BothSucceed()
    {
        var creator1 = await CreateUserAsync("createur-a@test.com", emailConfirmed: true);
        var creator2 = await CreateUserAsync("createur-b@test.com", emailConfirmed: true);

        var choir1 = await CreateService(creator1).CreateChoirAsync(
            new CreateChoirViewModel { Name = "Choir A", Structure = "Ecole de Musique" });
        var choir2 = await CreateService(creator2).CreateChoirAsync(
            new CreateChoirViewModel { Name = "Choir B", Structure = "Ecole de Musique" });

        var space1 = await _context.Spaces.AsNoTracking().SingleAsync(e => e.Id == choir1.Id);
        var space2 = await _context.Spaces.AsNoTracking().SingleAsync(e => e.Id == choir2.Id);

        Assert.That(space1.ClientId, Is.Not.EqualTo(space2.ClientId));
    }

    [Test]
    public async Task CreateEventAsync_Standalone_SpaceClientIdIsNeverEmpty()
    {
        var creatorId = await CreateUserAsync("organisateur-1@test.com", emailConfirmed: true);
        var service = CreateService(creatorId);

        var evt = await service.CreateEventAsync(new CreateEventViewModel
        {
            Title = "Concert de Noël",
            StartDate = DateTime.UtcNow.AddDays(30),
            Type = EventTypeEnum.Concert,
            Location = "Église Saint-Jean"
        });

        var space = await _context.Spaces.AsNoTracking().SingleAsync(e => e.Id == evt.Id);
        Assert.That(space.ClientId, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public async Task ChoirControllerCreate_UnverifiedAccount_IsRejected()
    {
        var creatorId = await CreateUserAsync("cree-direct@test.com", emailConfirmed: false);
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, creatorId)], "Test"))
            }
        };

        var mapper = new MapperConfiguration(
            cfg => cfg.AddMaps(typeof(EventViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

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
        var choirService = new ChoirService(
            serviceProvider,
            new AuditLogService(serviceProvider),
            new FakeServiceLimitService(),
            new MembershipService(serviceProvider),
            new ClientRoleResolverService(_context),
            new SpaceRoleResolverService(_context),
            new SectionService(serviceProvider));

        var exception = Assert.ThrowsAsync<CustomException>(() => choirService.CreateAsync(
            new ChoirViewModel { Name = "Choir Directe", ClientId = Guid.NewGuid() }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    private async Task<string> CreateUserAsync(string email, bool emailConfirmed)
    {
        var id = Guid.NewGuid().ToString();
        _context.Users.Add(new User
        {
            Id = id,
            UserName = email,
            Email = email,
            IsActive = true,
            EmailConfirmed = emailConfirmed
        });
        await _context.SaveChangesAsync();
        return id;
    }

    private OnboardingCreationService CreateService(string userId)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test"))
            }
        };

        var mapper = new MapperConfiguration(
            cfg => cfg.AddMaps(typeof(EventViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

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
        var spaceRoleResolverService = new SpaceRoleResolverService(_context);
        var serviceLimitService = new ServiceLimitService(serviceProvider);
        var joinCodeService = new JoinCodeService(
            serviceProvider, spaceRoleResolverService, serviceLimitService, new MemoryCache(new MemoryCacheOptions()));

        return new OnboardingCreationService(serviceProvider, joinCodeService);
    }
}
