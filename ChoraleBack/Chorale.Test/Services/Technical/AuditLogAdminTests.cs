using System;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
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
using ChoraleBackEnd.ViewModels.Choirs;

namespace ChoraleBackEnd.Test.Services.Technical;

/// <summary>
/// Traçabilité des écritures admin (<c>AuditLogService</c>, lot 3) : chaque écriture admin
/// doit produire une ligne, un échec ne doit jamais en produire une, et la ligne survit à la
/// disparition de l'entité visée — <c>AdminAuditLog.EntityId</c> est un simple <c>string</c>,
/// sans clé étrangère vers elle.
/// </summary>
[TestFixture]
public sealed class AuditLogAdminTests
{
    private const string AdminUserId = "admin-1";

    private ChoraleDbContext _context = null!;
    private Guid _clientId;
    private Guid _choirId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _clientId = ChoraleDbContext.NewIdGuid();
        _choirId = ChoraleDbContext.NewIdGuid();

        _context.Users.Add(new User { Id = AdminUserId, UserName = "admin@test.com", Email = "admin@test.com" });
        _context.Clients.Add(new Client
        {
            Id = _clientId, Name = "Client Test", Status = ClientStatusEnum.Active,
            ChoirLimit = 5, MemberLimit = 250, StorageQuotaBytes = 1_000_000, MaxFileSizeBytes = 100_000
        });
        _context.Spaces.Add(new Space { Id = _choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = _clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir { Id = _choirId, ClientId = _clientId, Name = "Choir Test", Status = ChoirStatusEnum.Published });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task ChangeStatusAsync_SuccessfulWrite_ProducesAnAuditRow()
    {
        await AdminChoirServiceSut().ChangeStatusAsync(_choirId, ChoirStatusEnum.Archived);

        var row = await _context.AdminAuditLogs.FirstOrDefaultAsync(
            a => a.Action == "AdminChoirStatusChanged" && a.EntityId == _choirId.ToString());

        Assert.That(row, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(row!.UserId, Is.EqualTo(AdminUserId));
            Assert.That(row.EntityType, Is.EqualTo(nameof(Data.Entities.Choir)));
        });
    }

    [Test]
    public async Task ChangeStatusAsync_FailureOnForbiddenTransition_AddsNoExtraRow()
    {
        await AdminChoirServiceSut().ChangeStatusAsync(_choirId, ChoirStatusEnum.Archived);

        var before = await _context.AdminAuditLogs.CountAsync(a => a.Action == "AdminChoirStatusChanged");

        // Archive -> Annule n'est pas une transition autorisee (ChoraleEtatHelper) : l'echec
        // ne doit produire aucune ligne d'audit supplementaire.
        Assert.ThrowsAsync<CustomException>(
            () => AdminChoirServiceSut().ChangeStatusAsync(_choirId, ChoirStatusEnum.Cancelled));

        var after = await _context.AdminAuditLogs.CountAsync(a => a.Action == "AdminChoirStatusChanged");
        Assert.That(after, Is.EqualTo(before), "Un échec ne doit jamais produire de ligne d'audit supplémentaire.");
    }

    [Test]
    public async Task AllRows_UserIdAndEntityIdAreNeverNull()
    {
        await AdminChoirServiceSut().ChangeStatusAsync(_choirId, ChoirStatusEnum.Archived);
        await AdminChoirServiceSut().ChangeStatusAsync(_choirId, ChoirStatusEnum.Published);

        var rows = await _context.AdminAuditLogs.ToListAsync();
        Assert.That(rows, Is.Not.Empty);

        Assert.Multiple(() =>
        {
            foreach (var row in rows)
            {
                Assert.That(row.UserId, Is.Not.Null.And.Not.Empty);
                Assert.That(row.EntityId, Is.Not.Null.And.Not.Empty);
            }
        });
    }

    [Test]
    public async Task AuditRow_IsKeptAfterTheTargetEntityDisappears()
    {
        await AdminChoirServiceSut().ChangeStatusAsync(_choirId, ChoirStatusEnum.Archived);

        // AdminAuditLog ne porte aucune clé étrangère vers l'entité visée (EntityId est un
        // simple string) : simuler sa disparition physique ne doit rien casser.
        var choir = await _context.Choirs.IgnoreQueryFilters().FirstAsync(c => c.Id == _choirId);
        var space = await _context.Spaces.FirstAsync(e => e.Id == _choirId);
        var client = await _context.Clients.IgnoreQueryFilters().FirstAsync(c => c.Id == _clientId);
        _context.Choirs.Remove(choir);
        _context.Spaces.Remove(space);
        _context.Clients.Remove(client);
        await _context.SaveChangesAsync();

        var row = await _context.AdminAuditLogs.FirstOrDefaultAsync(
            a => a.Action == "AdminChoirStatusChanged" && a.EntityId == _choirId.ToString());
        Assert.That(row, Is.Not.Null, "La ligne d'audit doit survivre à la disparition de l'entité qu'elle documente.");
    }

    private AdminChoirService AdminChoirServiceSut()
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

        var sp = services.BuildServiceProvider();
        return new AdminChoirService(sp, new AuditLogService(sp), new ServiceLimitService(sp));
    }
}
