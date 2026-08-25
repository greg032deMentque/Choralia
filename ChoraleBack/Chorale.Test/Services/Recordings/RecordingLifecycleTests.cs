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
using ChoraleBackEnd.Test.Fakes;
using ChoraleBackEnd.ViewModels.Recordings;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Services.Recordings;

/// <summary>
/// Cycle de vie d'un enregistrement : <c>Draft → PendingReview → Published</c>, plus
/// <c>Reject</c>, <c>Archive</c> et <c>Restore</c>. Fichier distinct de
/// <see cref="RecordingServiceTests"/>, qui porte la creation, le controle de format et la
/// playlist.
/// </summary>
/// <remarks>
/// Deux familles de gardes cohabitent sur ce service et se ressemblent assez pour etre
/// confondues en revue : <c>Publish</c> et <c>Reject</c> exigent un Manager de la chorale
/// (<c>EnsureManagerChoirAsync</c>), tandis que <c>Update</c>, <c>SubmitForReview</c>,
/// <c>Archive</c> et <c>Restore</c> se contentent d'un droit d'ecriture sur la voix
/// (<c>EnsureVoicePartWriteAccessAsync</c>) — donc du chef de pupitre concerne. Les trois
/// tests de la section « dissymetrie » figent cet ecart : sans eux, aligner Publish sur les
/// autres donnerait a un chef de pupitre le droit de valider son propre enregistrement.
/// </remarks>
[TestFixture]
public sealed class RecordingLifecycleTests
{
    private const string ManagerUserId = "manager-choir";
    private const string SopranoLeaderUserId = "leader-soprano";

    private ChoraleDbContext _context = null!;
    private Guid _choirId;
    private Guid _songId;
    private Guid _otherSongId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _choirId = ChoraleDbContext.NewIdGuid();
        _songId = ChoraleDbContext.NewIdGuid();
        _otherSongId = ChoraleDbContext.NewIdGuid();
        var clientId = ChoraleDbContext.NewIdGuid();

        _context.Clients.Add(new Client { Id = clientId, Name = "Client Test", Status = ClientStatusEnum.Active });
        _context.Spaces.Add(new Space { Id = _choirId, ClientId = clientId, SpaceType = SpaceTypeEnum.Choir });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = _choirId, ClientId = clientId, Name = "Choir Test", Status = ChoirStatusEnum.Published
        });
        _context.Songs.Add(new Song { Id = _songId, ChoirId = _choirId, Title = "Chant Test", Status = SongStatusEnum.Active });
        _context.Songs.Add(new Song { Id = _otherSongId, ChoirId = _choirId, Title = "Autre Chant", Status = SongStatusEnum.Active });

        foreach (var userId in new[] { ManagerUserId, SopranoLeaderUserId })
            _context.Users.Add(new User { Id = userId, UserName = $"{userId}@test.com", Email = $"{userId}@test.com" });

        var managerMembership = AddChoirMembership(ManagerUserId);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceMemberId = managerMembership.Id,
            Role = UserRoleEnum.Manager
        });

        AddChoirMembership(SopranoLeaderUserId);
        _context.Sections.Add(new Section
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ChoirId = _choirId,
            VoicePart = VoicePartEnum.Soprano,
            SectionLeaderId = SopranoLeaderUserId
        });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    // ---------- SubmitForReview ----------

    [Test]
    public async Task SubmitForReviewAsync_Draft_MovesToPendingReview()
    {
        var id = await AddRecordingAsync(RecordingStatusEnum.Draft);

        var result = await Sut(ManagerUserId).SubmitForReviewAsync(id);

        Assert.That(result.Status, Is.EqualTo(RecordingStatusEnum.PendingReview));
    }

    [TestCase(RecordingStatusEnum.PendingReview)]
    [TestCase(RecordingStatusEnum.Published)]
    [TestCase(RecordingStatusEnum.Archived)]
    public async Task SubmitForReviewAsync_StatusOtherThanDraft_ThrowsConflict(RecordingStatusEnum status)
    {
        var id = await AddRecordingAsync(status);

        var exception = Assert.ThrowsAsync<CustomException>(() => Sut(ManagerUserId).SubmitForReviewAsync(id));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    // ---------- Publish ----------

    [Test]
    public async Task PublishAsync_PendingReview_MovesToPublishedAndStampsPublicationDate()
    {
        var id = await AddRecordingAsync(RecordingStatusEnum.PendingReview);

        var result = await Sut(ManagerUserId).PublishAsync(id);

        var stored = await _context.Recordings.AsNoTracking().FirstAsync(r => r.Id == id);
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(RecordingStatusEnum.Published));
            Assert.That(stored.PublicationDate, Is.Not.Null);
        });
    }

    // Le cas Archived est deja couvert par RecordingServiceTests.PublishAsync_ArchivedRecording_
    // ThrowsCustomExceptionConflict : non redupliqué ici.
    [TestCase(RecordingStatusEnum.Draft)]
    [TestCase(RecordingStatusEnum.Published)]
    public async Task PublishAsync_StatusOtherThanPendingReview_ThrowsConflict(RecordingStatusEnum status)
    {
        var id = await AddRecordingAsync(status);

        var exception = Assert.ThrowsAsync<CustomException>(() => Sut(ManagerUserId).PublishAsync(id));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    // ---------- Reject ----------

    [Test]
    public async Task RejectAsync_PendingReview_ReturnsToDraft()
    {
        var id = await AddRecordingAsync(RecordingStatusEnum.PendingReview);

        var result = await Sut(ManagerUserId).RejectAsync(id);

        Assert.That(result.Status, Is.EqualTo(RecordingStatusEnum.Draft),
            "Un rejet renvoie au brouillon pour correction, il n'archive pas.");
    }

    [TestCase(RecordingStatusEnum.Draft)]
    [TestCase(RecordingStatusEnum.Published)]
    [TestCase(RecordingStatusEnum.Archived)]
    public async Task RejectAsync_StatusOtherThanPendingReview_ThrowsConflict(RecordingStatusEnum status)
    {
        var id = await AddRecordingAsync(status);

        var exception = Assert.ThrowsAsync<CustomException>(() => Sut(ManagerUserId).RejectAsync(id));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    // ---------- Archive / Restore ----------

    [TestCase(RecordingStatusEnum.Draft)]
    [TestCase(RecordingStatusEnum.PendingReview)]
    [TestCase(RecordingStatusEnum.Published)]
    public async Task ArchiveAsync_AnyNonArchivedStatus_MovesToArchived(RecordingStatusEnum status)
    {
        var id = await AddRecordingAsync(status);

        var result = await Sut(ManagerUserId).ArchiveAsync(id);

        Assert.That(result.Status, Is.EqualTo(RecordingStatusEnum.Archived));
    }

    [Test]
    public async Task ArchiveAsync_AlreadyArchived_ThrowsConflict()
    {
        var id = await AddRecordingAsync(RecordingStatusEnum.Archived);

        var exception = Assert.ThrowsAsync<CustomException>(() => Sut(ManagerUserId).ArchiveAsync(id));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task RestoreAsync_Archived_ReturnsToDraft()
    {
        var id = await AddRecordingAsync(RecordingStatusEnum.Archived);

        var result = await Sut(ManagerUserId).RestoreAsync(id);

        Assert.That(result.Status, Is.EqualTo(RecordingStatusEnum.Draft),
            "Une restauration repasse par le brouillon : elle ne rend jamais un contenu publie directement.");
    }

    [TestCase(RecordingStatusEnum.Draft)]
    [TestCase(RecordingStatusEnum.PendingReview)]
    [TestCase(RecordingStatusEnum.Published)]
    public async Task RestoreAsync_StatusOtherThanArchived_ThrowsConflict(RecordingStatusEnum status)
    {
        var id = await AddRecordingAsync(status);

        var exception = Assert.ThrowsAsync<CustomException>(() => Sut(ManagerUserId).RestoreAsync(id));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    // ---------- Update ----------

    [Test]
    public async Task UpdateAsync_Draft_UpdatesEditableFields()
    {
        var id = await AddRecordingAsync(RecordingStatusEnum.Draft);

        await Sut(ManagerUserId).UpdateAsync(id, new UpdateRecordingViewModel
        {
            ContentOwner = "Nouveau proprietaire",
            DownloadAllowed = true,
            DurationSeconds = 245
        });

        var stored = await _context.Recordings.AsNoTracking().FirstAsync(r => r.Id == id);
        Assert.Multiple(() =>
        {
            Assert.That(stored.ContentOwner, Is.EqualTo("Nouveau proprietaire"));
            Assert.That(stored.DownloadAllowed, Is.True);
            Assert.That(stored.DurationSeconds, Is.EqualTo(245));
        });
    }

    [TestCase(RecordingStatusEnum.PendingReview)]
    [TestCase(RecordingStatusEnum.Published)]
    [TestCase(RecordingStatusEnum.Archived)]
    public async Task UpdateAsync_StatusOtherThanDraft_ThrowsConflict(RecordingStatusEnum status)
    {
        var id = await AddRecordingAsync(status);

        var exception = Assert.ThrowsAsync<CustomException>(
            () => Sut(ManagerUserId).UpdateAsync(id, new UpdateRecordingViewModel
            {
                ContentOwner = "Tentative",
                DurationSeconds = 10
            }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    // ---------- Dissymetrie des gardes : voix vs chef de choeur ----------

    [Test]
    public async Task ArchiveAsync_SectionLeaderOfTheTargetVoicePart_Succeeds()
    {
        var id = await AddRecordingAsync(
            RecordingStatusEnum.Published, RecordingTypeEnum.ByVoicePart, VoicePartEnum.Soprano);

        var result = await Sut(SopranoLeaderUserId).ArchiveAsync(id);

        Assert.That(result.Status, Is.EqualTo(RecordingStatusEnum.Archived));
    }

    [Test]
    public async Task PublishAsync_SectionLeaderOfTheTargetVoicePart_ThrowsForbidden()
    {
        var id = await AddRecordingAsync(
            RecordingStatusEnum.PendingReview, RecordingTypeEnum.ByVoicePart, VoicePartEnum.Soprano);

        var exception = Assert.ThrowsAsync<CustomException>(() => Sut(SopranoLeaderUserId).PublishAsync(id));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
            "Valider son propre enregistrement reviendrait a supprimer l'etape de validation.");
    }

    [Test]
    public async Task RejectAsync_SectionLeaderOfTheTargetVoicePart_ThrowsForbidden()
    {
        var id = await AddRecordingAsync(
            RecordingStatusEnum.PendingReview, RecordingTypeEnum.ByVoicePart, VoicePartEnum.Soprano);

        var exception = Assert.ThrowsAsync<CustomException>(() => Sut(SopranoLeaderUserId).RejectAsync(id));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    // ---------- GetPagedBySongAsync ----------

    [Test]
    public async Task GetPagedBySongAsync_ReturnsOnlyTheRecordingsOfTheRequestedSong()
    {
        var wanted = await AddRecordingAsync(RecordingStatusEnum.Published);
        await AddRecordingAsync(RecordingStatusEnum.Published, songId: _otherSongId);

        var result = await Sut(ManagerUserId).GetPagedBySongAsync(
            new RecordingBySongFilterViewModel { SongId = _songId, PageSize = 100 });

        Assert.Multiple(() =>
        {
            Assert.That(result.TotalCount, Is.EqualTo(1));
            Assert.That(result.Items.Single().Id, Is.EqualTo(wanted));
        });
    }

    [Test]
    public void GetPagedBySongAsync_UnknownSong_ThrowsNotFound()
        => Assert.ThrowsAsync<KeyNotFoundException>(
            () => Sut(ManagerUserId).GetPagedBySongAsync(
                new RecordingBySongFilterViewModel { SongId = ChoraleDbContext.NewIdGuid() }));

    // ---------- Montage ----------

    private SpaceMember AddChoirMembership(string userId)
    {
        var membership = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ChoirId = _choirId,
            SpaceId = _choirId,
            UserId = userId,
            Status = MemberStatusEnum.Active
        };
        _context.SpaceMembers.Add(membership);
        return membership;
    }

    private async Task<Guid> AddRecordingAsync(
        RecordingStatusEnum status,
        RecordingTypeEnum type = RecordingTypeEnum.General,
        VoicePartEnum? targetVoicePart = null,
        Guid? songId = null)
    {
        var id = ChoraleDbContext.NewIdGuid();
        _context.Recordings.Add(new Recording
        {
            Id = id,
            SongId = songId ?? _songId,
            Type = type,
            TargetVoicePart = targetVoicePart,
            ChoirOwnerId = _choirId,
            CreatorUserId = ManagerUserId,
            Status = status,
            Source = RecordingSourceEnum.RecordedInApp,
            DurationSeconds = 60,
            ContentOwner = "Choir Test",
            FilePath = $"{id}.mp3"
        });
        await _context.SaveChangesAsync();
        return id;
    }

    private RecordingService Sut(string userId)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test"))
            }
        };

        var mapper = new MapperConfiguration(
            cfg => cfg.AddMaps(typeof(RecordingViewModel).Assembly),
            NullLoggerFactory.Instance).CreateMapper();

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton(mapper);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<ILogService>(new LogService(httpContextAccessor));
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>().AddEntityFrameworkStores<ChoraleDbContext>();

        var serviceProvider = services.BuildServiceProvider();
        var membershipService = new MembershipService(serviceProvider);
        var choirAuthorization = new ChoirAuthorizationService(serviceProvider, membershipService);

        return new RecordingService(
            serviceProvider,
            new RecordingFileService(new FakePathService()),
            new RecordingAuthorizationService(serviceProvider, choirAuthorization, membershipService),
            choirAuthorization,
            new FakeServiceLimitService(),
            membershipService);
    }
}
