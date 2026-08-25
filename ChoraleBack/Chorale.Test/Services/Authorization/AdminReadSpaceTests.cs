using System.Security.Claims;
using AutoMapper;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using ChoraleBackEnd.ViewModels.Choirs;

namespace ChoraleBackEnd.Test.Services.Authorization;

/// <summary>
/// Mode support de l'administration generale (`10-D23`) : acces en LECTURE a tout espace
/// (chorale ou evenement), AUCUNE ecriture de contenu, et une trace systematique de chaque
/// lecture hors appartenance.
/// </summary>
[TestFixture]
public sealed class AdminReadSpaceTests
{
    private const string AdminUserId = "admin-1";
    private const string ForeignUserId = "etranger-1";

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

        _context.Users.Add(new User { Id = AdminUserId, UserName = "admin@t.com", Email = "admin@t.com" });
        _context.Users.Add(new User { Id = ForeignUserId, UserName = "e@t.com", Email = "e@t.com" });
        _context.Clients.Add(new Client { Id = _clientId, Name = "Client", Status = ClientStatusEnum.Active });
        _context.Spaces.Add(new Space { Id = _choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = _clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = _choirId, ClientId = _clientId, Name = "Choir", Status = ChoirStatusEnum.Published
        });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task EnsureReadAsync_AdminWithoutMembership_IsAllowed()
    {
        Assert.DoesNotThrowAsync(() => SutAdmin().EnsureReadAsync(_choirId));
        await Task.CompletedTask;
    }

    [Test]
    public async Task EnsureReadAsync_EachAdminRead_ProducesExactlyOneAuditRow()
    {
        await SutAdmin().EnsureReadAsync(_choirId);

        var rows = await _context.AdminAuditLogs
            .CountAsync(a => a.EntityId == _choirId.ToString() && a.EntityType == nameof(Space));

        Assert.That(rows, Is.EqualTo(1));
    }

    [Test]
    public async Task EnsureReadAsync_TwoAdminReads_ProduceTwoAuditRows()
    {
        var sut = SutAdmin();
        await sut.EnsureReadAsync(_choirId);
        await sut.EnsureReadAsync(_choirId);

        var rows = await _context.AdminAuditLogs
            .CountAsync(a => a.EntityId == _choirId.ToString() && a.EntityType == nameof(Space));

        Assert.That(rows, Is.EqualTo(2));
    }

    [Test]
    public void EnsureReadAsync_NonAdminWithoutMembership_Throws404NotForbidden()
    {
        var exception = Assert.ThrowsAsync<KeyNotFoundException>(
            () => SutForUser(ForeignUserId).EnsureReadAsync(_choirId));

        Assert.That(exception, Is.Not.Null);
    }

    [Test]
    public async Task EnsureReadAsync_AdminOnSuspendedClient_ReadAllowed()
    {
        var client = await _context.Clients.FirstAsync(c => c.Id == _clientId);
        client.Status = ClientStatusEnum.Suspended;
        await _context.SaveChangesAsync();

        Assert.DoesNotThrowAsync(() => SutAdmin().EnsureReadAsync(_choirId));
    }

    [Test]
    public async Task SpaceRoleAuthorizationHandler_AdminOnSuspendedClient_WriteRejected()
    {
        var client = await _context.Clients.FirstAsync(c => c.Id == _clientId);
        client.Status = ClientStatusEnum.Suspended;
        await _context.SaveChangesAsync();

        var context = await RunHandlerAsync(
            new SpaceRoleRequirement(UserRoleEnum.Manager),
            roleClaim: UserRoleEnum.Admin.ToString(),
            choirIdHeader: _choirId.ToString());

        Assert.That(context.HasSucceeded, Is.False);
    }

    [Test]
    public async Task SpaceRoleAuthorizationHandler_AdminWriteOnSpaceWithNoRole_IsRejected()
    {
        var context = await RunHandlerAsync(
            new SpaceRoleRequirement(UserRoleEnum.Manager),
            roleClaim: UserRoleEnum.Admin.ToString(),
            choirIdHeader: _choirId.ToString());

        Assert.That(context.HasSucceeded, Is.False);
    }

    private ISpaceAccessAuditService SutAdmin() => SutForUser(AdminUserId, isAdmin: true);

    private ISpaceAccessAuditService SutForUser(string userId, bool isAdmin = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        if (isAdmin)
            claims.Add(new Claim(ClaimTypes.Role, UserRoleEnum.Admin.ToString()));

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
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>().AddEntityFrameworkStores<ChoraleDbContext>();

        var serviceProvider = services.BuildServiceProvider();
        var auditLogService = new AuditLogService(serviceProvider);
        var spaceRoleResolverService = new SpaceRoleResolverService(_context);

        return new SpaceAccessAuditService(serviceProvider, auditLogService, spaceRoleResolverService);
    }

    private async Task<AuthorizationHandlerContext> RunHandlerAsync(
        SpaceRoleRequirement requirement, string? roleClaim, string? choirIdHeader)
    {
        var httpContext = new DefaultHttpContext();
        if (choirIdHeader is not null)
            httpContext.Request.Headers[SpaceRoleAuthorizationHandler.ChoirIdHeaderName] = choirIdHeader;

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, AdminUserId) };
        if (roleClaim is not null)
            claims.Add(new Claim(ClaimTypes.Role, roleClaim));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        httpContext.User = principal;

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var spaceRoleResolverService = new SpaceRoleResolverService(_context);
        var handler = new SpaceRoleAuthorizationHandler(accessor, spaceRoleResolverService);
        var authorizationContext = new AuthorizationHandlerContext([requirement], principal, null);

        await handler.HandleAsync(authorizationContext);
        return authorizationContext;
    }
}
