using Microsoft.AspNetCore.Identity;

namespace ChoraleBackEnd.Api.Identity;

/// <summary>
/// Options propres au fournisseur de jeton d'invitation.
/// </summary>
/// <remarks>
/// Le type distinct est ce qui porte la separation : <c>DataProtectorTokenProvider</c> lit sa
/// duree de vie dans <c>IOptions&lt;DataProtectionTokenProviderOptions&gt;</c>, partage par
/// TOUS les jetons Identity. Sous-classer les options donne a l'invitation sa propre duree
/// sans deplacer celle du « mot de passe oublie ».
///
/// <c>Name</c> sert de purpose au DataProtector sous-jacent : distinct du fournisseur par
/// defaut, un jeton d'invitation n'est donc pas lisible comme jeton de reinitialisation.
/// </remarks>
public sealed class InvitationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public const int DefaultLifespanHours = 72;

    public InvitationTokenProviderOptions()
    {
        Name = "InvitationTokenProvider";
        TokenLifespan = TimeSpan.FromHours(DefaultLifespanHours);
    }
}
