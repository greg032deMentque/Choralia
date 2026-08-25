using System.Security.Claims;
using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Services.UserServices;
using ChoraleBackEnd.Test.Fakes;
using ChoraleBackEnd.ViewModels;
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
/// Liste blanche de tri de l'ecran Membres. Un `SortActive` hors liste blanche est ignore en
/// SILENCE par TriHelper : la colonne Email de l'ecran etait cliquable et ne triait rien, sans
/// le moindre signal d'echec. Ce fichier verrouille les valeurs reellement acceptees.
/// </summary>
[TestFixture]
public sealed class ChoirMembersServiceSortTests
{
    private const string ManagerUserId = "user-manager";

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
        _choirId = ChoraleDbContext.NewIdGuid();

        _context.Clients.Add(new Client { Id = clientId, Name = "Client", Status = ClientStatusEnum.Active });
        _context.Spaces.Add(new Space { Id = _choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = _choirId, ClientId = clientId, Name = "Chorale", Status = ChoirStatusEnum.Published
        });

        // Nom de famille et email en ordre INVERSE l'un de l'autre : un tri par Email qui
        // retomberait sur l'ordre par defaut (Lastname) produirait la sequence inverse, donc
        // un test qui echoue — ce qu'un jeu de donnees aligne n'aurait pas detecte.
        AddMember(ManagerUserId, "Alpha", "zoe@test.local", UserRoleEnum.Manager);
        AddMember("user-b", "Beta", "marc@test.local");
        AddMember("user-c", "Gamma", "anne@test.local");

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Test]
    public async Task GetPagedAsync_SortActiveEmail_OrdersByEmailAndNotByTheDefaultLastname()
    {
        var page = await Service().GetPagedAsync(
            _choirId, new PaginateViewModel { Page = 1, PageSize = 10, SortActive = "Email", SortDirection = "asc" });

        Assert.That(
            page.Items.Select(i => i.UserEmail),
            Is.EqualTo(new[] { "anne@test.local", "marc@test.local", "zoe@test.local" }));
    }

    [Test]
    public async Task GetPagedAsync_SortActiveName_OrdersByLastname()
    {
        var page = await Service().GetPagedAsync(
            _choirId, new PaginateViewModel { Page = 1, PageSize = 10, SortActive = "Name", SortDirection = "desc" });

        Assert.That(
            page.Items.Select(i => i.UserEmail),
            Is.EqualTo(new[] { "anne@test.local", "marc@test.local", "zoe@test.local" }),
            "Gamma, Beta, Alpha en décroissant — soit anne, marc, zoe par email");
    }

    private void AddMember(string userId, string lastname, string email, UserRoleEnum? role = null)
    {
        _context.Users.Add(new User
        {
            Id = userId, Email = email, UserName = email,
            Firstname = "Prénom", Lastname = lastname, IsActive = true, EmailConfirmed = true
        });

        var member = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), UserId = userId, SpaceId = _choirId,
            ChoirId = _choirId, Status = MemberStatusEnum.Active
        };
        _context.SpaceMembers.Add(member);

        if (role is { } assignedRole)
            _context.SpaceMemberRoles.Add(new SpaceMemberRole
            {
                Id = ChoraleDbContext.NewIdGuid(), SpaceMemberId = member.Id, Role = assignedRole
            });
    }

    private ChoirMembersService Service()
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, ManagerUserId) };
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
        services.AddSingleton<IEmailService>(new FakeEmailService());
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>().AddEntityFrameworkStores<ChoraleDbContext>();

        var sp = services.BuildServiceProvider();
        return new ChoirMembersService(
            sp, new SectionService(sp), new AuditLogService(sp),
            new FakeServiceLimitService(), new MembershipService(sp),
            new UserInvitationService(sp, new FakeEmailService()), new MemberEnrollmentService(sp),
            new SectionVoicePartLookupService(_context));
    }
}
