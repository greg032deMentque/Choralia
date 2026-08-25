using System.Net;
using System.Security.Claims;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Services.UserServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Helpers;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Test.Fakes;
using ChoraleBackEnd.ViewModels.Events;
using ChoraleBackEnd.ViewModels.Auth;

namespace ChoraleBackEnd.Test.Services.Users;

[TestFixture]
public sealed class AccountServiceGuestLoginTests
{
    private const string Password = "MotDePasse!123";

    private ChoraleDbContext _context = null!;
    private AccountService _sut = null!;
    private UserManager<User> _userManager = null!;
    private FakeEmailService _fakeEmailService = null!;
    private Guid _clientId;

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
            HttpContext = new DefaultHttpContext()
        };

        var configuration = new ConfigurationManager();
        configuration["JWTToken:Secret"] = "test-secret-key-64-characters-minimum-for-hmacsha512-signing-xxxxxxxxxxxx";
        configuration["JWTToken:Issuer"] = "chorale-test";
        configuration["JWTToken:Audience"] = "chorale-test";
        configuration["JWTToken:ExpiresInMinutes"] = "60";

        _fakeEmailService = new FakeEmailService();

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton(mapper);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<ILogService>(new LogService(httpContextAccessor));
        services.AddSingleton<IEmailService>(_fakeEmailService);
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>()
            .AddEntityFrameworkStores<ChoraleDbContext>()
            .AddDefaultTokenProviders();

        var serviceProvider = services.BuildServiceProvider();
        _userManager = serviceProvider.GetRequiredService<UserManager<User>>();

        var jwtGeneratorService = new JwtGeneratorService(serviceProvider);
        var userRoleDataService = new UserRoleDataService(serviceProvider);
        var spaceRoleResolverService = new SpaceRoleResolverService(_context);
        var sectionVoicePartLookupService = new SectionVoicePartLookupService(_context);

        _sut = new AccountService(
            serviceProvider,
            jwtGeneratorService,
            userRoleDataService,
            spaceRoleResolverService,
            sectionVoicePartLookupService,
            _fakeEmailService);

        _clientId = ChoraleDbContext.NewIdGuid();
        _context.Clients.Add(new Client { Id = _clientId, Name = "Client Test", Status = ClientStatusEnum.Active });
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Test]
    public async Task Login_UnclaimedInviteOnSingleFinishedEvent_ThrowsUnauthorized()
    {
        var eventId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = eventId, ClientId = _clientId, SpaceType = SpaceTypeEnum.Event });
        _context.Events.Add(new Event
        {
            Id = eventId,
            Title = "Concert Termine",
            StartDate = DateTime.UtcNow.AddDays(-2),
            EndDate = DateTime.UtcNow.AddDays(-1),
            Type = EventTypeEnum.Concert
        });
        var guest = await CreateInviteWithPasswordAsync("invite-termine@test.com");
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = guest.Id,
            SpaceId = eventId,
            Status = MemberStatusEnum.Active
        });
        await _context.SaveChangesAsync();

        var exception = Assert.ThrowsAsync<CustomException>(
            () => _sut.Login(new LoginViewModel { Email = guest.Email!, Password = Password }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Login_UnclaimedInviteOnSingleFinishedAndDeletedEvent_ThrowsUnauthorized()
    {
        const string managerId = "gestionnaire-suppression";
        var eventId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = eventId, ClientId = _clientId, SpaceType = SpaceTypeEnum.Event });
        _context.Events.Add(new Event
        {
            Id = eventId,
            Title = "Concert Termine Puis Supprime",
            StartDate = DateTime.UtcNow.AddDays(-2),
            EndDate = DateTime.UtcNow.AddDays(-1),
            Type = EventTypeEnum.Concert
        });
        var guest = await CreateInviteWithPasswordAsync("invite-termine-supprime@test.com");
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = guest.Id,
            SpaceId = eventId,
            Status = MemberStatusEnum.Active
        });
        await _context.SaveChangesAsync();

        var eventService = await CreateEventServiceWithManagerAsync(managerId, eventId);
        await eventService.DeleteAsync(eventId);

        var exception = Assert.ThrowsAsync<CustomException>(
            () => _sut.Login(new LoginViewModel { Email = guest.Email!, Password = Password }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Login_ClaimedInvite_IsAllowed()
    {
        var eventId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = eventId, ClientId = _clientId, SpaceType = SpaceTypeEnum.Event });
        _context.Events.Add(new Event
        {
            Id = eventId,
            Title = "Concert Termine",
            StartDate = DateTime.UtcNow.AddDays(-2),
            EndDate = DateTime.UtcNow.AddDays(-1),
            Type = EventTypeEnum.Concert
        });
        var guest = await CreateInviteWithPasswordAsync("invite-revendique@test.com", emailConfirmed: true);
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = guest.Id,
            SpaceId = eventId,
            Status = MemberStatusEnum.Active
        });
        await _context.SaveChangesAsync();

        var result = await _sut.Login(new LoginViewModel { Email = guest.Email!, Password = Password });

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task Login_InviteWithActiveChoirInParallel_IsAllowed()
    {
        var choirId = ChoraleDbContext.NewIdGuid();
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir { Id = choirId, ClientId = _clientId, Name = "Chorale Test", Status = ChoirStatusEnum.Published });
        _context.Spaces.Add(new Space { Id = choirId, ClientId = _clientId, SpaceType = SpaceTypeEnum.Choir });

        var eventId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = eventId, ClientId = _clientId, SpaceType = SpaceTypeEnum.Event });
        _context.Events.Add(new Event
        {
            Id = eventId,
            Title = "Concert Termine",
            StartDate = DateTime.UtcNow.AddDays(-2),
            EndDate = DateTime.UtcNow.AddDays(-1),
            Type = EventTypeEnum.Concert
        });

        var guest = await CreateInviteWithPasswordAsync("invite-chorale@test.com");
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = guest.Id,
            SpaceId = eventId,
            Status = MemberStatusEnum.Active
        });
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = guest.Id,
            SpaceId = choirId,
            ChoirId = choirId,
            Status = MemberStatusEnum.Active
        });
        await _context.SaveChangesAsync();

        var result = await _sut.Login(new LoginViewModel { Email = guest.Email!, Password = Password });

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task ResetPassword_Successful_ConfirmsEmailAndClaimsTheInvitedAccount()
    {
        // Ce lien est celui envoye a l'invitation : le suivre est la revendication du
        // compte. Sans desactiver IsGuestAccount ici, l'invite converti restait
        // indefiniment eligible a l'anonymisation par GuestAccountLifecycleService, dont le
        // filtre est IsGuestAccount && !EmailConfirmed.
        var guest = await CreateInviteWithPasswordAsync("invite-reset@test.com");
        var token = await _userManager.GeneratePasswordResetTokenAsync(guest);
        var tokenBase64 = UrlTokenHelper.Encode(token);

        var result = await _sut.ResetPassword(new ResetPasswordRequestViewModel
        {
            UserId = guest.Id,
            Token = tokenBase64,
            NewPassword = "NouveauMotDePasse!456"
        });

        Assert.That(result.Succeeded, Is.True);
        var reloaded = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == guest.Id);
        Assert.Multiple(() =>
        {
            Assert.That(reloaded.EmailConfirmed, Is.True);
            Assert.That(reloaded.IsGuestAccount, Is.False);
        });
    }

    /// <summary>
    /// Reproduit le défaut corrigé : un jeton dont la longueur en octets n'est pas un
    /// multiple de 3 perd un remplissage <c>=</c> à l'émission. Sans restauration à la
    /// lecture, <c>Convert.FromBase64String</c> levait une <see cref="FormatException"/> et
    /// le lien d'invitation mourait en 400 — deux jetons sur trois.
    /// </summary>
    [Test]
    public async Task ResetPassword_TokenNotAlignedOnThreeBytes_IsDecodedBeforeValidation()
    {
        var guest = await CreateInviteWithPasswordAsync("invite-jeton-non-aligne@test.com");

        // 7 octets : reste 1 modulo 3, donc deux caractères de remplissage retirés.
        var tokenBase64 = UrlTokenHelper.Encode(new string('A', 7));
        Assert.That(tokenBase64.Length % 4, Is.Not.Zero, "Le jeton doit être non aligné pour reproduire le défaut.");

        // Le jeton est bien décodé : c'est Identity qui le refuse (jeton inconnu), et non le
        // décodage qui échoue. Avant correction, l'appel levait une FormatException.
        var result = await _sut.ResetPassword(new ResetPasswordRequestViewModel
        {
            UserId = guest.Id,
            Token = tokenBase64,
            NewPassword = "NouveauMotDePasse!456"
        });

        Assert.That(result.Succeeded, Is.False);
    }

    private async Task<EventService> CreateEventServiceWithManagerAsync(string managerId, Guid eventId)
    {
        _context.Users.Add(new User { Id = managerId, UserName = $"{managerId}@test.com", Email = $"{managerId}@test.com" });
        var managerMember = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = managerId,
            SpaceId = eventId,
            Status = MemberStatusEnum.Active,
            Presence = AttendanceEnum.NoReply
        };
        _context.SpaceMembers.Add(managerMember);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceMemberId = managerMember.Id,
            Role = UserRoleEnum.Organizer
        });
        await _context.SaveChangesAsync();

        var mapper = new MapperConfiguration(cfg => cfg.AddMaps(typeof(EventViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, managerId)], "Test"))
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
        var authorizationService = new EventAuthorizationService(serviceProvider, new ChoirAuthorizationService(serviceProvider, new MembershipService(serviceProvider)));
        var auditLogService = new AuditLogService(serviceProvider);
        var guestAccountLifecycleService = new GuestAccountLifecycleService(serviceProvider, auditLogService);
        var clientRoleResolverService = new ClientRoleResolverService(_context);
        return new EventService(
            serviceProvider, authorizationService, guestAccountLifecycleService, clientRoleResolverService,
            new MembershipService(serviceProvider), new EventParticipationSeedingService(serviceProvider));
    }

    private async Task<User> CreateInviteWithPasswordAsync(string email, bool emailConfirmed = false)
    {
        var user = new User
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            Email = email,
            IsGuestAccount = true,
            IsActive = true,
            EmailConfirmed = emailConfirmed
        };
        var result = await _userManager.CreateAsync(user, Password);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(";", result.Errors.Select(e => e.Description)));
        return user;
    }
}
