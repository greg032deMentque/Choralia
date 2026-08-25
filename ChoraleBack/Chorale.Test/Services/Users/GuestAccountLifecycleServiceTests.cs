using System.Security.Claims;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.Technical;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.UserServices;
using ChoraleBackEnd.ViewModels.Events;

namespace ChoraleBackEnd.Test.Services.Users;

[TestFixture]
public sealed class GuestAccountLifecycleServiceTests
{
    private const string AdminUserId = "admin-1";

    private ChoraleDbContext _context = null!;
    private GuestAccountLifecycleService _sut = null!;

    [SetUp]
    public void SetUp()
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
                    [new Claim(ClaimTypes.NameIdentifier, AdminUserId)], "Test"))
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
            .AddEntityFrameworkStores<ChoraleDbContext>();

        var serviceProvider = services.BuildServiceProvider();
        var auditLogService = new AuditLogService(serviceProvider);
        _sut = new GuestAccountLifecycleService(serviceProvider, auditLogService);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Test]
    public async Task AnonymizeUnclaimedGuestsForSpaceAsync_UnclaimedInviteWithoutOtherMembership_IsAnonymized()
    {
        var spaceId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = spaceId, SpaceType = SpaceTypeEnum.Event });
        var guest = await CreateInviteAsync("guest-1", emailConfirmed: false);
        await AddMembershipAsync(guest.Id, spaceId);

        var count = await _sut.AnonymizeUnclaimedGuestsForSpaceAsync(spaceId);
        await _context.SaveChangesAsync();

        Assert.That(count, Is.EqualTo(1));
        var reloaded = await _context.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == guest.Id);
        Assert.That(reloaded.IsDeleted, Is.True);
        Assert.That(reloaded.Email, Does.Contain("anonymise"));
    }

    [Test]
    public async Task AnonymizeUnclaimedGuestsForSpaceAsync_ClaimedInvite_IsKept()
    {
        var spaceId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = spaceId, SpaceType = SpaceTypeEnum.Event });
        var guest = await CreateInviteAsync("guest-2", emailConfirmed: true);
        await AddMembershipAsync(guest.Id, spaceId);

        var count = await _sut.AnonymizeUnclaimedGuestsForSpaceAsync(spaceId);

        Assert.That(count, Is.EqualTo(0));
        var reloaded = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == guest.Id);
        Assert.That(reloaded.IsDeleted, Is.False);
    }

    [Test]
    public async Task AnonymizeUnclaimedGuestsForSpaceAsync_OtherActiveMembership_IsKept()
    {
        var spaceId = ChoraleDbContext.NewIdGuid();
        var otherSpaceId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = spaceId, SpaceType = SpaceTypeEnum.Event });
        _context.Spaces.Add(new Space { Id = otherSpaceId, SpaceType = SpaceTypeEnum.Event });
        var guest = await CreateInviteAsync("guest-3", emailConfirmed: false);
        await AddMembershipAsync(guest.Id, spaceId);
        await AddMembershipAsync(guest.Id, otherSpaceId);

        var count = await _sut.AnonymizeUnclaimedGuestsForSpaceAsync(spaceId);

        Assert.That(count, Is.EqualTo(0));
        var reloaded = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == guest.Id);
        Assert.That(reloaded.IsDeleted, Is.False);
    }

    [Test]
    public async Task PurgeInactiveGuestsAsync_AccountInactiveForMoreThan12Months_IsAnonymized()
    {
        var inactive = await CreateInviteAsync("guest-inactif", emailConfirmed: false, lastActive: DateTime.UtcNow.AddDays(-400));

        var result = await _sut.PurgeInactiveGuestsAsync();

        Assert.That(result.AnonymizedCount, Is.EqualTo(1));
        var reloaded = await _context.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == inactive.Id);
        Assert.That(reloaded.IsDeleted, Is.True);
    }

    [Test]
    public async Task PurgeInactiveGuestsAsync_RecentlyActiveAccount_IsKept()
    {
        var active = await CreateInviteAsync("guest-actif", emailConfirmed: false, lastActive: DateTime.UtcNow.AddDays(-10));

        var result = await _sut.PurgeInactiveGuestsAsync();

        Assert.That(result.AnonymizedCount, Is.EqualTo(0));
        var reloaded = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == active.Id);
        Assert.That(reloaded.IsDeleted, Is.False);
    }

    [Test]
    public async Task PurgeInactiveGuestsAsync_NonInvitedAccount_IsNeverTouched()
    {
        var nonGuest = new User
        {
            Id = "user-standard",
            UserName = "standard@test.com",
            NormalizedUserName = "STANDARD@TEST.COM",
            Email = "standard@test.com",
            NormalizedEmail = "STANDARD@TEST.COM",
            IsGuestAccount = false,
            EmailConfirmed = false,
            CreatedAt = DateTime.UtcNow.AddDays(-1000)
        };
        _context.Users.Add(nonGuest);
        await _context.SaveChangesAsync();

        var result = await _sut.PurgeInactiveGuestsAsync();

        Assert.That(result.AnonymizedCount, Is.EqualTo(0));
        var reloaded = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == nonGuest.Id);
        Assert.That(reloaded.IsDeleted, Is.False);
    }

    private async Task<User> CreateInviteAsync(string id, bool emailConfirmed, DateTime? lastActive = null)
    {
        var email = $"{id}@test.com";
        var user = new User
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            IsGuestAccount = true,
            IsActive = true,
            EmailConfirmed = emailConfirmed,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            LastActive = lastActive
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    private async Task AddMembershipAsync(string userId, Guid spaceId)
    {
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = userId,
            SpaceId = spaceId,
            Status = MemberStatusEnum.Active
        });
        await _context.SaveChangesAsync();
    }
}
