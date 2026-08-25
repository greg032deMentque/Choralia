using ChoraleBackEnd.Api.Identity;
using ChoraleBackEnd.Common.Constants;
using Microsoft.AspNetCore.Identity;

namespace ChoraleBackEnd.Test.TestSupport;

/// <summary>
/// Réplique côté test l'enregistrement du fournisseur de jeton d'invitation fait par
/// <c>Program.cs</c>.
/// </summary>
/// <remarks>
/// Point unique volontaire : le nom du fournisseur ne doit exister qu'une fois par surface.
/// Recopié dans chaque fixture, il aurait divergé du nom réel sans qu'aucun test ne le voie —
/// tous auraient échoué de la même façon, sur un défaut qui n'est pas celui qu'ils testent.
/// </remarks>
public static class IdentityBuilderTestExtensions
{
    public static IdentityBuilder AddInvitationTokenProvider(this IdentityBuilder builder)
        => builder.AddTokenProvider<InvitationTokenProvider>(AccountTokenConstants.InvitationTokenProvider);
}
