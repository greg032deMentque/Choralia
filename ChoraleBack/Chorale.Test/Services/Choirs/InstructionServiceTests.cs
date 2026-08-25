using System.Net;
using System.Security.Claims;
using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Test.Fakes;
using ChoraleBackEnd.ViewModels.Choirs;
using ChoraleBackEnd.ViewModels.Instructions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Services.Choirs;

/// <summary>
/// Consignes de chant : droits d'ecriture par role et visibilite des brouillons. Ces deux
/// regles sont les seules du service qui ne se lisent pas dans un attribut de controleur —
/// la policy `ChoirManagerOrSectionLeader` ouvre la porte, mais c'est ici que se decide qui
/// ecrit reellement quoi (`02` § Matrice, domaine Consigne).
/// </summary>
[TestFixture]
public sealed class InstructionServiceTests
{
    private const string ManagerUserId = "user-manager";
    private const string SectionLeaderAltoUserId = "user-leader-alto";
    private const string SimpleMemberUserId = "user-member";
    private const string OtherChoirManagerUserId = "user-manager-other";

    private ChoraleDbContext _context = null!;
    private Guid _choirId;
    private Guid _otherChoirId;
    private Guid _songId;
    private Guid _otherChoirSongId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        var clientId = ChoraleDbContext.NewIdGuid();
        _choirId = ChoraleDbContext.NewIdGuid();
        _otherChoirId = ChoraleDbContext.NewIdGuid();
        _songId = ChoraleDbContext.NewIdGuid();
        _otherChoirSongId = ChoraleDbContext.NewIdGuid();

        _context.Clients.Add(new Client { Id = clientId, Name = "Client", Status = ClientStatusEnum.Active });

        foreach (var (choirId, name) in new[] { (_choirId, "Chorale"), (_otherChoirId, "Autre chorale") })
        {
            _context.Spaces.Add(new Space { Id = choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = clientId });
            _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
            {
                Id = choirId, ClientId = clientId, Name = name, Status = ChoirStatusEnum.Published
            });
        }

        _context.Songs.Add(new Song
        {
            Id = _songId, ChoirId = _choirId, Title = "Alléluia", Status = SongStatusEnum.Active
        });
        _context.Songs.Add(new Song
        {
            Id = _otherChoirSongId, ChoirId = _otherChoirId, Title = "Locus iste", Status = SongStatusEnum.Active
        });

        AddUser(ManagerUserId);
        AddUser(SectionLeaderAltoUserId);
        AddUser(SimpleMemberUserId);
        AddUser(OtherChoirManagerUserId);

        AddMember(ManagerUserId, _choirId, UserRoleEnum.Manager);
        AddMember(SectionLeaderAltoUserId, _choirId);
        AddMember(SimpleMemberUserId, _choirId);
        AddMember(OtherChoirManagerUserId, _otherChoirId, UserRoleEnum.Manager);

        // Le role SectionLeader est derive de Section.SectionLeaderId, jamais d'une ligne
        // SpaceMemberRole (SpaceRoleResolverService).
        _context.Sections.Add(new Section
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId,
            VoicePart = VoicePartEnum.Alto, SectionLeaderId = SectionLeaderAltoUserId
        });
        _context.Sections.Add(new Section
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId, VoicePart = VoicePartEnum.Soprano
        });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Test]
    public async Task CreateAsync_Manager_CreatesADraftForTheWholeChoirOnThatSong()
    {
        var created = await Service(ManagerUserId).CreateAsync(new CreateInstructionViewModel
        {
            SongId = _songId, Title = "Prononciation", Content = "Attention au latin."
        });

        Assert.Multiple(() =>
        {
            Assert.That(created.SongId, Is.EqualTo(_songId));
            Assert.That(created.VoicePart, Is.Null, "aucune voix visée = consigne pour tout le chœur");
            Assert.That(created.Status, Is.EqualTo(InstructionStatusEnum.Draft));
            Assert.That(created.AuthorUserId, Is.EqualTo(ManagerUserId));
        });
    }

    [Test]
    public void CreateAsync_SectionLeaderOnOwnVoicePart_IsAllowed()
    {
        Assert.DoesNotThrowAsync(() => Service(SectionLeaderAltoUserId).CreateAsync(new CreateInstructionViewModel
        {
            SongId = _songId, VoicePart = VoicePartEnum.Alto, Content = "Altos : reprendre la mesure 12."
        }));
    }

    [Test]
    public void CreateAsync_SectionLeaderOnAnotherVoicePart_IsForbidden()
    {
        var ex = Assert.ThrowsAsync<CustomException>(() => Service(SectionLeaderAltoUserId).CreateAsync(
            new CreateInstructionViewModel
            {
                SongId = _songId, VoicePart = VoicePartEnum.Soprano, Content = "Sopranos : plus doux."
            }));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>
    /// Sans voix visee, la consigne s'adresse a TOUT le choeur : elle releve du responsable.
    /// Un chef de pupitre qui l'obtiendrait signerait de son autorite une consigne generale.
    /// </summary>
    [Test]
    public void CreateAsync_SectionLeaderWithoutVoicePart_IsForbidden()
    {
        var ex = Assert.ThrowsAsync<CustomException>(() => Service(SectionLeaderAltoUserId).CreateAsync(
            new CreateInstructionViewModel { SongId = _songId, Content = "Tout le monde : par cœur." }));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public void CreateAsync_SimpleMember_IsForbidden()
    {
        var ex = Assert.ThrowsAsync<CustomException>(() => Service(SimpleMemberUserId).CreateAsync(
            new CreateInstructionViewModel { SongId = _songId, Content = "Contenu" }));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>
    /// La chorale d'une consigne se deduit de `Song.ChoirId` : un responsable ne doit pas
    /// pouvoir ecrire sur le chant d'une autre chorale en passant simplement son identifiant.
    /// </summary>
    /// <remarks>
    /// Le code retourne est 409 et non 403 : la premiere garde franchie est
    /// <c>MembershipService.EnsureCanWriteAsync</c>, commune a tous les services de chorale,
    /// qui repond « cette chorale n'accepte plus d'écriture » des que l'appelant n'y est pas
    /// membre actif. Message trompeur pour ce cas precis, mais comportement partage — le
    /// changer releverait d'un lot dedie. Ce qui compte ici : l'ecriture est refusee et RIEN
    /// n'est persiste.
    /// </remarks>
    [Test]
    public void CreateAsync_ManagerOnASongOfAnotherChoir_IsRefusedAndWritesNothing()
    {
        var ex = Assert.ThrowsAsync<CustomException>(() => Service(ManagerUserId).CreateAsync(
            new CreateInstructionViewModel { SongId = _otherChoirSongId, Content = "Contenu" }));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(_context.Instructions.Any(), Is.False);
        });
    }

    [Test]
    public void CreateAsync_UnknownSong_IsNotFound()
        => Assert.ThrowsAsync<KeyNotFoundException>(() => Service(ManagerUserId).CreateAsync(
            new CreateInstructionViewModel { SongId = ChoraleDbContext.NewIdGuid(), Content = "Contenu" }));

    /// <summary>
    /// Non-regression : la liste filtrait les brouillons sur le seul auteur, alors que
    /// GetByIdAsync autorisait deja le responsable a les lire. Un responsable ne voyait donc
    /// jamais dans sa liste les brouillons que le detail lui servait pourtant.
    /// </summary>
    [Test]
    public async Task GetPagedAsync_ManagerSeesTheDraftWrittenBySomeoneElse()
    {
        await Service(SectionLeaderAltoUserId).CreateAsync(new CreateInstructionViewModel
        {
            SongId = _songId, VoicePart = VoicePartEnum.Alto, Content = "Brouillon du chef de pupitre."
        });

        var page = await Service(ManagerUserId).GetPagedAsync(new InstructionPagedFilterViewModel { SongId = _songId });

        Assert.That(page.TotalCount, Is.EqualTo(1));
    }

    [Test]
    public async Task GetPagedAsync_SimpleMemberSeesNoDraftAtAll()
    {
        await Service(ManagerUserId).CreateAsync(new CreateInstructionViewModel
        {
            SongId = _songId, Content = "Brouillon du responsable."
        });

        var page = await Service(SimpleMemberUserId).GetPagedAsync(new InstructionPagedFilterViewModel { SongId = _songId });

        Assert.That(page.TotalCount, Is.EqualTo(0));
    }

    [Test]
    public async Task GetPagedAsync_SimpleMemberSeesAPublishedInstruction()
    {
        var created = await Service(ManagerUserId).CreateAsync(new CreateInstructionViewModel
        {
            SongId = _songId, Content = "Consigne publiée."
        });
        await Service(ManagerUserId).PublishAsync(created.Id!.Value);

        var page = await Service(SimpleMemberUserId).GetPagedAsync(new InstructionPagedFilterViewModel { SongId = _songId });

        Assert.That(page.TotalCount, Is.EqualTo(1));
    }

    /// <summary>
    /// Isolation : les consignes d'une chorale ne doivent jamais remonter a un responsable
    /// d'une autre chorale, meme sans filtre SongId.
    /// </summary>
    [Test]
    public async Task GetPagedAsync_ManagerOfAnotherChoirSeesNothing()
    {
        var created = await Service(ManagerUserId).CreateAsync(new CreateInstructionViewModel
        {
            SongId = _songId, Content = "Consigne publiée."
        });
        await Service(ManagerUserId).PublishAsync(created.Id!.Value);

        var page = await Service(OtherChoirManagerUserId).GetPagedAsync(new InstructionPagedFilterViewModel());

        Assert.That(page.TotalCount, Is.EqualTo(0));
    }

    [Test]
    public async Task UpdateAsync_ArchivedInstruction_IsConflict()
    {
        var created = await Service(ManagerUserId).CreateAsync(new CreateInstructionViewModel
        {
            SongId = _songId, Content = "Contenu"
        });
        await Service(ManagerUserId).ArchiveAsync(created.Id!.Value);

        var ex = Assert.ThrowsAsync<CustomException>(() => Service(ManagerUserId).UpdateAsync(
            new UpdateInstructionViewModel { Id = created.Id!.Value, Content = "Nouveau contenu" }));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    private void AddUser(string userId)
        => _context.Users.Add(new User
        {
            Id = userId, Email = $"{userId}@test.local", UserName = $"{userId}@test.local",
            Firstname = "Prénom", Lastname = "Nom", IsActive = true, EmailConfirmed = true
        });

    private void AddMember(string userId, Guid choirId, UserRoleEnum? role = null)
    {
        var member = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), UserId = userId, SpaceId = choirId,
            ChoirId = choirId, Status = MemberStatusEnum.Active
        };
        _context.SpaceMembers.Add(member);

        if (role is { } assignedRole)
            _context.SpaceMemberRoles.Add(new SpaceMemberRole
            {
                Id = ChoraleDbContext.NewIdGuid(), SpaceMemberId = member.Id, Role = assignedRole
            });
    }

    private InstructionService Service(string userId)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        };

        var mapper = new MapperConfiguration(
            cfg => cfg.AddMaps(typeof(ChoirViewModel).Assembly),
            NullLoggerFactory.Instance).CreateMapper();

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton(mapper);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<ILogService>(new LogService(httpContextAccessor));
        services.AddSingleton<IEmailService>(new FakeEmailService());
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>().AddEntityFrameworkStores<ChoraleDbContext>();

        var sp = services.BuildServiceProvider();
        return new InstructionService(sp, new MembershipService(sp));
    }
}
