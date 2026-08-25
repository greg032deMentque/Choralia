using System.Net;
using System.Security.Claims;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.UserServices;
using ChoraleBackEnd.Test.Fakes;
using ChoraleBackEnd.Test.TestSupport;
using ChoraleBackEnd.ViewModels.Events;

namespace ChoraleBackEnd.Test.Services.Users;

/// <summary>
/// Le seul canal de rattachement d'un compte invitÃ© est l'email envoyÃ©. Ces tests
/// protègent trois défauts vérifiés : un renvoi qui ne renvoyait jamais rien en silence, un
/// objet d'email figé sur "événement" même pour une invitation de chorale, et un compte
/// invité qui restait éligible à l'anonymisation après avoir défini son mot de passe.
/// </summary>
[TestFixture]
public sealed class UserInvitationServiceTests
{
    private const string ChoirName = "Chorale des Alpes";
    private const string EventName = "Concert de Noël";

    private ChoraleDbContext _context = null!;
    private UserInvitationService _sut = null!;
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
                    [new Claim(ClaimTypes.NameIdentifier, "organisateur-1")], "Test"))
            }
        };

        _fakeEmailService = new FakeEmailService();

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
        await roleManager.CreateAsync(new IdentityRole(UserRoleEnum.Singer.ToString()));

        _sut = new UserInvitationService(serviceProvider, _fakeEmailService);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Test]
    public async Task InviteGuestAsync_UnknownEmail_CreatesInvitedAccountAndSendsEmail()
    {
        var user = await _sut.InviteGuestAsync(
            "nouveau@test.com", "Alex", SpaceTypeEnum.Event, EventName);

        Assert.That(user.IsGuestAccount, Is.True);
        Assert.That(user.EmailConfirmed, Is.False);
        Assert.That(user.Firstname, Is.EqualTo("Alex"));
        Assert.That(_fakeEmailService.SentEmails, Has.Count.EqualTo(1));
        Assert.That(_fakeEmailService.SentEmails[0].To, Is.EqualTo("nouveau@test.com"));
    }

    [Test]
    public async Task InviteGuestAsync_UnclaimedInvitedAccount_ResendsANewEmail()
    {
        // Avant correction : le compte existingUser etait retourne tel quel, sans aucun email.
        // L'invitant voyait un succes, l'invite ne recevait jamais rien.
        var invite = new User
        {
            Id = "invite-1",
            UserName = "invite@test.com",
            NormalizedUserName = "INVITE@TEST.COM",
            Email = "invite@test.com",
            NormalizedEmail = "INVITE@TEST.COM",
            IsGuestAccount = true,
            EmailConfirmed = false
        };
        _context.Users.Add(invite);
        await _context.SaveChangesAsync();

        var result = await _sut.InviteGuestAsync(
            "invite@test.com", null, SpaceTypeEnum.Choir, ChoirName);

        Assert.That(result.Id, Is.EqualTo(invite.Id));
        Assert.That(_fakeEmailService.SentEmails, Has.Count.EqualTo(1));
        Assert.That(_fakeEmailService.SentEmails[0].To, Is.EqualTo("invite@test.com"));
    }

    [Test]
    public void InviteGuestAsync_AlreadyClaimedActiveAccount_ThrowsConflictWithoutDuplicate()
    {
        var existingUser = new User
        {
            Id = "active-1",
            UserName = "active@test.com",
            NormalizedUserName = "ACTIVE@TEST.COM",
            Email = "active@test.com",
            NormalizedEmail = "ACTIVE@TEST.COM",
            IsGuestAccount = false,
            EmailConfirmed = true
        };
        _context.Users.Add(existingUser);
        _context.SaveChanges();

        var exception = Assert.ThrowsAsync<CustomException>(
            () => _sut.InviteGuestAsync("active@test.com", null, SpaceTypeEnum.Choir, ChoirName));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(_fakeEmailService.SentEmails, Is.Empty);
        Assert.That(_context.Users.Count(u => u.Email == "active@test.com"), Is.EqualTo(1));
    }

    [Test]
    public async Task InviteGuestAsync_SoftDeletedInvitedAccount_IsReactivatedWithoutEmail()
    {
        var deletedInvitedUser = new User
        {
            Id = "invite-supprime",
            UserName = "invite-supprime@test.com",
            NormalizedUserName = "INVITE-SUPPRIME@TEST.COM",
            Email = "invite-supprime@test.com",
            NormalizedEmail = "INVITE-SUPPRIME@TEST.COM",
            IsGuestAccount = true,
            EmailConfirmed = false,
            IsDeleted = true
        };
        _context.Users.Add(deletedInvitedUser);
        await _context.SaveChangesAsync();

        var result = await _sut.InviteGuestAsync(
            "invite-supprime@test.com", null, SpaceTypeEnum.Choir, ChoirName);

        Assert.That(result.Id, Is.EqualTo(deletedInvitedUser.Id));
        var reloaded = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == deletedInvitedUser.Id);
        Assert.That(reloaded.IsDeleted, Is.False);
    }

    [Test]
    public void InviteGuestAsync_SoftDeletedNonInvitedAccount_ThrowsConflict()
    {
        var deletedAccount = new User
        {
            Id = "non-invite-supprime",
            UserName = "non-invite-supprime@test.com",
            NormalizedUserName = "NON-INVITE-SUPPRIME@TEST.COM",
            Email = "non-invite-supprime@test.com",
            NormalizedEmail = "NON-INVITE-SUPPRIME@TEST.COM",
            IsGuestAccount = false,
            EmailConfirmed = true,
            IsDeleted = true
        };
        _context.Users.Add(deletedAccount);
        _context.SaveChanges();

        var exception = Assert.ThrowsAsync<CustomException>(
            () => _sut.InviteGuestAsync(
                "non-invite-supprime@test.com", null, SpaceTypeEnum.Choir, ChoirName));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task InviteGuestAsync_ChoirType_SubjectMentionsChoirName()
    {
        await _sut.InviteGuestAsync("choriste@test.com", null, SpaceTypeEnum.Choir, ChoirName);

        var email = _fakeEmailService.SentEmails.Single();
        Assert.Multiple(() =>
        {
            Assert.That(email.Subject, Does.Contain("chorale"));
            // Echappe : le nom d'espace est saisi par un utilisateur et part dans un corps HTML.
            Assert.That(email.HtmlBody, Does.Contain(WebUtility.HtmlEncode(ChoirName)));
        });
    }

    [Test]
    public async Task InviteGuestAsync_EventType_SubjectMentionsEvent()
    {
        await _sut.InviteGuestAsync("participant@test.com", null, SpaceTypeEnum.Event, EventName);

        var email = _fakeEmailService.SentEmails.Single();
        Assert.Multiple(() =>
        {
            Assert.That(email.Subject, Does.Contain("événement"));
            Assert.That(email.HtmlBody, Does.Contain(WebUtility.HtmlEncode(EventName)));
        });
    }
}
