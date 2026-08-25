using System.Reflection;
using System.Security.Claims;
using AutoMapper;
using ChoraleBackEnd.Api.Controllers.AdminControllers;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.AdminAudit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using ChoraleBackEnd.ViewModels.Choirs;

namespace ChoraleBackEnd.Test.Services.Technical;

/// <summary>
/// Ecran d'audit : <c>AdminAuditLog</c> est alimente depuis le lot 3 mais rien ne l'affichait
/// jusqu'ici. Verifie les filtres, le repli sur periode inversee, l'enrichissement acteur
/// (y compris quand l'acteur a disparu) et le tri par defaut deterministe.
/// </summary>
[TestFixture]
public sealed class AdminAuditListTests
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
    public async Task GetPagedAsync_ActorTypeActionAndPeriodFilters_ReturnsOnlyMatchingRows()
    {
        var actorId = await CreateUserAsync("acteur-1");
        await CreateUserAsync("acteur-2");

        var withinThePeriod = new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        AddRow(actorId, "ClientCreated", "Client", withinThePeriod);
        AddRow(actorId, "ClientCreated", "Client", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        AddRow("acteur-2", "ClientCreated", "Client", withinThePeriod);
        AddRow(actorId, "ClientDeleted", "Client", withinThePeriod);
        AddRow(actorId, "ClientCreated", "Choir", withinThePeriod);
        await _context.SaveChangesAsync();

        var result = await Sut().GetPagedAsync(new AdminAuditLogPagedFilterViewModel
        {
            PageSize = 100,
            UserId = actorId,
            EntityType = "Client",
            Action = "ClientCreated",
            StartDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc)
        });

        Assert.That(result.Items, Has.Count.EqualTo(1));
        Assert.That(result.Items[0].UserId, Is.EqualTo(actorId));
    }

    [Test]
    public async Task GetPagedAsync_ReversedPeriod_ReturnsEmptyWithoutException()
    {
        var actorId = await CreateUserAsync("acteur-1");
        AddRow(actorId, "Action", "Type", DateTime.UtcNow);
        await _context.SaveChangesAsync();

        PagedListViewModel<AdminAuditLogListItemViewModel> result = null!;

        Assert.DoesNotThrowAsync(async () => result = await Sut().GetPagedAsync(new AdminAuditLogPagedFilterViewModel
        {
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(-1)
        }));

        Assert.Multiple(() =>
        {
            Assert.That(result.Items, Is.Empty);
            Assert.That(result.TotalCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task GetPagedAsync_DeletedOrNonExistentActor_RowStillDisplayed_WithReadableFallback()
    {
        var actorId = await CreateUserAsync("acteur-supprime", isDeleted: true);
        AddRow(actorId, "Action", "Type", DateTime.UtcNow);
        AddRow("acteur-jamais-exists", "Action", "Type", DateTime.UtcNow);
        await _context.SaveChangesAsync();

        var result = await Sut().GetPagedAsync(new AdminAuditLogPagedFilterViewModel { PageSize = 100 });

        Assert.That(result.Items, Has.Count.EqualTo(2));
        var deletedActorRow = result.Items.Single(i => i.UserId == actorId);
        var unknownActorRow = result.Items.Single(i => i.UserId == "acteur-jamais-exists");

        Assert.Multiple(() =>
        {
            Assert.That(deletedActorRow.UserFullName, Is.Not.Empty);
            Assert.That(deletedActorRow.UserFullName, Is.Not.EqualTo("Utilisateur inconnu"));
            Assert.That(unknownActorRow.UserFullName, Is.EqualTo("Utilisateur inconnu"));
        });
    }

    [Test]
    public async Task GetPagedAsync_NoSortActive_SortsByOccurredAtDescending_DeterministicOnTiedValues()
    {
        var actorId = await CreateUserAsync("acteur-1");
        var sameInstant = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

        AddRow(actorId, "Action1", "Type", sameInstant);
        AddRow(actorId, "Action2", "Type", sameInstant);
        var mostRecentId = AddRow(actorId, "Action3", "Type", sameInstant.AddMinutes(5));
        await _context.SaveChangesAsync();

        var firstCall = await Sut().GetPagedAsync(new AdminAuditLogPagedFilterViewModel { PageSize = 100 });
        var secondCall = await Sut().GetPagedAsync(new AdminAuditLogPagedFilterViewModel { PageSize = 100 });

        Assert.Multiple(() =>
        {
            Assert.That(firstCall.Items[0].Id, Is.EqualTo(mostRecentId));
            Assert.That(
                firstCall.Items.Select(i => i.Id).ToList(),
                Is.EqualTo(secondCall.Items.Select(i => i.Id).ToList()));
        });
    }

    [Test]
    public void AdminAuditController_ExposesNoWriteEndpoint()
    {
        var methods = typeof(AdminAuditController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(methods, Has.Count.EqualTo(1));
            Assert.That(
                methods.Single().GetCustomAttributes<HttpPostAttribute>().Any(a => a.Template == "GetPaged"),
                Is.True);
            Assert.That(
                methods.Any(m => m.GetCustomAttributes<HttpDeleteAttribute>().Any()
                                   || m.GetCustomAttributes<HttpPutAttribute>().Any()),
                Is.False);
        });
    }

    private async Task<string> CreateUserAsync(string id, bool isDeleted = false)
    {
        var email = $"{id}@test.com";
        _context.Users.Add(new User
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Firstname = "Prenom",
            Lastname = id,
            IsDeleted = isDeleted
        });
        await _context.SaveChangesAsync();
        return id;
    }

    private Guid AddRow(string userId, string action, string entityType, DateTime occurredAt)
    {
        var id = ChoraleDbContext.NewIdGuid();
        _context.AdminAuditLogs.Add(new AdminAuditLog
        {
            Id = id,
            UserId = userId,
            Action = action,
            EntityType = entityType,
            OccurredAt = occurredAt
        });
        return id;
    }

    private AdminAuditListService Sut()
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin-1")], "Test"))
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
        return new AdminAuditListService(serviceProvider);
    }
}
