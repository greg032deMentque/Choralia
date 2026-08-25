using System;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.ViewModels.Choirs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Services.Choirs;

/// <summary>
/// Isolation entre chorales sur les pupitres. <c>UpdateLeaderAsync</c> ne controlait rien :
/// il chargeait la section par son seul id et ecrivait. La policy HTTP
/// <c>ChoirManagerOrSectionLeader</c> ne compense pas, car
/// <c>SpaceRoleAuthorizationHandler</c> resout le role sur l'espace du header
/// <c>X-Space-Id</c>, jamais sur la chorale reelle de la section visee — un Manager de la
/// chorale A pouvait donc reaffecter le chef de pupitre d'une chorale B appartenant a un
/// autre client.
/// </summary>
[TestFixture]
public sealed class SectionIsolationTests
{
    private const string ManagerChoirAUserId = "manager-choir-a";
    private const string MemberChoirBUserId = "member-choir-b";

    private ChoraleDbContext _context = null!;
    private Guid _choirAId;
    private Guid _choirBId;
    private Guid _sectionChoirBId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        var clientAId = ChoraleDbContext.NewIdGuid();
        var clientBId = ChoraleDbContext.NewIdGuid();
        _choirAId = ChoraleDbContext.NewIdGuid();
        _choirBId = ChoraleDbContext.NewIdGuid();
        _sectionChoirBId = ChoraleDbContext.NewIdGuid();

        _context.Users.Add(new User { Id = ManagerChoirAUserId, UserName = "a@test.com", Email = "a@test.com" });
        _context.Users.Add(new User { Id = MemberChoirBUserId, UserName = "b@test.com", Email = "b@test.com" });

        foreach (var (clientId, choirId, name) in new[]
                 {
                     (clientAId, _choirAId, "Choir A"),
                     (clientBId, _choirBId, "Choir B")
                 })
        {
            _context.Clients.Add(new Client
            {
                Id = clientId, Name = $"Client {name}", Status = ClientStatusEnum.Active,
                ChoirLimit = 5, MemberLimit = 250, StorageQuotaBytes = 1_000_000, MaxFileSizeBytes = 100_000
            });
            _context.Spaces.Add(new Space { Id = choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = clientId });
            _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
            {
                Id = choirId, ClientId = clientId, Name = name, Status = ChoirStatusEnum.Published
            });
        }

        // L'attaquant est membre actif de la chorale A, et de A seulement.
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirAId, SpaceId = _choirAId,
            UserId = ManagerChoirAUserId, Status = MemberStatusEnum.Active, IsDeleted = false
        });

        // Le pupitre vise appartient a la chorale B, avec un membre eligible au role de chef.
        _context.Sections.Add(new Section
        {
            Id = _sectionChoirBId, ChoirId = _choirBId, VoicePart = VoicePartEnum.Soprano
        });
        _context.SectionMembers.Add(new SectionMember
        {
            Id = ChoraleDbContext.NewIdGuid(), SectionId = _sectionChoirBId, UserId = MemberChoirBUserId
        });
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirBId, SpaceId = _choirBId,
            UserId = MemberChoirBUserId, Status = MemberStatusEnum.Active, IsDeleted = false
        });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public void UpdateLeaderAsync_CallerOutsideChoir_ThrowsForbidden()
    {
        var exception = Assert.ThrowsAsync<CustomException>(
            () => SectionServiceSut(ManagerChoirAUserId)
                .UpdateLeaderAsync(_sectionChoirBId, MemberChoirBUserId));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task UpdateLeaderAsync_ActiveChoirMember_Succeeds()
    {
        await SectionServiceSut(MemberChoirBUserId)
            .UpdateLeaderAsync(_sectionChoirBId, MemberChoirBUserId);

        var section = await _context.Sections.AsNoTracking().FirstAsync(p => p.Id == _sectionChoirBId);
        Assert.That(section.SectionLeaderId, Is.EqualTo(MemberChoirBUserId));
    }

    [Test]
    public async Task UpdateLeaderAsync_NonActiveMember_ThrowsForbidden()
    {
        // Un compte Invited (jamais reclame), Inactive ou Archived n'a plus acces au pupitre.
        var membership = await _context.SpaceMembers
            .FirstAsync(m => m.ChoirId == _choirBId && m.UserId == MemberChoirBUserId);
        membership.Status = MemberStatusEnum.Inactive;
        await _context.SaveChangesAsync();

        var exception = Assert.ThrowsAsync<CustomException>(
            () => SectionServiceSut(MemberChoirBUserId)
                .UpdateLeaderAsync(_sectionChoirBId, MemberChoirBUserId));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    private SectionService SectionServiceSut(string userId) => new(BuildServiceProvider(userId));

    private IServiceProvider BuildServiceProvider(string userId)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test"))
            }
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
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>().AddEntityFrameworkStores<ChoraleDbContext>();

        return services.BuildServiceProvider();
    }
}
