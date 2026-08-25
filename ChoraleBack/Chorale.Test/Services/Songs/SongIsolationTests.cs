using System;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
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
using ChoraleBackEnd.ViewModels.Songs;

namespace ChoraleBackEnd.Test.Services.Songs;

/// <summary>
/// Isolation des lists de chants entre chorales et entre clients.
/// </summary>
/// <remarks>
/// Fuite reproduite en exercant l'API : un chanteur d'une chorale recevait le repertoire de
/// <b>toutes</b> les chorales de <b>tous</b> les clients, simplement en appelant
/// <c>GetPaged</c> sans filter. Et <c>GetPagedByChoir</c> acceptait n'importe quel
/// identifiant de chorale sans verifier l'appartenance.
///
/// L'isolation existait sur la lecture unitaire (<c>GetById</c> renvoyait bien 403) et pas
/// sur les lists — c'est ce genre d'asymetrie qu'un test de liste doit desormais empecher.
/// </remarks>
[TestFixture]
public sealed class SongIsolationTests
{
    private const string MemberAUserId = "member-a";

    private ChoraleDbContext _context = null!;
    private Guid _choirA;
    private Guid _choirB;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _choirA = ChoraleDbContext.NewIdGuid();
        _choirB = ChoraleDbContext.NewIdGuid();

        _context.Users.Add(new User { Id = MemberAUserId, UserName = "a@t.com", Email = "a@t.com" });

        // Deux CLIENTS distincts : la fuite franchissait aussi cette frontiere.
        await AddChoirAsync(_choirA, "Choir A", "Chant de A");
        await AddChoirAsync(_choirB, "Choir B", "Chant secret de B");

        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = MemberAUserId,
            ChoirId = _choirA,
            SpaceId = _choirA,
            Status = MemberStatusEnum.Active
        });
        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task GetPaged_WithoutFilter_ReturnsOnlyAccessibleRepertoire()
    {
        var result = await Sut().GetPagedAsync(new SongPagedFilterViewModel { Page = 1, PageSize = 50 });

        Assert.Multiple(() =>
        {
            Assert.That(result.TotalCount, Is.EqualTo(1));
            Assert.That(result.Items[0].Title, Is.EqualTo("Chant de A"),
                "Le repertoire d'un autre client ne doit jamais remonter.");
        });
    }

    [Test]
    public void GetPagedByChoir_OnAForeignChoir_IsRejected()
    {
        var filter = new SongByChoirFilterViewModel
        {
            ChoirId = _choirB, Page = 1, PageSize = 50
        };

        Assert.ThrowsAsync<CustomException>(() => Sut().GetPagedByChoirAsync(filter),
            "Connaitre l'identifiant d'une chorale ne doit pas suffire a lire son repertoire.");
    }

    [Test]
    public async Task GetPagedByChoir_OnOwnChoir_IsAllowed()
    {
        var filter = new SongByChoirFilterViewModel
        {
            ChoirId = _choirA, Page = 1, PageSize = 50
        };

        var result = await Sut().GetPagedByChoirAsync(filter);

        Assert.That(result.TotalCount, Is.EqualTo(1));
    }

    // Le membre de la fixture n'a AUCUN role : exactement le profil qui ne doit pas ecrire.
    // `02` § Matrice reserve create/update au Responsable — avant la garde, tout chanteur
    // ecrivait dans le repertoire de sa chorale.

    [Test]
    public void Create_MemberWithoutRole_IsRejected()
    {
        var model = new SongViewModel
        {
            ChoirId = _choirA,
            Title = "Tentative",
            Status = SongStatusEnum.Active
        };

        Assert.ThrowsAsync<CustomException>(() => Sut().CreateAsync(model));
    }

    [Test]
    public async Task Delete_MemberWithoutRole_IsRejected()
    {
        var song = await _context.Songs.AsNoTracking().FirstAsync(c => c.ChoirId == _choirA);

        Assert.ThrowsAsync<CustomException>(() => Sut().DeleteAsync(song.Id));

        var reloaded = await _context.Songs.AsNoTracking().FirstAsync(c => c.Id == song.Id);
        Assert.That(reloaded.IsDeleted, Is.False, "Le chant ne doit pas avoir été archivé.");
    }

    [Test]
    public async Task Delete_SectionLeaderOfConcernedVoicePart_IsAllowed()
    {
        // Le membre devient chef de pupitre Soprano, et le chant est lié à la voix Soprano :
        // `02` § Matrice lui ouvre l'archivage de ce chant — mais toujours pas la création.
        var song = await _context.Songs.FirstAsync(c => c.ChoirId == _choirA);
        _context.SongVoiceParts.Add(new SongVoicePart
        {
            Id = ChoraleDbContext.NewIdGuid(), SongId = song.Id, VoicePart = VoicePartEnum.Soprano
        });
        _context.Sections.Add(new Section
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ChoirId = _choirA,
            VoicePart = VoicePartEnum.Soprano,
            SectionLeaderId = MemberAUserId
        });
        await _context.SaveChangesAsync();

        await Sut().DeleteAsync(song.Id);

        var reloaded = await _context.Songs.AsNoTracking()
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == song.Id);
        Assert.That(reloaded.IsDeleted, Is.True);
    }

    private async Task AddChoirAsync(Guid choirId, string name, string titleSong)
    {
        var clientId = ChoraleDbContext.NewIdGuid();
        _context.Clients.Add(new Client
        {
            Id = clientId, Name = $"Client {name}", Status = ClientStatusEnum.Active
        });
        _context.Spaces.Add(new Space { Id = choirId, ClientId = clientId, SpaceType = SpaceTypeEnum.Choir });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = choirId, ClientId = clientId, Name = name, Status = ChoirStatusEnum.Published
        });
        _context.Songs.Add(new Song
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ChoirId = choirId,
            Title = titleSong,
            Status = SongStatusEnum.Active
        });
        await _context.SaveChangesAsync();
    }

    private SongService Sut()
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, MemberAUserId)], "Test"))
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
        return new SongService(
            sp,
            new MembershipService(sp),
            new ChoirAuthorizationService(sp, new MembershipService(sp)));
    }
}
