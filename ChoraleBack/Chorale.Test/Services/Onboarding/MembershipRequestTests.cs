using System.Net;
using System.Security.Claims;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.ViewModels.Onboarding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.OnboardingServices;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.Events;

namespace ChoraleBackEnd.Test.Services.Onboarding;

/// <summary>
/// Demandes d'adhesion via code de rattachement (lot 6). Le canal ne decide pas de
/// l'admission, il decide seulement si elle est deja prise — c'est le Responsable qui admet
/// ou refuse (matrice `02`).
/// </summary>
[TestFixture]
public sealed class MembershipRequestTests
{
    private const string ManagerId = "responsable-1";
    private const string SectionLeaderId = "chef-1";

    private ChoraleDbContext _context = null!;
    private Guid _choirId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        var clientId = ChoraleDbContext.NewIdGuid();
        _context.Clients.Add(new Client
        {
            Id = clientId,
            Name = "Client Test",
            Status = ClientStatusEnum.Active,
            ChoirLimit = 5,
            MemberLimit = 250,
            StorageQuotaBytes = 1_000_000,
            MaxFileSizeBytes = 100_000,
            IsDeleted = false
        });

        _choirId = ChoraleDbContext.NewIdGuid();
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = _choirId,
            ClientId = clientId,
            Name = "Choir Test",
            Status = ChoirStatusEnum.Published,
            IsDeleted = false
        });
        _context.Spaces.Add(new Space
        {
            Id = _choirId,
            SpaceType = SpaceTypeEnum.Choir,
            ClientId = clientId,
            IsDeleted = false
        });

        foreach (var voicePart in Enum.GetValues<VoicePartEnum>())
        {
            _context.Sections.Add(new Section
            {
                Id = ChoraleDbContext.NewIdGuid(),
                ChoirId = _choirId,
                VoicePart = voicePart
            });
        }

        _context.Users.Add(new User { Id = ManagerId, UserName = $"{ManagerId}@test.com", Email = $"{ManagerId}@test.com" });
        var memberManager = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = ManagerId,
            ChoirId = _choirId,
            SpaceId = _choirId,
            Status = MemberStatusEnum.Active,
            IsDeleted = false
        };
        _context.SpaceMembers.Add(memberManager);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceMemberId = memberManager.Id,
            Role = UserRoleEnum.Manager
        });

        // SectionLeader : membre actif, chef d'un des pupitres — sans role Responsable.
        _context.Users.Add(new User { Id = SectionLeaderId, UserName = $"{SectionLeaderId}@test.com", Email = $"{SectionLeaderId}@test.com" });
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = SectionLeaderId,
            ChoirId = _choirId,
            SpaceId = _choirId,
            Status = MemberStatusEnum.Active,
            IsDeleted = false
        });
        var sectionChef = _context.Sections.Local.First(p => p.VoicePart == VoicePartEnum.Alto);
        sectionChef.SectionLeaderId = SectionLeaderId;

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Test]
    public async Task RequestThenApprove_Nominal_CreatesMemberWithVoicePartAndRole()
    {
        var requesterId = await CreateRequesterAsync("demandeur-nominal@test.com", emailConfirmed: true);
        var code = await GenerateCodeActiveAsync();

        var request = await CreateServiceRequestAsync(requesterId).RequestMembershipAsync(
            new RequestMembershipViewModel { Code = code });

        var approved = await CreateServiceRequestAsync(ManagerId).ApproveAsync(
            _choirId, request.Id, new ApproveRequestViewModel { PrimaryVoicePart = VoicePartEnum.Soprano, Role = UserRoleEnum.Singer });

        Assert.That(approved.Status, Is.EqualTo(MembershipRequestStatusEnum.Approved));

        var member = await _context.SpaceMembers
            .AsNoTracking()
            .SingleAsync(m => m.UserId == requesterId && m.SpaceId == _choirId);
        var section = await _context.SectionMembers
            .AsNoTracking()
            .Include(mp => mp.Section)
            .SingleAsync(mp => mp.UserId == requesterId);

        Assert.Multiple(() =>
        {
            Assert.That(member.Status, Is.EqualTo(MemberStatusEnum.Active));
            Assert.That(section.Section.VoicePart, Is.EqualTo(VoicePartEnum.Soprano));
        });
    }

    [Test]
    public async Task GetPagedAsync_RequestFromUnverifiedAccount_AbsentFromQueue()
    {
        var requesterId = await CreateRequesterAsync("non-verifie@test.com", emailConfirmed: false);
        var code = await GenerateCodeActiveAsync();
        await CreateServiceRequestAsync(requesterId).RequestMembershipAsync(new RequestMembershipViewModel { Code = code });

        var queue = await CreateServiceRequestAsync(ManagerId).GetPagedAsync(_choirId, new PaginateViewModel());

        Assert.That(queue.TotalCount, Is.EqualTo(0));
    }

    [Test]
    public async Task RequestMembershipAsync_ExistingPendingRequest_IsRejected()
    {
        var requesterId = await CreateRequesterAsync("double@test.com", emailConfirmed: true);
        var code = await GenerateCodeActiveAsync();
        var service = CreateServiceRequestAsync(requesterId);
        await service.RequestMembershipAsync(new RequestMembershipViewModel { Code = code });

        var exception = Assert.ThrowsAsync<CustomException>(
            () => service.RequestMembershipAsync(new RequestMembershipViewModel { Code = code }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task RequestMembershipAsync_RecentRefusalUnder30Days_IsRejectedWithoutReason()
    {
        var requesterId = await CreateRequesterAsync("refus-recent@test.com", emailConfirmed: true);
        var code = await GenerateCodeActiveAsync();
        var requesterService = CreateServiceRequestAsync(requesterId);
        var firstRequest = await requesterService.RequestMembershipAsync(new RequestMembershipViewModel { Code = code });

        await CreateServiceRequestAsync(ManagerId).DeclineAsync(
            _choirId, firstRequest.Id, new DeclineRequestViewModel { DeclineReason = "Comportement inadapté" });

        var exception = Assert.ThrowsAsync<CustomException>(
            () => requesterService.RequestMembershipAsync(new RequestMembershipViewModel { Code = code }));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(exception.FrontMessage, Does.Not.Contain("inadapté"));
        });
    }

    [Test]
    public async Task RequestMembershipAsync_MoreThan30DaysAfterRefusal_IsAccepted()
    {
        var requesterId = await CreateRequesterAsync("refus-previous@test.com", emailConfirmed: true);
        var code = await GenerateCodeActiveAsync();
        var requesterService = CreateServiceRequestAsync(requesterId);
        var firstRequest = await requesterService.RequestMembershipAsync(new RequestMembershipViewModel { Code = code });

        await CreateServiceRequestAsync(ManagerId).DeclineAsync(
            _choirId, firstRequest.Id, new DeclineRequestViewModel());

        var refusedRequest = await _context.MembershipRequests.FirstAsync(d => d.Id == firstRequest.Id);
        refusedRequest.HandledAt = DateTime.UtcNow.AddDays(-31);
        await _context.SaveChangesAsync();

        var newRequest = await requesterService.RequestMembershipAsync(new RequestMembershipViewModel { Code = code });

        Assert.That(newRequest.Status, Is.EqualTo(MembershipRequestStatusEnum.Pending));
    }

    [Test]
    public async Task ApproveAsync_MemberCapReached_RejectsWithoutRemovingRequestFromQueue()
    {
        // La demande est deposee AVANT que le plafond ne soit atteint (soumission non
        // controlee sur le quota, decision produit) : c'est seulement au moment de
        // l'ADMISSION que le plafond, atteint entre-temps, doit bloquer.
        var requesterId = await CreateRequesterAsync("plafond@test.com", emailConfirmed: true);
        var code = await GenerateCodeActiveAsync();
        var request = await CreateServiceRequestAsync(requesterId).RequestMembershipAsync(
            new RequestMembershipViewModel { Code = code });

        var client = await _context.Clients.FirstAsync();
        client.MemberLimit = 1; // Le Responsable occupe deja la seule place.
        await _context.SaveChangesAsync();

        var managerService = CreateServiceRequestAsync(ManagerId);
        var exception = Assert.ThrowsAsync<CustomException>(() => managerService.ApproveAsync(
            _choirId, request.Id, new ApproveRequestViewModel { PrimaryVoicePart = VoicePartEnum.Soprano, Role = UserRoleEnum.Singer }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        var stillPending = await _context.MembershipRequests.AsNoTracking().SingleAsync(d => d.Id == request.Id);
        Assert.That(stillPending.Status, Is.EqualTo(MembershipRequestStatusEnum.Pending));
    }

    [Test]
    public async Task ApproveAsync_WithoutVoicePartOrRole_IsRejected()
    {
        var requesterId = await CreateRequesterAsync("sans-voix@test.com", emailConfirmed: true);
        var code = await GenerateCodeActiveAsync();
        var request = await CreateServiceRequestAsync(requesterId).RequestMembershipAsync(
            new RequestMembershipViewModel { Code = code });

        var managerService = CreateServiceRequestAsync(ManagerId);
        var exception = Assert.ThrowsAsync<CustomException>(() => managerService.ApproveAsync(
            _choirId, request.Id, new ApproveRequestViewModel { PrimaryVoicePart = null, Role = null }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task ApproveAsync_BySectionLeader_Is403()
    {
        var requesterId = await CreateRequesterAsync("chef-tente@test.com", emailConfirmed: true);
        var code = await GenerateCodeActiveAsync();
        var request = await CreateServiceRequestAsync(requesterId).RequestMembershipAsync(
            new RequestMembershipViewModel { Code = code });

        var sectionLeaderService = CreateServiceRequestAsync(SectionLeaderId);
        var exception = Assert.ThrowsAsync<CustomException>(() => sectionLeaderService.ApproveAsync(
            _choirId, request.Id, new ApproveRequestViewModel { PrimaryVoicePart = VoicePartEnum.Soprano, Role = UserRoleEnum.Singer }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task CancelAsync_ByTheRequester_LeavesTheQueue()
    {
        var requesterId = await CreateRequesterAsync("annule@test.com", emailConfirmed: true);
        var code = await GenerateCodeActiveAsync();
        var requesterService = CreateServiceRequestAsync(requesterId);
        var request = await requesterService.RequestMembershipAsync(new RequestMembershipViewModel { Code = code });

        await requesterService.CancelAsync(request.Id);

        var cancelledRequest = await _context.MembershipRequests.AsNoTracking().SingleAsync(d => d.Id == request.Id);
        Assert.That(cancelledRequest.Status, Is.EqualTo(MembershipRequestStatusEnum.Cancelled));

        var queue = await CreateServiceRequestAsync(ManagerId).GetPagedAsync(_choirId, new PaginateViewModel());
        Assert.That(queue.TotalCount, Is.EqualTo(0));
    }

    private async Task<string> CreateRequesterAsync(string email, bool emailConfirmed)
    {
        var id = Guid.NewGuid().ToString();
        _context.Users.Add(new User
        {
            Id = id,
            UserName = email,
            Email = email,
            IsActive = true,
            EmailConfirmed = emailConfirmed
        });
        await _context.SaveChangesAsync();
        return id;
    }

    private async Task<string> GenerateCodeActiveAsync()
    {
        var codeService = CreateCodeServiceAsync(ManagerId);
        var genere = await codeService.GenerateOrRotateAsync(_choirId);
        return genere.Code!;
    }

    private JoinCodeService CreateCodeServiceAsync(string userId)
    {
        var serviceProvider = BuildServiceProvider(userId, out _);
        return new JoinCodeService(
            serviceProvider, new SpaceRoleResolverService(_context), new ServiceLimitService(serviceProvider),
            new MemoryCache(new MemoryCacheOptions()));
    }

    private MembershipRequestService CreateServiceRequestAsync(string userId)
    {
        var serviceProvider = BuildServiceProvider(userId, out _);
        var spaceRoleResolverService = new SpaceRoleResolverService(_context);
        var serviceLimitService = new ServiceLimitService(serviceProvider);
        var joinCodeService = new JoinCodeService(
            serviceProvider, spaceRoleResolverService, serviceLimitService, new MemoryCache(new MemoryCacheOptions()));

        return new MembershipRequestService(
            serviceProvider, joinCodeService, spaceRoleResolverService, serviceLimitService,
            new MemberEnrollmentService(serviceProvider));
    }

    private IServiceProvider BuildServiceProvider(string userId, out HttpContextAccessor httpContextAccessor)
    {
        var mapper = new MapperConfiguration(
            cfg => cfg.AddMaps(typeof(EventViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

        httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test"))
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

        return services.BuildServiceProvider();
    }
}
