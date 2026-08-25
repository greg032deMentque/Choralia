using System.Security.Claims;
using AutoMapper;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.UserServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using ChoraleBackEnd.ViewModels.Events;

namespace ChoraleBackEnd.Test.Services.Technical;

/// <summary>
/// Apercu de purge RGPD (<c>GetPurgeCandidatesAsync</c>) versus execution
/// (<c>PurgeInactiveGuestsAsync</c>) : le nombre annonce et le nombre reellement purge peuvent
/// diverger si un compte est revendique entre les deux — la purge recompte toujours au
/// moment de l'action, jamais a partir d'un apercu perime.
/// </summary>
[TestFixture]
public sealed class PurgeCandidatesTests
{
    private const string AdminUserId = "admin-1";
    private static readonly TimeSpan InactivityThreshold = TimeSpan.FromDays(365);

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
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task GetPurgeCandidatesAsync_NothingChangesBetweenPreviewAndPurge_BothCountsMatch()
    {
        await CreateInactiveGuestAsync("guest-1");
        await CreateInactiveGuestAsync("guest-2");

        var preview = await Sut().GetPurgeCandidatesAsync();
        var purgeResult = await Sut().PurgeInactiveGuestsAsync();

        Assert.That(purgeResult.AnonymizedCount, Is.EqualTo(preview.Count));
    }

    [Test]
    public async Task PurgeInactiveGuestsAsync_AccountClaimedBetweenPreviewAndExecution_NotPurged_ReturnsRealCount()
    {
        var claimedUser = await CreateInactiveGuestAsync("guest-claimedUser");
        await CreateInactiveGuestAsync("guest-toujours-candidat");

        var preview = await Sut().GetPurgeCandidatesAsync();
        Assert.That(preview.Count, Is.EqualTo(2));

        // Revendication survenant entre l'apercu et l'execution : le compte rejoint un espace.
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = claimedUser.Id,
            SpaceId = ChoraleDbContext.NewIdGuid(),
            Status = MemberStatusEnum.Active
        });
        await _context.SaveChangesAsync();

        var purgeResult = await Sut().PurgeInactiveGuestsAsync();

        Assert.That(purgeResult.AnonymizedCount, Is.EqualTo(1));
        Assert.That(purgeResult.AnonymizedCount, Is.Not.EqualTo(preview.Count));

        var reloadedClaimedUser = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == claimedUser.Id);
        Assert.That(reloadedClaimedUser.IsDeleted, Is.False);
    }

    [Test]
    public async Task GetPurgeCandidatesAsync_InvitedAccountAttachedToActiveSpace_NeverACandidate()
    {
        var attachedUser = await CreateInactiveGuestAsync("guest-attachedUser");
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = attachedUser.Id,
            SpaceId = ChoraleDbContext.NewIdGuid(),
            Status = MemberStatusEnum.Active
        });
        await _context.SaveChangesAsync();

        var preview = await Sut().GetPurgeCandidatesAsync();

        Assert.That(preview.Candidates.Select(c => c.UserId), Does.Not.Contain(attachedUser.Id));
    }

    [Test]
    public async Task PurgeInactiveGuestsAsync_RecordsAnAuditRowWithTheRealCount()
    {
        await CreateInactiveGuestAsync("guest-1");
        await CreateInactiveGuestAsync("guest-2");

        var result = await Sut().PurgeInactiveGuestsAsync();

        var aggregatedRow = await _context.AdminAuditLogs
            .SingleAsync(a => a.Action == "PurgeInactiveGuestsExecuted");

        Assert.That(aggregatedRow.Detail, Does.Contain($"AnonymizedCount={result.AnonymizedCount}"));
    }

    [Test]
    public async Task GetPurgeCandidatesAsync_ModifiesNoData()
    {
        await CreateInactiveGuestAsync("guest-1");

        await Sut().GetPurgeCandidatesAsync();

        var user = await _context.Users.AsNoTracking().SingleAsync(u => u.Id == "guest-1");
        var auditLogs = await _context.AdminAuditLogs.ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(user.IsDeleted, Is.False);
            Assert.That(user.Email, Does.Not.Contain("anonymise"));
            Assert.That(auditLogs, Is.Empty);
        });
    }

    private async Task<User> CreateInactiveGuestAsync(string id)
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
            EmailConfirmed = false,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            LastActive = DateTime.UtcNow - InactivityThreshold - TimeSpan.FromDays(10)
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    private GuestAccountLifecycleService Sut()
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, AdminUserId)], "Test"))
            }
        };

        var mapper = new MapperConfiguration(
            cfg => cfg.AddMaps(typeof(EventViewModel).Assembly),
            NullLoggerFactory.Instance).CreateMapper();

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
        return new GuestAccountLifecycleService(serviceProvider, auditLogService);
    }
}
