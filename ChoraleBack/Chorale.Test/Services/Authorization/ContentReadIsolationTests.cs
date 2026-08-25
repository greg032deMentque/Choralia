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
using ChoraleBackEnd.ViewModels.Scores;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Services.Authorization;

[TestFixture]
public sealed class ContentReadIsolationTests
{
    private const string InactiveManagerUserId = "inactive-manager";
    private const string InactiveCreatorUserId = "inactive-creator";
    private const string ActiveMemberUserId = "active-member";
    private const string AdminUserId = "admin";

    private ChoraleDbContext _context = null!;
    private Guid _choirId;
    private Guid _songId;
    private Guid _scoreId;
    private Guid _recordingId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        var clientId = ChoraleDbContext.NewIdGuid();
        _choirId = ChoraleDbContext.NewIdGuid();
        _songId = ChoraleDbContext.NewIdGuid();
        _scoreId = ChoraleDbContext.NewIdGuid();
        _recordingId = ChoraleDbContext.NewIdGuid();

        AddUser(InactiveManagerUserId);
        AddUser(InactiveCreatorUserId);
        AddUser(ActiveMemberUserId);
        AddUser(AdminUserId);

        _context.Clients.Add(new Client
        {
            Id = clientId,
            Name = "Isolation client",
            Status = ClientStatusEnum.Active
        });
        _context.Spaces.Add(new Space
        {
            Id = _choirId,
            ClientId = clientId,
            SpaceType = SpaceTypeEnum.Choir
        });
        _context.Choirs.Add(new Choir
        {
            Id = _choirId,
            ClientId = clientId,
            Name = "Isolation choir",
            Status = ChoirStatusEnum.Published
        });
        _context.Songs.Add(new Song
        {
            Id = _songId,
            ChoirId = _choirId,
            Title = "Isolation song",
            Status = SongStatusEnum.Active
        });

        var inactiveManager = AddChoirMembership(InactiveManagerUserId, MemberStatusEnum.Inactive);
        AddChoirMembership(InactiveCreatorUserId, MemberStatusEnum.Inactive);
        AddChoirMembership(ActiveMemberUserId, MemberStatusEnum.Active);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceMemberId = inactiveManager.Id,
            Role = UserRoleEnum.Manager
        });

        _context.Scores.Add(new Score
        {
            Id = _scoreId,
            SongId = _songId,
            Type = ScoreTypeEnum.General,
            Version = "draft",
            Status = ScoreStatusEnum.Draft,
            OwnerUserId = InactiveManagerUserId,
            FilePath = "score.pdf"
        });
        _context.Recordings.Add(new Recording
        {
            Id = _recordingId,
            SongId = _songId,
            Type = RecordingTypeEnum.General,
            ChoirOwnerId = _choirId,
            CreatorUserId = InactiveCreatorUserId,
            Status = RecordingStatusEnum.Draft,
            Source = RecordingSourceEnum.RecordedInApp,
            DurationSeconds = 60,
            ContentOwner = "Inactive creator",
            FilePath = "recording.mp3"
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
    public async Task ScoreRead_InactiveManagerCannotListOrReadDraft()
    {
        var service = CreateScoreService(InactiveManagerUserId);

        var page = await service.GetPagedAsync(new ScorePagedFilterViewModel());
        var detailException = Assert.ThrowsAsync<CustomException>(() => service.GetByIdAsync(_scoreId));
        var streamException = Assert.ThrowsAsync<CustomException>(() => service.StreamAsync(_scoreId));

        Assert.Multiple(() =>
        {
            Assert.That(page.Items, Is.Empty);
            Assert.That(detailException!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(streamException!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        });
    }

    [Test]
    public async Task RecordingRead_InactiveCreatorCannotListOrReadDraft()
    {
        var service = CreateRecordingService(InactiveCreatorUserId);

        var page = await service.GetPagedAsync(new RecordingPagedFilterViewModel());
        var detailException = Assert.ThrowsAsync<CustomException>(() => service.GetByIdAsync(_recordingId));
        var streamException = Assert.ThrowsAsync<CustomException>(() => service.StreamAsync(_recordingId));

        Assert.Multiple(() =>
        {
            Assert.That(page.Items, Is.Empty);
            Assert.That(detailException!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(streamException!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        });
    }

    [TestCase(MemberStatusEnum.Invited, false)]
    [TestCase(MemberStatusEnum.Inactive, false)]
    [TestCase(MemberStatusEnum.Archived, false)]
    [TestCase(MemberStatusEnum.Active, true)]
    public async Task EventRead_RequiresActiveSpaceMembership(MemberStatusEnum status, bool expected)
    {
        var userId = $"event-member-{status}";
        var eventId = ChoraleDbContext.NewIdGuid();
        AddUser(userId);
        _context.Spaces.Add(new Space
        {
            Id = eventId,
            ClientId = _context.Choirs.Single().ClientId,
            SpaceType = SpaceTypeEnum.Event
        });
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = userId,
            ChoirId = _choirId,
            SpaceId = eventId,
            Status = status
        });
        await _context.SaveChangesAsync();

        var serviceProvider = CreateServiceProvider(userId);
        var result = await new EventAuthorizationService(
                serviceProvider,
                new ChoirAuthorizationService(serviceProvider, new MembershipService(serviceProvider)))
            .IsSpaceMemberAsync(eventId);

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public async Task PublishedContent_ActiveMemberCanListAndRead()
    {
        var score = await _context.Scores.SingleAsync(item => item.Id == _scoreId);
        score.Status = ScoreStatusEnum.Published;
        var recording = await _context.Recordings.SingleAsync(item => item.Id == _recordingId);
        recording.Status = RecordingStatusEnum.Published;
        await _context.SaveChangesAsync();

        var scoreAuthorization = CreateScoreAuthorizationService(ActiveMemberUserId);
        var scoreQuery = await scoreAuthorization.RestrictDraftVisibilityAsync(
            _context.Scores.AsNoTracking().Include(item => item.Song));
        var loadedScore = await _context.Scores.AsNoTracking().Include(item => item.Song)
            .SingleAsync(item => item.Id == _scoreId);
        var recordingService = CreateRecordingService(ActiveMemberUserId);

        var recordingPage = await recordingService.GetPagedAsync(new RecordingPagedFilterViewModel());

        Assert.Multiple(() =>
        {
            Assert.That(scoreQuery.Select(item => item.Id), Does.Contain(_scoreId));
            Assert.DoesNotThrowAsync(() => scoreAuthorization.EnsureReadAsync(loadedScore));
            Assert.That(recordingPage.Items.Select(item => item.Id), Does.Contain(_recordingId));
            Assert.DoesNotThrowAsync(() => recordingService.GetByIdAsync(_recordingId));
        });
    }

    [Test]
    public async Task ContentRead_AdminWithoutMembershipKeepsSupportAccess()
    {
        var scoreAuthorization = CreateScoreAuthorizationService(AdminUserId, isAdmin: true);
        var scoreQuery = await scoreAuthorization.RestrictDraftVisibilityAsync(
            _context.Scores.AsNoTracking().Include(item => item.Song));
        var score = await _context.Scores.AsNoTracking().Include(item => item.Song)
            .SingleAsync(item => item.Id == _scoreId);
        var recordingService = CreateRecordingService(AdminUserId, isAdmin: true);

        var recordingPage = await recordingService.GetPagedAsync(new RecordingPagedFilterViewModel());

        Assert.Multiple(() =>
        {
            Assert.That(scoreQuery.Select(item => item.Id), Does.Contain(_scoreId));
            Assert.DoesNotThrowAsync(() => scoreAuthorization.EnsureReadAsync(score));
            Assert.That(recordingPage.Items.Select(item => item.Id), Does.Contain(_recordingId));
            Assert.DoesNotThrowAsync(() => recordingService.GetByIdAsync(_recordingId));
        });
    }

    private void AddUser(string userId)
        => _context.Users.Add(new User
        {
            Id = userId,
            UserName = $"{userId}@test.local",
            Email = $"{userId}@test.local"
        });

    private SpaceMember AddChoirMembership(string userId, MemberStatusEnum status)
    {
        var membership = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = userId,
            ChoirId = _choirId,
            SpaceId = _choirId,
            Status = status
        };
        _context.SpaceMembers.Add(membership);
        return membership;
    }

    private ScoreAuthorizationService CreateScoreAuthorizationService(string userId, bool isAdmin = false)
    {
        var serviceProvider = CreateServiceProvider(userId, isAdmin);
        var membershipService = new MembershipService(serviceProvider);
        return new ScoreAuthorizationService(
            serviceProvider,
            new ChoirAuthorizationService(serviceProvider, membershipService),
            membershipService);
    }

    private ScoreService CreateScoreService(string userId, bool isAdmin = false)
    {
        var serviceProvider = CreateServiceProvider(userId, isAdmin);
        var membershipService = new MembershipService(serviceProvider);
        var choirAuthorization = new ChoirAuthorizationService(serviceProvider, membershipService);
        return new ScoreService(
            serviceProvider,
            new ScoreFileService(new StubPathService()),
            new ScoreAuthorizationService(serviceProvider, choirAuthorization, membershipService),
            choirAuthorization,
            new FakeServiceLimitService(),
            membershipService);
    }

    private RecordingService CreateRecordingService(string userId, bool isAdmin = false)
    {
        var serviceProvider = CreateServiceProvider(userId, isAdmin);
        var membershipService = new MembershipService(serviceProvider);
        var choirAuthorization = new ChoirAuthorizationService(serviceProvider, membershipService);
        return new RecordingService(
            serviceProvider,
            new RecordingFileService(new StubPathService()),
            new RecordingAuthorizationService(serviceProvider, choirAuthorization, membershipService),
            choirAuthorization,
            new FakeServiceLimitService(),
            membershipService);
    }

    private IServiceProvider CreateServiceProvider(string userId, bool isAdmin = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        if (isAdmin)
            claims.Add(new Claim(ClaimTypes.Role, nameof(UserRoleEnum.Admin)));

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
        var mapper = new MapperConfiguration(
            configuration => configuration.AddMaps(typeof(ScoreViewModel).Assembly),
            NullLoggerFactory.Instance).CreateMapper();
        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton(mapper);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<ILogService>(new LogService(httpContextAccessor));
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>().AddEntityFrameworkStores<ChoraleDbContext>();
        return services.BuildServiceProvider();
    }

    private sealed class StubPathService : IPathService
    {
        public string GetFilePath(string fileName) => fileName;

        public string SanitizeFileName(string name) => name;
    }
}
