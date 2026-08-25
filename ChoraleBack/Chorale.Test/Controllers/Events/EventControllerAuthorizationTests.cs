using System.Linq;
using System.Reflection;
using ChoraleBackEnd.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Controllers.Events;

[TestFixture]
public sealed class EventControllerAuthorizationTests
{
    [TestCase(nameof(EventController.Update))]
    [TestCase(nameof(EventController.Delete))]
    public void ActionsDwrite_RequierentLaPolicySpaceManager(string methodName)
    {
        var methode = typeof(EventController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == methodName);

        var authorizeAttribute = methode.GetCustomAttribute<AuthorizeAttribute>();

        Assert.That(authorizeAttribute, Is.Not.Null, $"{methodName} doit porter un [Authorize].");
        Assert.That(authorizeAttribute!.Policy, Is.EqualTo("SpaceManager"));
        Assert.That(authorizeAttribute.Roles, Is.Null, "Ne doit plus utiliser Roles = \"Admin\".");
    }

    [Test]
    public void Create_NePorteNoPolicyDeMethode_HeriteDuBearerDeClasse()
    {
        var methode = typeof(EventController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == nameof(EventController.Create));

        var authorizeAttribute = methode.GetCustomAttribute<AuthorizeAttribute>();

        Assert.That(authorizeAttribute, Is.Null,
            "Create ne doit pas porter de policy de méthode : tout utilisateur authentifié peut créer un événement.");
    }
}
