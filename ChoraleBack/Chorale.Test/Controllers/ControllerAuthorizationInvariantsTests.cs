using System.Reflection;
using ChoraleBackEnd.Api.Authorization;
using ChoraleBackEnd.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Controllers;

/// <summary>
/// Invariants d'autorisation portes par TOUS les controllers, par opposition aux fixtures
/// <c>*ControllerAuthorizationTests</c> qui figent la policy attendue action par action.
/// </summary>
/// <remarks>
/// Motif : <c>AuthController</c> est le seul controller sans <c>[Authorize]</c> de classe.
/// Une action ajoutee la est donc anonyme par defaut, et rien ne le signale — ni la
/// compilation, ni la relecture du diff, qui ne montre que la nouvelle methode. Le meme
/// oubli sur un controller porteur d'un <c>[Authorize]</c> de classe serait sans consequence.
/// La liste blanche ci-dessous est la seule declaration d'intention : y ajouter une entree
/// est un acte delibere, visible en revue.
/// </remarks>
[TestFixture]
public sealed class ControllerAuthorizationInvariantsTests
{
    /// <summary>
    /// Actions accessibles sans jeton, par conception : entree dans le produit (inscription,
    /// connexion, verification d'email, reinitialisation de mot de passe, activation de compte)
    /// et pre-visualisation d'un code d'invitation avant de rejoindre un espace.
    /// </summary>
    private static readonly HashSet<string> IntentionallyAnonymousActions =
    [
        $"{nameof(AuthController)}.{nameof(AuthController.Register)}",
        $"{nameof(AuthController)}.{nameof(AuthController.VerifyEmail)}",
        $"{nameof(AuthController)}.{nameof(AuthController.ResendVerification)}",
        $"{nameof(AuthController)}.{nameof(AuthController.Login)}",
        $"{nameof(AuthController)}.{nameof(AuthController.RefreshToken)}",
        $"{nameof(AuthController)}.{nameof(AuthController.ForgotPassword)}",
        $"{nameof(AuthController)}.{nameof(AuthController.ResetPassword)}",
        $"{nameof(AuthController)}.{nameof(AuthController.ActivateAccount)}",
        $"{nameof(OnboardingController)}.{nameof(OnboardingController.PreviewCode)}"
    ];

    private static IEnumerable<TestCaseData> AllActions()
    {
        foreach (var controller in ControllerTypes())
        foreach (var action in ActionsOf(controller))
            yield return new TestCaseData(controller, action)
                .SetName($"{controller.Name}.{action.Name}");
    }

    [TestCaseSource(nameof(AllActions))]
    public void EveryAction_IsProtected_OrExplicitlyDeclaredAnonymous(Type controller, MethodInfo action)
    {
        var key = $"{controller.Name}.{action.Name}";
        var isProtected = !action.IsDefined(typeof(AllowAnonymousAttribute), inherit: true)
            && (action.IsDefined(typeof(AuthorizeAttribute), inherit: true)
                || controller.IsDefined(typeof(AuthorizeAttribute), inherit: true));

        if (isProtected)
        {
            Assert.That(IntentionallyAnonymousActions, Does.Not.Contain(key),
                $"{key} est protegee : retirer son entree de la liste blanche, qui ne doit contenir que des actions reellement anonymes.");
            return;
        }

        Assert.That(IntentionallyAnonymousActions, Does.Contain(key),
            $"{key} est accessible sans authentification. Si c'est voulu, l'ajouter explicitement "
            + $"a IntentionallyAnonymousActions ; sinon, poser un [Authorize] sur l'action ou sur {controller.Name}.");
    }

    /// <summary>
    /// Une policy inconnue du conteneur ne se voit ni a la compilation ni au demarrage : elle
    /// leve <c>InvalidOperationException</c> a la premiere requete sur l'endpoint concerne,
    /// donc en production, sur ce seul endpoint.
    /// </summary>
    [TestCaseSource(nameof(AllActions))]
    public void EveryPolicyName_IsADeclaredAuthorizationPolicy(Type controller, MethodInfo action)
    {
        var declared = typeof(AuthorizationPolicies)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false })
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        var policies = action.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
            .Select(a => a.Policy)
            .Where(p => !string.IsNullOrEmpty(p));

        foreach (var policy in policies)
            Assert.That(declared, Does.Contain(policy),
                $"{controller.Name}.{action.Name} reference la policy « {policy} », absente de AuthorizationPolicies.");
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
