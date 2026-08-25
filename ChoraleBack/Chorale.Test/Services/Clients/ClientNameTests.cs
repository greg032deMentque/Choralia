using System.Net;
using System.Security.Claims;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using ChoraleBackEnd.Services.ClientServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.ViewModels.Clients;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.ViewModels.Choirs;

namespace ChoraleBackEnd.Test.Services.Clients;

/// <summary>
/// Le nom d'un client est un libelle d'exploitation, pas une cle (`04` § Client). L'unicite
/// qui portait jusqu'ici sur les clients actifs exigeait une gymnastique de renommage sans
/// aucune raison metier ; elle est levee, mais le nom reste obligatoire.
/// </summary>
[TestFixture]
public sealed class ClientNameTests
{
    private ChoraleDbContext _context = null!;
    private ClientService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);
        _sut = CreateService();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task CreateAsync_TwoActiveClientsSameName_BothAccepted()
    {
        await _sut.CreateAsync(new CreateClientViewModel { Name = "Chorale Diocésaine" });

        Assert.DoesNotThrowAsync(
            () => _sut.CreateAsync(new CreateClientViewModel { Name = "Chorale Diocésaine" }));

        var total = await _context.Clients.CountAsync(c => c.Name == "Chorale Diocésaine");
        Assert.That(total, Is.EqualTo(2));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void CreateAsync_NameEmptyOrBlank_IsRejected(string name)
    {
        var exception = Assert.ThrowsAsync<CustomException>(
            () => _sut.CreateAsync(new CreateClientViewModel { Name = name }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task UpdateAsync_NameEmptyOrBlank_IsRejected(string name)
    {
        var client = await _sut.CreateAsync(new CreateClientViewModel { Name = "Client Initial" });

        var exception = Assert.ThrowsAsync<CustomException>(
            () => _sut.UpdateAsync(new UpdateClientViewModel { Id = client.Id!.Value, Name = name }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    private ClientService CreateService()
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "admin-1"),
                     new Claim(ClaimTypes.Role, UserRoleEnum.Admin.ToString())], "Test"))
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

        var serviceProvider = services.BuildServiceProvider();
        var auditLogService = new AuditLogService(serviceProvider);
        var serviceLimitService = new ServiceLimitService(serviceProvider);
        var clientRoleResolverService = new ClientRoleResolverService(_context);

        return new ClientService(serviceProvider, auditLogService, serviceLimitService, clientRoleResolverService);
    }
}
