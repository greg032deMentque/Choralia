namespace ChoraleBackEnd.Common.Constants;

/// <summary>
/// Nom du fournisseur de jeton Identity dedie a l'invitation, et purpose du lien
/// d'activation de compte.
/// </summary>
/// <remarks>
/// L'invitation ne peut pas partager le fournisseur Identity par defaut : sa duree de vie
/// (<c>DataProtectionTokenProviderOptions</c>, 1 h dans <c>Program.cs</c>) est celle du
/// « mot de passe oublie ». Un invite qui ouvrait son mail le lendemain ne pouvait plus
/// rejoindre la chorale.
///
/// Ces deux litteraux lient trois points distants — l'enregistrement du fournisseur
/// (<c>Program.cs</c>), l'emission (<c>UserInvitationService</c>, <c>AdminUserService</c>) et
/// la verification (<c>AccountService.ActivateAccountAsync</c>). Une divergence entre eux ne
/// se voit qu'a l'execution, sur un lien deja parti chez l'invite : d'ou le point unique.
/// </remarks>
public static class AccountTokenConstants
{
    public const string InvitationTokenProvider = "Invitation";

    public const string AccountActivationPurpose = "AccountActivation";
}
