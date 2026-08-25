using System.Reflection;
using ChoraleBackEnd.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Controllers;

/// <summary>
/// Invariants de model binding portes par TOUS les controllers, sur le meme principe que
/// <c>ControllerAuthorizationInvariantsTests</c> : un seul test structurel plutot qu'une
/// verification repetee endpoint par endpoint.
/// </summary>
/// <remarks>
/// Piege ASP.NET Core : quand un parametre d'action <c>[FromQuery]</c> de type complexe porte
/// le meme nom (insensible a la casse) qu'une des proprietes publiques de son propre type, le
/// model binder confond la resolution de prefixe et retombe silencieusement sur les valeurs
/// par defaut de TOUT l'objet — sans erreur ni avertissement, ni a la compilation ni a
/// l'execution. Cas reel corrige ici : `GetPaged([FromQuery] XxxPagedFilterViewModel filter, ...)`,
/// ou `filter` collisionnait avec la propriete `Filter` heritee de `PaginateViewModel` — des
/// qu'un appelant envoyait `?Filter=...`, toute la pagination (Page, PageSize, Status, etc.)
/// retombait sur ses valeurs par defaut. Correctif : renommer le PARAMETRE (`request`), jamais
/// la propriete — le nom d'un parametre C# est invisible cote HTTP, aucun contrat casse.
/// </remarks>
[TestFixture]
public sealed class ControllerModelBindingInvariantsTests
{
    private static IEnumerable<TestCaseData> AllFromQueryComplexParameters()
    {
        foreach (var controller in ControllerTypes())
        foreach (var action in ActionsOf(controller))
        foreach (var parameter in action.GetParameters())
        {
            if (!parameter.IsDefined(typeof(FromQueryAttribute), inherit: true))
                continue;

            if (!IsComplexType(parameter.ParameterType))
                continue;

            yield return new TestCaseData(controller, action, parameter)
                .SetName($"{controller.Name}.{action.Name}({parameter.Name})");
        }
    }

    [TestCaseSource(nameof(AllFromQueryComplexParameters))]
    public void FromQueryParameterName_NeverCollidesWithItsOwnPropertyNames(
        Type controller, MethodInfo action, ParameterInfo parameter)
    {
        var propertyNames = parameter.ParameterType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name);

        Assert.That(propertyNames, Has.None.EqualTo(parameter.Name).IgnoreCase,
            $"{controller.Name}.{action.Name} : le parametre [FromQuery] « {parameter.Name} » "
            + $"porte le meme nom qu'une propriete de {parameter.ParameterType.Name}. ASP.NET Core "
            + "ne peut pas resoudre ce binding et retombe silencieusement sur les valeurs par "
            + "defaut de tout l'objet. Renommer le PARAMETRE (jamais la propriete).");
    }

    private static bool IsComplexType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsClass && underlying != typeof(string);
    }

    private static IEnumerable<Type> ControllerTypes()
        => typeof(AuthController).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsPublic: true } && typeof(ControllerBase).IsAssignableFrom(t))
            .OrderBy(t => t.Name);

    /// <summary>
    /// Actions au sens MVC : methodes publiques d'instance declarees par le controller
    /// lui-meme, hors membres herites de <see cref="ControllerBase"/> et hors accesseurs.
    /// </summary>
    private static IEnumerable<MethodInfo> ActionsOf(Type controller)
        => controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && !m.IsDefined(typeof(NonActionAttribute), inherit: true))
            .OrderBy(m => m.Name);
}
