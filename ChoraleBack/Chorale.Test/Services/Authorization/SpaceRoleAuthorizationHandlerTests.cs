using System.Security.Claims;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Services.Authorization;

[TestFixture]
public sealed class SpaceRoleAuthorizationHandlerTests
{
    private ChoraleDbContext _context = null!;
    private const string UserId = "user-1";
    private static readonly Guid ChoirId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task Admin_claim_alone_no_longer_suffices_for_write()
    {
        // Decision produit (10-D23) : l'administration generale a acces en LECTURE a tout
        // le contenu, mais AUCUNE ecriture, meme en mode support. Cette policy scope
        // l'ecriture (Responsable/Organizer/SectionLeader) : le claim Admin seul, sans role
        // effectif sur l'espace, ne doit plus suffire â€” l'ancien bypass inconditionnel
        // permettait a l'administration d'ecrire n'importe quel contenu sans laisser de
        // trace, ce que `10-D23` interdit desormais.
        var requirement = new SpaceRoleRequirement(UserRoleEnum.Manager);
        var context = await RunHandlerAsync(
            requirement, roleClaim: UserRoleEnum.Admin.ToString(), choirIdHeader: ChoirId.ToString());

        Assert.That(context.HasSucceeded, Is.False);
    }

    [Test]
    public async Task Admin_with_effective_role_succeeds_like_any_manager()
    {
        // L'Admin qui detient reellement le role Responsable sur l'espace (parce qu'il y a
        // ete nomme, comme n'importe quel autre utilisateur) reussit — ce n'est plus le
        // claim Admin qui l'autorise, mais le role effectif.
        await SeedMemberWithRoleAsync(UserRoleEnum.Manager);
        var requirement = new SpaceRoleRequirement(UserRoleEnum.Manager);
        var context = await RunHandlerAsync(
            requirement, roleClaim: UserRoleEnum.Admin.ToString(), choirIdHeader: ChoirId.ToString());

        Assert.That(context.HasSucceeded, Is.True);
    }

    [Test]
    public async Task Missing_header_fails()
    {
        var requirement = new SpaceRoleRequirement(UserRoleEnum.Manager);
        var context = await RunHandlerAsync(requirement, choirIdHeader: null);

        Assert.That(context.HasSucceeded, Is.False);
    }

    [Test]
    public async Task Invalid_header_fails()
    {
        var requirement = new SpaceRoleRequirement(UserRoleEnum.Manager);
        var context = await RunHandlerAsync(requirement, choirIdHeader: "not-a-guid");

        Assert.That(context.HasSucceeded, Is.False);
    }

    [Test]
    public async Task Manager_via_SpaceMemberRole_succeeds()
    {
        await SeedMemberWithRoleAsync(UserRoleEnum.Manager);
        var requirement = new SpaceRoleRequirement(UserRoleEnum.Manager);
        var context = await RunHandlerAsync(requirement, choirIdHeader: ChoirId.ToString());

        Assert.That(context.HasSucceeded, Is.True);
    }

    [Test]
    public async Task Organizer_via_SpaceMemberRole_succeeds()
    {
        await SeedMemberWithRoleAsync(UserRoleEnum.Organizer);
        var requirement = new SpaceRoleRequirement(UserRoleEnum.Manager, UserRoleEnum.Organizer);
        var context = await RunHandlerAsync(requirement, choirIdHeader: ChoirId.ToString());

        Assert.That(context.HasSucceeded, Is.True);
    }

    [Test]
    public async Task SectionLeader_via_Section_succeeds()
    {
        await SeedMemberAsync();
        _context.Sections.Add(new Section
        {
            Id = Guid.NewGuid(),
            ChoirId = ChoirId,
            VoicePart = VoicePartEnum.Soprano,
            SectionLeaderId = UserId
        });
        await _context.SaveChangesAsync();

        var requirement = new SpaceRoleRequirement(UserRoleEnum.Manager, UserRoleEnum.SectionLeader);
        var context = await RunHandlerAsync(requirement, choirIdHeader: ChoirId.ToString());

        Assert.That(context.HasSucceeded, Is.True);
    }

    [Test]
    public async Task Manager_Archived_no_longer_resolves_any_role_write_rejected()
    {
        // Regression du defaut corrige (`10-D23` / correction ciblee) : un Responsable dont
        // l'appartenance est Archive ne doit plus pouvoir ecrire, alors qu'avant le filtre
        // Statut du resolveur son role Responsable restait resolu indefiniment.
        await SeedMemberWithRoleAsync(UserRoleEnum.Manager, MemberStatusEnum.Archived);
        var requirement = new SpaceRoleRequirement(UserRoleEnum.Manager);
        var context = await RunHandlerAsync(requirement, choirIdHeader: ChoirId.ToString());

        Assert.That(context.HasSucceeded, Is.False);
    }

    [Test]
    public async Task Manager_Inactive_no_longer_resolves_any_role_write_rejected()
    {
        await SeedMemberWithRoleAsync(UserRoleEnum.Manager, MemberStatusEnum.Inactive);
        var requirement = new SpaceRoleRequirement(UserRoleEnum.Manager);
        var context = await RunHandlerAsync(requirement, choirIdHeader: ChoirId.ToString());

        Assert.That(context.HasSucceeded, Is.False);
    }

    [Test]
    public async Task Member_without_scoped_role_fails()
    {
        await SeedMemberAsync();
        var requirement = new SpaceRoleRequirement(UserRoleEnum.Manager, UserRoleEnum.SectionLeader);
        var context = await RunHandlerAsync(requirement, choirIdHeader: ChoirId.ToString());

        Assert.That(context.HasSucceeded, Is.False);
    }

    [Test]
    public async Task No_membership_at_all_fails()
    {
        var requirement = new SpaceRoleRequirement(UserRoleEnum.Manager, UserRoleEnum.SectionLeader);
        var context = await RunHandlerAsync(requirement, choirIdHeader: ChoirId.ToString());

        Assert.That(context.HasSucceeded, Is.False);
    }

    [Test]
    public async Task Cumulative_Manager_and_SectionLeader_succeeds()
    {
        await SeedMemberWithRoleAsync(UserRoleEnum.Manager);
        _context.Sections.Add(new Section
        {
            Id = Guid.NewGuid(),
            ChoirId = ChoirId,
            VoicePart = VoicePartEnum.Alto,
            SectionLeaderId = UserId
        });
        await _context.SaveChangesAsync();

        var requirement = new SpaceRoleRequirement(UserRoleEnum.Manager, UserRoleEnum.SectionLeader);
        var context = await RunHandlerAsync(requirement, choirIdHeader: ChoirId.ToString());

        Assert.That(context.HasSucceeded, Is.True);
    }

    [Test]
    public async Task Header_X_Space_Id_succeeds()
    {
        await SeedMemberWithRoleAsync(UserRoleEnum.Manager);
        var requirement = new SpaceRoleRequirement(UserRoleEnum.Manager);
        var context = await RunHandlerAsync(requirement, spaceIdHeader: ChoirId.ToString(), choirIdHeader: null);

        Assert.That(context.HasSucceeded, Is.True);
    }

    [Test]
    public async Task Fallback_X_Choir_Id_used_when_X_Space_Id_absent_succeeds()
    {
        await SeedMemberWithRoleAsync(UserRoleEnum.Manager);
        var requirement = new SpaceRoleRequirement(UserRoleEnum.Manager);
        var context = await RunHandlerAsync(requirement, spaceIdHeader: null, choirIdHeader: ChoirId.ToString());

        Assert.That(context.HasSucceeded, Is.True);
    }

    private async Task<SpaceMember> SeedMemberAsync(MemberStatusEnum status = MemberStatusEnum.Active)
    {
        // Un espace de type Chorale doit avoir sa ligne Chorale et un client actif : depuis
        // `10-D23`, le resolveur de roles ne confere aucun role sur une chorale dont le
        // client n'est pas actif. Une chorale sans client est desormais un etat invalide.
        var clientId = Guid.NewGuid();
        _context.Clients.Add(new Client
        {
            Id = clientId,
            Name = $"Client Test {clientId}",
            Status = ClientStatusEnum.Active
        });
        _context.Spaces.Add(new Space { Id = ChoirId, SpaceType = SpaceTypeEnum.Choir, ClientId = clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = ChoirId,
            ClientId = clientId,
            Name = "Choir Test",
            Status = ChoirStatusEnum.Published
        });
        // Statut explicite requis : depuis la correction du resolveur, seule une appartenance
        // Active confere un role. `Status` vaut par defaut `Invite` (ordinal 0) sur une
        // instance non initialisee — le laisser implicite ferait passer tous les tests
        // "succeeds" de ce fichier pour la mauvaise raison.
        var spaceMember = new SpaceMember
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            ChoirId = ChoirId,
            SpaceId = ChoirId,
            Status = status
        };
        _context.SpaceMembers.Add(spaceMember);
        await _context.SaveChangesAsync();
        return spaceMember;
    }

    private async Task SeedMemberWithRoleAsync(UserRoleEnum role, MemberStatusEnum status = MemberStatusEnum.Active)
    {
        var spaceMember = await SeedMemberAsync(status);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = Guid.NewGuid(),
            SpaceMemberId = spaceMember.Id,
            Role = role
        });
        await _context.SaveChangesAsync();
    }

    private async Task<AuthorizationHandlerContext> RunHandlerAsync(
        SpaceRoleRequirement requirement,
        string? roleClaim = null,
        string? choirIdHeader = "",
        string? spaceIdHeader = null)
    {
        var httpContext = new DefaultHttpContext();
        if (spaceIdHeader is not null)
            httpContext.Request.Headers[SpaceRoleAuthorizationHandler.SpaceIdHeaderName] = spaceIdHeader;
        if (choirIdHeader is not null)
            httpContext.Request.Headers[SpaceRoleAuthorizationHandler.ChoirIdHeaderName] = choirIdHeader;

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, UserId) };
        if (roleClaim is not null)
            claims.Add(new Claim(ClaimTypes.Role, roleClaim));

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        httpContext.User = principal;

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var spaceRoleResolverService = new SpaceRoleResolverService(_context);
        var handler = new SpaceRoleAuthorizationHandler(accessor, spaceRoleResolverService);
        var authorizationContext = new AuthorizationHandlerContext([requirement], principal, null);

        await handler.HandleAsync(authorizationContext);
        return authorizationContext;
    }
}
