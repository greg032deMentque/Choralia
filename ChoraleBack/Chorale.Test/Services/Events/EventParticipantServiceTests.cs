using ChoraleBackEnd.ViewModels.Events;
using System.Net;
using System.Security.Claims;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Services.UserServices;
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
using ChoraleBackEnd.Test.Fakes;
using ChoraleBackEnd.Test.TestSupport;

namespace ChoraleBackEnd.Test.Services.Events;

[TestFixture]
public sealed class EventParticipantServiceTests
{
    private const string OrganizerUserId = "organisateur-1";
    private const string OtherUserId = "autre-1";
    private const string OtherUserEmail = "autre@test.com";

    private ChoraleDbContext _context = null!;
    private EventParticipantService _sut = null!;
    private FakeEmailService _fakeEmailService = null!;
    private Guid _eventId;
    private Guid _clientId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        var mapper = new MapperConfiguration(cfg => cfg.AddMaps(typeof(EventViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, OrganizerUserId)], "Test"))
            }
        };

        _fakeEmailService = new FakeEmailService();

        var configuration = new ConfigurationManager();
        configuration["Frontend:BaseUrl"] = "http://localhost:4200";

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton(mapper);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<ILogService>(new LogService(httpContextAccessor));
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>()
            .AddEntityFrameworkStores<ChoraleDbContext>()
            .AddDefaultTokenProviders()
            .AddInvitationTokenProvider();

        var serviceProvider = services.BuildServiceProvider();
        await SeedSingerRoleAsync(serviceProvider);
        var authorizationService = new EventAuthorizationService(serviceProvider, new ChoirAuthorizationService(serviceProvider, new MembershipService(serviceProvider)));
        var userInvitationService = new UserInvitationService(serviceProvider, _fakeEmailService);
        _sut = new EventParticipantService(
            serviceProvider, authorizationService, userInvitationService,
            new FakeServiceLimitService(), new MembershipService(serviceProvider), new AuditLogService(serviceProvider));

        _context.Users.Add(new User
        {
            Id = OrganizerUserId,
            UserName = "organisateur@test.com",
            NormalizedUserName = "ORGANISATEUR@TEST.COM",
            Email = "organisateur@test.com",
            NormalizedEmail = "ORGANISATEUR@TEST.COM"
        });
        _context.Users.Add(new User
        {
            Id = OtherUserId,
            UserName = OtherUserEmail,
            NormalizedUserName = OtherUserEmail.ToUpperInvariant(),
            Email = OtherUserEmail,
            NormalizedEmail = OtherUserEmail.ToUpperInvariant()
        });

        _clientId = ChoraleDbContext.NewIdGuid();
        _context.Clients.Add(new Client { Id = _clientId, Name = "Client Test", Status = ClientStatusEnum.Active });

        _eventId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = _eventId, ClientId = _clientId, SpaceType = SpaceTypeEnum.Event });
        _context.Events.Add(new Event
        {
            Id = _eventId,
            Title = "Concert de Test",
            StartDate = DateTime.UtcNow.AddDays(1),
            Type = EventTypeEnum.Concert,
            ChoirId = null
        });

        var organizerMember = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = OrganizerUserId,
            SpaceId = _eventId,
            ChoirId = null,
            Status = MemberStatusEnum.Active,
            Presence = AttendanceEnum.NoReply
        };
        _context.SpaceMembers.Add(organizerMember);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceMemberId = organizerMember.Id,
            Role = UserRoleEnum.Organizer
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
    public async Task InviteAsync_UnknownEmail_CreatesInvitedAccount()
    {
        var model = new InviteEventParticipantViewModel
        {
            EventId = _eventId,
            Email = "inconnu@test.com",
            Firstname = "Camille"
        };

        var result = await _sut.InviteAsync(model);

        var user = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == result.UserId);
        Assert.That(user.IsGuestAccount, Is.True);
        Assert.That(user.EmailConfirmed, Is.False);
        Assert.That(user.Firstname, Is.EqualTo("Camille"));
        Assert.That(_fakeEmailService.SentEmails, Has.Count.EqualTo(1));

        var isParticipant = await _context.SpaceMembers
            .AnyAsync(m => m.UserId == user.Id && m.SpaceId == _eventId);
        Assert.That(isParticipant, Is.True);
    }

    [Test]
    public async Task InviteAsync_AlreadyParticipant_ThrowsConflict()
    {
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = OtherUserId,
            SpaceId = _eventId,
            ChoirId = null,
            Status = MemberStatusEnum.Active,
            Presence = AttendanceEnum.NoReply
        });
        await _context.SaveChangesAsync();

        var model = new InviteEventParticipantViewModel
        {
            EventId = _eventId,
            Email = OtherUserEmail
        };

        var exception = Assert.ThrowsAsync<CustomException>(() => _sut.InviteAsync(model));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task InviteAsync_EventFinished_LeveBadRequest()
    {
        var eventFinished = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = eventFinished, ClientId = _clientId, SpaceType = SpaceTypeEnum.Event });
        _context.Events.Add(new Event
        {
            Id = eventFinished,
            Title = "Event Finished",
            StartDate = DateTime.UtcNow.AddDays(-2),
            EndDate = DateTime.UtcNow.AddDays(-1),
            Type = EventTypeEnum.Concert,
            ChoirId = null
        });
        var organizerMemberFinished = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = OrganizerUserId,
            SpaceId = eventFinished,
            ChoirId = null,
            Status = MemberStatusEnum.Active,
            Presence = AttendanceEnum.NoReply
        };
        _context.SpaceMembers.Add(organizerMemberFinished);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceMemberId = organizerMemberFinished.Id,
            Role = UserRoleEnum.Organizer
        });
        await _context.SaveChangesAsync();

        var model = new InviteEventParticipantViewModel
        {
            EventId = eventFinished,
            Email = OtherUserEmail
        };

        var exception = Assert.ThrowsAsync<CustomException>(() => _sut.InviteAsync(model));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task GetPagedAsync_NonMemberOfTheEvent_ThrowsForbidden()
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, OtherUserId)], "Test"))
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton(new MapperConfiguration(cfg => cfg.AddMaps(typeof(EventViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<ILogService>(new LogService(httpContextAccessor));
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>()
            .AddEntityFrameworkStores<ChoraleDbContext>()
            .AddDefaultTokenProviders()
            .AddInvitationTokenProvider();

        var otherUserServiceProvider = services.BuildServiceProvider();
        await SeedSingerRoleAsync(otherUserServiceProvider);
        var otherUserAuthorizationService = new EventAuthorizationService(otherUserServiceProvider, new ChoirAuthorizationService(otherUserServiceProvider, new MembershipService(otherUserServiceProvider)));
        var otherUserInvitationService = new UserInvitationService(otherUserServiceProvider, new FakeEmailService());
        var otherUserSut = new EventParticipantService(
            otherUserServiceProvider, otherUserAuthorizationService, otherUserInvitationService,
            new FakeServiceLimitService(), new MembershipService(otherUserServiceProvider), new AuditLogService(otherUserServiceProvider));

        var filter = new EventParticipantsPagedFilterViewModel { EventId = _eventId };

        var exception = Assert.ThrowsAsync<CustomException>(() => otherUserSut.GetPagedAsync(filter));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task RsvpAsync_EventFinished_LeveBadRequest()
    {
        var eventFinished = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = eventFinished, ClientId = _clientId, SpaceType = SpaceTypeEnum.Event });
        _context.Events.Add(new Event
        {
            Id = eventFinished,
            Title = "Event Finished",
            StartDate = DateTime.UtcNow.AddDays(-2),
            EndDate = DateTime.UtcNow.AddDays(-1),
            Type = EventTypeEnum.Concert,
            ChoirId = null
        });
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = OrganizerUserId,
            SpaceId = eventFinished,
            ChoirId = null,
            Status = MemberStatusEnum.Active,
            Presence = AttendanceEnum.NoReply
        });
        await _context.SaveChangesAsync();

        var model = new EventRsvpViewModel
        {
            EventId = eventFinished,
            Presence = AttendanceEnum.Attending
        };

        var exception = Assert.ThrowsAsync<CustomException>(() => _sut.RsvpAsync(model));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task RsvpAsync_NonParticipant_LeveForbidden()
    {
        var otherEventId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = otherEventId, ClientId = _clientId, SpaceType = SpaceTypeEnum.Event });
        _context.Events.Add(new Event
        {
            Id = otherEventId,
            Title = "Event Sans Le Currentuser",
            StartDate = DateTime.UtcNow.AddDays(1),
            Type = EventTypeEnum.Concert,
            ChoirId = null
        });
        await _context.SaveChangesAsync();

        var model = new EventRsvpViewModel
        {
            EventId = otherEventId,
            Presence = AttendanceEnum.Attending
        };

        var exception = Assert.ThrowsAsync<CustomException>(() => _sut.RsvpAsync(model));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    private static async Task SeedSingerRoleAsync(IServiceProvider serviceProvider)
    {
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync(UserRoleEnum.Singer.ToString()))
            await roleManager.CreateAsync(new IdentityRole(UserRoleEnum.Singer.ToString()));
    }
}
