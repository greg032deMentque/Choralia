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

namespace ChoraleBackEnd.Test.Services.Dashboard;

/// <summary>
/// Indicateurs du tableau de bord.
/// </summary>
/// <remarks>
/// Deux comportements valent d'etre proteges.
///
/// La completude chorale (`10-D10`) est <b>reecrite</b> en requete dans DashboardService,
/// pour ne pas charger tout le repertoire chant par chant. Elle existe donc a deux endroits
/// et peut diverger de <c>SongService.ApplyCompleteness</c> sans que rien ne le signale :
/// le compteur afficherait simplement un autre nombre.
///
/// Et le taux de reponse est nul — pas zero — quand personne n'est target. Un 0 % se lirait
/// comme « personne n'a repondu » alors qu'il n'y a aucun destinataire.
/// </remarks>
[TestFixture]
public sealed class DashboardServiceTests
{
    private const string MemberUserId = "member-1";
    private const string EtrangerUserId = "etranger-1";

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
        _context.Users.Add(new User { Id = EtrangerUserId, UserName = "e@t.com", Email = "e@t.com" });
        _context.Clients.Add(new Client
        {
            Id = _clientId, Name = "Client", Status = ClientStatusEnum.Active
        });
        _context.Spaces.Add(new Space { Id = _choirId, ClientId = _clientId, SpaceType = SpaceTypeEnum.Choir });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = _choirId, ClientId = _clientId, Name = "Choir", Status = ChoirStatusEnum.Published
        });
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = MemberUserId,
            ChoirId = _choirId,
            SpaceId = _choirId,
            Status = MemberStatusEnum.Active
        });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task NonMember_EstRefuse()
    {
        var sut = CreateService(EtrangerUserId);
        Assert.ThrowsAsync<CustomException>(() => sut.GetChoirKpiAsync(_choirId));
        await Task.CompletedTask;
    }

    [Test]
    public async Task SongIncomplet_SansScorePublished()
    {
        await AddSongAsync(VoicePartEnum.Soprano, avecScorePublished: false,
            voicePartEnregistrees: [VoicePartEnum.Soprano]);

        var kpi = await CreateService(MemberUserId).GetChoirKpiAsync(_choirId);

        Assert.Multiple(() =>
        {
            Assert.That(kpi.SongsInRepertoire, Is.EqualTo(1));
            Assert.That(kpi.IncompleteSongs, Is.EqualTo(1),
                "Toutes les voix couvertes ne suffisent pas : la partition de reference "
                + "publiee fait partie de la completude.");
        });
    }

    [Test]
    public async Task SongIncomplet_UneVoicePartNonCouverte()
    {
        await AddSongAsync(VoicePartEnum.Soprano, avecScorePublished: true,
            voicePartEnregistrees: []);

        var kpi = await CreateService(MemberUserId).GetChoirKpiAsync(_choirId);

        Assert.That(kpi.IncompleteSongs, Is.EqualTo(1));
    }

    [Test]
    public async Task SongComplet_ScorePublishedEtToutesVoicePartCouvertes()
    {
        await AddSongAsync(VoicePartEnum.Soprano, avecScorePublished: true,
            voicePartEnregistrees: [VoicePartEnum.Soprano]);

        var kpi = await CreateService(MemberUserId).GetChoirKpiAsync(_choirId);

        Assert.Multiple(() =>
        {
            Assert.That(kpi.SongsInRepertoire, Is.EqualTo(1));
            Assert.That(kpi.IncompleteSongs, Is.Zero);
        });
    }

    [Test]
    public async Task ResponseRate_EstNul_QuandNoParticipant()
    {
        await AddEventAsync(participants: []);

        var kpi = await CreateService(MemberUserId).GetChoirKpiAsync(_choirId);

        Assert.Multiple(() =>
        {
            Assert.That(kpi.UpcomingEvents, Has.Count.EqualTo(1));
            Assert.That(kpi.UpcomingEvents[0].ResponseRate, Is.Null,
                "0 % se lirait comme une absence de reponse, pas de destinataire.");
        });
    }

    [Test]
    public async Task ResponseRate_SansReponseCompteCommeTargetMaisPasCommeReponse()
    {
        await AddEventAsync(participants:
        [
            AttendanceEnum.Attending,
            AttendanceEnum.NotAttending,
            AttendanceEnum.NoReply,
            AttendanceEnum.NoReply
        ]);

        var kpi = await CreateService(MemberUserId).GetChoirKpiAsync(_choirId);
        var evt = kpi.UpcomingEvents[0];

        Assert.Multiple(() =>
        {
            Assert.That(evt.Targets, Is.EqualTo(4));
            Assert.That(evt.Responses, Is.EqualTo(2));
            Assert.That(evt.ResponseRate, Is.EqualTo(50));
        });
    }

    [Test]
    public async Task UpcomingEvents_ExcluentDraftsEtPasses()
    {
        await AddEventAsync([], EventStatusEnum.Draft);
        await AddEventAsync([], EventStatusEnum.Published, DateTime.UtcNow.AddDays(-2));

        var kpi = await CreateService(MemberUserId).GetChoirKpiAsync(_choirId);

        Assert.That(kpi.UpcomingEvents, Is.Empty);
    }

    private async Task AddSongAsync(
        VoicePartEnum voicePartAttendue, bool avecScorePublished, VoicePartEnum[] voicePartEnregistrees)
    {
        var songId = ChoraleDbContext.NewIdGuid();
        _context.Songs.Add(new Song
        {
            Id = songId, ChoirId = _choirId, Title = "Chant", Status = SongStatusEnum.Active
        });
        _context.SongVoiceParts.Add(new SongVoicePart
        {
            Id = ChoraleDbContext.NewIdGuid(), SongId = songId, VoicePart = voicePartAttendue
        });

        if (avecScorePublished)
        {
            _context.Scores.Add(new Score
            {
                Id = ChoraleDbContext.NewIdGuid(),
                SongId = songId,
                Type = ScoreTypeEnum.General,
                Version = "v1",
                Status = ScoreStatusEnum.Published,
                OwnerUserId = MemberUserId,
                FilePath = "f.pdf"
            });
        }

        foreach (var voicePart in voicePartEnregistrees)
        {
            _context.Recordings.Add(new Recording
            {
                Id = ChoraleDbContext.NewIdGuid(),
                SongId = songId,
                ChoirOwnerId = _choirId,
                Type = RecordingTypeEnum.ByVoicePart,
                TargetVoicePart = voicePart,
                Status = RecordingStatusEnum.Published,
                Source = RecordingSourceEnum.UploadedFile,
                CreatorUserId = MemberUserId,
                ContentOwner = MemberUserId,
                FilePath = "f.mp3"
            });
        }

        await _context.SaveChangesAsync();
    }

    private async Task AddEventAsync(
        AttendanceEnum?[] participants,
        EventStatusEnum status = EventStatusEnum.Published,
        DateTime? dateDebut = null)
    {
        var eventId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space { Id = eventId, ClientId = _clientId, SpaceType = SpaceTypeEnum.Event });
        _context.Events.Add(new Event
        {
            Id = eventId,
            ChoirId = _choirId,
            Title = "Event",
            Location = "Eglise",
            Status = status,
            Type = EventTypeEnum.Concert,
            StartDate = dateDebut ?? DateTime.UtcNow.AddDays(3)
        });

        for (var i = 0; i < participants.Length; i++)
        {
            var userId = $"participant-{eventId}-{i}";
            _context.Users.Add(new User { Id = userId, UserName = userId, Email = $"{userId}@t.com" });
            _context.SpaceMembers.Add(new SpaceMember
            {
                Id = ChoraleDbContext.NewIdGuid(),
                UserId = userId,
                SpaceId = eventId,
                Status = MemberStatusEnum.Active,
                Presence = participants[i]
            });
        }

        await _context.SaveChangesAsync();
    }

    private DashboardService CreateService(string userId)
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

        var sp = services.BuildServiceProvider();
        return new DashboardService(sp, new MembershipService(sp));
    }
}
