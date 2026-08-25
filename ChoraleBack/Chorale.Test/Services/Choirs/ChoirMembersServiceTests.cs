using System.Net;
using System.Security.Claims;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services.AuthServices;
using ChoraleBackEnd.Services.ChoirServices;
using ChoraleBackEnd.Services.Technical;
using ChoraleBackEnd.Services.UserServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ChoraleBackEnd.Test.Fakes;
using ChoraleBackEnd.Test.TestSupport;
using NUnit.Framework;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Common.Exceptions;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services;
using ChoraleBackEnd.ViewModels.ChoirMembers;
using ChoraleBackEnd.ViewModels.Choirs;

namespace ChoraleBackEnd.Test.Services.Choirs;

/// <summary>
/// Fichier canonique manquant avant ce lot pour <see cref="ChoirMembersService"/> (seul
/// <c>ChoirMembersServiceSortTests</c> existait, dédié au tri). Couvre deux corrections :
/// la fuite d'identité inter-clients sur <see cref="ChoirMembersService.UpdateAsync"/>, et
/// l'impasse « dernier chef de chœur » sur les portes de ce service.
/// </summary>
[TestFixture]
public sealed class ChoirMembersServiceTests
{
    private const string CallerUserId = "caller-1";
    private const string InvitedUserId = "invited-1";
    private const string ActivatedUserId = "activated-1";
    private const string SoleManagerUserId = "sole-manager-1";
    private const string SecondManagerUserId = "second-manager-1";
    private const string OutsiderUserId = "outsider-1";
    private const string OutsiderEmail = "outsider@t.com";

    private ChoraleDbContext _context = null!;
    private FakeEmailService _fakeEmailService = null!;
    private Guid _clientId;
    private Guid _choirId;
    private Guid _invitedMemberId;
    private Guid _activatedMemberId;
    private Guid _soleManagerMemberId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);
        _fakeEmailService = new FakeEmailService();

        _clientId = ChoraleDbContext.NewIdGuid();
        _choirId = ChoraleDbContext.NewIdGuid();

        _context.Users.Add(new User { Id = CallerUserId, UserName = "caller@t.com", Email = "caller@t.com" });
        _context.Users.Add(new User
        {
            Id = InvitedUserId, UserName = "invited@t.com", Email = "invited@t.com", EmailConfirmed = false
        });
        _context.Users.Add(new User
        {
            Id = ActivatedUserId, UserName = "activated@t.com", Email = "activated@t.com", EmailConfirmed = true
        });
        _context.Users.Add(new User
        {
            Id = SoleManagerUserId, UserName = "manager@t.com", Email = "manager@t.com", EmailConfirmed = true
        });
        _context.Users.Add(new User
        {
            Id = SecondManagerUserId, UserName = "manager2@t.com", Email = "manager2@t.com", EmailConfirmed = true
        });

        // Compte deja actif, membre d'aucune chorale : c'est la branche « compte existant »
        // de InviteAsync. NormalizedEmail renseigne comme le fait UserManager.CreateAsync,
        // sinon FindByEmailAsync ne le retrouve pas.
        _context.Users.Add(new User
        {
            Id = OutsiderUserId, UserName = OutsiderEmail, Email = OutsiderEmail,
            NormalizedUserName = OutsiderEmail.ToUpperInvariant(),
            NormalizedEmail = OutsiderEmail.ToUpperInvariant(),
            EmailConfirmed = true
        });

        // Rôle applicatif attribué à tout compte invité (UserInvitationService). Absent du
        // store, UserManager.AddToRoleAsync lève — en production il vient de SeedDatabase.
        _context.Roles.Add(new IdentityRole
        {
            Id = Guid.NewGuid().ToString(),
            Name = UserRoleEnum.Singer.ToString(),
            NormalizedName = UserRoleEnum.Singer.ToString().ToUpperInvariant()
        });

        _context.Clients.Add(new Client { Id = _clientId, Name = "Client", Status = ClientStatusEnum.Active });
        _context.Spaces.Add(new Space { Id = _choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = _clientId });
        _context.Choirs.Add(new Choir
        {
            Id = _choirId, ClientId = _clientId, Name = "Choir", Status = ChoirStatusEnum.Published
        });

        // Les 4 sections existent des la creation de la chorale (ChoirService) : l'invitation
        // rattache a une section existante, elle n'en cree jamais.
        foreach (var voicePart in Enum.GetValues<VoicePartEnum>())
        {
            _context.Sections.Add(new Section
            {
                Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId, VoicePart = voicePart
            });
        }

        // EnsureCanWriteAsync (appele par toutes les methodes testees ici via
        // EnsureWriteMemberAsync) exige que l'APPELANT soit lui-meme membre actif de la
        // chorale — sans cette ligne, tous les appels echouent en 409 avant meme d'atteindre
        // le code sous test.
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId, SpaceId = _choirId,
            UserId = CallerUserId, Status = MemberStatusEnum.Active
        });

        var invitedMember = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId, SpaceId = _choirId,
            UserId = InvitedUserId, Status = MemberStatusEnum.Invited
        };
        _invitedMemberId = invitedMember.Id;
        _context.SpaceMembers.Add(invitedMember);

        var activatedMember = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId, SpaceId = _choirId,
            UserId = ActivatedUserId, Status = MemberStatusEnum.Active
        };
        _activatedMemberId = activatedMember.Id;
        _context.SpaceMembers.Add(activatedMember);

        var soleManager = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId, SpaceId = _choirId,
            UserId = SoleManagerUserId, Status = MemberStatusEnum.Active
        };
        _soleManagerMemberId = soleManager.Id;
        _context.SpaceMembers.Add(soleManager);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(), SpaceMemberId = soleManager.Id, Role = UserRoleEnum.Manager
        });

        await _context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    // ---- Point 1 : fuite d'identite inter-clients (UpdateAsync) --------------------------

    [Test]
    public async Task UpdateAsync_ActiveAccount_RejectsRenameAsForbidden()
    {
        var sut = CreateService(CallerUserId);

        var exception = Assert.ThrowsAsync<CustomException>(() => sut.UpdateAsync(
            _choirId, new UpdateChoirMemberViewModel { Id = _activatedMemberId, Firstname = "Nouveau" }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        var user = await _context.Users.FirstAsync(u => u.Id == ActivatedUserId);
        Assert.That(user.Firstname, Is.Not.EqualTo("Nouveau"));
    }

    [Test]
    public async Task UpdateAsync_NonActiveAccount_AllowsRename()
    {
        var sut = CreateService(CallerUserId);

        await sut.UpdateAsync(
            _choirId, new UpdateChoirMemberViewModel { Id = _invitedMemberId, Firstname = "Nouveau" });

        var user = await _context.Users.FirstAsync(u => u.Id == InvitedUserId);
        Assert.That(user.Firstname, Is.EqualTo("Nouveau"));
    }

    [Test]
    public void UpdateAsync_ActiveAccountWithoutFieldsSet_DoesNotThrow()
    {
        var sut = CreateService(CallerUserId);

        Assert.DoesNotThrowAsync(() => sut.UpdateAsync(
            _choirId, new UpdateChoirMemberViewModel { Id = _activatedMemberId }));
    }

    // ---- Point 2 : impasse "dernier chef de chœur" ---------------------------------------

    [Test]
    public async Task ChangeRoleAsync_LastManagerToSinger_ThrowsConflict409()
    {
        var sut = CreateService(CallerUserId);

        var exception = Assert.ThrowsAsync<CustomException>(() => sut.ChangeRoleAsync(
            _choirId, new ChangeMemberRoleViewModel { Id = _soleManagerMemberId, Role = UserRoleEnum.Singer }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        var stillHasRole = await _context.SpaceMemberRoles
            .AnyAsync(r => r.SpaceMemberId == _soleManagerMemberId && r.Role == UserRoleEnum.Manager);
        Assert.That(stillHasRole, Is.True);
    }

    [Test]
    public async Task ChangeRoleAsync_ToSinger_WithAnotherActiveManager_Succeeds()
    {
        await AddSecondManagerAsync();
        var sut = CreateService(CallerUserId);

        await sut.ChangeRoleAsync(
            _choirId, new ChangeMemberRoleViewModel { Id = _soleManagerMemberId, Role = UserRoleEnum.Singer });

        var stillHasRole = await _context.SpaceMemberRoles
            .AnyAsync(r => r.SpaceMemberId == _soleManagerMemberId && r.Role == UserRoleEnum.Manager);
        Assert.That(stillHasRole, Is.False);
    }

    [Test]
    public async Task ChangeStatusAsync_ArchivesLastManager_ThrowsConflict409()
    {
        var sut = CreateService(CallerUserId);

        var exception = Assert.ThrowsAsync<CustomException>(() => sut.ChangeStatusAsync(
            _choirId, new ChangeMemberStatusViewModel { Id = _soleManagerMemberId, Status = MemberStatusEnum.Archived }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        var member = await _context.SpaceMembers.FirstAsync(m => m.Id == _soleManagerMemberId);
        Assert.That(member.Status, Is.EqualTo(MemberStatusEnum.Active));
    }

    [Test]
    public async Task ChangeStatusAsync_SetsLastManagerToInactive_ThrowsConflict409()
    {
        var sut = CreateService(CallerUserId);

        var exception = Assert.ThrowsAsync<CustomException>(() => sut.ChangeStatusAsync(
            _choirId, new ChangeMemberStatusViewModel { Id = _soleManagerMemberId, Status = MemberStatusEnum.Inactive }));

        Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        var member = await _context.SpaceMembers.FirstAsync(m => m.Id == _soleManagerMemberId);
        Assert.That(member.Status, Is.EqualTo(MemberStatusEnum.Active));
    }

    // ---- Point 3 : la voix a l'invitation --------------------------------------------------

    [Test]
    public async Task InviteAsync_UnknownAccount_WithVoicePart_CreatesSectionMember()
    {
        const string email = "nouveau-choriste@t.com";
        var sut = CreateService(CallerUserId);

        await sut.InviteAsync(_choirId, new InviteMemberViewModel
        {
            ChoirId = _choirId,
            Email = email,
            Firstname = "Nouveau",
            PrimaryVoicePart = VoicePartEnum.Soprano
        });

        var user = await _context.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        var sectionMember = await _context.SectionMembers
            .AsNoTracking()
            .Include(sm => sm.Section)
            .SingleAsync(sm => sm.UserId == user.Id);
        var member = await _context.SpaceMembers.AsNoTracking().SingleAsync(m => m.UserId == user.Id);

        Assert.Multiple(() =>
        {
            Assert.That(sectionMember.Section.VoicePart, Is.EqualTo(VoicePartEnum.Soprano));
            Assert.That(sectionMember.Section.ChoirId, Is.EqualTo(_choirId));
            Assert.That(member.Status, Is.EqualTo(MemberStatusEnum.Invited));
            // Cette branche réimplémentait la création du compte invité en oubliant ce
            // drapeau : les comptes ainsi créés échappaient à GuestAccountLifecycleService.
            Assert.That(user.IsGuestAccount, Is.True);
        });
    }

    [Test]
    public async Task InviteAsync_UnknownAccount_WithoutVoicePart_CreatesNoSectionMember()
    {
        const string email = "sans-voix@t.com";
        var sut = CreateService(CallerUserId);

        // La voix reste optionnelle tant que le front ne la propose pas : l'invitation doit
        // aboutir sans elle, et surtout ne pas inventer de pupitre par défaut.
        await sut.InviteAsync(_choirId, new InviteMemberViewModel
        {
            ChoirId = _choirId,
            Email = email,
            Firstname = "Sans"
        });

        var user = await _context.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        var sectionMemberCount = await _context.SectionMembers.CountAsync(sm => sm.UserId == user.Id);

        Assert.That(sectionMemberCount, Is.Zero);
    }

    [Test]
    public async Task InviteAsync_ExistingAccount_WithVoicePart_CreatesSectionMember()
    {
        var sut = CreateService(CallerUserId);

        await sut.InviteAsync(_choirId, new InviteMemberViewModel
        {
            ChoirId = _choirId,
            Email = OutsiderEmail,
            PrimaryVoicePart = VoicePartEnum.Bass
        });

        var sectionMember = await _context.SectionMembers
            .AsNoTracking()
            .Include(sm => sm.Section)
            .SingleAsync(sm => sm.UserId == OutsiderUserId);
        var member = await _context.SpaceMembers.AsNoTracking().SingleAsync(m => m.UserId == OutsiderUserId);

        Assert.Multiple(() =>
        {
            Assert.That(sectionMember.Section.VoicePart, Is.EqualTo(VoicePartEnum.Bass));
            // Un compte déjà revendiqué n'a rien à activer : il entre directement en Actif.
            Assert.That(member.Status, Is.EqualTo(MemberStatusEnum.Active));
        });
    }

    private async Task AddSecondManagerAsync()
    {
        var secondManager = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(), ChoirId = _choirId, SpaceId = _choirId,
            UserId = SecondManagerUserId, Status = MemberStatusEnum.Active
        };
        _context.SpaceMembers.Add(secondManager);
        _context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(), SpaceMemberId = secondManager.Id, Role = UserRoleEnum.Manager
        });
        await _context.SaveChangesAsync();
    }

    private ChoirMembersService CreateService(string userId)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId)], "Test"))
            }
        };

        var mapper = new MapperConfiguration(cfg => cfg.AddMaps(typeof(ChoirViewModel).Assembly), NullLoggerFactory.Instance).CreateMapper();

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
            .AddDefaultTokenProviders()
            .AddInvitationTokenProvider();

        var serviceProvider = services.BuildServiceProvider();
        return new ChoirMembersService(
            serviceProvider,
            new SectionService(serviceProvider),
            new AuditLogService(serviceProvider),
            new FakeServiceLimitService(),
            new MembershipService(serviceProvider),
            new UserInvitationService(serviceProvider, _fakeEmailService),
            new MemberEnrollmentService(serviceProvider),
            new SectionVoicePartLookupService(_context));
    }
}
