using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using ChoraleBackEnd.Test.Fakes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.ViewModels.Auth;
using ChoraleBackEnd.ViewModels.Events;

namespace ChoraleBackEnd.Test.Services.Auth;

/// <summary>
/// Correction ciblée (défaut 1/2/3 vérifiés, lot 6) :
/// - défaut 1 : le lien d'activation pointait vers une route absente
///   du front — personne ne pouvait
///   activer son compte ;
/// - défaut 2 : <c>Frontend:BaseUrl</c> absent de la configuration produisait un lien relatif
///   silencieux (<c>?? ""</c>) — remplacé par un échec explicite au premier consommation ;
/// - défaut 3 : un compte auto-inscrit dont l'email d'activation n'était jamais délivré (échec
///   SMTP, ou lien perdu) retombait indéfiniment sur "vous avez déjà un compte" à la
///   rÃ©inscription â€” aucun chemin pour l'activer. Ce fichier complÃ¨te <see cref="RegisterTests"/>
///   sur ce point précis.
/// </summary>
[TestFixture]
public sealed class RegisterReenrollmentTests
{
    private const string ValidPassword = "MotDePasse!2026";
    private const string FrontUrl = "http://localhost:4200";

    private ChoraleDbContext _context = null!;
    private IServiceProvider _serviceProvider = null!;
    private FakeEmailService _fakeEmailService = null!;
    private RegistrationService _sut = null!;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _fakeEmailService = new FakeEmailService();
        _serviceProvider = await BuildServiceProviderAsync(_fakeEmailService);

        var dataProtectionProvider = _serviceProvider.GetRequiredService<IDataProtectionProvider>();
        _sut = new RegistrationService(_serviceProvider, _fakeEmailService, dataProtectionProvider);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private async Task<IServiceProvider> BuildServiceProviderAsync(IEmailService emailService)
    {
        var mapper = new MapperConfiguration(
            cfg => cfg.AddMaps(typeof(EventViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };

        var configuration = new ConfigurationManager();
        configuration["Frontend:BaseUrl"] = FrontUrl;

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton(mapper);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<ILogService>(new LogService(httpContextAccessor));
        services.AddSingleton(emailService);
        services.AddLogging();
        services.AddDataProtection();
        services.AddIdentity<User, IdentityRole>()
            .AddEntityFrameworkStores<ChoraleDbContext>()
            .AddDefaultTokenProviders();

        var serviceProvider = services.BuildServiceProvider();
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(UserRoleEnum.Singer.ToString()))
            await roleManager.CreateAsync(new IdentityRole(UserRoleEnum.Singer.ToString()));

        return serviceProvider;
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

    [Test]
    public async Task RegisterAsync_SelfRegisteredUnconfirmedAccount_ResendsActivationEmailNotExistingAccount()
    {
        var nonActiveUser = CreateRawUser("non-active-1", "non-active@test.com", isGuest: false, emailConfirmed: false);
        _context.Users.Add(nonActiveUser);
        await _context.SaveChangesAsync();

        await _sut.RegisterAsync(NewModel("non-active@test.com"));

        var email = _fakeEmailService.SentEmails.Single();
        Assert.Multiple(() =>
        {
            Assert.That(email.To, Is.EqualTo("non-active@test.com"));
            Assert.That(email.Subject, Does.Contain("Activez"));
            Assert.That(email.Subject, Does.Not.Contain("déjà"));
            Assert.That(email.HtmlBody, Does.Contain("verify-email"));
            Assert.That(_context.Users.Count(u => u.Email == "non-active@test.com"), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RegisterAsync_SelfRegisteredConfirmedAccount_AlwaysSendsExistingAccountEmail()
    {
        var confirmedUser = CreateRawUser("confirmedUser-1", "confirmedUser@test.com", isGuest: false, emailConfirmed: true);
        _context.Users.Add(confirmedUser);
        await _context.SaveChangesAsync();

        await _sut.RegisterAsync(NewModel("confirmedUser@test.com"));

        var email = _fakeEmailService.SentEmails.Single();
        Assert.Multiple(() =>
        {
            Assert.That(email.To, Is.EqualTo("confirmedUser@test.com"));
            Assert.That(email.Subject, Does.Contain("déjà"));
        });
    }

    /// <summary>
    /// Les quatre branches d'entrée de <see cref="RegistrationService.RegisterAsync"/> — email
    /// libre, compte invité non revendiqué, compte auto-inscrit non confirmé, compte confirmé —
    /// doivent produire une réponse HTTP strictement identique. C'est la garantie
    /// anti-énumération du lot 6 : la correction du défaut 3 ajoute une quatrième branche sans
    /// l'affaiblir.
    /// </summary>
    [Test]
    public async Task RegisterAsync_FourBranches_StrictlyIdenticalHttpResponse()
    {
        var invitedUser = CreateRawUser("invitedUser-cmp-1", "invitedUser-cmp@test.com", isGuest: true, emailConfirmed: false);
        var nonActiveUser = CreateRawUser("non-active-cmp-1", "non-active-cmp@test.com", isGuest: false, emailConfirmed: false);
        var confirmedUser = CreateRawUser("confirmedUser-cmp-1", "confirmedUser-cmp@test.com", isGuest: false, emailConfirmed: true);
        _context.Users.AddRange(invitedUser, nonActiveUser, confirmedUser);
        await _context.SaveChangesAsync();

        var freeResult = await _sut.RegisterAsync(NewModel("libre-cmp@test.com"));
        var invitedResult = await _sut.RegisterAsync(NewModel("invitedUser-cmp@test.com"));
        var nonActiveUserResult = await _sut.RegisterAsync(NewModel("non-active-cmp@test.com"));
        var confirmedResult = await _sut.RegisterAsync(NewModel("confirmedUser-cmp@test.com"));

        Assert.Multiple(() =>
        {
            Assert.That(invitedResult.Message, Is.EqualTo(freeResult.Message));
            Assert.That(nonActiveUserResult.Message, Is.EqualTo(freeResult.Message));
            Assert.That(confirmedResult.Message, Is.EqualTo(freeResult.Message));
        });
    }

    /// <summary>
    /// Défaut 3, second volet : si l'envoi de l'email échoue (SMTP indisponible), le compte
    /// fraîchement créé est conservé — option retenue plutôt qu'un rollback. Justification :
    /// une fois la branche "auto-inscrit non confirmé" corrigée (voir le test ci-dessus), ce
    /// compte est rattrapable par une simple réinscription ou par <c>ResendVerificationAsync</c>,
    /// ce n'est plus une impasse. Annuler la création introduirait une complexité
    /// transactionnelle (retrait du rôle Identity déjà posé, éventuelles écritures
    /// concurrentes) pour un gain nul dans ce cas précis.
    /// </summary>
    [Test]
    public async Task RegisterAsync_ActivationEmailSendFailure_KeepsCreatedAccountAndPropagatesException()
    {
        var throwingEmailService = new ThrowingFakeEmailService();
        var serviceProvider = await BuildServiceProviderAsync(throwingEmailService);
        var dataProtectionProvider = serviceProvider.GetRequiredService<IDataProtectionProvider>();
        var failingSut = new RegistrationService(serviceProvider, throwingEmailService, dataProtectionProvider);

        Assert.ThrowsAsync<InvalidOperationException>(
            () => failingSut.RegisterAsync(NewModel("echec-envoi@test.com")));

        var user = await _context.Users.AsNoTracking().SingleAsync(u => u.Email == "echec-envoi@test.com");
        Assert.Multiple(() =>
        {
            Assert.That(user.IsGuestAccount, Is.False);
            Assert.That(user.EmailConfirmed, Is.False);
        });

        // Rattrapage : une reinscription apres l'incident SMTP doit desormais renvoyer un
        // email d'activation frais (defaut 3 corrige), pas "vous avez deja un compte".
        var recoveryResult = await _sut.RegisterAsync(NewModel("echec-envoi@test.com"));

        Assert.That(recoveryResult.Message, Is.Not.Empty);
        var recoveryEmail = _fakeEmailService.SentEmails.Single();
        Assert.Multiple(() =>
        {
            Assert.That(recoveryEmail.Subject, Does.Contain("Activez"));
            Assert.That(_context.Users.Count(u => u.Email == "echec-envoi@test.com"), Is.EqualTo(1));
        });
    }

    /// <summary>
    /// Défaut 1 : le lien d'activation doit pointer vers le segment de route réellement servi
    /// par le front (<c>RoutePaths.VerifyEmail = 'verify-email'</c>, voir
    /// <c>ChoralFront/src/app/core/route-paths.ts</c>) â€” pas vers un chemin qui ressemble mais
    /// ne correspond à aucune route déclarée. Ce test doit casser si quelqu'un renomme le
    /// segment d'un côté sans reporter le changement dans
    /// <see cref="RegistrationService"/> (ou l'inverse).
    /// </summary>
    [Test]
    public async Task RegisterAsync_ActivationLink_PointsToTheRouteSegmentExpectedByTheFrontend()
    {
        const string frontRouteSegment = "verify-email";

        await _sut.RegisterAsync(NewModel("lien-segment@test.com"));

        var email = _fakeEmailService.SentEmails.Single();
        Assert.That(email.HtmlBody, Does.Contain($"{FrontUrl}/{frontRouteSegment}?userId="));
    }
}
