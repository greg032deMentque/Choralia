using ChoraleBackEnd.ViewModels.AdminUsers;
using System.Net;
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
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.Services.UserServices;
using ChoraleBackEnd.Test.Fakes;
using ChoraleBackEnd.Test.TestSupport;
using ChoraleBackEnd.ViewModels.Events;

namespace ChoraleBackEnd.Test.Services.Users;

[TestFixture]
public sealed class AdminUserActionsTests
{
    private const string CurrentAdminId = "admin-courant";

    private ChoraleDbContext _context = null!;
    private AdminUserService _sut = null!;
    private AdminUserQueryService _querySut = null!;
    private UserManager<User> _userManager = null!;
    private FakeAccountService _fakeAccountService = null!;
    private FakeEmailService _fakeEmailService = null!;

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
                    [new Claim(ClaimTypes.NameIdentifier, CurrentAdminId)], "Test"))
            }
        };

        var configuration = new ConfigurationManager();
        configuration["Frontend:BaseUrl"] = "http://localhost:4200";

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton(mapper);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<ILogService>(new LogService(httpContextAccessor));
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>()
            .AddEntityFrameworkStores<ChoraleDbContext>()
            .AddDefaultTokenProviders()
            .AddInvitationTokenProvider();

        var serviceProvider = services.BuildServiceProvider();
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        _userManager = serviceProvider.GetRequiredService<UserManager<User>>();
        await roleManager.CreateAsync(new IdentityRole(UserRoleEnum.Admin.ToString()));

        var auditLogService = new AuditLogService(serviceProvider);
        _fakeAccountService = new FakeAccountService();
        _fakeEmailService = new FakeEmailService();
        _querySut = new AdminUserQueryService(serviceProvider, new SectionVoicePartLookupService(_context));
        _sut = new AdminUserService(serviceProvider, auditLogService, _fakeAccountService, _fakeEmailService, _querySut);

        var adminCourant = CreateUser(CurrentAdminId, "Admin", "Courant", $"{CurrentAdminId}@test.com");
        await _context.SaveChangesAsync();
        await _userManager.AddToRoleAsync(adminCourant, UserRoleEnum.Admin.ToString());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Test]
    public async Task SetActiveAsync_NominalDeactivation_SetsIsActiveToFalse()
    {
        var user = CreateUser("member-1", "Alice", "Martin", "alice@test.com");
        await _context.SaveChangesAsync();

        await _sut.SetActiveAsync(user.Id, false);

        var reloaded = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == user.Id);
        Assert.That(reloaded.IsActive, Is.False);
    }

    [Test]
    public async Task ResetPasswordAsync_StandardAccount_CallsAccountServiceForgotPassword()
    {
        var user = CreateUser("member-2", "Bob", "Durand", "bob@test.com");
        await _context.SaveChangesAsync();

        await _sut.ResetPasswordAsync(user.Id);

        Assert.That(_fakeAccountService.ForgotPasswordCalls, Does.Contain("bob@test.com"));
    }

    [Test]
    public async Task DeleteAsync_NominalUser_IsSoftDeletedAndEmailFreed()
    {
        var user = CreateUser("member-3", "Carla", "Petit", "carla@test.com");
        await _context.SaveChangesAsync();

        await _sut.DeleteAsync(user.Id);

        var reloaded = await _context.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == user.Id);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.IsDeleted, Is.True);
            Assert.That(reloaded.IsActive, Is.False);
            Assert.That(reloaded.Email, Is.Not.EqualTo("carla@test.com"));
            Assert.That(reloaded.Email, Does.Contain("supprime"));
        });
    }

    [Test]
    public void SetActiveAsync_AdminDeactivatesSelf_ThrowsForbidden()
    {
        var exception = Assert.ThrowsAsync<CustomException>(
            () => _sut.SetActiveAsync(CurrentAdminId, false));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public void DeleteAsync_AdminDeletesSelf_ThrowsForbidden()
    {
        var exception = Assert.ThrowsAsync<CustomException>(
            () => _sut.DeleteAsync(CurrentAdminId));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public void DeleteAsync_LastAdmin_IsRejectedWithExplicitMessage()
    {
        var otherCaller = "autre-caller-non-admin";

        var exception = Assert.ThrowsAsync<CustomException>(
            () => CallDeleteFromAnotherUserAsync(otherCaller, CurrentAdminId));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(exception.FrontMessage, Does.Contain("dernier administrateur"));
        });
    }

    [Test]
    public async Task DeleteAsync_UserWithLiveMemberships_SoftDeletesMembershipsInTheSameSaveChanges()
    {
        var clientId = ChoraleDbContext.NewIdGuid();
        var choirId = ChoraleDbContext.NewIdGuid();
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir { Id = choirId, ClientId = clientId, Name = "Choir Test", Status = ChoirStatusEnum.Published });
        var user = CreateUser("member-4", "Dan", "Lefevre", "dan@test.com");
        var member = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = user.Id,
            SpaceId = choirId,
            ChoirId = choirId,
            Status = MemberStatusEnum.Active
        };
        _context.SpaceMembers.Add(member);
        await _context.SaveChangesAsync();

        await _sut.DeleteAsync(user.Id);

        var reloadedMember = await _context.SpaceMembers.IgnoreQueryFilters().AsNoTracking().FirstAsync(m => m.Id == member.Id);
        Assert.That(reloadedMember.IsDeleted, Is.True);
    }

    [Test]
    public async Task DeleteAsync_ContentCreatedByUser_IsKeptWithCreatedByUserIdIntact()
    {
        var clientId = ChoraleDbContext.NewIdGuid();
        var choirId = ChoraleDbContext.NewIdGuid();
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir { Id = choirId, ClientId = clientId, Name = "Choir Test", Status = ChoirStatusEnum.Published });
        var user = CreateUser("member-5", "Eve", "Rousseau", "eve@test.com");
        var song = new Song
        {
            Id = ChoraleDbContext.NewIdGuid(),
            Title = "Chant Test",
            ChoirId = choirId,
            Status = SongStatusEnum.Active,
            CreatedByUserId = user.Id
        };
        _context.Songs.Add(song);
        await _context.SaveChangesAsync();

        await _sut.DeleteAsync(user.Id);

        var reloadedSong = await _context.Songs.AsNoTracking().FirstAsync(c => c.Id == song.Id);
        Assert.Multiple(() =>
        {
            Assert.That(reloadedSong.IsDeleted, Is.False);
            Assert.That(reloadedSong.CreatedByUserId, Is.EqualTo(user.Id));
        });
    }

    [Test]
    public async Task ResetPasswordAsync_UnclaimedInvitedAccount_SendsInvitationNotReset()
    {
        var invite = CreateUser("invite-1", "Invite", "Test", "invite@test.com", isGuestAccount: true, emailConfirmed: false);
        await _context.SaveChangesAsync();

        await _sut.ResetPasswordAsync(invite.Id);

        Assert.Multiple(() =>
        {
            Assert.That(_fakeAccountService.ForgotPasswordCalls, Is.Empty);
            Assert.That(_fakeEmailService.SentEmails, Has.Count.EqualTo(1));
            Assert.That(_fakeEmailService.SentEmails[0].Subject, Does.Contain("invitation"));
        });
    }

    [Test]
    public async Task DeleteAsync_InvitedAccount_IsNotAnonymizedByTheDeletePath()
    {
        var invite = CreateUser("invite-2", "Invite", "AGarder", "invite-supprime@test.com", isGuestAccount: true, emailConfirmed: false);
        await _context.SaveChangesAsync();

        await _sut.DeleteAsync(invite.Id);

        var reloaded = await _context.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == invite.Id);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.IsDeleted, Is.True);
            Assert.That(reloaded.Firstname, Is.EqualTo("Invite"));
            Assert.That(reloaded.Lastname, Is.EqualTo("AGarder"));
            Assert.That(reloaded.Email, Does.Contain("supprime"));
            Assert.That(reloaded.Email, Does.Not.Contain("anonymise"));
        });
    }

    [Test]
    public async Task UpdateIdentityAsync_EmailAlreadyUsed_ThrowsConflictWithoutUpdatingData()
    {
        var user1 = CreateUser("member-6", "Fred", "Girard", "fred@test.com");
        var user2 = CreateUser("member-7", "Gina", "Herve", "gina@test.com");
        await _context.SaveChangesAsync();

        var model = new AdminUserUpdateIdentityViewModel
        {
            Id = user2.Id,
            Firstname = "Modifie",
            Lastname = "Modifie",
            Email = "fred@test.com"
        };

        var exception = Assert.ThrowsAsync<CustomException>(() => _sut.UpdateIdentityAsync(model));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        var reloaded = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == user2.Id);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Firstname, Is.EqualTo("Gina"));
            Assert.That(reloaded.Lastname, Is.EqualTo("Herve"));
            Assert.That(reloaded.Email, Is.EqualTo("gina@test.com"));
        });
    }

    private async Task CallDeleteFromAnotherUserAsync(string callerId, string targetId)
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new ChoraleDbContext(options);

        var mapper = new MapperConfiguration(cfg => cfg.AddMaps(typeof(EventViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, callerId)], "Test"))
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(mapper);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<ILogService>(new LogService(httpContextAccessor));
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>()
            .AddEntityFrameworkStores<ChoraleDbContext>()
            .AddDefaultTokenProviders()
            .AddInvitationTokenProvider();

        var serviceProvider = services.BuildServiceProvider();
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = serviceProvider.GetRequiredService<UserManager<User>>();
        await roleManager.CreateAsync(new IdentityRole(UserRoleEnum.Admin.ToString()));

        var caller = new User
        {
            Id = callerId,
            UserName = $"{callerId}@test.com",
            NormalizedUserName = $"{callerId}@test.com".ToUpperInvariant(),
            Email = $"{callerId}@test.com",
            NormalizedEmail = $"{callerId}@test.com".ToUpperInvariant(),
            Firstname = "Autre",
            Lastname = "Admin",
            IsActive = true
        };
        var target = new User
        {
            Id = targetId,
            UserName = $"{targetId}@test.com",
            NormalizedUserName = $"{targetId}@test.com".ToUpperInvariant(),
            Email = $"{targetId}@test.com",
            NormalizedEmail = $"{targetId}@test.com".ToUpperInvariant(),
            Firstname = "Seul",
            Lastname = "Admin",
            IsActive = true
        };
        context.Users.AddRange(caller, target);
        await context.SaveChangesAsync();
        await userManager.AddToRoleAsync(target, UserRoleEnum.Admin.ToString());

        var auditLogService = new AuditLogService(serviceProvider);
        var sut = new AdminUserService(
            serviceProvider, auditLogService, new FakeAccountService(), new FakeEmailService(),
            new AdminUserQueryService(serviceProvider, new SectionVoicePartLookupService(context)));

        await sut.DeleteAsync(targetId);
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
}
