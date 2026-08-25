using System.Net;
using System.Security.Claims;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
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
using ChoraleBackEnd.ViewModels.Songs;

namespace ChoraleBackEnd.Test.Services.Songs;

[TestFixture]
public sealed class SongServiceTests
{
    private const string UserId = "responsable-1";

    private ChoraleDbContext _context = null!;
    private IServiceProvider _serviceProvider = null!;
    private SongService _sut = null!;
    private Guid _choirId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        var mapper = new MapperConfiguration(cfg => cfg.AddMaps(typeof(SongViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

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
        _sut = new SongService(
            _serviceProvider,
            new MembershipService(_serviceProvider),
            new ChoirAuthorizationService(_serviceProvider, new MembershipService(_serviceProvider)));

        _choirId = ChoraleDbContext.NewIdGuid();

        _context.Users.Add(new User { Id = UserId, UserName = "responsable@test.com", Email = "responsable@test.com" });
        // Une chorale doit avoir un client actif : l'acces au contenu passe desormais par
        // IMembershipService, qui exige appartenance + statut actif + client actif.
        var clientId = ChoraleDbContext.NewIdGuid();
        _context.Clients.Add(new Client
        {
            Id = clientId, Name = $"Client {clientId}", Status = ClientStatusEnum.Active
        });
        _context.Spaces.Add(new Space { Id = _choirId, ClientId = clientId, SpaceType = SpaceTypeEnum.Choir });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = _choirId, ClientId = clientId, Name = "Choir Test", Status = ChoirStatusEnum.Published
        });

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
    public async Task GetByIdAsync_ScorePublishedAndAllVoicePartsCovered_IsCompleteForChoirTrue()
    {
        var song = CreateSong([VoicePartEnum.Alto, VoicePartEnum.Soprano]);
        AddScorePublished(song.Id);
        AddPublishedRecording(song.Id, VoicePartEnum.Alto);
        AddPublishedRecording(song.Id, VoicePartEnum.Soprano);
        await _context.SaveChangesAsync();

        var result = await _sut.GetByIdAsync(song.Id);

        Assert.That(result.IsCompleteForChoir, Is.True);
        Assert.That(result.VoicePartsWithoutPublishedRecording, Is.Empty);
    }

    [Test]
    public async Task GetByIdAsync_VoicePartsWithoutPublishedRecording_IsCompleteForChoirFalse()
    {
        var song = CreateSong([VoicePartEnum.Alto, VoicePartEnum.Soprano]);
        AddScorePublished(song.Id);
        AddPublishedRecording(song.Id, VoicePartEnum.Alto);
        await _context.SaveChangesAsync();

        var result = await _sut.GetByIdAsync(song.Id);

        Assert.That(result.IsCompleteForChoir, Is.False);
        Assert.That(result.VoicePartsWithoutPublishedRecording, Is.EquivalentTo(new[] { VoicePartEnum.Soprano }));
    }

    [Test]
    public async Task GetByIdAsync_NoScorePublished_IsCompleteForChoirFalseEvenIfVoicePartsCovered()
    {
        var song = CreateSong([VoicePartEnum.Alto]);
        AddPublishedRecording(song.Id, VoicePartEnum.Alto);
        await _context.SaveChangesAsync();

        var result = await _sut.GetByIdAsync(song.Id);

        Assert.That(result.IsCompleteForChoir, Is.False);
        Assert.That(result.VoicePartsWithoutPublishedRecording, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_ArchivedManagerOfOwnChoir_ThrowsForbidden()
    {
        // Effet reellement recherche par la correction (10-D23 / correction ciblee) : un
        // Responsable archive ne doit plus pouvoir ecrire dans le repertoire de la chorale
        // qui l'a archive. Ici, EnsureMemberActiveAsync (IMembershipService, qui exige deja
        // Statut Active pour l'utilisateur courant) bloque avant meme EnsureResponsableAsync :
        // ce test protege le comportement de bout en bout, complementaire de la correction du
        // resolveur de roles qui ferme le meme trou au niveau de la policy d'ecriture ASP.NET
        // (SpaceRoleAuthorizationHandler, verifiee separement).
        var member = await _context.SpaceMembers
            .FirstAsync(m => m.UserId == UserId && m.ChoirId == _choirId);
        member.Status = MemberStatusEnum.Archived;
        await _context.SaveChangesAsync();

        var model = new SongViewModel
        {
            ChoirId = _choirId,
            Title = "Nouveau chant",
            Status = SongStatusEnum.Active,
            VoiceParts = [VoicePartEnum.Soprano]
        };

        var exception = Assert.ThrowsAsync<CustomException>(() => _sut.CreateAsync(model));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    private Song CreateSong(IReadOnlyCollection<VoicePartEnum> voiceParts)
    {
        var song = new Song
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ChoirId = _choirId,
            Title = "Chant Test",
            Status = SongStatusEnum.Active
        };
        _context.Songs.Add(song);

        foreach (var voicePart in voiceParts)
            _context.SongVoiceParts.Add(new SongVoicePart { Id = ChoraleDbContext.NewIdGuid(), SongId = song.Id, VoicePart = voicePart });

        return song;
    }

    private void AddScorePublished(Guid songId)
        => _context.Scores.Add(new Score
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SongId = songId,
            Type = ScoreTypeEnum.General,
            TargetVoicePart = null,
            Version = "v1",
            Status = ScoreStatusEnum.Published,
            OwnerUserId = UserId,
            FilePath = "score.pdf",
            PublishedAt = DateTime.UtcNow
        });

    private void AddPublishedRecording(Guid songId, VoicePartEnum voicePart)
        => _context.Recordings.Add(new Recording
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SongId = songId,
            Type = RecordingTypeEnum.ByVoicePart,
            TargetVoicePart = voicePart,
            ChoirOwnerId = _choirId,
            CreatorUserId = UserId,
            Status = RecordingStatusEnum.Published,
            Source = RecordingSourceEnum.RecordedInApp,
            DurationSeconds = 60,
            ContentOwner = "Choir Test",
            FilePath = $"{voicePart}.mp3",
            PublicationDate = DateTime.UtcNow
        });
}
