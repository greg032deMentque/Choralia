namespace ChoraleBackEnd.Common.Enums;

// ATTENTION : les ordinaux sont PERSISTES en base (stockage entier) et exposes
// au front avec les memes valeurs. Ne jamais reorder ni inserer au milieu :
// toute evolution se fait en FIN de liste, sinon migration de donnees obligatoire.

public enum EventTypeEnum
{
    Concert = 0,
    Rehearsal = 1,
    Wedding = 2,
    Mass = 3,
    Funeral = 4,
    Other = 5
}
