using System.Reflection;
using ChoraleBackEnd.Api.Controllers.AdminControllers;
using ChoraleBackEnd.Services.ChoirServices;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Services.Songs;

/// <summary>
/// Decision produit (Q17/Q18, lot 4) : le catalogue chants de l'administration est un
/// perimetre STRICTEMENT lecture seule, sans acces au contenu (partitions, enregistrements).
/// Aucun endpoint d'ecriture n'existe pour cette raison â€” plutot que de tenter un appel qui
/// n'a pas de methode a appeler, ce fichier verifie par reflexion que la surface exposee
/// (interface de service + contrôleur) ne contient reellement aucune methode d'ecriture ni de
/// fichier, pour qu'un ajout futur ne puisse pas regresser cette garantie silencieusement.
/// </summary>
[TestFixture]
public sealed class AdminSongWriteTests
{
    [Test]
    public void IAdminSongService_ExposesNoWriteMethodNorFileAccess()
    {
        var methodNames = typeof(IAdminSongService).GetMethods().Select(m => m.Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(methodNames, Has.None.Match("(?i)^(create|update|delete|update|delete|ajouter|retirer)"));
            Assert.That(methodNames, Has.None.Match("(?i)(score|recording|fichier|upload|download)"));
        });
    }

    [Test]
    public void AdminSongController_ExposesOnlyTwoReadOnlyActions()
    {
        var actions = typeof(AdminSongController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .ToList();

        Assert.That(actions.Select(a => a.Name), Is.EquivalentTo(new[] { "GetPagedCatalogue", "GetGroupChoirs" }));

        foreach (var action in actions)
        {
            var attributeTypes = action.GetCustomAttributes(inherit: true).Select(a => a.GetType());

            Assert.That(attributeTypes, Has.None.EqualTo(typeof(HttpPutAttribute)),
                $"{action.Name} ne doit porter aucun verbe d'ecriture.");
            Assert.That(attributeTypes, Has.None.EqualTo(typeof(HttpDeleteAttribute)),
                $"{action.Name} ne doit porter aucun verbe d'ecriture.");
        }
    }

    [Test]
    public void AdminSongService_DoesNotDependOnAnyFileService()
    {
        var constructor = typeof(AdminSongService).GetConstructors().Single();
        var parameterTypeNames = constructor.GetParameters().Select(p => p.ParameterType.Name);

        Assert.That(parameterTypeNames, Has.None.Match("(?i)path|file|fichier"));
    }
}
