using System.Net;
using System.Security.Claims;
using System.Text;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.ViewModels.Scores;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ChoraleBackEnd.Test.Fakes;
using NUnit.Framework;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.ChoirServices;

namespace ChoraleBackEnd.Test.Services.Scores;

[TestFixture]
public sealed class ScoreServiceTests
{
    private const string UserId = "responsable-1";

    private ChoraleDbContext _context = null!;
    private IServiceProvider _serviceProvider = null!;
    private ScoreService _sut = null!;
    private FakePathService _fakePathService = null!;
    private Guid _choirId;
    private Guid _songId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        var mapper = new MapperConfiguration(cfg => cfg.AddMaps(typeof(ScoreViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, UserId)], "Test"))
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

        _serviceProvider = services.BuildServiceProvider();
        _fakePathService = new FakePathService();
        var choirAuthorization = new ChoirAuthorizationService(_serviceProvider, new MembershipService(_serviceProvider));
        _sut = new ScoreService(
            _serviceProvider,
            new ScoreFileService(_fakePathService),
            new ScoreAuthorizationService(_serviceProvider, choirAuthorization, new MembershipService(_serviceProvider)),
            choirAuthorization,
            new FakeServiceLimitService(),
            new MembershipService(_serviceProvider));

        _choirId = ChoraleDbContext.NewIdGuid();
        _songId = ChoraleDbContext.NewIdGuid();

        _context.Users.Add(new User { Id = UserId, UserName = "responsable@test.com", Email = "responsable@test.com" });
        // MembershipService.EstMembreActifAsync exige un Client Active rattache a la
        // chorale : sans lui, l'ecriture (desormais cablee via EnsureCanWriteAsync) est
        // refusee pour tout le monde, y compris le Responsable.
        var clientId = ChoraleDbContext.NewIdGuid();
        _context.Clients.Add(new Client { Id = clientId, Name = "Client Test", Status = ClientStatusEnum.Active });
        _context.Spaces.Add(new Space { Id = _choirId, ClientId = clientId, SpaceType = SpaceTypeEnum.Choir });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir { Id = _choirId, ClientId = clientId, Name = "Choir Test", Status = ChoirStatusEnum.Published });
        _context.Songs.Add(new Song { Id = _songId, ChoirId = _choirId, Title = "Chant Test", Status = SongStatusEnum.Active });

        var memberChoir = new SpaceMember { Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId, SpaceId = _choirId, UserId = UserId, Status = MemberStatusEnum.Active };
        _context.SpaceMembers.Add(memberChoir);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceMemberId = memberChoir.Id,
            Role = UserRoleEnum.Manager
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
    public async Task PublishAsync_AutomaticallyArchivesThePreviousPublishedOfSameTypeAndVoicePart()
    {
        var previous = new Score
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SongId = _songId,
            Type = ScoreTypeEnum.General,
            TargetVoicePart = null,
            Version = "v1",
            Status = ScoreStatusEnum.Published,
            OwnerUserId = UserId,
            FilePath = "previous.pdf"
        };
        var newOne = new Score
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SongId = _songId,
            Type = ScoreTypeEnum.General,
            TargetVoicePart = null,
            Version = "v2",
            Status = ScoreStatusEnum.Draft,
            OwnerUserId = UserId,
            FilePath = "nouveau.pdf"
        };
        _context.Scores.AddRange(previous, newOne);
        await _context.SaveChangesAsync();

        await _sut.PublishAsync(newOne.Id);

        var reloadedPrevious = await _context.Scores.AsNoTracking().FirstAsync(p => p.Id == previous.Id);
        var reloadedNew = await _context.Scores.AsNoTracking().FirstAsync(p => p.Id == newOne.Id);

        Assert.That(reloadedPrevious.Status, Is.EqualTo(ScoreStatusEnum.Archived));
        Assert.That(reloadedNew.Status, Is.EqualTo(ScoreStatusEnum.Published));
        Assert.That(reloadedNew.PublishedAt, Is.Not.Null);
    }

    [Test]
    public void CreateAsync_FileFormatNotAllowed_ThrowsCustomExceptionBadRequest()
    {
        var model = new CreateScoreViewModel
        {
            SongId = _songId,
            Type = ScoreTypeEnum.General,
            Version = "v1",
            DownloadAllowed = false,
            File = CreateFakeFile("virus.exe", "application/x-msdownload")
        };

        var exception = Assert.ThrowsAsync<CustomException>(() => _sut.CreateAsync(model));
        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public void CreateAsync_FileRenamedToPdf_ThrowsCustomExceptionBadRequest()
    {
        var model = new CreateScoreViewModel
        {
            SongId = _songId,
            Type = ScoreTypeEnum.General,
            Version = "v1",
            DownloadAllowed = false,
            // Nom de fichier et Content-Type annoncent un PDF ; les octets disent « MZ »,
            // soit un executable Windows. Les deux premiers viennent du client, pas les
            // octets : c'est le seul controle qui tienne.
            File = CreateFakeFile(
                "partition.pdf",
                "application/pdf",
                [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00])
        };

        var exception = Assert.ThrowsAsync<CustomException>(() => _sut.CreateAsync(model));
        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CreateAsync_ValidPdf_WritesTheEntireFileToDisk()
    {
        // Bout en bout du chemin passant : un PDF authentique est accepte, et il arrive
        // entier sur le disque. Le controle par octets lit le debut du flux avant la copie —
        // si les deux venaient a partager la meme vue du fichier, l'ecriture repartirait
        // d'apres l'en-tete et stockerait un fichier tronque, sans lever aucune erreur.
        var content = CreatePdfContent();
        var model = new CreateScoreViewModel
        {
            SongId = _songId,
            Type = ScoreTypeEnum.General,
            Version = "v1",
            DownloadAllowed = false,
            File = CreateFakeFile("partition.pdf", "application/pdf", content)
        };

        var created = await _sut.CreateAsync(model);

        var score = await _context.Scores.AsNoTracking().FirstAsync(p => p.Id == created.Id);
        var writtenBytes = await File.ReadAllBytesAsync(_fakePathService.GetFilePath(score.FilePath));

        Assert.That(writtenBytes, Is.EqualTo(content));
    }

    [Test]
    public void CreateAsync_ByVoicePartWithoutTargetVoicePart_ThrowsCustomExceptionBadRequest()
    {
        // Refus explicite plutot que coercition a null : une partition ParVoix sans voix
        // cible n'aurait plus de chef de pupitre habilite a la modifier, et entrerait en
        // concurrence de publication avec les partitions General du meme chant.
        var model = new CreateScoreViewModel
        {
            SongId = _songId,
            Type = ScoreTypeEnum.ByVoicePart,
            TargetVoicePart = null,
            Version = "v1",
            DownloadAllowed = false,
            File = CreateFakeFile("partition.pdf", "application/pdf", CreatePdfContent())
        };

        var exception = Assert.ThrowsAsync<CustomException>(() => _sut.CreateAsync(model));
        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task DeleteAsync_PhysicallyDeletesTheFileOnDisk()
    {
        var fileName = $"{Guid.NewGuid()}.pdf";
        var path = _fakePathService.GetFilePath(fileName);
        await File.WriteAllTextAsync(path, "content-score");

        var score = new Score
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SongId = _songId,
            Type = ScoreTypeEnum.General,
            TargetVoicePart = null,
            Version = "v1",
            Status = ScoreStatusEnum.Draft,
            OwnerUserId = UserId,
            FilePath = fileName
        };
        _context.Scores.Add(score);
        await _context.SaveChangesAsync();

        Assert.That(File.Exists(path), Is.True);

        await _sut.DeleteAsync(score.Id);

        Assert.That(File.Exists(path), Is.False);
    }

    [Test]
    public async Task DeleteAsync_PublishedScore_DowngradesStatusToArchivedBeforeSoftDelete()
    {
        var score = new Score
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SongId = _songId,
            Type = ScoreTypeEnum.General,
            TargetVoicePart = null,
            Version = "v1",
            Status = ScoreStatusEnum.Published,
            OwnerUserId = UserId,
            FilePath = "publiee.pdf"
        };
        _context.Scores.Add(score);
        await _context.SaveChangesAsync();

        await _sut.DeleteAsync(score.Id);

        var reloadedScore = await _context.Scores
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(p => p.Id == score.Id);

        Assert.That(reloadedScore.Status, Is.EqualTo(ScoreStatusEnum.Archived));
        Assert.That(reloadedScore.IsDeleted, Is.True);
    }

    [Test]
    public async Task CreateAsync_SectionLeaderOfAnotherVoicePart_ThrowsForbidden()
    {
        const string sectionLeaderUserId = "chef-section-soprano";
        _context.Users.Add(new User { Id = sectionLeaderUserId, UserName = "chef-soprano@test.com", Email = "chef-soprano@test.com" });
        _context.Sections.Add(new Section
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ChoirId = _choirId,
            VoicePart = VoicePartEnum.Soprano,
            SectionLeaderId = sectionLeaderUserId
        });
        // En production, un SectionLeader est necessairement deja SpaceMember de sa chorale.
        // MembershipService.EstMembreActifAsync (cable sur l'ecriture via
        // EnsureCanWriteAsync) exige cette appartenance avant meme d'evaluer le role.
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId, SpaceId = _choirId, UserId = sectionLeaderUserId, Status = MemberStatusEnum.Active
        });
        await _context.SaveChangesAsync();

        var service = CreateServiceForUser(sectionLeaderUserId);
        var model = new CreateScoreViewModel
        {
            SongId = _songId,
            Type = ScoreTypeEnum.ByVoicePart,
            TargetVoicePart = VoicePartEnum.Alto,
            Version = "v1",
            DownloadAllowed = false,
            File = CreateFakeFile("score.pdf", "application/pdf")
        };

        var exception = Assert.ThrowsAsync<CustomException>(() => service.CreateAsync(model));
        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    private ScoreService CreateServiceForUser(string userId)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId)], "Test"))
            }
        };

        var mapper = new MapperConfiguration(cfg => cfg.AddMaps(typeof(ScoreViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

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
        var choirAuthorization = new ChoirAuthorizationService(serviceProvider, new MembershipService(serviceProvider));
        return new ScoreService(
            serviceProvider,
            new ScoreFileService(_fakePathService),
            new ScoreAuthorizationService(serviceProvider, choirAuthorization, new MembershipService(serviceProvider)),
            choirAuthorization,
            new FakeServiceLimitService(),
            new MembershipService(serviceProvider));
    }

    private static IFormFile CreateFakeFile(string fileName, string contentType)
        => CreateFakeFile(fileName, contentType, Encoding.UTF8.GetBytes("content-test"));

    private static IFormFile CreateFakeFile(string fileName, string contentType, byte[] content)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "Fichier", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    /// <summary>
    /// En-tete `%PDF` suivi d'un corps assez long et deterministe pour qu'une troncature de
    /// l'ecriture disque se voie a la comparaison octet a octet.
    /// </summary>
    private static byte[] CreatePdfContent()
    {
        const int bodyLength = 512;
        var body = Enumerable.Range(0, bodyLength).Select(i => (byte)(i % 256));
        return [.. "%PDF-1.7\n"u8, .. body];
    }
}
