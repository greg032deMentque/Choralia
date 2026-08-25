using ChoraleBackEnd.Api.Data;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Test.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Api;

[TestFixture]
public sealed class SeedDatabaseTests
{
    private const string EmailAdmin = "admin@chorale.local";
    private const string EmailManager = "responsable@chorale.local";
    private const string EmailCoManager = "responsable.adjoint@chorale.local";
    private const string EmailSecondManager = "responsable2@chorale.local";
    private const string EmailChoirClientManager = "structure.chorale@chorale.local";
    private const string EmailEventClientManager = "structure.evenement@chorale.local";
    private const string EmailSinger = "chanteur@chorale.local";
    private const string EmailSectionLeaderAlto = "chef.alto@chorale.local";
    private const string EmailSectionLeaderSoprano = "chef.soprano@chorale.local";
    private const string EmailSectionLeaderBass = "chef.basse@chorale.local";
    private const string EmailSectionLeaderTenor = "chef.tenor@chorale.local";
    private const string EmailChoirEventOrganizer = "organisateur@chorale.local";
    private const string EmailChoirEventParticipant = "participant@chorale.local";
    private const string EmailStandaloneOrganizer = "organisateur.structure@chorale.local";
    private const string EmailStandaloneParticipant = "participant.structure@chorale.local";
    private const string EmailDeletedAccountProbe = "ancien.compte@chorale.local";
    private const string ValidPassword = "MotDePasse!2026";
    private const string NameLegacyClient = "Client de démonstration";
    private const string NameChoirClient = "Client de démonstration — Chorale";
    private const string NameEventClient = "Client de démonstration — Évènement";
    private const string NameMainChoir = "Chorale de démonstration";
    private const string NameSecondaryChoir = "Ensemble vocal de démonstration";

    // Compteurs attendus du jeu de demonstration. Centralises : une evolution du seed ne doit
    // pas obliger a chasser des nombres nus dans une dizaine d'assertions.
    // Le compte "sonde de suppression douce" (DeletedAccountProbeKey) est cree en plus de ces
    // 15 comptes fonctionnels, mais IsDeleted = true l'exclut du HasQueryFilter par defaut de
    // User — _context.Users.Count() ne le voit donc jamais, ce compteur reste inchange.
    private const int ExpectedUserCount = 15;
    private const int ExpectedSpaceCount = 4;
    private const int ExpectedChoirCount = 2;
    private const int ExpectedSectionCount = 8;
    private const int ExpectedSpaceMemberCount = 17;
    private const int ExpectedSectionMemberCount = 10;
    private const int ExpectedClientManagerCount = 4;
    private const int ExpectedSongCount = 5;

    private ChoraleDbContext _context = null!;
    private FakeLogService _logService = null!;
    private string _storageRoot = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);
        _logService = new FakeLogService();

        // Racine de stockage dediee et jetable par test : sans elle, PathService (construit
        // des que IPathService est resolu par SeedDatabase) creerait un dossier storage/uploads
        // reel derive du repertoire courant du test runner (FakeEnvironment.ContentRootPath
        // vaut "").
        _storageRoot = Path.Combine(Path.GetTempPath(), "ChoraleTests", Guid.NewGuid().ToString());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();

        if (Directory.Exists(_storageRoot))
            Directory.Delete(_storageRoot, recursive: true);
    }

    [Test]
    public async Task InitializeAsync_DemoSeedFailure_DoesNotThrowAndLogsTheReason()
    {
        var configuration = ConfigurationWith(EmailAdmin, ValidPassword);
        var provider = BuildProvider(
            configuration, environmentName: "Development",
            extraValidator: new EmailFailingUserValidator(EmailManager));

        Assert.DoesNotThrowAsync(() => SeedDatabase.InitializeAsync(provider, configuration));

        Assert.Multiple(() =>
        {
            Assert.That(_logService.Warnings, Has.Some.Contains("ForcedTestFailure"));
            Assert.That(_context.Clients.Count(), Is.EqualTo(0));
        });
    }

    [Test]
    public async Task InitializeAsync_RepeatedCalls_DemoSeedStaysIdempotent()
    {
        var configuration = ConfigurationWith(EmailAdmin, ValidPassword);
        var provider = BuildProvider(configuration, environmentName: "Development");

        await SeedDatabase.InitializeAsync(provider, configuration);
        await SeedDatabase.InitializeAsync(provider, configuration);

        Assert.Multiple(() =>
        {
            Assert.That(_context.Clients.Count(c => c.Name == NameChoirClient), Is.EqualTo(1));
            Assert.That(_context.Clients.Count(c => c.Name == NameEventClient), Is.EqualTo(1));
            Assert.That(_context.Clients.Count(), Is.EqualTo(2));
            foreach (var email in AllDemoEmails())
                Assert.That(_context.Users.Count(u => u.Email == email), Is.EqualTo(1), email);
            Assert.That(_context.Users.Count(u => u.Email == EmailAdmin), Is.EqualTo(1));
            Assert.That(_context.Users.Count(), Is.EqualTo(ExpectedUserCount));
            Assert.That(_context.Spaces.Count(), Is.EqualTo(ExpectedSpaceCount));
            Assert.That(_context.Choirs.Count(), Is.EqualTo(ExpectedChoirCount));
            Assert.That(_context.Events.Count(), Is.EqualTo(2));
            Assert.That(_context.Sections.Count(), Is.EqualTo(ExpectedSectionCount));
            Assert.That(_context.SpaceMembers.Count(), Is.EqualTo(ExpectedSpaceMemberCount));
            Assert.That(_context.SectionMembers.Count(), Is.EqualTo(ExpectedSectionMemberCount));
            Assert.That(_context.Songs.Count(), Is.EqualTo(ExpectedSongCount));
            Assert.That(
                _context.ClientMembers.Count(m => m.Role == UserRoleEnum.ClientManager),
                Is.EqualTo(ExpectedClientManagerCount));
        });
    }

    [Test]
    public async Task InitializeAsync_DemoSeed_FourSpacesAttachedToTheRightClient()
    {
        // Non-regression : la migration AjouteClientSurEspace a rendu Espace.ClientId
        // obligatoire avec une FK vers Clients. Le seed a longtemps laisse ce champ a sa
        // valeur par defaut (Guid.Empty), ce qui faisait echouer l'INSERT sur SQL Server —
        // un defaut qu'EF Core InMemory ne peut pas detecter puisqu'il n'applique aucune
        // contrainte de cle etrangere. Cette assertion verifie directement la valeur
        // persistee plutot que l'absence d'exception, et aurait donc echoue sur le code
        // fautif meme sous InMemory.
        var configuration = ConfigurationWith(EmailAdmin, ValidPassword);
        var provider = BuildProvider(configuration, environmentName: "Development");

        await SeedDatabase.InitializeAsync(provider, configuration);

        var choirClient = _context.Clients.Single(c => c.Name == NameChoirClient);
        var eventClient = _context.Clients.Single(c => c.Name == NameEventClient);
        var spaces = _context.Spaces.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(spaces, Has.Count.EqualTo(ExpectedSpaceCount));
            Assert.That(spaces, Has.All.Matches<Space>(s => s.ClientId != Guid.Empty));
            Assert.That(
                spaces.Count(s => s.ClientId == choirClient.Id), Is.EqualTo(3),
                "les deux chorales et leur événement autonome appartiennent au client A");
            Assert.That(
                spaces.Count(s => s.ClientId == eventClient.Id), Is.EqualTo(1),
                "l'événement du client B lui appartient seul");
        });
    }

    [Test]
    public async Task InitializeAsync_DemoSeed_StandaloneEventsHaveNoChoir()
    {
        var configuration = ConfigurationWith(EmailAdmin, ValidPassword);
        var provider = BuildProvider(configuration, environmentName: "Development");

        await SeedDatabase.InitializeAsync(provider, configuration);

        var events = _context.Events.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(events, Has.Count.EqualTo(2));
            Assert.That(events, Has.All.Matches<Event>(e => e.ChoirId == null));
        });
    }

    [Test]
    public async Task InitializeAsync_DemoSeed_EventsArePublishedWithLocationAndFutureDate()
    {
        var configuration = ConfigurationWith(EmailAdmin, ValidPassword);
        var provider = BuildProvider(configuration, environmentName: "Development");

        await SeedDatabase.InitializeAsync(provider, configuration);

        var events = _context.Events.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(events, Has.All.Matches<Event>(e => e.Status == EventStatusEnum.Published));
            Assert.That(events, Has.All.Matches<Event>(e => !string.IsNullOrWhiteSpace(e.Location)));
            Assert.That(events, Has.All.Matches<Event>(e => e.StartDate > DateTime.UtcNow));
        });
    }

    [Test]
    public async Task InitializeAsync_DemoSeed_NoOrganizerOnAChoirSpace()
    {
        var configuration = ConfigurationWith(EmailAdmin, ValidPassword);
        var provider = BuildProvider(configuration, environmentName: "Development");

        await SeedDatabase.InitializeAsync(provider, configuration);

        var choirSpaceIds = _context.Spaces
            .Where(s => s.SpaceType == SpaceTypeEnum.Choir)
            .Select(s => s.Id)
            .ToList();

        var organizerOnChoirSpace = _context.SpaceMemberRoles
            .Where(r => r.Role == UserRoleEnum.Organizer)
            .Join(_context.SpaceMembers, r => r.SpaceMemberId, m => m.Id, (r, m) => m.SpaceId)
            .Any(spaceId => choirSpaceIds.Contains(spaceId));

        Assert.That(organizerOnChoirSpace, Is.False);
    }

    [Test]
    public async Task InitializeAsync_DemoSeed_SingerHasAVoicePartAndASectionInEachChoir()
    {
        var configuration = ConfigurationWith(EmailAdmin, ValidPassword);
        var provider = BuildProvider(configuration, environmentName: "Development");

        await SeedDatabase.InitializeAsync(provider, configuration);

        var singerId = await FindUserIdAsync(provider, EmailSinger);
        var sections = _context.SectionMembers
            .Where(m => m.UserId == singerId)
            .Join(_context.Sections, m => m.SectionId, s => s.Id, (_, s) => s)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(sections, Has.Count.EqualTo(ExpectedChoirCount), "un pupitre par chorale");
            Assert.That(
                sections.Select(s => s.ChoirId).Distinct().Count(), Is.EqualTo(ExpectedChoirCount),
                "les deux pupitres appartiennent à deux chorales distinctes");
        });
    }

    /// <summary>
    /// Le role SectionLeader n'est PAS une ligne SpaceMemberRole : SpaceRoleResolverService le
    /// derive de Section.SectionLeaderId. Un seed qui ajouterait une ligne de role sans
    /// renseigner le pupitre produirait un chef de pupitre invisible du resolveur.
    /// </summary>
    [Test]
    public async Task InitializeAsync_DemoSeed_EverySectionHasItsLeaderAndTheLeaderBelongsToIt()
    {
        var configuration = ConfigurationWith(EmailAdmin, ValidPassword);
        var provider = BuildProvider(configuration, environmentName: "Development");

        await SeedDatabase.InitializeAsync(provider, configuration);

        var sections = _context.Sections.ToList();
        var sectionMembers = _context.SectionMembers.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(sections, Has.Count.EqualTo(ExpectedSectionCount));
            Assert.That(
                sections, Has.All.Matches<Section>(s => !string.IsNullOrWhiteSpace(s.SectionLeaderId)),
                "chaque pupitre des deux chorales a un chef");
            Assert.That(
                sections.All(s => sectionMembers.Any(m => m.SectionId == s.Id && m.UserId == s.SectionLeaderId)),
                Is.True,
                "le chef de pupitre est aussi membre du pupitre qu'il dirige (04 § Membre)");
            Assert.That(
                _context.SpaceMemberRoles.Any(r => r.Role == UserRoleEnum.SectionLeader), Is.False,
                "SectionLeader est dérivé de Section.SectionLeaderId, jamais stocké en SpaceMemberRole");
        });
    }

    /// <summary>
    /// Sans un compte dont le SEUL rattachement est ClientManager, la zone `/client/:clientId`
    /// est inatteignable : `resolveZone` (front) classe Management avant Client, et la topbar
    /// ne liste que les espaces. Ce test verrouille cette propriete du jeu de demonstration.
    /// </summary>
    [Test]
    public async Task InitializeAsync_DemoSeed_DedicatedClientManagerHasNoSpaceMembership()
    {
        var configuration = ConfigurationWith(EmailAdmin, ValidPassword);
        var provider = BuildProvider(configuration, environmentName: "Development");

        await SeedDatabase.InitializeAsync(provider, configuration);

        var choirClientManagerId = await FindUserIdAsync(provider, EmailChoirClientManager);
        var eventClientManagerId = await FindUserIdAsync(provider, EmailEventClientManager);

        Assert.Multiple(() =>
        {
            Assert.That(
                _context.ClientMembers.Count(m => m.UserId == choirClientManagerId && m.Role == UserRoleEnum.ClientManager),
                Is.EqualTo(1));
            Assert.That(
                _context.ClientMembers.Count(m => m.UserId == eventClientManagerId && m.Role == UserRoleEnum.ClientManager),
                Is.EqualTo(1));
            Assert.That(
                _context.SpaceMembers.Any(m => m.UserId == choirClientManagerId || m.UserId == eventClientManagerId),
                Is.False,
                "aucun assignment d'espace : sinon le front les aiguillerait vers /management");
        });
    }

    [Test]
    public async Task InitializeAsync_DemoSeed_MainChoirHasTwoManagers()
    {
        var configuration = ConfigurationWith(EmailAdmin, ValidPassword);
        var provider = BuildProvider(configuration, environmentName: "Development");

        await SeedDatabase.InitializeAsync(provider, configuration);

        var mainChoir = _context.Choirs.Single(c => c.Name == NameMainChoir);
        var secondaryChoir = _context.Choirs.Single(c => c.Name == NameSecondaryChoir);

        Assert.Multiple(() =>
        {
            Assert.That(ManagerCountOf(mainChoir.Id), Is.EqualTo(2));
            Assert.That(ManagerCountOf(secondaryChoir.Id), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task InitializeAsync_DemoSeed_EachChoirHasItsOwnRepertoire()
    {
        var configuration = ConfigurationWith(EmailAdmin, ValidPassword);
        var provider = BuildProvider(configuration, environmentName: "Development");

        await SeedDatabase.InitializeAsync(provider, configuration);

        var mainChoir = _context.Choirs.Single(c => c.Name == NameMainChoir);
        var secondaryChoir = _context.Choirs.Single(c => c.Name == NameSecondaryChoir);

        Assert.Multiple(() =>
        {
            Assert.That(_context.Songs.Count(s => s.ChoirId == mainChoir.Id), Is.EqualTo(3));
            Assert.That(_context.Songs.Count(s => s.ChoirId == secondaryChoir.Id), Is.EqualTo(2));
            Assert.That(
                _context.Songs.ToList(),
                Has.All.Matches<Song>(s => _context.SongVoiceParts.Any(v => v.SongId == s.Id)),
                "un chant sans voix concernée est incomplet et inutilisable en recette");
        });
    }

    [Test]
    public async Task InitializeAsync_DemoSeed_EachClientHasTwoClientManagers()
    {
        var configuration = ConfigurationWith(EmailAdmin, ValidPassword);
        var provider = BuildProvider(configuration, environmentName: "Development");

        await SeedDatabase.InitializeAsync(provider, configuration);

        var choirClient = _context.Clients.Single(c => c.Name == NameChoirClient);
        var eventClient = _context.Clients.Single(c => c.Name == NameEventClient);

        Assert.Multiple(() =>
        {
            Assert.That(
                _context.ClientMembers.Count(m => m.ClientId == choirClient.Id && m.Role == UserRoleEnum.ClientManager),
                Is.EqualTo(2),
                "le fondateur (premier responsable de la première chorale) et le compte dédié");
            Assert.That(
                _context.ClientMembers.Count(m => m.ClientId == eventClient.Id && m.Role == UserRoleEnum.ClientManager),
                Is.EqualTo(2));
        });
    }

    [Test]
    public async Task InitializeAsync_LegacySingleClientDatasetPresent_SeedSkippedAndLogged()
    {
        // Decision idempotence : une base deja porteuse de l'ancien jeu (un seul client
        // "Client de démonstration") n'est ni completee ni fusionnee — le seed ne sait pas
        // reconstituer ce qui manquerait (ClientMember, second client, evenements) sans
        // risquer un etat a moitie peuple si une etape echoue en route. Il se contente de
        // journaliser et de ne RIEN ecrire ; a l'utilisateur de nettoyer sa base de dev.
        var configuration = ConfigurationWith(EmailAdmin, ValidPassword);
        var provider = BuildProvider(configuration, environmentName: "Development");

        SeedLegacyClient();

        await SeedDatabase.InitializeAsync(provider, configuration);

        Assert.Multiple(() =>
        {
            Assert.That(_logService.Warnings, Has.Some.Contains("legacy single-client demo dataset"));
            Assert.That(_context.Clients.Count(), Is.EqualTo(1));
            Assert.That(_context.Clients.Count(c => c.Name == NameLegacyClient), Is.EqualTo(1));
            Assert.That(_context.Users.Count(u => u.Email == EmailManager), Is.EqualTo(0));
        });
    }

    [Test]
    public async Task InitializeAsync_PartialDataset_SeedSkippedAndLogged()
    {
        // Meme decision que pour l'ancien jeu, appliquee au cas ou seul le client A (ou B)
        // existe deja : etat intermediaire qui ne doit jamais se completer implicitement.
        var configuration = ConfigurationWith(EmailAdmin, ValidPassword);
        var provider = BuildProvider(configuration, environmentName: "Development");

        _context.Clients.Add(new Client
        {
            Id = Guid.NewGuid(),
            Name = NameChoirClient,
            Status = ClientStatusEnum.Active,
            ChoirLimit = Client.DefaultLimits.Choirs,
            MemberLimit = Client.DefaultLimits.Members,
            StorageQuotaBytes = Client.DefaultLimits.StorageOctets,
            MaxFileSizeBytes = Client.DefaultLimits.FileSizeBytes
        });
        await _context.SaveChangesAsync();

        await SeedDatabase.InitializeAsync(provider, configuration);

        Assert.Multiple(() =>
        {
            Assert.That(_logService.Warnings, Has.Some.Contains("partial demo dataset"));
            Assert.That(_context.Clients.Count(), Is.EqualTo(1));
            Assert.That(_context.Clients.Count(c => c.Name == NameEventClient), Is.EqualTo(0));
        });
    }

    /// <summary>
    /// Une chorale sans responsable est ingerable : aucune ecriture n'y serait possible et
    /// aucun compte n'y atterrirait en zone /management. Le seed doit refuser plutot que
    /// produire un jeu de demonstration muet.
    /// </summary>
    [Test]
    public async Task InitializeAsync_ChoirWithoutManagerAccountKey_SkipsSeedAndNamesTheFaultyKey()
    {
        // Troisieme chorale ajoutee sans responsable plutot que mutation d'une existante : un
        // ConfigurationManager ne sait pas retirer une cle, seulement la mettre a null.
        var configuration = ConfigurationWith(EmailAdmin, ValidPassword);
        configuration["Seed:Demo:ChoirClient:Choirs:C-SansResponsable:Name"] = "Chorale sans responsable";
        var provider = BuildProvider(configuration, environmentName: "Development");

        await SeedDatabase.InitializeAsync(provider, configuration);

        Assert.Multiple(() =>
        {
            Assert.That(_logService.Warnings, Has.Some.Contains("ManagerAccountKeys"));
            Assert.That(_context.Clients.Count(), Is.EqualTo(0));
        });
    }

    /// <summary>
    /// Une cle de compte referencee par une chorale mais absente d'`Accounts` doit produire un
    /// avertissement nommant la cle, jamais une exception de dictionnaire au milieu des
    /// ecritures — le seed serait alors a moitie applique.
    /// </summary>
    [Test]
    public async Task InitializeAsync_ChoirReferencesUnknownAccountKey_SkipsSeedAndNamesTheKey()
    {
        var configuration = ConfigurationWith(EmailAdmin, ValidPassword);
        configuration["Seed:Demo:ChoirClient:Choirs:A-Principale:ManagerAccountKeys:0"] = "CompteInexistant";
        var provider = BuildProvider(configuration, environmentName: "Development");

        await SeedDatabase.InitializeAsync(provider, configuration);

        Assert.Multiple(() =>
        {
            Assert.That(_logService.Warnings, Has.Some.Contains("CompteInexistant"));
            Assert.That(_context.Clients.Count(), Is.EqualTo(0));
        });
    }

    /// <summary>
    /// Meme discipline que <see cref="InitializeAsync_ChoirWithoutManagerAccountKey_SkipsSeedAndNamesTheKey"/> :
    /// un enregistrement `ByVoicePart` sans voix cible est une erreur de configuration, pas un
    /// defaut silencieux vers `General` — sans quoi le contenu perdrait tout proprietaire de
    /// voix (cf. remarks de <c>RecordingService.ResolveTargetVoicePart</c>).
    /// </summary>
    [Test]
    public async Task InitializeAsync_RecordingByVoicePartWithoutTargetVoicePart_SkipsSeedAndNamesTheKey()
    {
        var configuration = ConfigurationWith(EmailAdmin, ValidPassword);
        configuration["Seed:Demo:ChoirClient:Choirs:A-Principale:Songs:Alleluia:Recordings:Invalid:Type"] = "ByVoicePart";
        var provider = BuildProvider(configuration, environmentName: "Development");

        await SeedDatabase.InitializeAsync(provider, configuration);

        Assert.Multiple(() =>
        {
            Assert.That(_logService.Warnings, Has.Some.Contains("TargetVoicePart"));
            Assert.That(_context.Clients.Count(), Is.EqualTo(0));
        });
    }

    /// <summary>
    /// Le mot de passe seul ne suffit pas en Staging : sans <c>EnabledInStaging</c>, un secret
    /// deja present sur l'App Service pour une autre raison ne doit pas peupler des comptes de
    /// demonstration par accident. Verrouille aussi le log de diagnostic qui explique cette
    /// absence sans avoir a relire le code.
    /// </summary>
    [Test]
    public async Task InitializeAsync_Staging_WithoutEnabledInStagingFlag_DemoSeedNotCreated()
    {
        var configuration = ConfigurationWith(EmailAdmin, ValidPassword);
        var provider = BuildProvider(configuration, environmentName: "Staging");

        await SeedDatabase.InitializeAsync(provider, configuration);

        Assert.Multiple(() =>
        {
            Assert.That(_context.Clients.Count(), Is.EqualTo(0));
            Assert.That(_logService.Informations, Has.Some.Contains("EnabledInStaging"));
        });
    }

    /// <summary>
    /// Miroir du test Development : avec les deux garde-fous reunis (mot de passe +
    /// EnabledInStaging), le jeu de demonstration se cree en Staging exactement comme en
    /// Development.
    /// </summary>
    [Test]
    public async Task InitializeAsync_Staging_WithEnabledInStagingFlag_DemoSeedCreated()
    {
        var configuration = ConfigurationWith(EmailAdmin, ValidPassword);
        configuration["Seed:Demo:EnabledInStaging"] = "true";
        var provider = BuildProvider(configuration, environmentName: "Staging");

        await SeedDatabase.InitializeAsync(provider, configuration);

        Assert.Multiple(() =>
        {
            Assert.That(_context.Clients.Count(c => c.Name == NameChoirClient), Is.EqualTo(1));
            Assert.That(_context.Clients.Count(c => c.Name == NameEventClient), Is.EqualTo(1));
            Assert.That(_context.Clients.Count(), Is.EqualTo(2));
        });
    }

    private void SeedLegacyClient()
    {
        _context.Clients.Add(new Client
        {
            Id = Guid.NewGuid(),
            Name = NameLegacyClient,
            Status = ClientStatusEnum.Active,
            ChoirLimit = Client.DefaultLimits.Choirs,
            MemberLimit = Client.DefaultLimits.Members,
            StorageQuotaBytes = Client.DefaultLimits.StorageOctets,
            MaxFileSizeBytes = Client.DefaultLimits.FileSizeBytes
        });
        _context.SaveChanges();
    }

    [Test]
    public async Task InitializeAsync_DemoSeed_ChoirsCreatedAsPublished()
    {
        // Non-regression : la migration AjouteStatutChorale a ajoute Chorale.Statut, non
        // renseigne par le seed a l'origine (valeur par defaut Draft = 0). Une chorale
        // de demonstration en Draft est invisible de ses propres membres — le jeu de
        // demo serait inutilisable meme si la FK sur Espace.ClientId etait corrigee.
        var configuration = ConfigurationWith(EmailAdmin, ValidPassword);
        var provider = BuildProvider(configuration, environmentName: "Development");

        await SeedDatabase.InitializeAsync(provider, configuration);

        var choirs = _context.Choirs.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(choirs, Has.Count.EqualTo(ExpectedChoirCount));
            Assert.That(choirs, Has.All.Matches<Choir>(c => c.Status == ChoirStatusEnum.Published));
        });
    }

    [Test]
    public async Task InitializeAsync_AdminPasswordMissing_SeedSkippedWithoutException()
    {
        var configuration = ConfigurationWith(EmailAdmin, password: null);
        var provider = BuildProvider(configuration, environmentName: "Development");

        Assert.DoesNotThrowAsync(() => SeedDatabase.InitializeAsync(provider, configuration));

        Assert.Multiple(() =>
        {
            Assert.That(_context.Users.Count(u => u.Email == EmailAdmin), Is.EqualTo(0));
            Assert.That(_context.Clients.Count(), Is.EqualTo(0));
            Assert.That(_logService.Warnings, Has.Some.Contains("Seed super admin skipped"));
            Assert.That(_logService.Warnings, Has.Some.Contains("Seed demo skipped"));
        });
    }

    [Test]
    public void InitializeAsync_AdminPasswordNonCompliant_ThrowsNamingTheViolatedRule()
    {
        const string passwordWithoutSpecialChar = "Password2026";
        var configuration = ConfigurationWith(EmailAdmin, passwordWithoutSpecialChar);
        var provider = BuildProvider(configuration, environmentName: "Development");

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            () => SeedDatabase.InitializeAsync(provider, configuration));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("Seed:Admin:Password"));
            Assert.That(exception.Message, Does.Contain("PasswordRequiresNonAlphanumeric"));
        });
    }

    [Test]
    public async Task InitializeAsync_AdminAndManager_CreatedByTheSamePathAndShareTheSamePassword()
    {
        var configuration = ConfigurationWith(EmailAdmin, ValidPassword);
        var provider = BuildProvider(configuration, environmentName: "Development");

        await SeedDatabase.InitializeAsync(provider, configuration);

        using var scope = provider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var admin = await userManager.FindByEmailAsync(EmailAdmin);
        var manager = await userManager.FindByEmailAsync(EmailManager);

        Assert.Multiple(() =>
        {
            Assert.That(admin, Is.Not.Null);
            Assert.That(manager, Is.Not.Null);
        });

        var adminPasswordValid = await userManager.CheckPasswordAsync(admin!, ValidPassword);
        var managerPasswordValid = await userManager.CheckPasswordAsync(manager!, ValidPassword);

        Assert.Multiple(() =>
        {
            Assert.That(adminPasswordValid, Is.True);
            Assert.That(managerPasswordValid, Is.True);
        });
    }

    private int ManagerCountOf(Guid choirId)
        => _context.SpaceMemberRoles
            .Where(r => r.Role == UserRoleEnum.Manager)
            .Join(_context.SpaceMembers, r => r.SpaceMemberId, m => m.Id, (_, m) => m)
            .Count(m => m.SpaceId == choirId);

    private static async Task<string> FindUserIdAsync(IServiceProvider provider, string email)
    {
        using var scope = provider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.That(user, Is.Not.Null, email);
        return user!.Id;
    }

    private static IEnumerable<string> AllDemoEmails() =>
    [
        EmailManager, EmailCoManager, EmailSecondManager,
        EmailChoirClientManager, EmailEventClientManager,
        EmailSinger,
        EmailSectionLeaderAlto, EmailSectionLeaderSoprano, EmailSectionLeaderBass, EmailSectionLeaderTenor,
        EmailChoirEventOrganizer, EmailChoirEventParticipant,
        EmailStandaloneOrganizer, EmailStandaloneParticipant
    ];

    /// <summary>
    /// Reproduit la configuration reelle : la section `Seed` d'appsettings.json, les deux
    /// mots de passe d'appsettings.Development.json. Passer `password: null` simule un
    /// poste ou aucun des deux secrets n'est renseigne.
    /// </summary>
    private ConfigurationManager ConfigurationWith(string emailAdmin, string? password)
    {
        var configuration = new ConfigurationManager();
        configuration["Storage:Root"] = _storageRoot;
        configuration["Seed:Admin:Email"] = emailAdmin;
        configuration["Seed:Admin:Firstname"] = "Admin";
        configuration["Seed:Admin:Lastname"] = "Super";
        if (password is not null)
        {
            configuration["Seed:Admin:Password"] = password;
            configuration["Seed:Demo:Password"] = password;
        }

        AddDemoAccount(configuration, "Manager", EmailManager, "Manager", "Démo");
        AddDemoAccount(configuration, "CoManager", EmailCoManager, "Responsable", "Adjoint");
        AddDemoAccount(configuration, "SecondManager", EmailSecondManager, "Manager", "Ensemble");
        AddDemoAccount(configuration, "ChoirClientManager", EmailChoirClientManager, "Responsable", "Structure Chorale");
        AddDemoAccount(configuration, "EventClientManager", EmailEventClientManager, "Responsable", "Structure Évènement");
        AddDemoAccount(configuration, "Singer", EmailSinger, "Chanteur", "Démo");
        AddDemoAccount(configuration, "SectionLeaderAlto", EmailSectionLeaderAlto, "Chef", "Alto");
        AddDemoAccount(configuration, "SectionLeaderSoprano", EmailSectionLeaderSoprano, "Chef", "Soprano");
        AddDemoAccount(configuration, "SectionLeaderBass", EmailSectionLeaderBass, "Chef", "Basse");
        AddDemoAccount(configuration, "SectionLeaderTenor", EmailSectionLeaderTenor, "Chef", "Ténor");
        AddDemoAccount(configuration, "ChoirEventOrganizer", EmailChoirEventOrganizer, "Organisateur", "Démo");
        AddDemoAccount(configuration, "ChoirEventParticipant", EmailChoirEventParticipant, "Participant", "Démo");
        AddDemoAccount(configuration, "StandaloneOrganizer", EmailStandaloneOrganizer, "Organisateur", "Structure");
        AddDemoAccount(configuration, "StandaloneParticipant", EmailStandaloneParticipant, "Participant", "Structure");
        // Cle fixe requise depuis l'ajout de la sonde de suppression douce (lot audit admin) :
        // TryBuildDemoPlan l'exige desormais pour TOUTE configuration, y compris celle-ci.
        AddDemoAccount(configuration, DemoSeedOptions.DeletedAccountProbeKey, EmailDeletedAccountProbe, "Ancien", "Compte");

        configuration["Seed:Demo:ChoirClient:Name"] = NameChoirClient;

        AddDemoChoir(
            configuration, "A-Principale", NameMainChoir,
            managerKeys: ["Manager", "CoManager"]);
        AddDemoSong(configuration, "A-Principale", "Alleluia", "Alléluia", "Georg Friedrich Haendel", "Active", "High");
        AddDemoSong(configuration, "A-Principale", "AveVerum", "Ave verum corpus", "Wolfgang Amadeus Mozart", "Active", "Normal");
        AddDemoSong(configuration, "A-Principale", "CantiqueDeJeanRacine", "Cantique de Jean Racine", "Gabriel Fauré", "Active", "Low");

        AddDemoChoir(
            configuration, "B-Ensemble", NameSecondaryChoir,
            managerKeys: ["SecondManager"]);
        AddDemoSong(configuration, "B-Ensemble", "Locus", "Locus iste", "Anton Bruckner", "Active", "Normal");
        AddDemoSong(configuration, "B-Ensemble", "Panis", "Panis angelicus", "César Franck", "Archived", priority: null);

        configuration["Seed:Demo:ChoirClient:Event:Title"] = "Concert de printemps";
        configuration["Seed:Demo:ChoirClient:Event:Description"] = "Concert annuel de la chorale de démonstration.";
        configuration["Seed:Demo:ChoirClient:Event:Location"] = "Église Saint-Martin, 12 rue de la Paix, 75003 Paris";
        configuration["Seed:Demo:ChoirClient:Event:StartInMonths"] = "2";

        configuration["Seed:Demo:EventClient:Name"] = NameEventClient;
        configuration["Seed:Demo:EventClient:Event:Title"] = "Concert caritatif";
        configuration["Seed:Demo:EventClient:Event:Description"] = "Événement organisé sans chorale préexistante.";
        configuration["Seed:Demo:EventClient:Event:Location"] = "Salle des fêtes, 5 place de la Mairie, 69001 Lyon";
        configuration["Seed:Demo:EventClient:Event:StartInMonths"] = "1";

        return configuration;
    }

    private static void AddDemoChoir(
        ConfigurationManager configuration, string choirKey, string name, string[] managerKeys)
    {
        var prefix = $"Seed:Demo:ChoirClient:Choirs:{choirKey}";
        configuration[$"{prefix}:Name"] = name;

        for (var i = 0; i < managerKeys.Length; i++)
            configuration[$"{prefix}:ManagerAccountKeys:{i}"] = managerKeys[i];

        configuration[$"{prefix}:SectionLeaderAccountKeys:{nameof(VoicePartEnum.Alto)}"] = "SectionLeaderAlto";
        configuration[$"{prefix}:SectionLeaderAccountKeys:{nameof(VoicePartEnum.Soprano)}"] = "SectionLeaderSoprano";
        configuration[$"{prefix}:SectionLeaderAccountKeys:{nameof(VoicePartEnum.Bass)}"] = "SectionLeaderBass";
        configuration[$"{prefix}:SectionLeaderAccountKeys:{nameof(VoicePartEnum.Tenor)}"] = "SectionLeaderTenor";
        configuration[$"{prefix}:SingerAccountVoiceParts:Singer"] = nameof(VoicePartEnum.Soprano);
    }

    private static void AddDemoSong(
        ConfigurationManager configuration, string choirKey, string songKey, string title,
        string composer, string status, string? priority)
    {
        var prefix = $"Seed:Demo:ChoirClient:Choirs:{choirKey}:Songs:{songKey}";
        configuration[$"{prefix}:Title"] = title;
        configuration[$"{prefix}:Composer"] = composer;
        configuration[$"{prefix}:Status"] = status;
        if (priority is not null)
            configuration[$"{prefix}:Priority"] = priority;
        configuration[$"{prefix}:VoiceParts:0"] = nameof(VoicePartEnum.Soprano);
        configuration[$"{prefix}:VoiceParts:1"] = nameof(VoicePartEnum.Alto);
    }

    private static void AddDemoAccount(
        ConfigurationManager configuration, string cle, string email, string prenom, string nom)
    {
        configuration[$"Seed:Demo:Accounts:{cle}:Email"] = email;
        configuration[$"Seed:Demo:Accounts:{cle}:Firstname"] = prenom;
        configuration[$"Seed:Demo:Accounts:{cle}:Lastname"] = nom;
    }

    private IServiceProvider BuildProvider(
        IConfiguration configuration, string environmentName, IUserValidator<User>? extraValidator = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton(configuration);
        services.AddSingleton<ILogService>(_logService);
        services.AddSingleton<IWebHostEnvironment>(new FakeEnvironment(environmentName));
        services.AddScoped<IPathService, PathService>();
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>(o =>
            {
                o.Password.RequireDigit = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireNonAlphanumeric = true;
                o.Password.RequireUppercase = true;
                o.Password.RequiredLength = 8;
                o.Password.RequiredUniqueChars = 1;
            })
            .AddEntityFrameworkStores<ChoraleDbContext>()
            .AddDefaultTokenProviders();

        if (extraValidator is not null)
            services.AddScoped(_ => extraValidator);

        return services.BuildServiceProvider();
    }

    private sealed class EmailFailingUserValidator(string emailToFail) : IUserValidator<User>
    {
        public Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user)
            => Task.FromResult(string.Equals(user.Email, emailToFail, StringComparison.OrdinalIgnoreCase)
                ? IdentityResult.Failed(new IdentityError { Code = "ForcedTestFailure", Description = "Echec force pour le test" })
                : IdentityResult.Success);
    }

    private sealed class FakeEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Choir.Test";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = environmentName;
    }
}
