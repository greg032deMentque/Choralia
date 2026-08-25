using System.Net;
using System.Security.Claims;
using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels.SongLists;
using ChoraleBackEnd.ViewModels.Songs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Services.Authorization;

/// <summary>
/// Pendant ecriture de <see cref="ContentReadIsolationTests"/> : une ressource ne change jamais
/// de conteneur parce que l'appelant l'a demande dans le corps de la requete.
/// </summary>
/// <remarks>
/// Defaut ferme par ces tests : les gardes d'autorisation de <c>SongService.UpdateAsync</c> et
/// <c>SongListService.UpdateAsync</c> s'executent sur la valeur LUE EN BASE, puis
/// <c>_mapper.Map(model, entity)</c> ecrasait la cle de rattachement avec celle du corps. Le
/// controle et l'ecriture portaient sur deux valeurs differentes — le chant, ses partitions et
/// ses enregistrements basculaient chez un autre client sans qu'aucune garde ne se declenche.
///
/// Ces tests valent par leur cote negatif : ils ne verifient pas qu'une mise a jour fonctionne,
/// ils verifient qu'un rattachement etranger reste sans effet.
/// </remarks>
[TestFixture]
public sealed class ContentRepointingTests
{
    private const string UserId = "manager-choir-a";

    private ChoraleDbContext _context = null!;
    private SongService _songService = null!;
    private SongListService _songListService = null!;

    private Guid _choirA;
    private Guid _foreignChoirB;
    private Guid _foreignSectionId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        var mapper = new MapperConfiguration(
            cfg => cfg.AddMaps(typeof(SongViewModel).Assembly),
            NullLoggerFactory.Instance).CreateMapper();

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, UserId)], "Test"))
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton(mapper);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<ILogService>(new LogService(httpContextAccessor));
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>()
            .AddEntityFrameworkStores<ChoraleDbContext>();

        var serviceProvider = services.BuildServiceProvider();
        _songService = new SongService(
            serviceProvider,
            new MembershipService(serviceProvider),
            new ChoirAuthorizationService(serviceProvider, new MembershipService(serviceProvider)));
        _songListService = new SongListService(
            serviceProvider,
            new MembershipService(serviceProvider),
            new ChoirAuthorizationService(serviceProvider, new MembershipService(serviceProvider)));

        _context.Users.Add(new User { Id = UserId, UserName = "a@test.com", Email = "a@test.com" });

        // Deux chorales, deux clients distincts : c'est le pire cas, celui ou le repointage
        // faisait franchir la frontiere de facturation autant que celle de la chorale.
        _choirA = CreateChoir("Chorale A");
        _foreignChoirB = CreateChoir("Chorale B");

        // L'appelant est Responsable de A, et n'a AUCUNE relation avec B.
        var member = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ChoirId = _choirA,
            SpaceId = _choirA,
            UserId = UserId,
            Status = MemberStatusEnum.Active
        };
        _context.SpaceMembers.Add(member);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceMemberId = member.Id,
            Role = UserRoleEnum.Manager
        });

        _foreignSectionId = ChoraleDbContext.NewIdGuid();
        _context.Sections.Add(new Section
        {
            Id = _foreignSectionId, ChoirId = _foreignChoirB, VoicePart = VoicePartEnum.Soprano
        });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private Guid CreateChoir(string name)
    {
        var clientId = ChoraleDbContext.NewIdGuid();
        var choirId = ChoraleDbContext.NewIdGuid();

        _context.Clients.Add(new Client
        {
            Id = clientId, Name = $"Client {name}", Status = ClientStatusEnum.Active
        });
        _context.Spaces.Add(new Space
        {
            Id = choirId, ClientId = clientId, SpaceType = SpaceTypeEnum.Choir
        });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = choirId, ClientId = clientId, Name = name, Status = ChoirStatusEnum.Published
        });

        return choirId;
    }

    [Test]
    public async Task UpdateSong_DeclaringAForeignChoir_LeavesTheSongWhereItWas()
    {
        var created = await _songService.CreateAsync(new SongViewModel
        {
            Title = "Ave Verum",
            Status = SongStatusEnum.Active,
            VoiceParts = [VoicePartEnum.Soprano],
            ChoirId = _choirA
        });

        await _songService.UpdateAsync(new SongViewModel
        {
            Id = created.Id,
            Title = "Ave Verum (v2)",
            Status = SongStatusEnum.Active,
            VoiceParts = [VoicePartEnum.Soprano],
            ChoirId = _foreignChoirB
        });

        var stored = await _context.Songs.AsNoTracking()
            .FirstAsync(song => song.Id == created.Id);

        Assert.Multiple(() =>
        {
            Assert.That(stored.ChoirId, Is.EqualTo(_choirA),
                "Le chant a change de chorale sur simple declaration dans le corps de la requete.");
            Assert.That(stored.Title, Is.EqualTo("Ave Verum (v2)"),
                "Le contenu legitime doit rester modifiable — seul le rattachement est verrouille.");
        });
    }

    [Test]
    public async Task CreateSong_StillAttachesTheChoir_DespiteTheIgnoredMapping()
    {
        // Garde-fou du piege de ce lot : `.Ignore()` pose sans affectation explicite dans
        // CreateAsync donnerait Guid.Empty, donc une violation de cle etrangere.
        var created = await _songService.CreateAsync(new SongViewModel
        {
            Title = "Laudate Dominum",
            Status = SongStatusEnum.Active,
            VoiceParts = [VoicePartEnum.Alto],
            ChoirId = _choirA
        });

        var stored = await _context.Songs.AsNoTracking()
            .FirstAsync(song => song.Id == created.Id);

        Assert.That(stored.ChoirId, Is.EqualTo(_choirA));
    }

    [Test]
    public async Task UpdateSongList_DeclaringForeignKeys_LeavesTheListWhereItWas()
    {
        var created = await _songListService.CreateAsync(new SongListViewModel
        {
            Name = "Dimanche",
            Type = SongListTypeEnum.Free,
            ChoirId = _choirA
        });

        await _songListService.UpdateAsync(new SongListViewModel
        {
            Id = created.Id,
            Name = "Dimanche (v2)",
            Type = SongListTypeEnum.Free,
            ChoirId = _foreignChoirB
        });

        var stored = await _context.SongLists.AsNoTracking()
            .FirstAsync(songList => songList.Id == created.Id);

        Assert.Multiple(() =>
        {
            Assert.That(stored.ChoirId, Is.EqualTo(_choirA),
                "La liste a change de chorale sur simple declaration dans le corps de la requete.");
            Assert.That(stored.Name, Is.EqualTo("Dimanche (v2)"));
        });
    }

    [Test]
    public async Task UpdateSongList_DeclaringAForeignSection_LeavesTheListWhereItWas()
    {
        var created = await _songListService.CreateAsync(new SongListViewModel
        {
            Name = "Dimanche",
            Type = SongListTypeEnum.Free,
            ChoirId = _choirA
        });

        // SectionId seul : ValidateMembershipAsync refuse ChoirId et SectionId ensemble.
        await _songListService.UpdateAsync(new SongListViewModel
        {
            Id = created.Id,
            Name = "Dimanche (v2)",
            Type = SongListTypeEnum.Free,
            SectionId = _foreignSectionId
        });

        var stored = await _context.SongLists.AsNoTracking()
            .FirstAsync(songList => songList.Id == created.Id);

        Assert.Multiple(() =>
        {
            Assert.That(stored.SectionId, Is.Null, "La liste a ete rattachee a un pupitre etranger.");
            Assert.That(stored.ChoirId, Is.EqualTo(_choirA));
        });
    }

    [Test]
    public async Task UpdateSongList_ClaimingEventType_WithoutAStoredEvent_IsRefused()
    {
        var created = await _songListService.CreateAsync(new SongListViewModel
        {
            Name = "Dimanche",
            Type = SongListTypeEnum.Free,
            ChoirId = _choirA
        });

        var foreignEventId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space
        {
            Id = foreignEventId,
            ClientId = (await _context.Choirs.AsNoTracking().FirstAsync(c => c.Id == _foreignChoirB)).ClientId,
            SpaceType = SpaceTypeEnum.Event
        });
        _context.Events.Add(new Event
        {
            Id = foreignEventId,
            Title = "Mariage",
            ChoirId = _foreignChoirB,
            StartDate = DateTime.UtcNow.AddDays(7),
            Location = "Eglise",
            Status = EventStatusEnum.Published
        });
        await _context.SaveChangesAsync();

        // Le rattachement etant immuable en update, declarer Type = Event sur une liste sans
        // evenement stocke produirait une ligne incoherente : EnsureTypeMatchesStoredScope
        // refuse explicitement plutot que d'ecrire un etat impossible.
        var exception = Assert.ThrowsAsync<CustomException>(async () =>
            await _songListService.UpdateAsync(new SongListViewModel
            {
                Id = created.Id,
                Name = "Dimanche (v2)",
                Type = SongListTypeEnum.Event,
                EventId = foreignEventId
            }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var stored = await _context.SongLists.AsNoTracking()
            .FirstAsync(songList => songList.Id == created.Id);

        Assert.Multiple(() =>
        {
            Assert.That(stored.EventId, Is.Null);
            Assert.That(stored.ChoirId, Is.EqualTo(_choirA));
            Assert.That(stored.Type, Is.EqualTo(SongListTypeEnum.Free));
        });
    }
}
