using System.Net;
using System.Security.Claims;
using System.Text;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
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
using ChoraleBackEnd.ViewModels.Recordings;

namespace ChoraleBackEnd.Test.Services.Recordings;

[TestFixture]
public sealed class RecordingServiceTests
{
    private const string UserId = "responsable-1";
    private const string MemberAltoUserId = "member-alto";

    private ChoraleDbContext _context = null!;
    private IServiceProvider _serviceProvider = null!;
    private RecordingService _sut = null!;
    private FakePathService _fakePathService = null!;
    private Guid _choirId;
    private Guid _songId;
    private Guid _clientId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        var mapper = new MapperConfiguration(cfg => cfg.AddMaps(typeof(RecordingViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

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
        _sut = new RecordingService(
            _serviceProvider,
            new RecordingFileService(_fakePathService),
            new RecordingAuthorizationService(_serviceProvider, choirAuthorization, new MembershipService(_serviceProvider)),
            choirAuthorization,
            new FakeServiceLimitService(),
            new MembershipService(_serviceProvider));

        _choirId = ChoraleDbContext.NewIdGuid();
        _songId = ChoraleDbContext.NewIdGuid();

        _context.Users.Add(new User { Id = UserId, UserName = "responsable@test.com", Email = "responsable@test.com" });
        // MembershipService.EstMembreActifAsync exige un Client Active rattache a la
        // chorale : sans lui, l'ecriture (desormais cablee via EnsureCanWriteAsync) est
        // refusee pour tout le monde, y compris le Responsable.
        _clientId = ChoraleDbContext.NewIdGuid();
        _context.Clients.Add(new Client { Id = _clientId, Name = "Client Test", Status = ClientStatusEnum.Active });
        _context.Spaces.Add(new Space { Id = _choirId, ClientId = _clientId, SpaceType = SpaceTypeEnum.Choir });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir { Id = _choirId, ClientId = _clientId, Name = "Choir Test", Status = ChoirStatusEnum.Published });
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
    public void CreateAsync_FileFormatNotAllowed_ThrowsCustomExceptionBadRequest()
    {
        var model = new CreateRecordingViewModel
        {
            SongId = _songId,
            Type = RecordingTypeEnum.General,
            ContentOwner = "Choir Test",
            DurationSeconds = 120,
            Source = RecordingSourceEnum.RecordedInApp,
            File = CreateFakeFile("musique.ogg", "audio/ogg")
        };

        var exception = Assert.ThrowsAsync<CustomException>(() => _sut.CreateAsync(model));
        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PublishAsync_ArchivedRecording_ThrowsCustomExceptionConflict()
    {
        var recording = new Recording
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SongId = _songId,
            Type = RecordingTypeEnum.General,
            ChoirOwnerId = _choirId,
            CreatorUserId = UserId,
            Status = RecordingStatusEnum.Archived,
            Source = RecordingSourceEnum.RecordedInApp,
            DurationSeconds = 90,
            ContentOwner = "Choir Test",
            FilePath = "archive.mp3"
        };
        _context.Recordings.Add(recording);
        await _context.SaveChangesAsync();

        var exception = Assert.ThrowsAsync<CustomException>(() => _sut.PublishAsync(recording.Id));
        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task DeleteAsync_PhysicallyDeletesTheFileOnDisk()
    {
        var fileName = $"{Guid.NewGuid()}.mp3";
        var path = _fakePathService.GetFilePath(fileName);
        await File.WriteAllTextAsync(path, "contenu-audio");

        var recording = new Recording
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SongId = _songId,
            Type = RecordingTypeEnum.General,
            ChoirOwnerId = _choirId,
            CreatorUserId = UserId,
            Status = RecordingStatusEnum.Draft,
            Source = RecordingSourceEnum.RecordedInApp,
            DurationSeconds = 60,
            ContentOwner = "Choir Test",
            FilePath = fileName
        };
        _context.Recordings.Add(recording);
        await _context.SaveChangesAsync();

        Assert.That(File.Exists(path), Is.True);

        await _sut.DeleteAsync(recording.Id);

        Assert.That(File.Exists(path), Is.False);
    }

    [Test]
    public async Task CreateAsync_SectionLeaderOfAnotherVoicePart_ThrowsForbidden()
    {
        const string sectionLeaderUserId = "chef-section-soprano";
        _context.Users.Add(new User { Id = sectionLeaderUserId, UserName = "chef-soprano2@test.com", Email = "chef-soprano2@test.com" });
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
        var model = new CreateRecordingViewModel
        {
            SongId = _songId,
            Type = RecordingTypeEnum.ByVoicePart,
            TargetVoicePart = VoicePartEnum.Alto,
            ContentOwner = "Choir Test",
            DurationSeconds = 60,
            Source = RecordingSourceEnum.RecordedInApp,
            File = CreateFakeFile("musique.mp3", "audio/mpeg")
        };

        var exception = Assert.ThrowsAsync<CustomException>(() => service.CreateAsync(model));
        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>
    /// Ce test verrouillait le comportement inverse — un alto ne voyait pas les
    /// enregistrements soprano. La spec dit le contraire :
    /// <c>Spec/chorale/02-roles-droits-et-visibilite.md</c> § 164 (« Voir toutes les voix de sa
    /// chorale ✓ Membre ») et <c>06-ecrans-application-mobile.md</c> § 55 (« Accès aux
    /// enregistrements des autres voix »). Décision produit : suivre la spec, un choriste doit
    /// pouvoir travailler sa partie en écoutant les autres pupitres.
    /// </summary>
    /// <remarks>
    /// Seule la LECTURE s'ouvre. L'écriture reste cantonnée à la voix du chef de pupitre —
    /// c'est <c>CreateAsync_SectionLeaderOfAnotherVoicePart_ThrowsForbidden</c>, ci-dessus, qui
    /// le verrouille et qui ne bouge pas.
    /// </remarks>
    [Test]
    public async Task GetPagedAsync_MemberOfAnotherVoicePart_SeesAndReadsPublishedRecordingsOfOtherVoiceParts()
    {
        var sopranoRecordingId = await AddAltoMemberAndSopranoRecordingAsync(RecordingStatusEnum.Published);

        var service = CreateServiceForUser(MemberAltoUserId);
        var result = await service.GetPagedAsync(new RecordingPagedFilterViewModel());
        var detail = await service.GetByIdAsync(sopranoRecordingId);

        Assert.That(result.Items, Has.Count.EqualTo(1));
        Assert.That(detail.Id, Is.EqualTo(sopranoRecordingId));
    }

    [Test]
    public async Task GetPagedAsync_DraftOfAnotherVoicePart_StaysInvisible()
    {
        // L'ouverture inter-voix ne porte que sur le contenu publie : un brouillon reste
        // reserve a son createur et aux Manager de la chorale, quelle que soit la voix.
        await AddAltoMemberAndSopranoRecordingAsync(RecordingStatusEnum.Draft);

        var service = CreateServiceForUser(MemberAltoUserId);
        var result = await service.GetPagedAsync(new RecordingPagedFilterViewModel());

        Assert.That(result.Items, Is.Empty);
    }

    [Test]
    public void CreateAsync_FileRenamedToMp3_ThrowsCustomExceptionBadRequest()
    {
        var model = new CreateRecordingViewModel
        {
            SongId = _songId,
            Type = RecordingTypeEnum.General,
            ContentOwner = "Choir Test",
            DurationSeconds = 120,
            Source = RecordingSourceEnum.RecordedInApp,
            // Nom de fichier et Content-Type annoncent un MP3 ; les octets disent « MZ »,
            // soit un executable Windows. Les deux premiers viennent du client, pas les
            // octets : c'est le seul controle qui tienne.
            File = CreateFakeFile(
                "musique.mp3",
                "audio/mpeg",
                [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00])
        };

        var exception = Assert.ThrowsAsync<CustomException>(() => _sut.CreateAsync(model));
        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public void CreateAsync_ByVoicePartWithoutTargetVoicePart_ThrowsCustomExceptionBadRequest()
    {
        // Refus explicite plutot que coercition a null : l'enregistrement etait alors
        // invisible dans les listes filtrees par voix mais lisible par GetById, et plus aucun
        // chef de pupitre n'etait habilite a le modifier.
        var model = new CreateRecordingViewModel
        {
            SongId = _songId,
            Type = RecordingTypeEnum.ByVoicePart,
            TargetVoicePart = null,
            ContentOwner = "Choir Test",
            DurationSeconds = 120,
            Source = RecordingSourceEnum.RecordedInApp,
            File = CreateFakeFile("musique.mp3", "audio/mpeg", CreateMp3Content())
        };

        var exception = Assert.ThrowsAsync<CustomException>(() => _sut.CreateAsync(model));
        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task GetEventPlaylistByVoicePartAsync_SongInMultipleLists_AppearsOnlyOnce()
    {
        var evt = new Event
        {
            Id = ChoraleDbContext.NewIdGuid(),
            Title = "Mariage Test",
            StartDate = DateTime.UtcNow,
            Type = EventTypeEnum.Wedding,
            ChoirId = _choirId
        };
        _context.Spaces.Add(new Space { Id = evt.Id, ClientId = _clientId, SpaceType = SpaceTypeEnum.Event });
        _context.Events.Add(evt);

        var songList1 = CreatePublishedEventList(evt.Id, 1);
        var songList2 = CreatePublishedEventList(evt.Id, 2);
        _context.SongLists.AddRange(songList1, songList2);
        await _context.SaveChangesAsync();

        _context.SongListSongs.Add(new SongListSong { Id = ChoraleDbContext.NewIdGuid(), SongListId = songList1.Id, SongId = _songId, Position = 0 });
        _context.SongListSongs.Add(new SongListSong { Id = ChoraleDbContext.NewIdGuid(), SongListId = songList2.Id, SongId = _songId, Position = 0 });

        _context.Recordings.Add(new Recording
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SongId = _songId,
            Type = RecordingTypeEnum.ByVoicePart,
            TargetVoicePart = VoicePartEnum.Soprano,
            ChoirOwnerId = _choirId,
            CreatorUserId = UserId,
            Status = RecordingStatusEnum.Published,
            Source = RecordingSourceEnum.RecordedInApp,
            DurationSeconds = 100,
            ContentOwner = "Choir Test",
            FilePath = "piste1.mp3"
        });
        await _context.SaveChangesAsync();

        var playlist = await _sut.GetEventPlaylistByVoicePartAsync(evt.Id, VoicePartEnum.Soprano);

        Assert.That(playlist.Count(p => p.SongId == _songId), Is.EqualTo(1));
    }

    [Test]
    public async Task GetEventPlaylistByVoicePartAsync_GeneralRecording_IsNeverIncluded()
    {
        var evt = new Event
        {
            Id = ChoraleDbContext.NewIdGuid(),
            Title = "Concert Test",
            StartDate = DateTime.UtcNow,
            Type = EventTypeEnum.Concert,
            ChoirId = _choirId
        };
        _context.Spaces.Add(new Space { Id = evt.Id, ClientId = _clientId, SpaceType = SpaceTypeEnum.Event });
        _context.Events.Add(evt);

        var songList = CreatePublishedEventList(evt.Id, 1);
        _context.SongLists.Add(songList);
        await _context.SaveChangesAsync();

        _context.SongListSongs.Add(new SongListSong { Id = ChoraleDbContext.NewIdGuid(), SongListId = songList.Id, SongId = _songId, Position = 0 });

        _context.Recordings.Add(new Recording
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SongId = _songId,
            Type = RecordingTypeEnum.General,
            ChoirOwnerId = _choirId,
            CreatorUserId = UserId,
            Status = RecordingStatusEnum.Published,
            Source = RecordingSourceEnum.RecordedInApp,
            DurationSeconds = 100,
            ContentOwner = "Choir Test",
            FilePath = "general.mp3"
        });
        await _context.SaveChangesAsync();

        var playlist = await _sut.GetEventPlaylistByVoicePartAsync(evt.Id, VoicePartEnum.Soprano);

        Assert.That(playlist, Is.Empty);
    }

    [Test]
    public async Task GetEventPlaylistByVoicePartAsync_EventWithoutChoir_ThrowsConflict()
    {
        var evt = new Event
        {
            Id = ChoraleDbContext.NewIdGuid(),
            Title = "Event Sans Choir",
            StartDate = DateTime.UtcNow,
            Type = EventTypeEnum.Concert,
            ChoirId = null
        };
        _context.Spaces.Add(new Space { Id = evt.Id, ClientId = _clientId, SpaceType = SpaceTypeEnum.Event });
        _context.Events.Add(evt);
        await _context.SaveChangesAsync();

        var exception = Assert.ThrowsAsync<CustomException>(
            () => _sut.GetEventPlaylistByVoicePartAsync(evt.Id, VoicePartEnum.Soprano));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    private SongList CreatePublishedEventList(Guid eventId, int suffix)
        => new()
        {
            Id = ChoraleDbContext.NewIdGuid(),
            Name = $"Liste Event {suffix}",
            ChoirId = _choirId,
            EventId = eventId,
            Type = SongListTypeEnum.Event,
            Status = SongListStatusEnum.Published,
            CreatedById = UserId,
            OwnerUserId = UserId
        };

    private RecordingService CreateServiceForUser(string userId)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId)], "Test"))
            }
        };

        var mapper = new MapperConfiguration(cfg => cfg.AddMaps(typeof(RecordingViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

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
        return new RecordingService(
            serviceProvider,
            new RecordingFileService(_fakePathService),
            new RecordingAuthorizationService(serviceProvider, choirAuthorization, new MembershipService(serviceProvider)),
            choirAuthorization,
            new FakeServiceLimitService(),
            new MembershipService(serviceProvider));
    }

    /// <summary>
    /// Membre du pupitre Alto, et un enregistrement ciblant les Soprano dans le statut
    /// demandé. Le <c>SpaceMember</c> est indispensable : en production un <c>SectionMember</c>
    /// est toujours membre de l'espace chorale, l'appartenance au pupitre ne suffit pas à
    /// elle seule à ouvrir la lecture.
    /// </summary>
    private async Task<Guid> AddAltoMemberAndSopranoRecordingAsync(RecordingStatusEnum status)
    {
        _context.Users.Add(new User { Id = MemberAltoUserId, UserName = "alto@test.com", Email = "alto@test.com" });
        var sectionAlto = new Section { Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId, VoicePart = VoicePartEnum.Alto };
        _context.Sections.Add(sectionAlto);
        _context.SectionMembers.Add(new SectionMember { Id = ChoraleDbContext.NewIdGuid(), SectionId = sectionAlto.Id, UserId = MemberAltoUserId });
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId, SpaceId = _choirId, UserId = MemberAltoUserId, Status = MemberStatusEnum.Active
        });

        var sopranoRecordingId = ChoraleDbContext.NewIdGuid();
        _context.Recordings.Add(new Recording
        {
            Id = sopranoRecordingId,
            SongId = _songId,
            Type = RecordingTypeEnum.ByVoicePart,
            TargetVoicePart = VoicePartEnum.Soprano,
            ChoirOwnerId = _choirId,
            CreatorUserId = UserId,
            Status = status,
            Source = RecordingSourceEnum.RecordedInApp,
            DurationSeconds = 90,
            ContentOwner = "Choir Test",
            FilePath = "soprano.mp3"
        });
        await _context.SaveChangesAsync();

        return sopranoRecordingId;
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

    // Tag ID3v2 en tete, la signature reconnue pour un MP3 balise.
    private static byte[] CreateMp3Content()
        => [.. "ID3\0\0\0\0\0\0"u8, .. new byte[64]];
}
