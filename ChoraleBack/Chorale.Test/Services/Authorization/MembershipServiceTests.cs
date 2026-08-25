using System;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.AuthServices;
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
/// Le point de passage unique de l'acces au contenu de chorale.
/// </summary>
/// <remarks>
/// Ce controle existait en sept exemplaires divergents. Les trois conditions ci-dessous sont
/// desormais garanties partout, et chacune protege un scenario reel :
/// un membre desactive qui garde ses droits, un client suspendu dont les chorales restent
/// lisibles, et un compte invite qui accede au contenu avant sa premiere connexion.
///
/// Aucune ne casse a la compilation si elle disparait : seul un test peut les tenir.
/// </remarks>
[TestFixture]
public sealed class MembershipServiceTests
{
    private const string MemberUserId = "member-1";
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

        _context.Users.Add(new User { Id = MemberUserId, UserName = "m@t.com", Email = "m@t.com" });
        _context.Users.Add(new User { Id = ForeignUserId, UserName = "e@t.com", Email = "e@t.com" });
        _context.Clients.Add(new Client
        {
            Id = _clientId, Name = "Client", Status = ClientStatusEnum.Active
        });
        _context.Spaces.Add(new Space { Id = _choirId, ClientId = _clientId, SpaceType = SpaceTypeEnum.Choir });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = _choirId, ClientId = _clientId, Name = "Choir", Status = ChoirStatusEnum.Published
        });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task MemberActive_ClientActive_AccessGranted()
    {
        await AddMembershipAsync(MemberStatusEnum.Active);

        Assert.That(await Sut(MemberUserId).IsMemberActiveAsync(_choirId), Is.True);
    }

    [Test]
    public async Task NonMember_AccessRejected()
    {
        await AddMembershipAsync(MemberStatusEnum.Active);

        Assert.That(await Sut(ForeignUserId).IsMemberActiveAsync(_choirId), Is.False);
    }

    [TestCase(MemberStatusEnum.Inactive,
        Description = "04 § Membre : inactive = acces revoque. Desactiver un membre n'avait "
                      + "aucun effet cote serveur.")]
    [TestCase(MemberStatusEnum.Archived)]
    [TestCase(MemberStatusEnum.Invited,
        Description = "Compte cree avant sa premiere connexion : il ne lit pas de contenu.")]
    public async Task NonActiveStatus_AccessRejected(MemberStatusEnum status)
    {
        await AddMembershipAsync(status);

        Assert.That(await Sut(MemberUserId).IsMemberActiveAsync(_choirId), Is.False);
    }

    [TestCase(ClientStatusEnum.Suspended)]
    [TestCase(ClientStatusEnum.Archived)]
    public async Task NonActiveClient_AccessRejectedEvenForActiveMember(ClientStatusEnum status)
    {
        await AddMembershipAsync(MemberStatusEnum.Active);

        var client = await _context.Clients.FirstAsync(c => c.Id == _clientId);
        client.Status = status;
        await _context.SaveChangesAsync();

        Assert.That(await Sut(MemberUserId).IsMemberActiveAsync(_choirId), Is.False,
            "La suspension d'un client doit fermer la lecture du contenu, pas seulement "
            + "les routes portant une policy scopee.");
    }

    [Test]
    public async Task EnsureMemberActive_ThrowsForbidden()
    {
        await AddMembershipAsync(MemberStatusEnum.Inactive);

        var ex = Assert.ThrowsAsync<CustomException>(
            () => Sut(MemberUserId).EnsureMemberActiveAsync(_choirId));

        Assert.That(ex!.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task ChoirsAccessible_ExcludeThoseOfASuspendedClient()
    {
        await AddMembershipAsync(MemberStatusEnum.Active);

        var before = await Sut(MemberUserId).ChoirsAccessibleAsync();
        Assert.That(before, Does.Contain(_choirId));

        var client = await _context.Clients.FirstAsync(c => c.Id == _clientId);
        client.Status = ClientStatusEnum.Suspended;
        await _context.SaveChangesAsync();

        var after = await Sut(MemberUserId).ChoirsAccessibleAsync();
        Assert.That(after, Is.Empty);
    }

    private async Task AddMembershipAsync(MemberStatusEnum status)
    {
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = MemberUserId,
            ChoirId = _choirId,
            SpaceId = _choirId,
            Status = status
        });
        await _context.SaveChangesAsync();
    }

    private MembershipService Sut(string userId)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId)], "Test"))
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

        return new MembershipService(services.BuildServiceProvider());
    }
}
