using ChoraleBackEnd.ViewModels.ChoirMembers;
using System.Net;
using System.Security.Claims;
using System.Text;
using AutoMapper;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Services.UserServices;
using ChoraleBackEnd.Test.Fakes;
using ChoraleBackEnd.ViewModels.Instructions;
using ChoraleBackEnd.ViewModels.Recordings;
using ChoraleBackEnd.ViewModels.Scores;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.ChoirServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using ChoraleBackEnd.ViewModels.Events;
using ChoraleBackEnd.ViewModels.Choirs;
using ChoraleBackEnd.ViewModels.SongLists;
using ChoraleBackEnd.ViewModels.Songs;

namespace ChoraleBackEnd.Test.Services.Choirs;

/// <summary>
/// Defaut 1 (migration 13) : <c>MembershipService.CanWriteAsync</c>/
/// <c>EnsureCanWriteAsync</c> existait mais n'etait cable sur aucun service de contenu — une
/// chorale <c>Annule</c> acceptait toujours l'ecriture. Ce fichier verifie le cablage sur
/// chacun des sept services de contenu, et fige le comportement attendu par statut : la
/// lecture reste ouverte en <c>Annule</c>, l'ecriture s'y ferme, <c>Publie</c> continue de
/// fonctionner, <c>Archive</c> refuse tout, et <c>Draft</c> — le statut de preparation —
/// reste ouvert en ecriture au seul createur/Responsable.
/// </summary>
[TestFixture]
public sealed class ChoirCancelledWriteTests
{
    private const string ManagerUserId = "responsable-1";
    private const string MemberSimpleUserId = "member-simple-1";

    private ChoraleDbContext _context = null!;
    private Guid _clientId;
    private Guid _choirId;
    private Guid _songId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _clientId = ChoraleDbContext.NewIdGuid();
        _choirId = ChoraleDbContext.NewIdGuid();
        _songId = ChoraleDbContext.NewIdGuid();

        _context.Users.Add(new User { Id = ManagerUserId, UserName = "responsable@t.com", Email = "responsable@t.com" });
        _context.Users.Add(new User { Id = MemberSimpleUserId, UserName = "member@t.com", Email = "member@t.com" });

        _context.Clients.Add(new Client
        {
            Id = _clientId, Name = "Client Test", Status = ClientStatusEnum.Active,
            ChoirLimit = 10, MemberLimit = 100, StorageQuotaBytes = 1_000_000, MaxFileSizeBytes = 500_000
        });

        _context.Spaces.Add(new Space { Id = _choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = _clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = _choirId, ClientId = _clientId, Name = "Choir Test", Status = ChoirStatusEnum.Published
        });

        var memberManager = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId, SpaceId = _choirId,
            UserId = ManagerUserId, Status = MemberStatusEnum.Active
        };
        _context.SpaceMembers.Add(memberManager);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(), SpaceMemberId = memberManager.Id, Role = UserRoleEnum.Manager
        });

        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId, SpaceId = _choirId,
            UserId = MemberSimpleUserId, Status = MemberStatusEnum.Active
        });

        _context.Songs.Add(new Song
        {
            Id = _songId, ChoirId = _choirId, Title = "Chant Test", Status = SongStatusEnum.Active
        });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    private async Task SetChoirStatusAsync(ChoirStatusEnum status)
    {
        var choir = await _context.Choirs.FirstAsync(c => c.Id == _choirId);
        choir.Status = status;
        await _context.SaveChangesAsync();
    }

    // --- Un test par service de contenu : ecriture refusee en Annule ---------------------

    [Test]
    public async Task WriteRefused_SongCreateAsync_ChoirCancelled()
    {
        await SetChoirStatusAsync(ChoirStatusEnum.Cancelled);

        var ex = Assert.ThrowsAsync<CustomException>(() => CreateSongService(ManagerUserId).CreateAsync(new SongViewModel
        {
            Title = "Nouveau Chant",
            Status = SongStatusEnum.Active,
            VoiceParts = [VoicePartEnum.Soprano],
            ChoirId = _choirId
        }));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task WriteRefused_ScoreCreateAsync_ChoirCancelled()
    {
        await SetChoirStatusAsync(ChoirStatusEnum.Cancelled);

        var ex = Assert.ThrowsAsync<CustomException>(() => CreateScoreService(ManagerUserId).CreateAsync(new CreateScoreViewModel
        {
            SongId = _songId,
            Type = ScoreTypeEnum.General,
            Version = "v1",
            File = CreateFakeFile("score.pdf", "application/pdf")
        }));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task WriteRefused_RecordingCreateAsync_ChoirCancelled()
    {
        await SetChoirStatusAsync(ChoirStatusEnum.Cancelled);

        var ex = Assert.ThrowsAsync<CustomException>(() => CreateRecordingService(ManagerUserId).CreateAsync(new CreateRecordingViewModel
        {
            SongId = _songId,
            Type = RecordingTypeEnum.General,
            ContentOwner = "Choir Test",
            DurationSeconds = 60,
            Source = RecordingSourceEnum.UploadedFile,
            File = CreateFakeFile("audio.mp3", "audio/mpeg")
        }));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task WriteRefused_SongListCreateAsync_ChoirCancelled()
    {
        await SetChoirStatusAsync(ChoirStatusEnum.Cancelled);

        var ex = Assert.ThrowsAsync<CustomException>(() => CreateSongListService(ManagerUserId).CreateAsync(new SongListViewModel
        {
            Name = "Liste Test",
            Type = SongListTypeEnum.Free,
            ChoirId = _choirId
        }));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task WriteRefused_InstructionCreateAsync_ChoirCancelled()
    {
        await SetChoirStatusAsync(ChoirStatusEnum.Cancelled);

        var ex = Assert.ThrowsAsync<CustomException>(() => CreateInstructionService(ManagerUserId).CreateAsync(new CreateInstructionViewModel
        {
            SongId = _songId,
            Title = "Instruction Test",
            Content = "Content"
        }));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task WriteRefused_ChoirMembersInviteAsync_ChoirCancelled()
    {
        await SetChoirStatusAsync(ChoirStatusEnum.Cancelled);

        var ex = Assert.ThrowsAsync<CustomException>(() => CreateChoirMembersService(ManagerUserId).InviteAsync(_choirId, new InviteMemberViewModel
        {
            ChoirId = _choirId,
            Email = "nouveau-member@t.com"
        }));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    // --- Defaut A1 : ChoirService.AddMemberAsync/RemoveMemberAsync n'etaient pas ------
    // --- cables sur EnsureCanWriteAsync, a la difference des sept autres services -----------

    [Test]
    public async Task WriteRefused_ChoirAddMemberAsync_ChoirCancelled()
    {
        const string newMemberUserId = "nouveau-member-ajout-annule";
        _context.Users.Add(new User { Id = newMemberUserId, UserName = "nouveau-annule@t.com", Email = "nouveau-annule@t.com" });
        await _context.SaveChangesAsync();

        await SetChoirStatusAsync(ChoirStatusEnum.Cancelled);

        var ex = Assert.ThrowsAsync<CustomException>(
            () => CreateChoirService(ManagerUserId).AddMemberAsync(_choirId, newMemberUserId));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task WriteRefused_ChoirRemoveMemberAsync_ChoirArchived()
    {
        await SetChoirStatusAsync(ChoirStatusEnum.Archived);

        // Pas de statut HTTP precis affirme ici, meme convention que
        // WriteRefused_SongCreateAsync_ChoirArchived ci-dessus : en Archive,
        // EnsureMemberActiveAsync echoue deja pour TOUT appelant (y compris le Responsable),
        // avant meme EnsureCanWriteAsync — le rejet est donc un 403, mais ce test protege
        // le comportement (refus systematique), pas le code HTTP exact de ce cas limite.
        Assert.ThrowsAsync<CustomException>(
            () => CreateChoirService(ManagerUserId).RemoveMemberAsync(_choirId, MemberSimpleUserId));
    }

    [Test]
    public async Task Write_ChoirAddMemberAsync_ChoirPublished_Works()
    {
        const string newMemberUserId = "nouveau-member-ajout-publie";
        _context.Users.Add(new User { Id = newMemberUserId, UserName = "nouveau-publie@t.com", Email = "nouveau-publie@t.com" });
        await _context.SaveChangesAsync();

        await CreateChoirService(ManagerUserId).AddMemberAsync(_choirId, newMemberUserId);

        var isMember = await _context.SpaceMembers
            .AnyAsync(m => m.ChoirId == _choirId && m.UserId == newMemberUserId);
        Assert.That(isMember, Is.True);
    }

    [Test]
    public async Task WriteRefused_EventUpdateAsync_ChoirCancelled()
    {
        var evt = await CreateAttachedEventAsync();
        await SetChoirStatusAsync(ChoirStatusEnum.Cancelled);

        var ex = Assert.ThrowsAsync<CustomException>(() => CreateEventService(ManagerUserId).UpdateAsync(new EventViewModel
        {
            Id = evt.Id,
            Title = "Title Modifie",
            StartDate = evt.StartDate,
            Type = EventTypeEnum.Concert,
            ChoirId = _choirId
        }));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    // --- Non-regression : la lecture reste ouverte en Annule -----------------------------

    [Test]
    public async Task Read_SongGetByIdAsync_ChoirCancelled_IsAllowed()
    {
        await SetChoirStatusAsync(ChoirStatusEnum.Cancelled);

        var result = await CreateSongService(MemberSimpleUserId).GetByIdAsync(_songId);

        Assert.That(result.Id, Is.EqualTo(_songId));
    }

    // --- Non-regression : Publie continue d'accepter l'ecriture --------------------------

    [Test]
    public async Task Write_SongCreateAsync_ChoirPublished_Works()
    {
        var result = await CreateSongService(ManagerUserId).CreateAsync(new SongViewModel
        {
            Title = "Nouveau Chant Publie",
            Status = SongStatusEnum.Active,
            VoiceParts = [VoicePartEnum.Alto],
            ChoirId = _choirId
        });

        Assert.That(result.Id, Is.Not.Null);
    }

    // --- Archive refuse toute ecriture, comme Annule --------------------------------------

    [Test]
    public async Task WriteRefused_SongCreateAsync_ChoirArchived()
    {
        await SetChoirStatusAsync(ChoirStatusEnum.Archived);

        Assert.ThrowsAsync<CustomException>(() => CreateSongService(ManagerUserId).CreateAsync(new SongViewModel
        {
            Title = "Nouveau Chant Archive",
            Status = SongStatusEnum.Active,
            VoiceParts = [VoicePartEnum.Soprano],
            ChoirId = _choirId
        }));
    }

    // --- Draft : statut de preparation, ouvert au seul Responsable -------------------

    [Test]
    public async Task Write_SongCreateAsync_ChoirDraft_ManagerAllowed()
    {
        await SetChoirStatusAsync(ChoirStatusEnum.Draft);

        var result = await CreateSongService(ManagerUserId).CreateAsync(new SongViewModel
        {
            Title = "Nouveau Chant Draft",
            Status = SongStatusEnum.Active,
            VoiceParts = [VoicePartEnum.Tenor],
            ChoirId = _choirId
        });

        Assert.That(result.Id, Is.Not.Null);
    }

    [Test]
    public async Task WriteRefused_SongCreateAsync_ChoirDraft_SimpleMemberRejected()
    {
        await SetChoirStatusAsync(ChoirStatusEnum.Draft);

        var ex = Assert.ThrowsAsync<CustomException>(() => CreateSongService(MemberSimpleUserId).CreateAsync(new SongViewModel
        {
            Title = "Nouveau Chant Draft Refuse",
            Status = SongStatusEnum.Active,
            VoiceParts = [VoicePartEnum.Bass],
            ChoirId = _choirId
        }));

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    // --- Fabriques -------------------------------------------------------------------------

    private async Task<Event> CreateAttachedEventAsync()
    {
        var evt = new Event
        {
            Id = ChoraleDbContext.NewIdGuid(),
            Title = "Event Rattache",
            StartDate = DateTime.UtcNow.AddDays(7),
            Location = "Salle",
            Type = EventTypeEnum.Concert,
            Status = EventStatusEnum.Published,
            ChoirId = _choirId
        };
        _context.Spaces.Add(new Space { Id = evt.Id, SpaceType = SpaceTypeEnum.Event, ClientId = _clientId });
        _context.Events.Add(evt);
        await _context.SaveChangesAsync();
        return evt;
    }

    private static IFormFile CreateFakeFile(string fileName, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes("content-test");
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "Fichier", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private IServiceProvider BuildTestServiceProvider(string userId)
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

        return services.BuildServiceProvider();
    }

    private SongService CreateSongService(string userId)
    {
        var sp = BuildTestServiceProvider(userId);
        return new SongService(
            sp,
            new MembershipService(sp),
            new ChoirAuthorizationService(sp, new MembershipService(sp)));
    }

    private ScoreService CreateScoreService(string userId)
    {
        var sp = BuildTestServiceProvider(userId);
        var choirAuthorization = new ChoirAuthorizationService(sp, new MembershipService(sp));
        return new ScoreService(
            sp,
            new ScoreFileService(new FakePathService()),
            new ScoreAuthorizationService(sp, choirAuthorization, new MembershipService(sp)),
            choirAuthorization,
            new FakeServiceLimitService(),
            new MembershipService(sp));
    }

    private RecordingService CreateRecordingService(string userId)
    {
        var sp = BuildTestServiceProvider(userId);
        var choirAuthorization = new ChoirAuthorizationService(sp, new MembershipService(sp));
        return new RecordingService(
            sp,
            new RecordingFileService(new FakePathService()),
            new RecordingAuthorizationService(sp, choirAuthorization, new MembershipService(sp)),
            choirAuthorization,
            new FakeServiceLimitService(),
            new MembershipService(sp));
    }

    private SongListService CreateSongListService(string userId)
    {
        var sp = BuildTestServiceProvider(userId);
        return new SongListService(sp, new MembershipService(sp), new ChoirAuthorizationService(sp, new MembershipService(sp)));
    }

    private InstructionService CreateInstructionService(string userId)
    {
        var sp = BuildTestServiceProvider(userId);
        return new InstructionService(sp, new MembershipService(sp));
    }

    private ChoirMembersService CreateChoirMembersService(string userId)
    {
        var sp = BuildTestServiceProvider(userId);
        return new ChoirMembersService(
            sp, new SectionService(sp), new AuditLogService(sp),
            new FakeServiceLimitService(), new MembershipService(sp),
            new UserInvitationService(sp, new FakeEmailService()), new MemberEnrollmentService(sp),
            new SectionVoicePartLookupService(_context));
    }

    private EventService CreateEventService(string userId)
    {
        var sp = BuildTestServiceProvider(userId);
        var auditLogService = new AuditLogService(sp);
        return new EventService(
            sp, new EventAuthorizationService(sp, new ChoirAuthorizationService(sp, new MembershipService(sp))), new GuestAccountLifecycleService(sp, auditLogService),
            new ClientRoleResolverService(_context), new MembershipService(sp),
            new EventParticipationSeedingService(sp));
    }

    private ChoirService CreateChoirService(string userId)
    {
        var sp = BuildTestServiceProvider(userId);
        return new ChoirService(
            sp, new AuditLogService(sp), new FakeServiceLimitService(), new MembershipService(sp),
            new ClientRoleResolverService(_context), new SpaceRoleResolverService(_context),
            new SectionService(sp));
    }
}
