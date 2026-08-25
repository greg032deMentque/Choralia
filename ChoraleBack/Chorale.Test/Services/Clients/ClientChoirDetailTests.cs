using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Test.Fakes;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using ChoraleBackEnd.ViewModels.Choirs;

namespace ChoraleBackEnd.Test.Services.Clients;

/// <summary>
/// Fiche détail d'une chorale du client (<c>ClientService.GetChoirAsync</c>, `10-D23`) —
/// écran de détail de la zone « Ma structure », préalable à
/// <see cref="ClientChoirStatusTests"/> pour le changement de statut.
/// </summary>
[TestFixture]
public sealed class ClientChoirDetailTests
{
    private const string ManagerClientUserId = "resp-client-1";
    private const string MemberUserId = "member-1";

    private ChoraleDbContext _context = null!;
    private Guid _clientId;
    private Guid _otherClientId;
    private Guid _choirId;
    private Guid _choirOtherClientId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _clientId = ChoraleDbContext.NewIdGuid();
        _otherClientId = ChoraleDbContext.NewIdGuid();
        _choirId = ChoraleDbContext.NewIdGuid();
        _choirOtherClientId = ChoraleDbContext.NewIdGuid();

        _context.Users.Add(new User { Id = ManagerClientUserId, UserName = "rc@test.com", Email = "rc@test.com" });
        _context.Users.Add(new User { Id = MemberUserId, UserName = "member@test.com", Email = "member@test.com" });

        _context.Clients.Add(new Client
        {
            Id = _clientId, Name = "Client A", Status = ClientStatusEnum.Active,
            ChoirLimit = 5, MemberLimit = 250, StorageQuotaBytes = 1_000_000, MaxFileSizeBytes = 100_000
        });
        _context.Clients.Add(new Client
        {
            Id = _otherClientId, Name = "Client B", Status = ClientStatusEnum.Active,
            ChoirLimit = 5, MemberLimit = 250, StorageQuotaBytes = 1_000_000, MaxFileSizeBytes = 100_000
        });

        _context.ClientMembers.Add(new ClientMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ClientId = _clientId,
            UserId = ManagerClientUserId,
            Role = UserRoleEnum.ClientManager
        });

        _context.Spaces.Add(new Space { Id = _choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = _clientId });
        _context.Choirs.Add(new Choir
        {
            Id = _choirId, ClientId = _clientId, Name = "Choir A", Status = ChoirStatusEnum.Published
        });

        _context.Spaces.Add(new Space { Id = _choirOtherClientId, SpaceType = SpaceTypeEnum.Choir, ClientId = _otherClientId });
        _context.Choirs.Add(new Choir
        {
            Id = _choirOtherClientId, ClientId = _otherClientId, Name = "Choir B", Status = ChoirStatusEnum.Published
        });

        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = MemberUserId,
            ChoirId = _choirId,
            SpaceId = _choirId,
            Status = MemberStatusEnum.Active
        });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task GetChoirAsync_ClientManagerOfOwnClient_ReturnsDetailWithIndicators()
    {
        var detail = await Sut(ManagerClientUserId).GetChoirAsync(_clientId, _choirId);

        Assert.Multiple(() =>
        {
            Assert.That(detail.Id, Is.EqualTo(_choirId));
            Assert.That(detail.Name, Is.EqualTo("Choir A"));
            Assert.That(detail.Status, Is.EqualTo(ChoirStatusEnum.Published));
            Assert.That(detail.MemberCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void GetChoirAsync_ForeignClient_Is404()
    {
        // ManagerClientUserId n'est responsable que de _clientId : viser _otherClientId dans
        // la route doit être invisible, même si la chorale y existe bel et bien.
        Assert.ThrowsAsync<KeyNotFoundException>(
            () => Sut(ManagerClientUserId).GetChoirAsync(_otherClientId, _choirOtherClientId));
    }

    [Test]
    public void GetChoirAsync_ChoirIdBelongsToAnotherClient_Is404()
    {
        // Deuxième barrière IDOR : ManagerClientUserId est bien responsable de _clientId (la
        // policy HTTP le laisserait passer), mais _choirOtherClientId appartient à
        // _otherClientId — le service doit refuser même quand la route "clientId" est légitime.
        Assert.ThrowsAsync<KeyNotFoundException>(
            () => Sut(ManagerClientUserId).GetChoirAsync(_clientId, _choirOtherClientId));
    }

    [Test]
    public async Task GetChoirAsync_ArchivedChoir_StaysViewable()
    {
        var choir = await _context.Choirs.FirstAsync(c => c.Id == _choirId);
        choir.Status = ChoirStatusEnum.Archived;
        await _context.SaveChangesAsync();

        var detail = await Sut(ManagerClientUserId).GetChoirAsync(_clientId, _choirId);

        Assert.That(detail.Status, Is.EqualTo(ChoirStatusEnum.Archived));
    }

    private ClientService Sut(string userId)
    {
        var sp = BuildServiceProvider(userId);
        return new ClientService(
            sp, new AuditLogService(sp), new FakeServiceLimitService(), new ClientRoleResolverService(_context));
    }

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
