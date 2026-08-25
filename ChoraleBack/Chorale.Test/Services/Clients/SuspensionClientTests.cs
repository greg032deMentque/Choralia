using System;
using System.Threading.Tasks;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Services.AuthServices;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Services.Clients;

/// <summary>
/// Suspendre un client refuse l'acces a <b>toutes</b> ses chorales, d'un seul geste.
/// </summary>
/// <remarks>
/// C'est la raison d'etre du palier (`10-D23`). La regle vit dans le resolveur de roles
/// plutot que dans chaque service : un utilisateur sans role effectif est refuse par toutes
/// les policies scopees, sans qu'aucun appelant ait a y penser.
///
/// Si la propagation cesse de fonctionner, la suspension devient une fausse assurance —
/// l'ecran indique « suspendu » et les membres continuent d'acceder au contenu. Aucun test
/// existant ne le detecterait.
/// </remarks>
[TestFixture]
public sealed class SuspensionClientTests
{
    private const string UserId = "member-1";

    private ChoraleDbContext _context = null!;
    private SpaceRoleResolverService _sut = null!;
    private Guid _clientId;
    private Guid _choirA;
    private Guid _choirB;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ChoraleDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ChoraleDbContext(options);

        _clientId = ChoraleDbContext.NewIdGuid();
        _choirA = ChoraleDbContext.NewIdGuid();
        _choirB = ChoraleDbContext.NewIdGuid();

        _context.Users.Add(new User { Id = UserId, UserName = "m@test.com", Email = "m@test.com" });
        _context.Clients.Add(new Client
        {
            Id = _clientId, Name = "Client Test", Status = ClientStatusEnum.Active
        });

        foreach (var choirId in new[] { _choirA, _choirB })
        {
            _context.Spaces.Add(new Space { Id = choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = _clientId });
            _context.Choirs.Add(new ChoraleBackEnd.Data.Entities.Choir
            {
                Id = choirId, ClientId = _clientId, Name = $"Choir {choirId}", Status = ChoirStatusEnum.Published
            });
            _context.SpaceMembers.Add(new SpaceMember
            {
                Id = ChoraleDbContext.NewIdGuid(),
                UserId = UserId,
                ChoirId = choirId,
                SpaceId = choirId,
                Status = MemberStatusEnum.Active
            });
        }

        await _context.SaveChangesAsync();
        _sut = new SpaceRoleResolverService(_context);
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    [Test]
    public async Task ActiveClient_BothChoirsGrantARole()
    {
        var roles = await _sut.ResolveRolesAsync(UserId);

        Assert.Multiple(() =>
        {
            Assert.That(roles.ContainsKey(_choirA), Is.True);
            Assert.That(roles.ContainsKey(_choirB), Is.True);
        });
    }

    [TestCase(ClientStatusEnum.Suspended)]
    [TestCase(ClientStatusEnum.Archived)]
    public async Task NonActiveClient_NoChoirGrantsARole(ClientStatusEnum status)
    {
        var client = await _context.Clients.FirstAsync(c => c.Id == _clientId);
        client.Status = status;
        await _context.SaveChangesAsync();

        var roles = await _sut.ResolveRolesAsync(UserId);

        Assert.That(roles, Is.Empty,
            "La suspension doit porter sur toutes les chorales du client, pas sur une seule.");
    }

    [Test]
    public async Task StandaloneEvent_IsAffectedBySuspensionOfItsOwnClient()
    {
        // Depuis 10-D23, un evenement autonome (sans chorale porteuse) est rattache a un
        // client comme une chorale â€” via Espace.ClientId, chemin unique de resolution. Il
        // n'echappe donc plus a la suspension de CE client : le comportement inverse, valide
        // par ce test avant ce lot, laissait un trou ou n'importe quel evenement autonome
        // restait lisible quel que soit l'etat de son client.
        var otherClientId = ChoraleDbContext.NewIdGuid();
        _context.Clients.Add(new Client
        {
            Id = otherClientId, Name = "Autre Client", Status = ClientStatusEnum.Suspended
        });

        var eventId = ChoraleDbContext.NewIdGuid();
        _context.Spaces.Add(new Space
        {
            Id = eventId, SpaceType = SpaceTypeEnum.Event, ClientId = otherClientId
        });
        _context.Events.Add(new Event
        {
            Id = eventId, Title = "Event autonome", ChoirId = null,
            StartDate = DateTime.UtcNow, Type = EventTypeEnum.Concert
        });
        _context.SpaceMembers.Add(new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = UserId,
            ChoirId = null,
            SpaceId = eventId,
            Status = MemberStatusEnum.Active
        });

        await _context.SaveChangesAsync();

        var roles = await _sut.ResolveRolesAsync(UserId);

        Assert.That(roles.ContainsKey(eventId), Is.False,
            "Le client de l'evenement autonome est suspendu : l'evenement doit etre bloque, "
            + "meme si le client des DEUX autres chorales du test remaining actif.");
    }
}
