using System.Net;
using AutoMapper;
using ChoraleBackEnd.Api.Identity;
using ChoraleBackEnd.Common.Constants;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Common.Helpers;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Services.UserServices;
using ChoraleBackEnd.Test.Fakes;
using ChoraleBackEnd.Test.TestSupport;
using ChoraleBackEnd.ViewModels.Auth;
using ChoraleBackEnd.ViewModels.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Services.Auth;

/// <summary>
/// Revendication d'un compte invité via le lien d'invitation
/// (<see cref="AccountService.ActivateAccountAsync"/>).
/// </summary>
/// <remarks>
/// Trois invariants que rien d'autre ne signale : le lien vit plus longtemps qu'une heure
/// (il utilise son propre fournisseur de jeton), il ne sert qu'une fois, et un lien
/// inexploitable ne renseigne jamais sur l'existence du compte visé.
/// </remarks>
[TestFixture]
public sealed class AccountActivationTests
{
    private const string TemporaryPassword = "MotDePasseTemporaire!123";
    private const string ChosenPassword = "MonNouveauMotDePasse!456";
    private const string GuestEmail = "invite-activation@test.com";

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
    public async Task ActivateAccountAsync_ValidToken_SetsPasswordAndClaimsTheAccount()
    {
        var (sut, userManager) = BuildSut();
        var guest = await CreateGuestAsync(userManager);
        var token = await GenerateActivationTokenAsync(userManager, guest);

        await sut.ActivateAccountAsync(new ActivateAccountViewModel
        {
            UserId = guest.Id,
            Token = token,
            NewPassword = ChosenPassword
        });

        var reloaded = await userManager.FindByIdAsync(guest.Id);
        var acceptsTheNewOne = await userManager.CheckPasswordAsync(reloaded!, ChosenPassword);
        var stillAcceptsTheTemporary = await userManager.CheckPasswordAsync(reloaded!, TemporaryPassword);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded!.EmailConfirmed, Is.True);
            Assert.That(reloaded.IsGuestAccount, Is.False);
            Assert.That(acceptsTheNewOne, Is.True);
            Assert.That(stillAcceptsTheTemporary, Is.False,
                "Le mot de passe temporaire doit avoir été remplacé, pas doublé.");
        });
    }

    [Test]
    public async Task ActivateAccountAsync_ExpiredToken_ThrowsBadRequestWithoutTouchingTheAccount()
    {
        // Durée de vie négative : le jeton est expiré à l'instant même où il est émis. C'est
        // exactement la situation d'un invité qui ouvre son mail après l'échéance.
        var (sut, userManager) = BuildSut(TimeSpan.FromSeconds(-1));
        var guest = await CreateGuestAsync(userManager);
        var token = await GenerateActivationTokenAsync(userManager, guest);

        var exception = Assert.ThrowsAsync<CustomException>(() => sut.ActivateAccountAsync(
            new ActivateAccountViewModel { UserId = guest.Id, Token = token, NewPassword = ChosenPassword }));

        var reloaded = await userManager.FindByIdAsync(guest.Id);
        Assert.Multiple(() =>
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(reloaded!.EmailConfirmed, Is.False);
            Assert.That(reloaded.IsGuestAccount, Is.True);
        });
    }

    [Test]
    public async Task ActivateAccountAsync_AlreadyConsumedToken_ThrowsBadRequest()
    {
        var (sut, userManager) = BuildSut();
        var guest = await CreateGuestAsync(userManager);
        var token = await GenerateActivationTokenAsync(userManager, guest);

        await sut.ActivateAccountAsync(new ActivateAccountViewModel
        {
            UserId = guest.Id,
            Token = token,
            NewPassword = ChosenPassword
        });

        // Rien ne stocke les jetons consommés : c'est le changement de mot de passe qui fait
        // tourner le security stamp, et le stamp fait partie de ce que le jeton scelle. Un
        // second passage avec le même lien ne peut donc plus valider.
        var exception = Assert.ThrowsAsync<CustomException>(() => sut.ActivateAccountAsync(
            new ActivateAccountViewModel
            {
                UserId = guest.Id,
                Token = token,
                NewPassword = "EncoreUnAutreMotDePasse!789"
            }));

        var reloaded = await userManager.FindByIdAsync(guest.Id);
        var keepsTheFirstPassword = await userManager.CheckPasswordAsync(reloaded!, ChosenPassword);

        Assert.Multiple(() =>
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(keepsTheFirstPassword, Is.True,
                "Le mot de passe posé au premier passage doit rester celui du compte.");
        });
    }

    [Test]
    public async Task ActivateAccountAsync_UnknownUser_ReturnsSameRejectionAsInvalidToken()
    {
        var (sut, userManager) = BuildSut();
        var guest = await CreateGuestAsync(userManager);
        var token = await GenerateActivationTokenAsync(userManager, guest);

        // Anti-énumération : ce lien est public. Si l'utilisateur inconnu se distinguait du
        // jeton refusé, l'endpoint dirait quelles adresses ont un compte.
        var unknownUser = Assert.ThrowsAsync<CustomException>(() => sut.ActivateAccountAsync(
            new ActivateAccountViewModel
            {
                UserId = "utilisateur-qui-n-existe-pas",
                Token = token,
                NewPassword = ChosenPassword
            }));

        var invalidToken = Assert.ThrowsAsync<CustomException>(() => sut.ActivateAccountAsync(
            new ActivateAccountViewModel
            {
                UserId = guest.Id,
                Token = UrlTokenHelper.Encode("jeton-fabrique"),
                NewPassword = ChosenPassword
            }));

        Assert.Multiple(() =>
        {
            Assert.That(unknownUser!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(invalidToken!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(unknownUser.FrontMessage, Is.EqualTo(invalidToken.FrontMessage));
        });
    }

    private static async Task<string> GenerateActivationTokenAsync(UserManager<User> userManager, User guest)
    {
        var token = await userManager.GenerateUserTokenAsync(
            guest,
            AccountTokenConstants.InvitationTokenProvider,
            AccountTokenConstants.AccountActivationPurpose);

        return UrlTokenHelper.Encode(token);
    }

    private static async Task<User> CreateGuestAsync(UserManager<User> userManager)
    {
        var guest = new User
        {
            Id = Guid.NewGuid().ToString(),
            UserName = GuestEmail,
            Email = GuestEmail,
            IsActive = true,
            IsGuestAccount = true,
            EmailConfirmed = false
        };

        var result = await userManager.CreateAsync(guest, TemporaryPassword);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(";", result.Errors.Select(e => e.Description)));

        return guest;
    }

    private (AccountService Sut, UserManager<User> UserManager) BuildSut(TimeSpan? invitationLifespan = null)
    {
        var mapper = new MapperConfiguration(
            cfg => cfg.AddMaps(typeof(EventViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };

        var configuration = new ConfigurationManager();
        configuration["JWTToken:Secret"] = "test-secret-key-64-characters-minimum-for-hmacsha512-signing-xxxxxxxxxxxx";
        configuration["JWTToken:Issuer"] = "chorale-test";
        configuration["JWTToken:Audience"] = "chorale-test";
        configuration["JWTToken:ExpiresInMinutes"] = "60";
        configuration["Frontend:BaseUrl"] = "http://localhost:4200";

        var fakeEmailService = new FakeEmailService();

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton(mapper);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<ILogService>(new LogService(httpContextAccessor));
        services.AddSingleton<IEmailService>(fakeEmailService);
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>()
            .AddEntityFrameworkStores<ChoraleDbContext>()
            .AddDefaultTokenProviders()
            .AddInvitationTokenProvider();

        if (invitationLifespan is { } lifespan)
            services.Configure<InvitationTokenProviderOptions>(options => options.TokenLifespan = lifespan);

        var serviceProvider = services.BuildServiceProvider();

        var sut = new AccountService(
            serviceProvider,
            new JwtGeneratorService(serviceProvider),
            new UserRoleDataService(serviceProvider),
            new SpaceRoleResolverService(_context),
            new SectionVoicePartLookupService(_context),
            fakeEmailService);

        return (sut, serviceProvider.GetRequiredService<UserManager<User>>());
    }
}
