namespace ChoraleBackEnd.Api.Authorization;

public static class AuthorizationPolicies
{
    public const string Bearer = "Bearer";
    public const string ChoirManagerOrSectionLeader = "ChoirManagerOrSectionLeader";
    public const string ChoirManager = "ChoirManager";
    public const string SpaceManager = "SpaceManager";

    /// <summary>
    /// Scope <b>client</b>, lu dans la route (`clientId`) et non dans un en-tete : un client
    /// n'est pas un espace. Ne confere aucun droit sur le contenu d'une chorale (`10-D23`).
    /// </summary>
    public const string ClientManager = "ClientManager";

    /// <summary>
    /// Satisfaite par le claim global Admin, ou par le role <c>ManagerClient</c> sur le
    /// client vise. Utilisee pour les ecritures de chorale (`ChoirController`), ou le
    /// <c>clientId</c> n'est pas toujours dans la route : voir
    /// <see cref="ClientRoleAuthorizationHandler"/>, qui lit alors le corps de la requete.
    /// A la difference de <see cref="ClientManager"/>, cette policy porte sur la creation
    /// de <b>contenu</b> scope client (une chorale), pas sur le client lui-meme.
    /// </summary>
    public const string AdminOrClientManager = "AdminOrClientManager";
}
