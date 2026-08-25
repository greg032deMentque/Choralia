using System.Security.Claims;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Services.Authorization;

/// <summary>
/// Absent avant ce lot (`ClientRoleAuthorizationHandler` n'avait aucun test dedie, contrairement
/// a <c>SpaceRoleAuthorizationHandlerTests</c>) alors que la checklist qualite du projet exige
/// explicitement les handlers d'autorisation. Couvre en priorite le nouveau repli de resolution
/// par route <c>choirId</c> (<c>ChoirMastersController</c>), et verifie par non-regression que
/// les chemins <c>clientId</c>/<c>id</c> deja en production restent inchanges.
/// </summary>
[TestFixture]
public sealed class ClientRoleAuthorizationHandlerTests
{
    private const string UserId = "user-1";

    private ChoraleDbContext _context = null!;
    private Guid _clientId;
    private Guid _otherClientId;
    private Guid _choirId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _clientId = Guid.NewGuid();
        _otherClientId = Guid.NewGuid();
        _choirId = Guid.NewGuid();

        _context.Clients.Add(new Client { Id = _clientId, Name = "Client", Status = ClientStatusEnum.Active });
        _context.Clients.Add(new Client { Id = _otherClientId, Name = "Autre Client", Status = ClientStatusEnum.Active });
        _context.Spaces.Add(new Space { Id = _choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = _clientId });
        _context.Choirs.Add(new Choir { Id = _choirId, ClientId = _clientId, Name = "Choir", Status = ChoirStatusEnum.Published });

        _context.ClientMembers.Add(new ClientMember
        {
            Id = Guid.NewGuid(), ClientId = _clientId, UserId = UserId, Role = UserRoleEnum.ClientManager
        });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task RouteClientId_ClientManagerOfTheRightClient_Succeeds()
    {
        var context = await RunHandlerAsync(routeValues: new RouteValueDictionary { ["clientId"] = _clientId.ToString() });

        Assert.That(context.HasSucceeded, Is.True);
    }

    [Test]
    public async Task RouteClientId_ClientManagerOfAnotherClient_Fails()
    {
        var context = await RunHandlerAsync(routeValues: new RouteValueDictionary { ["clientId"] = _otherClientId.ToString() });

        Assert.That(context.HasSucceeded, Is.False);
    }

    [Test]
    public async Task RouteChoirId_ClientManagerOfOwningClient_Succeeds()
    {
        // Nouveau repli ajoute pour ChoirMastersController (api/choirs/{choirId}/ChoirMasters).
        var context = await RunHandlerAsync(routeValues: new RouteValueDictionary { ["choirId"] = _choirId.ToString() });

        Assert.That(context.HasSucceeded, Is.True);
    }

    [Test]
    public async Task RouteChoirId_ClientManagerOfAnotherClient_Fails()
    {
        _context.ClientMembers.Add(new ClientMember
        {
            Id = Guid.NewGuid(), ClientId = _otherClientId, UserId = "autre-user", Role = UserRoleEnum.ClientManager
        });
        await _context.SaveChangesAsync();

        var context = await RunHandlerAsync(
            userId: "autre-user", routeValues: new RouteValueDictionary { ["choirId"] = _choirId.ToString() });

        Assert.That(context.HasSucceeded, Is.False);
    }

    [Test]
    public async Task RouteChoirId_UnknownChoir_DoesNotSucceed()
    {
        var context = await RunHandlerAsync(
            routeValues: new RouteValueDictionary { ["choirId"] = Guid.NewGuid().ToString() });

        Assert.That(context.HasSucceeded, Is.False);
    }

    [Test]
    public async Task RouteId_ExistingChoirResource_ResolvesTheResourcesClient()
    {
        // Chemin deja exerce par ChoirController.Update/Delete — non modifie par ce lot,
        // verifie ici pour garantir que l'ajout du repli "choirId" ne l'a pas casse.
        var context = await RunHandlerAsync(routeValues: new RouteValueDictionary { ["id"] = _choirId.ToString() });

        Assert.That(context.HasSucceeded, Is.True);
    }

    [Test]
    public async Task AdminClaim_SucceedsWithoutClientRole()
    {
        var context = await RunHandlerAsync(
            userId: "admin-1", isAdmin: true,
            routeValues: new RouteValueDictionary { ["clientId"] = _otherClientId.ToString() });

        Assert.That(context.HasSucceeded, Is.True);
    }

    [Test]
    public async Task NoClientIdSource_DoesNotSucceed()
    {
        var context = await RunHandlerAsync(routeValues: new RouteValueDictionary());

        Assert.That(context.HasSucceeded, Is.False);
    }

    private async Task<AuthorizationHandlerContext> RunHandlerAsync(
        RouteValueDictionary? routeValues = null, string userId = UserId, bool isAdmin = false)
    {
        var httpContext = new DefaultHttpContext();
        if (routeValues is not null)
            foreach (var (key, value) in routeValues)
                httpContext.Request.RouteValues[key] = value;

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        if (isAdmin)
            claims.Add(new Claim(ClaimTypes.Role, UserRoleEnum.Admin.ToString()));

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        httpContext.User = principal;

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var clientRoleResolverService = new ClientRoleResolverService(_context);
        var handler = new ClientRoleAuthorizationHandler(accessor, clientRoleResolverService);
        var requirement = new ClientRoleRequirement(UserRoleEnum.ClientManager);
        var authorizationContext = new AuthorizationHandlerContext([requirement], principal, null);

        await handler.HandleAsync(authorizationContext);
        return authorizationContext;
    }
}
