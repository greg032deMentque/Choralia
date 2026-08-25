using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.ViewModels.Clients;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.ViewModels;
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
/// Filtre de <c>ClientController.GetPaged</c> (`10-D30`) : les tuiles du tableau de bord
/// d'administration ouvrent la liste des clients deja filtree — par statut, par identifiants
/// explicites (tuiles qui ne savent designer leurs clients autrement), ou par seuil de plafond
/// evalue a la demande.
/// </summary>
[TestFixture]
public sealed class ClientFiltersTests
{
    private ChoraleDbContext _context = null!;

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
    public async Task NullFilters_BehaveAsBeforeFilterIntroduction()
    {
        AddClient("Client A", ClientStatusEnum.Active);
        AddClient("Client B", ClientStatusEnum.Suspended);
        AddClient("Client C", ClientStatusEnum.Archived);
        await _context.SaveChangesAsync();

        var page = await Sut().GetPagedAsync(new ClientsPagedFilterViewModel { PageSize = 50 });

        Assert.That(page.TotalCount, Is.EqualTo(3));
    }

    [Test]
    public async Task Status_Filter_OnlyClientsOfThatStatus()
    {
        AddClient("Client A", ClientStatusEnum.Active);
        AddClient("Client B", ClientStatusEnum.Suspended);
        AddClient("Client C", ClientStatusEnum.Suspended);
        await _context.SaveChangesAsync();

        var page = await Sut().GetPagedAsync(new ClientsPagedFilterViewModel
        {
            Status = ClientStatusEnum.Suspended,
            PageSize = 50
        });

        Assert.Multiple(() =>
        {
            Assert.That(page.TotalCount, Is.EqualTo(2));
            Assert.That(page.Items.Select(c => c.Name), Is.EquivalentTo(new[] { "Client B", "Client C" }));
        });
    }

    [Test]
    public async Task ClientIds_Filter_OnlyTheseClients_InNormalSortOrder()
    {
        var clientC = AddClient("C", ClientStatusEnum.Active);
        var clientA = AddClient("A", ClientStatusEnum.Active);
        AddClient("B", ClientStatusEnum.Active);
        await _context.SaveChangesAsync();

        // Position transmis volontairement inverse de l'ordre de tri attendu (Nom croissant).
        var page = await Sut().GetPagedAsync(new ClientsPagedFilterViewModel
        {
            ClientIds = [clientC.Id, clientA.Id],
            PageSize = 50
        });

        Assert.Multiple(() =>
        {
            Assert.That(page.TotalCount, Is.EqualTo(2));
            Assert.That(page.Items.Select(c => c.Name), Is.EqualTo(new[] { "A", "C" }));
        });
    }

    [Test]
    public async Task ClientIds_NonExistentIdentifier_IgnoredWithoutException()
    {
        var clientA = AddClient("A", ClientStatusEnum.Active);
        await _context.SaveChangesAsync();

        PagedListViewModel<ClientViewModel>? page = null;
        Assert.DoesNotThrowAsync(async () => page = await Sut().GetPagedAsync(new ClientsPagedFilterViewModel
        {
            ClientIds = [clientA.Id, Guid.NewGuid()],
            PageSize = 50
        }));

        Assert.Multiple(() =>
        {
            Assert.That(page!.TotalCount, Is.EqualTo(1));
            Assert.That(page.Items.Single().Id, Is.EqualTo(clientA.Id));
        });
    }

    [Test]
    public async Task ClientIds_EmptyListProvided_ReturnsNoClient()
    {
        // Decision explicite : une liste presente mais vide designe « ces identifiants
        // precis », qui n'existent pas — zero result, jamais un repli sur la liste
        // complete. Une tuile « non demarres » vide ne doit pas afficher tous les clients.
        AddClient("Client A", ClientStatusEnum.Active);
        AddClient("Client B", ClientStatusEnum.Active);
        await _context.SaveChangesAsync();

        var page = await Sut().GetPagedAsync(new ClientsPagedFilterViewModel
        {
            ClientIds = [],
            PageSize = 50
        });

        Assert.Multiple(() =>
        {
            Assert.That(page.TotalCount, Is.EqualTo(0));
            Assert.That(page.Items, Is.Empty);
        });
    }

    [Test]
    public async Task ClientIds_AboveTheLimit_IsExplicitlyRejected()
    {
        AddClient("Client A", ClientStatusEnum.Active);
        await _context.SaveChangesAsync();

        var tooMany = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToList();

        var exception = Assert.ThrowsAsync<CustomException>(() => Sut().GetPagedAsync(
            new ClientsPagedFilterViewModel { ClientIds = tooMany, PageSize = 50 }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task NearCap_OnlyClientsAbove80Percent_ZeroCapIsExcluded()
    {
        // Au-dessus du seuil : 9/10 chorales = 90 %.
        var aboveThreshold = AddClient("Au-dessus", ClientStatusEnum.Active, limitChoirs: 10);
        AddChoirs(aboveThreshold.Id, 9);

        // Exactement au seuil : 8/10 = 80 %, pas strictement superieur -> exclu.
        var atThreshold = AddClient("Au seuil", ClientStatusEnum.Active, limitChoirs: 10);
        AddChoirs(atThreshold.Id, 8);

        // Plafond a 0 : ne doit jamais etre compte a 100 %, meme avec de la consommation.
        var zeroCap = AddClient("Plafond zero", ClientStatusEnum.Active, limitChoirs: 0);
        AddChoirs(zeroCap.Id, 5);

        await _context.SaveChangesAsync();

        var page = await Sut().GetPagedAsync(new ClientsPagedFilterViewModel
        {
            NearCap = true,
            PageSize = 50
        });

        Assert.Multiple(() =>
        {
            Assert.That(page.TotalCount, Is.EqualTo(1));
            Assert.That(page.Items.Single().Id, Is.EqualTo(aboveThreshold.Id));
        });
    }

    [Test]
    public async Task Filters_StatusAndClientIds_Combine()
    {
        var active = AddClient("Client Active", ClientStatusEnum.Active);
        var suspendedClient = AddClient("Client Suspendu", ClientStatusEnum.Suspended);
        await _context.SaveChangesAsync();

        var page = await Sut().GetPagedAsync(new ClientsPagedFilterViewModel
        {
            Status = ClientStatusEnum.Active,
            ClientIds = [active.Id, suspendedClient.Id],
            PageSize = 50
        });

        Assert.Multiple(() =>
        {
            Assert.That(page.TotalCount, Is.EqualTo(1));
            Assert.That(page.Items.Single().Id, Is.EqualTo(active.Id));
        });
    }

    [Test]
    public async Task TotalCount_ReflectsTheFilter_NotOverallTotal()
    {
        AddClient("Client A", ClientStatusEnum.Active);
        AddClient("Client B", ClientStatusEnum.Active);
        AddClient("Client C", ClientStatusEnum.Suspended);
        AddClient("Client D", ClientStatusEnum.Suspended);
        AddClient("Client E", ClientStatusEnum.Suspended);
        await _context.SaveChangesAsync();

        // PageSize=1 : sans filtrage correct de TotalCount, la derniere page serait vide.
        var page = await Sut().GetPagedAsync(new ClientsPagedFilterViewModel
        {
            Status = ClientStatusEnum.Suspended,
            PageSize = 1
        });

        Assert.That(page.TotalCount, Is.EqualTo(3));
    }

    private Client AddClient(string name, ClientStatusEnum status, int limitChoirs = 5)
    {
        var client = new Client
        {
            Id = ChoraleDbContext.NewIdGuid(),
            Name = name,
            Status = status,
            ChoirLimit = limitChoirs,
            MemberLimit = 250,
            StorageQuotaBytes = 1_000_000,
            MaxFileSizeBytes = 100_000
        };
        _context.Clients.Add(client);
        return client;
    }

    private void AddChoirs(Guid clientId, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var choirId = ChoraleDbContext.NewIdGuid();

            // Le filtre de requete de Chorale exclut !Espace.IsDeleted (jointure sur l'espace
            // partageant le meme Id) : sans cette ligne, une chorale sans espace correspondant
            // n'apparait dans aucune requete, quel que soit son propre IsDeleted.
            _context.Spaces.Add(new Space
            {
                Id = choirId,
                SpaceType = SpaceTypeEnum.Choir,
                ClientId = clientId
            });
            _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
            {
                Id = choirId,
                ClientId = clientId,
                Name = $"Choir {clientId}-{i}",
                Status = ChoirStatusEnum.Published
            });
        }
    }

    private ClientService Sut()
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "admin-1"),
                     new Claim(ClaimTypes.Role, nameof(UserRoleEnum.Admin))], "Test"))
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
        return new ClientService(
            sp,
            new AuditLogService(sp),
            new ServiceLimitService(sp),
            new ClientRoleResolverService(_context));
    }
}
