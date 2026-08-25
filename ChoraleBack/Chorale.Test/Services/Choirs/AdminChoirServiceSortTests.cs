using System.Security.Claims;
using AutoMapper;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Test.Fakes;
using ChoraleBackEnd.ViewModels.AdminChoirs;
using ChoraleBackEnd.Common.Enums;
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
using ChoraleBackEnd.ViewModels.Choirs;

namespace ChoraleBackEnd.Test.Services.Choirs;

/// <summary>
/// Verifie que la liste transverse des chorales pour l'administration generale respecte le
/// tri demande par le client (`SortActive`/`SortDirection`) et preserve exactement son tri
/// par defaut historique (Nom, puis Id) quand aucun tri n'est demande.
/// </summary>
[TestFixture]
public sealed class AdminChoirServiceSortTests
{
    private const string AdminUserId = "admin-1";

    private ChoraleDbContext _context = null!;
    private Guid _clientId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _clientId = ChoraleDbContext.NewIdGuid();
        _context.Users.Add(new User { Id = AdminUserId, UserName = "admin@test.com", Email = "admin@test.com" });
        _context.Clients.Add(new Client { Id = _clientId, Name = "Client Test", Status = ClientStatusEnum.Active });

        CreateChoir("Zoreille", _clientId);
        CreateChoir("Alouette", _clientId);
        CreateChoir("Mesange", _clientId);

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task GetPagedAsync_SortRequestOnAllowedColumn_EffectivelyChangesResultOrder()
    {
        var byDefault = await Sut().GetPagedAsync(new AdminChoirsPagedFilterViewModel { PageSize = 100 });
        var byNameDesc = await Sut().GetPagedAsync(
            new AdminChoirsPagedFilterViewModel { PageSize = 100, SortActive = "Name", SortDirection = "desc" });

        var orderByDefault = byDefault.Items.Select(i => i.Name).ToList();
        var orderByNameDesc = byNameDesc.Items.Select(i => i.Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(orderByNameDesc, Is.Not.EqualTo(orderByDefault));
            Assert.That(orderByNameDesc.First(), Is.EqualTo("Zoreille"));
            Assert.That(orderByNameDesc.Last(), Is.EqualTo("Alouette"));
        });
    }

    [Test]
    public async Task GetPagedAsync_NoSortActive_IdenticalToHistoricalDefaultSort()
    {
        var result = await Sut().GetPagedAsync(new AdminChoirsPagedFilterViewModel { PageSize = 100 });

        // Tri par defaut historique : Nom puis Id (voir AdminChoirService.GetPagedAsync).
        var expected = result.Items.OrderBy(i => i.Name).ThenBy(i => i.Id).Select(i => i.Id).ToList();

        Assert.That(result.Items.Select(i => i.Id).ToList(), Is.EqualTo(expected));
        Assert.That(result.Items.Select(i => i.Name).ToList(), Is.EqualTo(new[] { "Alouette", "Mesange", "Zoreille" }));
    }

    private void CreateChoir(string name, Guid clientId)
    {
        var choirId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir { Id = choirId, ClientId = clientId, Name = name, Status = ChoirStatusEnum.Published });
    }

    private AdminChoirService Sut()
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, AdminUserId),
                     new Claim(ClaimTypes.Role, nameof(UserRoleEnum.Admin))], "Test"))
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

        var serviceProvider = services.BuildServiceProvider();
        var auditLogService = new AuditLogService(serviceProvider);

        return new AdminChoirService(serviceProvider, auditLogService, new FakeServiceLimitService());
    }
}
