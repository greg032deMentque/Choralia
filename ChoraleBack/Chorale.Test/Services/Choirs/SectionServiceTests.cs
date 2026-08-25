using System.Net;
using System.Security.Claims;
using AutoMapper;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.ViewModels.Choirs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Services.Choirs;

/// <summary>
/// Composition des pupitres : lecture, ajout et retrait de membres. Complete
/// <see cref="SectionIsolationTests"/>, qui ne couvre que <c>UpdateLeaderAsync</c>.
/// </summary>
/// <remarks>
/// Deux regles portees par ce service ne sont visibles nulle part ailleurs :
/// l'appartenance ACTIVE exigee de l'appelant sur la chorale de la section visee
/// (<c>EnsureMembershipAsync</c> — la policy HTTP resout le role sur l'espace du header
/// <c>X-Space-Id</c>, jamais sur la chorale reelle de la section), et « un chanteur ne peut
/// appartenir qu'a un seul pupitre par chorale » (<c>EnsureUniqueSectionPerChoirAsync</c>).
/// </remarks>
[TestFixture]
public sealed class SectionServiceTests
{
    private const string MemberChoirAUserId = "member-choir-a";
    private const string OutsiderUserId = "outsider-choir-b";
    private const string NewcomerUserId = "newcomer-no-choir";

    private ChoraleDbContext _context = null!;
    private Guid _choirAId;
    private Guid _choirBId;
    private Guid _sopranoChoirAId;
    private Guid _altoChoirAId;
    private Guid _sopranoChoirBId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _choirAId = ChoraleDbContext.NewIdGuid();
        _choirBId = ChoraleDbContext.NewIdGuid();
        _sopranoChoirAId = ChoraleDbContext.NewIdGuid();
        _altoChoirAId = ChoraleDbContext.NewIdGuid();
        _sopranoChoirBId = ChoraleDbContext.NewIdGuid();

        foreach (var userId in new[] { MemberChoirAUserId, OutsiderUserId, NewcomerUserId })
            _context.Users.Add(new User
            {
                Id = userId, UserName = $"{userId}@test.com", Email = $"{userId}@test.com"
            });

        CreateChoir(_choirAId, "Choir A");
        CreateChoir(_choirBId, "Choir B");

        _context.Sections.Add(new Section { Id = _sopranoChoirAId, ChoirId = _choirAId, VoicePart = VoicePartEnum.Soprano });
        _context.Sections.Add(new Section { Id = _altoChoirAId, ChoirId = _choirAId, VoicePart = VoicePartEnum.Alto });
        _context.Sections.Add(new Section { Id = _sopranoChoirBId, ChoirId = _choirBId, VoicePart = VoicePartEnum.Soprano });

        AddChoirMembership(MemberChoirAUserId, _choirAId);
        AddChoirMembership(OutsiderUserId, _choirBId);

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    // ---------- GetByIdAsync ----------

    [Test]
    public void GetByIdAsync_UnknownSection_ThrowsNotFound()
        => Assert.ThrowsAsync<KeyNotFoundException>(
            () => Sut(MemberChoirAUserId).GetByIdAsync(ChoraleDbContext.NewIdGuid()));

    [Test]
    public void GetByIdAsync_CallerMemberOfAnotherChoir_ThrowsForbidden()
    {
        var exception = Assert.ThrowsAsync<CustomException>(
            () => Sut(OutsiderUserId).GetByIdAsync(_sopranoChoirAId));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [TestCase(MemberStatusEnum.Invited)]
    [TestCase(MemberStatusEnum.Inactive)]
    [TestCase(MemberStatusEnum.Archived)]
    public async Task GetByIdAsync_CallerMembershipNotActive_ThrowsForbidden(MemberStatusEnum status)
    {
        var membership = await _context.SpaceMembers
            .FirstAsync(m => m.ChoirId == _choirAId && m.UserId == MemberChoirAUserId);
        membership.Status = status;
        await _context.SaveChangesAsync();

        var exception = Assert.ThrowsAsync<CustomException>(
            () => Sut(MemberChoirAUserId).GetByIdAsync(_sopranoChoirAId));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task GetByIdAsync_ActiveMember_ReturnsSectionWithItsMembers()
    {
        AddSectionMembership(MemberChoirAUserId, _sopranoChoirAId);
        await _context.SaveChangesAsync();

        var result = await Sut(MemberChoirAUserId).GetByIdAsync(_sopranoChoirAId);

        Assert.Multiple(() =>
        {
            Assert.That(result.VoicePart, Is.EqualTo(VoicePartEnum.Soprano));
            Assert.That(result.Members.Select(m => m.UserId), Is.EquivalentTo(new[] { MemberChoirAUserId }));
        });
    }

    // ---------- AddMemberAsync ----------

    [Test]
    public async Task AddMemberAsync_CallerMemberOfAnotherChoir_ThrowsForbiddenAndWritesNothing()
    {
        var exception = Assert.ThrowsAsync<CustomException>(
            () => Sut(OutsiderUserId).AddMemberAsync(_sopranoChoirAId, NewcomerUserId));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(
            await _context.SectionMembers.AsNoTracking().AnyAsync(m => m.UserId == NewcomerUserId),
            Is.False,
            "Un refus d'autorisation ne doit laisser aucune ecriture partielle derriere lui.");
    }

    [Test]
    public async Task AddMemberAsync_NewUser_CreatesSectionMembershipAndActiveChoirMembership()
    {
        await Sut(MemberChoirAUserId).AddMemberAsync(_sopranoChoirAId, NewcomerUserId);

        var sectionMember = await _context.SectionMembers.AsNoTracking()
            .SingleAsync(m => m.UserId == NewcomerUserId);
        var choirMember = await _context.SpaceMembers.AsNoTracking()
            .SingleAsync(m => m.UserId == NewcomerUserId && m.ChoirId == _choirAId);

        Assert.Multiple(() =>
        {
            Assert.That(sectionMember.SectionId, Is.EqualTo(_sopranoChoirAId));
            Assert.That(choirMember.SpaceId, Is.EqualTo(_choirAId),
                "L'entree d'espace posee est celle de la chorale elle-meme, pas d'un evenement.");
            Assert.That(choirMember.Status, Is.EqualTo(MemberStatusEnum.Active));
        });
    }

    [Test]
    public async Task AddMemberAsync_AlreadyChoirMember_DoesNotDuplicateTheChoirMembership()
    {
        await Sut(MemberChoirAUserId).AddMemberAsync(_sopranoChoirAId, MemberChoirAUserId);

        Assert.That(
            await _context.SpaceMembers.AsNoTracking()
                .CountAsync(m => m.UserId == MemberChoirAUserId && m.ChoirId == _choirAId),
            Is.EqualTo(1));
    }

    [Test]
    public async Task AddMemberAsync_UserAlreadyInAnotherSectionOfTheSameChoir_ThrowsConflict()
    {
        AddSectionMembership(NewcomerUserId, _altoChoirAId);
        await _context.SaveChangesAsync();

        var exception = Assert.ThrowsAsync<CustomException>(
            () => Sut(MemberChoirAUserId).AddMemberAsync(_sopranoChoirAId, NewcomerUserId));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(
                _context.SectionMembers.AsNoTracking().Count(m => m.UserId == NewcomerUserId),
                Is.EqualTo(1),
                "Le pupitre d'origine reste le seul rattachement.");
        });
    }

    /// <summary>
    /// La garde d'unicite porte sur la chorale entiere, pupitre cible compris : re-ajouter
    /// quelqu'un dans SON pupitre est donc un conflit, pas une operation idempotente.
    /// </summary>
    /// <remarks>
    /// C'est le seul garde-fou du non-doublon en pupitre : <c>AddMemberAsync</c> ajoute sans
    /// controler une seconde fois, precisement parce que ce chemin-ci ne peut pas etre franchi.
    /// Si ce test tombe, ce n'est pas lui qu'il faut corriger — c'est qu'un doublon est devenu
    /// insérable.
    /// </remarks>
    [Test]
    public void AddMemberAsync_UserAlreadyInThisVerySection_ThrowsConflict()
    {
        AddSectionMembership(NewcomerUserId, _sopranoChoirAId);
        _context.SaveChanges();

        var exception = Assert.ThrowsAsync<CustomException>(
            () => Sut(MemberChoirAUserId).AddMemberAsync(_sopranoChoirAId, NewcomerUserId));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task AddMemberAsync_UserInASectionOfAnotherChoir_Succeeds()
    {
        AddSectionMembership(NewcomerUserId, _sopranoChoirBId);
        await _context.SaveChangesAsync();

        await Sut(MemberChoirAUserId).AddMemberAsync(_sopranoChoirAId, NewcomerUserId);

        Assert.That(
            await _context.SectionMembers.AsNoTracking()
                .CountAsync(m => m.UserId == NewcomerUserId),
            Is.EqualTo(2),
            "La regle « un seul pupitre » est bornee a une chorale : elle ne doit pas bloquer un rattachement dans une autre.");
    }

    // ---------- RemoveMemberAsync ----------

    [Test]
    public void RemoveMemberAsync_UserNotInThisSection_ThrowsNotFound()
        => Assert.ThrowsAsync<KeyNotFoundException>(
            () => Sut(MemberChoirAUserId).RemoveMemberAsync(_sopranoChoirAId, NewcomerUserId));

    [Test]
    public async Task RemoveMemberAsync_LastSectionOfTheChoir_AlsoRemovesTheChoirMembership()
    {
        AddSectionMembership(NewcomerUserId, _sopranoChoirAId);
        AddChoirMembership(NewcomerUserId, _choirAId);
        await _context.SaveChangesAsync();

        await Sut(MemberChoirAUserId).RemoveMemberAsync(_sopranoChoirAId, NewcomerUserId);

        Assert.Multiple(() =>
        {
            Assert.That(_context.SectionMembers.AsNoTracking().Any(m => m.UserId == NewcomerUserId), Is.False);
            Assert.That(
                _context.SpaceMembers.AsNoTracking().Any(m => m.UserId == NewcomerUserId && m.ChoirId == _choirAId),
                Is.False,
                "Sortir du dernier pupitre d'une chorale sort aussi de la chorale.");
        });
    }

    [Test]
    public async Task RemoveMemberAsync_StillInAnotherSectionOfTheChoir_KeepsTheChoirMembership()
    {
        // Etat impossible via AddMemberAsync (la garde d'unicite l'interdit) mais atteignable en
        // base : ce test fige la branche `otherSections` du service, qui existe pour lui.
        AddSectionMembership(NewcomerUserId, _sopranoChoirAId);
        AddSectionMembership(NewcomerUserId, _altoChoirAId);
        AddChoirMembership(NewcomerUserId, _choirAId);
        await _context.SaveChangesAsync();

        await Sut(MemberChoirAUserId).RemoveMemberAsync(_sopranoChoirAId, NewcomerUserId);

        Assert.Multiple(() =>
        {
            Assert.That(
                _context.SectionMembers.AsNoTracking().Count(m => m.UserId == NewcomerUserId),
                Is.EqualTo(1));
            Assert.That(
                _context.SpaceMembers.AsNoTracking().Any(m => m.UserId == NewcomerUserId && m.ChoirId == _choirAId),
                Is.True);
        });
    }

    [Test]
    public void RemoveMemberAsync_CallerMemberOfAnotherChoir_ThrowsForbidden()
    {
        AddSectionMembership(NewcomerUserId, _sopranoChoirAId);
        _context.SaveChanges();

        var exception = Assert.ThrowsAsync<CustomException>(
            () => Sut(OutsiderUserId).RemoveMemberAsync(_sopranoChoirAId, NewcomerUserId));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    // ---------- IsSectionLeaderAsync ----------

    [Test]
    public async Task IsSectionLeaderAsync_TargetsOneSection_NotTheWholeChoir()
    {
        var soprano = await _context.Sections.FirstAsync(s => s.Id == _sopranoChoirAId);
        soprano.SectionLeaderId = MemberChoirAUserId;
        await _context.SaveChangesAsync();

        var sut = Sut(MemberChoirAUserId);

        Assert.Multiple(async () =>
        {
            Assert.That(await sut.IsSectionLeaderAsync(_sopranoChoirAId, MemberChoirAUserId), Is.True);
            Assert.That(await sut.IsSectionLeaderAsync(_altoChoirAId, MemberChoirAUserId), Is.False,
                "IsSectionLeaderAsync porte sur UN pupitre — IsSectionLeaderInChoirAsync porte sur la chorale.");
        });
    }

    // ---------- Montage ----------

    private void CreateChoir(Guid choirId, string name)
    {
        var clientId = ChoraleDbContext.NewIdGuid();
        _context.Clients.Add(new Client
        {
            Id = clientId, Name = $"Client {name}", Status = ClientStatusEnum.Active
        });
        _context.Spaces.Add(new Space { Id = choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = clientId });
        _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = choirId, ClientId = clientId, Name = name, Status = ChoirStatusEnum.Published
        });
    }

    private void AddChoirMembership(string userId, Guid choirId)
        => _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = choirId, SpaceId = choirId,
            UserId = userId, Status = MemberStatusEnum.Active, IsDeleted = false
        });

    private void AddSectionMembership(string userId, Guid sectionId)
        => _context.SectionMembers.Add(new SectionMember
        {
            Id = ChoraleDbContext.NewIdGuid(), SectionId = sectionId, UserId = userId
        });

    private SectionService Sut(string userId)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test"))
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

        return new SectionService(services.BuildServiceProvider());
    }
}
