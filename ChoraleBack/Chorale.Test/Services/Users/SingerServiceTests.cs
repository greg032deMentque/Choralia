using System.Security.Claims;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.Technical;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.UserServices;
using ChoraleBackEnd.ViewModels;
using ChoraleBackEnd.ViewModels.Users;

namespace ChoraleBackEnd.Test.Services.Users;

/// <summary>
/// Corrige deux ecarts releves par un audit securite sur <see cref="SingerService"/> :
/// absence de tracabilite (OWASP A09) sur Create/Update/Delete, et absence de restriction au
/// role Singer sur GetById/Update/Delete (la surface pouvait lire/muter un compte non-chanteur
/// par son Id). L'IDOR initialement suspecte a ete infirme separement (policy Roles=Admin sur
/// tout SingerController) : aucun controle d'ownership n'est du a ce fichier.
/// </summary>
[TestFixture]
public sealed class SingerServiceTests
{
    private const string SingerUserId = "singer-1";
    private const string NonSingerUserId = "admin-1";

    private ChoraleDbContext _context = null!;
    private SingerService _sut = null!;
    private UserManager<User> _userManager = null!;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        var mapper = new MapperConfiguration(
            cfg => cfg.AddMaps(typeof(UserViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "admin-courant")], "Test"))
            }
        };

        var configuration = new ConfigurationManager();
        configuration["Frontend:BaseUrl"] = "http://localhost:4200";

        var services = new ServiceCollection();
        services.AddSingleton(_context);
        services.AddSingleton(mapper);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<ILogService>(new LogService(httpContextAccessor));
        services.AddLogging();
        services.AddIdentity<User, IdentityRole>()
            .AddEntityFrameworkStores<ChoraleDbContext>()
            .AddDefaultTokenProviders();

        var serviceProvider = services.BuildServiceProvider();
        var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        _userManager = serviceProvider.GetRequiredService<UserManager<User>>();
        await roleManager.CreateAsync(new IdentityRole(UserRoleEnum.Singer.ToString()));
        await roleManager.CreateAsync(new IdentityRole(UserRoleEnum.Admin.ToString()));

        var auditLogService = new AuditLogService(serviceProvider);
        _sut = new SingerService(serviceProvider, auditLogService);

        var singer = CreateUser(SingerUserId, "Alice", "Martin", "singer@t.com");
        var nonSinger = CreateUser(NonSingerUserId, "Admin", "Sansrole", "admin@t.com");
        await _context.SaveChangesAsync();
        await _userManager.AddToRoleAsync(singer, UserRoleEnum.Singer.ToString());
        await _userManager.AddToRoleAsync(nonSinger, UserRoleEnum.Admin.ToString());
    }

    [TearDown]
    public void TearDown()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    // ---- Ecart 2 : restriction au role Singer -----------------------------------------

    [Test]
    public void GetByIdAsync_TargetNotSinger_ThrowsNotFound()
    {
        Assert.ThrowsAsync<KeyNotFoundException>(() => _sut.GetByIdAsync(NonSingerUserId));
    }

    [Test]
    public void UpdateAsync_TargetNotSinger_ThrowsNotFound()
    {
        var exception = Assert.ThrowsAsync<KeyNotFoundException>(() => _sut.UpdateAsync(
            new UserViewModel { Id = NonSingerUserId, Firstname = "Renomme", Lastname = "Renomme" }));

        Assert.That(exception!.Message, Does.Contain(NonSingerUserId));
    }

    [Test]
    public async Task UpdateAsync_TargetNotSinger_DoesNotModifyTheAccount()
    {
        Assert.ThrowsAsync<KeyNotFoundException>(() => _sut.UpdateAsync(
            new UserViewModel { Id = NonSingerUserId, Firstname = "Renomme", Lastname = "Renomme" }));

        var user = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == NonSingerUserId);
        Assert.That(user.Firstname, Is.EqualTo("Admin"));
    }

    [Test]
    public void DeleteAsync_TargetNotSinger_ThrowsNotFound()
    {
        Assert.ThrowsAsync<KeyNotFoundException>(() => _sut.DeleteAsync(NonSingerUserId));
    }

    [Test]
    public async Task DeleteAsync_TargetNotSinger_DoesNotDeleteTheAccount()
    {
        Assert.ThrowsAsync<KeyNotFoundException>(() => _sut.DeleteAsync(NonSingerUserId));

        var user = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == NonSingerUserId);
        Assert.That(user.IsDeleted, Is.False);
    }

    // ---- Ecart 1 : tracabilite OWASP A09 ------------------------------------------------

    [Test]
    public async Task CreateAsync_RecordsAnAuditEntry()
    {
        var created = await _sut.CreateAsync(new UserViewModel
        {
            Firstname = "Claire", Lastname = "Durand", Email = "claire@t.com", Password = "Password1!"
        });

        var auditEntry = await _context.AdminAuditLogs
            .FirstOrDefaultAsync(a => a.EntityId == created.Id && a.Action == "SingerCreated");
        Assert.That(auditEntry, Is.Not.Null);
    }

    [Test]
    public async Task UpdateAsync_Singer_RecordsAnAuditEntry()
    {
        await _sut.UpdateAsync(new UserViewModel { Id = SingerUserId, Firstname = "Alicia", Lastname = "Martin" });

        var auditEntry = await _context.AdminAuditLogs
            .FirstOrDefaultAsync(a => a.EntityId == SingerUserId && a.Action == "SingerIdentityUpdated");
        Assert.That(auditEntry, Is.Not.Null);
    }

    [Test]
    public async Task DeleteAsync_Singer_RecordsAnAuditEntry()
    {
        await _sut.DeleteAsync(SingerUserId);

        var auditEntry = await _context.AdminAuditLogs
            .FirstOrDefaultAsync(a => a.EntityId == SingerUserId && a.Action == "SingerDeleted");
        Assert.That(auditEntry, Is.Not.Null);
    }

    // ---- GetPagedAsync : perimetre de la liste ----------------------------------------

    [Test]
    public async Task GetPagedAsync_ReturnsOnlyAccountsCarryingTheSingerRole()
    {
        var result = await _sut.GetPagedAsync(new PaginateViewModel { PageSize = 100 });

        var ids = result.Items.Select(i => i.Id).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(ids, Does.Contain(SingerUserId));
            Assert.That(ids, Does.Not.Contain(NonSingerUserId),
                "La liste des chanteurs ne doit pas exposer les comptes d'administration.");
            Assert.That(result.TotalCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GetPagedAsync_SoftDeletedSinger_NeverAppears()
    {
        var deleted = CreateUser("singer-deleted", "Ancien", "Chanteur", "deleted@t.com");
        deleted.IsDeleted = true;
        await _context.SaveChangesAsync();
        await _userManager.AddToRoleAsync(deleted, UserRoleEnum.Singer.ToString());

        var result = await _sut.GetPagedAsync(new PaginateViewModel { PageSize = 100 });

        Assert.That(result.Items.Select(i => i.Id), Does.Not.Contain("singer-deleted"));
    }

    [TestCase("Bertrand", Description = "sur le nom")]
    [TestCase("Chloe", Description = "sur le prenom")]
    [TestCase("cible@t.com", Description = "sur l'email")]
    public async Task GetPagedAsync_Filter_SearchesLastnameFirstnameAndEmail(string filter)
    {
        var target = CreateUser("singer-cible", "Chloe", "Bertrand", "cible@t.com");
        await _context.SaveChangesAsync();
        await _userManager.AddToRoleAsync(target, UserRoleEnum.Singer.ToString());

        var result = await _sut.GetPagedAsync(new PaginateViewModel { PageSize = 100, Filter = filter });

        Assert.That(result.Items.Select(i => i.Id), Is.EqualTo(new[] { "singer-cible" }));
    }

    /// <summary>
    /// Constate en exercant l'API : <c>CreateAsync</c> renvoyait les roles et
    /// <c>GetPagedAsync</c> renvoyait une liste vide pour le meme compte — le mapping ignore
    /// <c>Roles</c> et seul <c>CreateAsync</c> le repeuplait. Un appelant qui filtre sur les
    /// roles voyait donc deux verites selon le chemin.
    /// </summary>
    [Test]
    public async Task GetPagedAsync_PopulatesRolesLikeCreateDoes()
    {
        var result = await _sut.GetPagedAsync(new PaginateViewModel { PageSize = 100 });

        Assert.That(
            result.Items.Single(i => i.Id == SingerUserId).Roles,
            Does.Contain(UserRoleEnum.Singer.ToString()));
    }

    private User CreateUser(string id, string firstname, string lastname, string email)
    {
        var user = new User
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Firstname = firstname,
            Lastname = lastname,
            IsActive = true,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.Users.Add(user);
        return user;
    }
}
