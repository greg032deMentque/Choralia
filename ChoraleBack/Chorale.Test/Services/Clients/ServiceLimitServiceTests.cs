using System;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.ClientServices;
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
/// Les plafonds de service d'un client (`10-D23`).
/// </summary>
/// <remarks>
/// Un plafond qui n'est pas verifie a l'ecriture n'existe pas, et rien dans le code ne
/// signalerait qu'il a cesse de l'etre : la creation continuerait simplement de reussir.
/// C'est ce que ces tests protegent.
///
/// Le cas de l'abaissement sous la consommation est le plus subtil : il ne doit rien
/// amputer, seulement decline les creations suivantes (`04` § Client).
/// </remarks>
[TestFixture]
public sealed class ServiceLimitServiceTests
{
    private ChoraleDbContext _context = null!;
    private ServiceLimitService _sut = null!;
    private Guid _clientId;
    private Guid _choirId;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _clientId = ChoraleDbContext.NewIdGuid();
        _choirId = ChoraleDbContext.NewIdGuid();

        _context.Clients.Add(new Client
        {
            Id = _clientId,
            Name = "Client Test",
            Status = ClientStatusEnum.Active,
            ChoirLimit = 2,
            MemberLimit = 3,
            StorageQuotaBytes = 1000,
            MaxFileSizeBytes = 400
        });
        _context.Spaces.Add(new Space { Id = _choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = _clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = _choirId, ClientId = _clientId, Name = "Choir Test", Status = ChoirStatusEnum.Published
        });
        _context.SaveChanges();

        _sut = CreateService();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task CreateChoir_BelowCap_IsAllowed()
    {
        // 1 chorale existe, le plafond est a 2.
        Assert.DoesNotThrowAsync(() => _sut.EnsureCanCreateChoirAsync(_clientId));
        await Task.CompletedTask;
    }

    [Test]
    public async Task CreateChoir_AtExactCap_IsRejected()
    {
        await AddChoirAsync();

        var ex = Assert.ThrowsAsync<CustomException>(
            () => _sut.EnsureCanCreateChoirAsync(_clientId));

        Assert.That(ex!.Message, Does.Contain("2"),
            "Le refus doit nommer la limite atteinte, pas echouer en silence.");
    }

    [Test]
    public async Task AddMember_AtCap_IsRejected()
    {
        for (var i = 0; i < 3; i++)
            await AddMemberAsync($"user-{i}");

        Assert.ThrowsAsync<CustomException>(
            () => _sut.EnsureCanAddMemberAsync(_choirId));
    }

    [Test]
    public async Task AddMember_CapAppliesToClientNotToChoir()
    {
        // Deux chorales du meme client, trois membres repartis entre elles : le plafond
        // client est atteint alors qu'aucune chorale n'en compte trois. Repartir les
        // membres ne doit pas contourner la limite.
        var secondChoir = await AddChoirAsync();
        await AddMemberAsync("user-a");
        await AddMemberAsync("user-b");
        await AddMemberAsync("user-c", secondChoir);

        Assert.ThrowsAsync<CustomException>(
            () => _sut.EnsureCanAddMemberAsync(_choirId));
    }

    [Test]
    public void UploadFile_AboveUnitSize_IsRejected()
    {
        var ex = Assert.ThrowsAsync<CustomException>(
            () => _sut.EnsureCanUploadFileAsync(_choirId, 500));

        Assert.That(ex!.Message, Does.Contain("volumineux"));
    }

    [Test]
    public async Task UploadFile_QuotaAggregatesBothContentTypes()
    {
        // 300 en partition + 400 en enregistrement = 700 sur un quota de 1000.
        // Les deux depots testes restent sous le plafond unitaire de 400, pour que ce soit
        // bien le quota agrege qui tranche et non la taille du fichier.
        await AddScoreAsync(300);
        await AddRecordingAsync(400);

        // 700 + 300 = 1000, soit le quota exact : autorise.
        Assert.DoesNotThrowAsync(() => _sut.EnsureCanUploadFileAsync(_choirId, 300));

        // 700 + 350 = 1050 : refuse. Sans l'agregation des deux types, le total vu serait
        // de 300 ou 400 seulement, et ce depot passerait.
        var ex = Assert.ThrowsAsync<CustomException>(
            () => _sut.EnsureCanUploadFileAsync(_choirId, 350));
        Assert.That(ex!.Message, Does.Contain("Quota"));
    }

    [Test]
    public async Task Usage_AfterLoweringBelowExisting_DoesNotAmputateAnything()
    {
        await AddChoirAsync();

        var client = await _context.Clients.FirstAsync(c => c.Id == _clientId);
        client.ChoirLimit = 1;
        await _context.SaveChangesAsync();

        var usage = await _sut.GetUsageAsync(_clientId);

        Assert.Multiple(() =>
        {
            Assert.That(usage.Choirs, Is.EqualTo(2),
                "L'existant est conserve : abaisser un plafond n'ampute pas.");
            Assert.That(usage.ChoirLimit, Is.EqualTo(1));
        });

        // En revanche, toute creation nouvelle est refusee.
        Assert.ThrowsAsync<CustomException>(() => _sut.EnsureCanCreateChoirAsync(_clientId));
    }

    private async Task<Guid> AddChoirAsync()
    {
        var id = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = id, SpaceType = SpaceTypeEnum.Choir, ClientId = _clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = id, ClientId = _clientId, Name = $"Choir {id}", Status = ChoirStatusEnum.Published
        });
        await _context.SaveChangesAsync();
        return id;
    }

    private async Task AddMemberAsync(string userId, Guid? choirId = null)
    {
        var target = choirId ?? _choirId;
        _context.Users.Add(new User { Id = userId, UserName = userId, Email = $"{userId}@test.com" });
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = userId,
            ChoirId = target,
            SpaceId = target,
            Status = MemberStatusEnum.Active
        });
        await _context.SaveChangesAsync();
    }

    private async Task AddScoreAsync(long size)
    {
        var songId = ChoraleDbContext.NewIdGuid();
        _context.Songs.Add(new Song
        {
            Id = songId, ChoirId = _choirId, Title = "Chant", Status = SongStatusEnum.Active
        });
        _context.Scores.Add(new Score
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SongId = songId,
            Type = ScoreTypeEnum.General,
            Version = "v1",
            Status = ScoreStatusEnum.Published,
            OwnerUserId = "u",
            FilePath = "f.pdf",
            SizeBytes = size
        });
        await _context.SaveChangesAsync();
    }

    private async Task AddRecordingAsync(long size)
    {
        var songId = ChoraleDbContext.NewIdGuid();
        _context.Songs.Add(new Song
        {
            Id = songId, ChoirId = _choirId, Title = "Chant", Status = SongStatusEnum.Active
        });
        _context.Recordings.Add(new Recording
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SongId = songId,
            ChoirOwnerId = _choirId,
            Type = RecordingTypeEnum.General,
            Status = RecordingStatusEnum.Published,
            Source = RecordingSourceEnum.UploadedFile,
            CreatorUserId = "u",
            ContentOwner = "u",
            FilePath = "f.mp3",
            SizeBytes = size
        });
        await _context.SaveChangesAsync();
    }

    private ServiceLimitService CreateService()
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "admin-1")], "Test"))
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

        return new ServiceLimitService(services.BuildServiceProvider());
    }
}
