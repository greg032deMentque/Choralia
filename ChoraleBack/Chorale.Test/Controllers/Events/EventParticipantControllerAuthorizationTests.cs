using System.Linq;
using System.Reflection;
using ChoraleBackEnd.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Controllers.Events;

[TestFixture]
public sealed class EventParticipantControllerAuthorizationTests
{
    [TestCase(nameof(EventParticipantController.Invite))]
    [TestCase(nameof(EventParticipantController.Delete))]
    public void ActionsDeManagement_RequierentLaPolicySpaceManager(string methodName)
    {
        var methode = typeof(EventParticipantController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == methodName);

        var authorizeAttribute = methode.GetCustomAttribute<AuthorizeAttribute>();

        Assert.That(authorizeAttribute, Is.Not.Null, $"{methodName} doit porter un [Authorize].");
        Assert.That(authorizeAttribute!.Policy, Is.EqualTo("SpaceManager"));
    }

    [TestCase(nameof(EventParticipantController.Rsvp))]
    [TestCase(nameof(EventParticipantController.GetPaged))]
    public void ActionsEnLibreAcces_NePortentNoPolicyDeMethode(string methodName)
    {
        var methode = typeof(EventParticipantController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == methodName);

        var authorizeAttribute = methode.GetCustomAttribute<AuthorizeAttribute>();

        Assert.That(authorizeAttribute, Is.Null,
            $"{methodName} ne doit pas porter de policy de méthode : la garde métier est faite dans le service.");
    }
}
