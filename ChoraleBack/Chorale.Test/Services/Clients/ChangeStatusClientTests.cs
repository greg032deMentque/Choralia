using System;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Test.Fakes;
using ChoraleBackEnd.ViewModels.Clients;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
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
/// Changement de statut d'un client — deux defauts trouves en exercant l'API reelle.
/// </summary>
/// <remarks>
/// <b>Status hors plage.</b> <c>{"Status": 99}</c> etait accepte et persiste. Le client se
/// retrouvait dans un etat ni actif, ni suspendu, ni archive : hors d'atteinte de la regle
/// « archive est terminal », et invisible de tout filtre par statut.
///
/// <b>Statut absent.</b> Plus grave : omettre le champ <b>reactivait</b> un client suspendu.
/// Sur un type valeur non nullable, <c>[Required]</c> ne rejette que <c>null</c>, et un
/// champ manquant devient <c>0</c>, soit <c>Active</c>. Une request tronquee levait donc une
/// suspension sans que personne l'ait demande.
///
/// Ces deux cas passaient tous les tests existants : ils ne se voient qu'en appelant l'API.
/// </remarks>
[TestFixture]
public sealed class ChangeStatusClientTests
{
    private ChoraleDbContext _context = null!;
    private Guid _clientId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _clientId = ChoraleDbContext.NewIdGuid();
        _context.Clients.Add(new Client
        {
            Id = _clientId,
            Name = "Client Test",
            Status = ClientStatusEnum.Suspended,
            ChoirLimit = 5,
            MemberLimit = 250,
            StorageQuotaBytes = 1000,
            MaxFileSizeBytes = 100
        });
        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public void StatusMissing_DoesNotReactivateSuspendedClient()
    {
        // Le champ n'est pas renseigne : c'est exactement une request tronquee.
        var request = new ChangeClientStatusViewModel { Id = _clientId };

        Assert.ThrowsAsync<CustomException>(() => Sut().ChangeStatusAsync(request));
    }

    [Test]
    public async Task StatusMissing_ClientStaysSuspended()
    {
        try { await Sut().ChangeStatusAsync(new ChangeClientStatusViewModel { Id = _clientId }); }
        catch (CustomException) { /* expected */ }

        var client = await _context.Clients.AsNoTracking().FirstAsync(c => c.Id == _clientId);
        Assert.That(client.Status, Is.EqualTo(ClientStatusEnum.Suspended));
    }

    [TestCase(99)]
    [TestCase(-1)]
    public void StatusOutOfRange_IsRejected(int value)
    {
        var request = new ChangeClientStatusViewModel
        {
            Id = _clientId,
            Status = (ClientStatusEnum)value
        };

        Assert.ThrowsAsync<CustomException>(() => Sut().ChangeStatusAsync(request));
    }

    [Test]
    public async Task ValidStatus_IsApplied()
    {
        await Sut().ChangeStatusAsync(new ChangeClientStatusViewModel
        {
            Id = _clientId,
            Status = ClientStatusEnum.Active
        });

        var client = await _context.Clients.AsNoTracking().FirstAsync(c => c.Id == _clientId);
        Assert.That(client.Status, Is.EqualTo(ClientStatusEnum.Active));
    }

    [Test]
    public async Task Archived_IsTerminal()
    {
        var sut = Sut();
        await sut.ChangeStatusAsync(new ChangeClientStatusViewModel
        {
            Id = _clientId, Status = ClientStatusEnum.Archived
        });

        Assert.ThrowsAsync<CustomException>(() => Sut().ChangeStatusAsync(
            new ChangeClientStatusViewModel { Id = _clientId, Status = ClientStatusEnum.Active }));
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
            new FakeServiceLimitService(),
            new ClientRoleResolverService(_context));
    }
}
