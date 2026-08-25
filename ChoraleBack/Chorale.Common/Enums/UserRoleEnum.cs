namespace ChoraleBackEnd.Common.Enums;

// ATTENTION : les ordinaux sont PERSISTES en base (stockage entier) et exposes
// au front avec les memes valeurs. Ne jamais reorder ni inserer au milieu :
// toute evolution se fait en FIN de liste, sinon migration de donnees obligatoire.

public enum UserRoleEnum
{
    Admin = 0,
    SectionLeader = 1,
    Singer = 2,
    Manager = 3,
    Organizer = 4,
    Participant = 5,

    /// <summary>
    /// Role scope au <b>client</b>, pas a un espace (`10-D23`). Permet a une personne cote
    /// client de create et fermer ses propres chorales et d'y nommer les responsables, sans
    /// detenir le claim global Admin.
    ///
    /// Ne donne <b>aucun</b> droit sur le contenu d'une chorale : pour agir dedans, il faut
    /// y etre Responsable. Le role client ouvre la porte, il n'entre pas dans la piece.
    /// </summary>
    ClientManager = 6
}
