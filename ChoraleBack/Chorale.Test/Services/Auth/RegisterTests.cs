using System.Net;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using ChoraleBackEnd.Services.UserServices;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Helpers;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.Test.Fakes;
using ChoraleBackEnd.ViewModels.Auth;
using ChoraleBackEnd.ViewModels.Events;

namespace ChoraleBackEnd.Test.Services.Auth;

/// <summary>
/// Registration auto-service (lot 6). Les trois premiers tests protegent l'invariant central,
/// decision produit : email libre, compte deja complet, compte invitedUser non revendique doivent
/// produire EXACTEMENT la meme reponse — la desambiguisation se fait dans l'email envoye,
/// jamais dans le corps HTTP.
/// </summary>
[TestFixture]
public sealed class RegisterTests
{
    private const string ValidPassword = "MotDePasse!2026";

    private ChoraleDbContext _context = null!;
    private RegistrationService _sut = null!;
    private FakeEmailService _fakeEmailService = null!;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        var mapper = new MapperConfiguration(
            cfg => cfg.AddMaps(typeof(EventViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };

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
        services.AddDataProtection();
        services.AddIdentity<User, IdentityRole>()
            .AddEntityFrameworkStores<ChoraleDbContext>()
            .AddDefaultTokenProviders();

        var serviceProvider = services.BuildServiceProvider();
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        await roleManager.CreateAsync(new IdentityRole(UserRoleEnum.Singer.ToString()));

        var dataProtectionProvider = serviceProvider.GetRequiredService<IDataProtectionProvider>();
        _sut = new RegistrationService(serviceProvider, _fakeEmailService, dataProtectionProvider);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Test]
    public async Task RegisterAsync_FreeEmail_CreatesAccountAndSendsActivationEmail()
    {
        var result = await _sut.RegisterAsync(NewModel("nouveau@test.com"));

        var user = await _context.Users.AsNoTracking().SingleAsync(u => u.Email == "nouveau@test.com");
        Assert.Multiple(() =>
        {
            Assert.That(result.Message, Is.Not.Empty);
            Assert.That(user.EmailConfirmed, Is.False);
            Assert.That(user.IsGuestAccount, Is.False);
            Assert.That(_fakeEmailService.SentEmails, Has.Count.EqualTo(1));
            Assert.That(_fakeEmailService.SentEmails[0].To, Is.EqualTo("nouveau@test.com"));
        });
    }

    [Test]
    public async Task RegisterAsync_ExistingCompleteAccount_IdenticalResponseWithoutNewAccountOrDedicatedEmail()
    {
        var existingUser = CreateRawUser("existingUser-1", "existingUser@test.com", isGuest: false, emailConfirmed: true);
        _context.Users.Add(existingUser);
        await _context.SaveChangesAsync();

        var freeResult = await _sut.RegisterAsync(NewModel("libre-a@test.com"));
        var existingResult = await _sut.RegisterAsync(NewModel("existingUser@test.com"));

        Assert.Multiple(() =>
        {
            Assert.That(existingResult.Message, Is.EqualTo(freeResult.Message));
            Assert.That(_context.Users.Count(u => u.Email == "existingUser@test.com"), Is.EqualTo(1));
            Assert.That(_fakeEmailService.SentEmails.Last().To, Is.EqualTo("existingUser@test.com"));
            Assert.That(_fakeEmailService.SentEmails.Last().Subject, Does.Contain("déjà"));
        });
    }

    [Test]
    public async Task RegisterAsync_UnclaimedInvitedAccount_IdenticalResponseWithoutDuplicateOrClaimEmail()
    {
        var invitedUser = CreateRawUser("invitedUser-1", "invitedUser@test.com", isGuest: true, emailConfirmed: false);
        _context.Users.Add(invitedUser);
        await _context.SaveChangesAsync();

        var freeResult = await _sut.RegisterAsync(NewModel("libre-b@test.com"));
        var invitedResult = await _sut.RegisterAsync(NewModel("invitedUser@test.com"));

        Assert.Multiple(() =>
        {
            Assert.That(invitedResult.Message, Is.EqualTo(freeResult.Message));
            Assert.That(_context.Users.Count(u => u.Email == "invitedUser@test.com"), Is.EqualTo(1));
            Assert.That(_fakeEmailService.SentEmails.Last().To, Is.EqualTo("invitedUser@test.com"));
            Assert.That(_fakeEmailService.SentEmails.Last().HtmlBody, Does.Contain("reset-password"));
        });
    }

    [Test]
    public void RegisterAsync_NonCompliantPassword_ThrowsValidationError()
    {
        var exception = Assert.ThrowsAsync<CustomException>(
            () => _sut.RegisterAsync(NewModel("faible@test.com", "abc")));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task VerifyEmailAsync_ValidToken_SetsEmailConfirmed()
    {
        await _sut.RegisterAsync(NewModel("verif@test.com"));
        var user = await _context.Users.AsNoTracking().SingleAsync(u => u.Email == "verif@test.com");
        var token = ExtractToken(_fakeEmailService.SentEmails.Single().HtmlBody);

        await _sut.VerifyEmailAsync(user.Id, token);

        var reloaded = await _context.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.That(reloaded.EmailConfirmed, Is.True);
    }

    [Test]
    public async Task VerifyEmailAsync_ReusedToken_IsRejected()
    {
        await _sut.RegisterAsync(NewModel("reuse@test.com"));
        var user = await _context.Users.AsNoTracking().SingleAsync(u => u.Email == "reuse@test.com");
        var token = ExtractToken(_fakeEmailService.SentEmails.Single().HtmlBody);
        await _sut.VerifyEmailAsync(user.Id, token);

        var exception = Assert.ThrowsAsync<CustomException>(() => _sut.VerifyEmailAsync(user.Id, token));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public void VerifyEmailAsync_ExpiredOrInvalidToken_IsRejectedWithGenericMessage()
    {
        var user = CreateRawUser("expire-1", "expire@test.com", isGuest: false, emailConfirmed: false);
        _context.Users.Add(user);
        _context.SaveChanges();

        var exception = Assert.ThrowsAsync<CustomException>(
            () => _sut.VerifyEmailAsync(user.Id, "jeton-invalide-ou-expire"));

        Assert.That(exception!.FrontMessage, Is.EqualTo("Lien de vérification invalide ou expiré."));
    }

    [Test]
    public async Task ResendVerificationAsync_ExistingUnverifiedAccount_ResendsEmail()
    {
        await _sut.RegisterAsync(NewModel("renvoi@test.com"));
        _fakeEmailService.SentEmails.Clear();

        await _sut.ResendVerificationAsync("renvoi@test.com");

        Assert.That(_fakeEmailService.SentEmails, Has.Count.EqualTo(1));
    }

    /// <summary>
    /// Revendication d'un compte invitedUser (`AccountService.ResetPassword`, lot 0) : le clic sur
    /// le lien nominatif doit conserver les <see cref="SpaceMember"/> et <see cref="SpaceMember.Presence"/>
    /// existants, pas seulement poser <c>EmailConfirmed</c>/<c>IsGuestAccount</c>.
    /// </summary>
    [Test]
    public async Task ClaimInvitedAccount_PreservesSpaceMemberAndPresence()
    {
        const string password = "MotDePasse!123";
        var guest = new User
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "invitedUser-member@test.com",
            Email = "invitedUser-member@test.com",
            IsGuestAccount = true,
            IsActive = true,
            EmailConfirmed = false
        };

        var mapper = new MapperConfiguration(
            cfg => cfg.AddMaps(typeof(EventViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();
        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var configuration = new ConfigurationManager();
        configuration["JWTToken:Secret"] = "test-secret-key-64-characters-minimum-for-hmacsha512-signing-xxxxxxxxxxxx";
        configuration["JWTToken:Issuer"] = "choir-test";
        configuration["JWTToken:Audience"] = "choir-test";
        configuration["JWTToken:ExpiresInMinutes"] = "60";

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
        var userManager = serviceProvider.GetRequiredService<UserManager<User>>();
        var createResult = await userManager.CreateAsync(guest, password);
        if (!createResult.Succeeded)
            throw new InvalidOperationException(string.Join(";", createResult.Errors.Select(e => e.Description)));

        var choirId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = Guid.Parse(Client.ClientTechnique.WithoutStructureId) });
        var member = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = guest.Id,
            ChoirId = choirId,
            SpaceId = choirId,
            Status = MemberStatusEnum.Invited,
            Presence = AttendanceEnum.Attending,
            IsDeleted = false
        };
        _context.SpaceMembers.Add(member);
        await _context.SaveChangesAsync();

        var jwtGeneratorService = new JwtGeneratorService(serviceProvider);
        var userRoleDataService = new UserRoleDataService(serviceProvider);
        var spaceRoleResolverService = new SpaceRoleResolverService(_context);
        var sectionVoicePartLookupService = new SectionVoicePartLookupService(_context);
        var accountService = new AccountService(
            serviceProvider, jwtGeneratorService, userRoleDataService, spaceRoleResolverService,
            sectionVoicePartLookupService, _fakeEmailService);

        var token = await userManager.GeneratePasswordResetTokenAsync(guest);
        var tokenBase64 = UrlTokenHelper.Encode(token);

        var result = await accountService.ResetPassword(new ResetPasswordRequestViewModel
        {
            UserId = guest.Id,
            Token = tokenBase64,
            NewPassword = "NouveauMotDePasse!456"
        });

        Assert.That(result.Succeeded, Is.True);

        var reloadedMember = await _context.SpaceMembers.AsNoTracking().SingleAsync(m => m.Id == member.Id);
        var reloadedUser = await _context.Users.AsNoTracking().SingleAsync(u => u.Id == guest.Id);
        Assert.Multiple(() =>
        {
            Assert.That(reloadedUser.EmailConfirmed, Is.True);
            Assert.That(reloadedUser.IsGuestAccount, Is.False);
            Assert.That(reloadedMember.Status, Is.EqualTo(MemberStatusEnum.Invited));
            Assert.That(reloadedMember.Presence, Is.EqualTo(AttendanceEnum.Attending));
            Assert.That(reloadedMember.IsDeleted, Is.False);
        });
    }

    private static RegisterViewModel NewModel(string email, string password = ValidPassword)
        => new() { Firstname = "Alex", Lastname = "Dupont", Email = email, Password = password };

    private static User CreateRawUser(string id, string email, bool isGuest, bool emailConfirmed) => new()
    {
        Id = id,
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        IsGuestAccount = isGuest,
        EmailConfirmed = emailConfirmed
    };

    private static string ExtractToken(string htmlBody)
    {
        const string marker = "token=";
        var start = htmlBody.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = htmlBody.IndexOf('"', start);
        return htmlBody[start..end];
    }
}
