using System.Security.Claims;
using AutoMapper;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.ViewModels.AdminSongs;
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

namespace ChoraleBackEnd.Test.Services.Songs;

/// <summary>
/// Catalogue transverse des chants (lot 4) : une ligne par groupe d'AFFICHAGE (voir
/// <c>SongKeyHelper</c>), jamais une ligne par <c>Song</c>. Le regroupement a lieu
/// entierement en memoire avant toute pagination — voir <see cref="AdminSongService"/> —
/// c'est ce que ce fichier verifie en priority : un groupe ne doit jamais etre coupe entre
/// deux pages.
/// </summary>
[TestFixture]
public sealed class AdminSongCatalogueTests
{
    private ChoraleDbContext _context = null!;
    private AdminSongService _sut = null!;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        var clientActiveId = ChoraleDbContext.NewIdGuid();
        var suspendedClientId = ChoraleDbContext.NewIdGuid();

        _context.Clients.Add(new Client { Id = clientActiveId, Name = "Client Active", Status = ClientStatusEnum.Active });
        _context.Clients.Add(new Client { Id = suspendedClientId, Name = "Client Suspendu", Status = ClientStatusEnum.Suspended });

        // --- Group "Ave Maria" / Gounod, porte par 7 chorales -----------------------------
        for (var i = 0; i < 7; i++)
        {
            var choirId = ChoraleDbContext.NewIdGuid();
            _context.Spaces.Add(new Space { Id = choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = clientActiveId });
            _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
            {
                Id = choirId, ClientId = clientActiveId, Name = $"Choir Ave Maria {i}", Status = ChoirStatusEnum.Published
            });
            _context.Songs.Add(new Song
            {
                Id = ChoraleDbContext.NewIdGuid(), ChoirId = choirId, Title = "Ave Maria", Composer = "Gounod",
                Status = SongStatusEnum.Active, CreatedAt = DateTime.UtcNow
            });
        }

        // --- Chant soft-delete dans une 8e chorale : ne doit JAMAIS count -----------------
        var softDeletedChoirId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = softDeletedChoirId, SpaceType = SpaceTypeEnum.Choir, ClientId = clientActiveId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = softDeletedChoirId, ClientId = clientActiveId, Name = "Choir Ave Maria Soft Delete", Status = ChoirStatusEnum.Published
        });
        _context.Songs.Add(new Song
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = softDeletedChoirId, Title = "Ave Maria", Composer = "Gounod",
            Status = SongStatusEnum.Active, CreatedAt = DateTime.UtcNow, IsDeleted = true
        });

        // --- Group "Ave Maria" / Schubert : meme title, composer different, 1 choir ---
        var choirSchubertId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = choirSchubertId, SpaceType = SpaceTypeEnum.Choir, ClientId = clientActiveId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = choirSchubertId, ClientId = clientActiveId, Name = "Choir Ave Maria Schubert", Status = ChoirStatusEnum.Published
        });
        _context.Songs.Add(new Song
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = choirSchubertId, Title = "Ave Maria", Composer = "Schubert",
            Status = SongStatusEnum.Active, CreatedAt = DateTime.UtcNow
        });

        // --- Group "Alleluia", client SUSPENDU : doit rester INCLUS pour l'admin -----------
        var suspendedClientChoirId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = suspendedClientChoirId, SpaceType = SpaceTypeEnum.Choir, ClientId = suspendedClientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = suspendedClientChoirId, ClientId = suspendedClientId, Name = "Choir Client Suspendu", Status = ChoirStatusEnum.Published
        });
        _context.Songs.Add(new Song
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = suspendedClientChoirId, Title = "Alleluia", Composer = "Haendel",
            Status = SongStatusEnum.Active, CreatedAt = DateTime.UtcNow
        });

        // --- Group "Gloria", chorale ARCHIVE : doit etre EXCLU ------------------------------
        var archivedChoirId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = archivedChoirId, SpaceType = SpaceTypeEnum.Choir, ClientId = clientActiveId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = archivedChoirId, ClientId = clientActiveId, Name = "Choir Archivee", Status = ChoirStatusEnum.Archived
        });
        _context.Songs.Add(new Song
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = archivedChoirId, Title = "Gloria", Composer = "Vivaldi",
            Status = SongStatusEnum.Active, CreatedAt = DateTime.UtcNow
        });

        // --- Group "Sanctus", une seule chorale : non-doublon -------------------------------
        var choirSanctusId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = choirSanctusId, SpaceType = SpaceTypeEnum.Choir, ClientId = clientActiveId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = choirSanctusId, ClientId = clientActiveId, Name = "Choir Sanctus", Status = ChoirStatusEnum.Published
        });
        _context.Songs.Add(new Song
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = choirSanctusId, Title = "Sanctus", Composer = "Traditionnel",
            Status = SongStatusEnum.Active, CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        _sut = CreateService();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task GetPagedCatalogueAsync_SevenChoirsSameSong_OneRowCountsSeven()
    {
        var page = await _sut.GetPagedCatalogueAsync(new AdminSongCatalogPagedFilterViewModel { Page = 1, PageSize = 50 });

        var aveMariaGounodGroup = page.Items.Single(i => i.Title == "Ave Maria" && i.Composer == "Gounod");
        Assert.That(aveMariaGounodGroup.ChoirCount, Is.EqualTo(7));
        Assert.That(aveMariaGounodGroup.OccurrenceCount, Is.EqualTo(7));
    }

    [Test]
    public async Task GetPagedCatalogueAsync_SoftDeletedSong_ExcludedFromCount()
    {
        var page = await _sut.GetPagedCatalogueAsync(new AdminSongCatalogPagedFilterViewModel { Page = 1, PageSize = 50 });

        var aveMariaGounodGroup = page.Items.Single(i => i.Title == "Ave Maria" && i.Composer == "Gounod");
        // 7 chorales actives + la 8e soft-delete NE DOIT PAS etre comptee.
        Assert.That(aveMariaGounodGroup.ChoirCount, Is.EqualTo(7));
    }

    [Test]
    public async Task GetPagedCatalogueAsync_ArchivedChoir_Excluded()
    {
        var page = await _sut.GetPagedCatalogueAsync(new AdminSongCatalogPagedFilterViewModel { Page = 1, PageSize = 50 });

        Assert.That(page.Items.Select(i => i.Title), Does.Not.Contain("Gloria"));
    }

    [Test]
    public async Task GetPagedCatalogueAsync_SuspendedClient_Included()
    {
        var page = await _sut.GetPagedCatalogueAsync(new AdminSongCatalogPagedFilterViewModel { Page = 1, PageSize = 50 });

        Assert.That(page.Items.Select(i => i.Title), Does.Contain("Alleluia"));
    }

    [Test]
    public async Task GetPagedCatalogueAsync_GroupWithOnlyOneChoir_NotFlaggedAsDuplicate()
    {
        var page = await _sut.GetPagedCatalogueAsync(new AdminSongCatalogPagedFilterViewModel { Page = 1, PageSize = 50 });

        var sanctus = page.Items.Single(i => i.Title == "Sanctus");
        Assert.That(sanctus.ChoirCount, Is.EqualTo(1));

        var duplicatesPage = await _sut.GetPagedCatalogueAsync(new AdminSongCatalogPagedFilterViewModel
        {
            Page = 1, PageSize = 50, DuplicatesOnly = true
        });
        Assert.That(duplicatesPage.Items.Select(i => i.Title), Does.Not.Contain("Sanctus"));
    }

    [Test]
    public async Task GetPagedCatalogueAsync_DuplicatesOnlyFilter_ReturnsOnlyGroupsWithMultipleChoirs()
    {
        var page = await _sut.GetPagedCatalogueAsync(new AdminSongCatalogPagedFilterViewModel
        {
            Page = 1, PageSize = 50, DuplicatesOnly = true
        });

        Assert.That(page.Items, Has.Count.EqualTo(1));
        Assert.That(page.Items.Single().Title, Is.EqualTo("Ave Maria"));
        Assert.That(page.Items.Single().Composer, Is.EqualTo("Gounod"));
    }

    [Test]
    public async Task GetPagedCatalogueAsync_Pagination_AGroupIsNeverSplitAcrossTwoPages()
    {
        // 3 groupes visibles (Gloria et le doublon soft-delete sont deja exclus des le tri
        // par defaut) : Alleluia, Ave Maria/Gounod, Ave Maria/Schubert, Sanctus = 4 groupes.
        var fullPage = await _sut.GetPagedCatalogueAsync(new AdminSongCatalogPagedFilterViewModel { Page = 1, PageSize = 50 });
        var totalGroups = fullPage.TotalCount;

        var seenKeys = new HashSet<string>();
        for (var page = 1; page <= totalGroups; page++)
        {
            var result = await _sut.GetPagedCatalogueAsync(new AdminSongCatalogPagedFilterViewModel { Page = page, PageSize = 1 });

            Assert.That(result.Items, Has.Count.EqualTo(1));
            var item = result.Items.Single();

            // Le groupe Ave Maria/Gounod doit garder son compteur intact quelle que soit la
            // page qui le contient : jamais coupe entre deux pages.
            if (item.Title == "Ave Maria" && item.Composer == "Gounod")
                Assert.That(item.ChoirCount, Is.EqualTo(7));

            Assert.That(seenKeys.Add(item.Key), Is.True, $"Cle {item.Key} vue plus d'une fois : chevauchement entre pages.");
        }

        Assert.That(seenKeys, Has.Count.EqualTo(totalGroups));
    }

    [Test]
    public async Task GetPagedCatalogueAsync_SortByTitle_IsDeterministicOnTiedValues()
    {
        var filter = new AdminSongCatalogPagedFilterViewModel { Page = 1, PageSize = 50, SortActive = "Title", SortDirection = "asc" };

        var firstCall = await _sut.GetPagedCatalogueAsync(filter);
        var secondCall = await _sut.GetPagedCatalogueAsync(filter);

        Assert.That(firstCall.Items.Select(i => i.Key), Is.EqualTo(secondCall.Items.Select(i => i.Key)));

        var gounodIndex = firstCall.Items.ToList().FindIndex(i => i.Title == "Ave Maria" && i.Composer == "Gounod");
        var schubertIndex = firstCall.Items.ToList().FindIndex(i => i.Title == "Ave Maria" && i.Composer == "Schubert");
        Assert.That(gounodIndex, Is.Not.EqualTo(-1));
        Assert.That(schubertIndex, Is.Not.EqualTo(-1));
    }

    [Test]
    public async Task GetGroupChoirsAsync_ReturnsOneRowPerChoirOfTheGroup()
    {
        var page = await _sut.GetPagedCatalogueAsync(new AdminSongCatalogPagedFilterViewModel { Page = 1, PageSize = 50 });
        var aveMariaGounodGroup = page.Items.Single(i => i.Title == "Ave Maria" && i.Composer == "Gounod");

        var groupChoirs = await _sut.GetGroupChoirsAsync(aveMariaGounodGroup.Key);

        Assert.That(groupChoirs, Has.Count.EqualTo(7));
        Assert.That(groupChoirs.Select(c => c.ClientName).Distinct().Single(), Is.EqualTo("Client Active"));
    }

    private AdminSongService CreateService()
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "admin-catalogue-chants"), new Claim(ClaimTypes.Role, UserRoleEnum.Admin.ToString())],
                    "Test"))
            }
        };

        var mapper = new MapperConfiguration(cfg => cfg.AddMaps(typeof(ChoirViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton(mapper);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<ILogService>(new LogService(httpContextAccessor));
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>().AddEntityFrameworkStores<ChoraleDbContext>();

        return new AdminSongService(services.BuildServiceProvider());
    }
}
