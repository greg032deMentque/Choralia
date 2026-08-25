namespace ChoraleBackEnd.Common.Enums;

// ATTENTION : les ordinaux sont PERSISTES en base (stockage entier) et exposes
// au front avec les memes valeurs. Ne jamais reorder ni inserer au milieu :
// toute evolution se fait en FIN de liste, sinon migration de donnees obligatoire.

public enum SongListTypeEnum
{
    Free = 0,
    Event = 1,
    Season = 2,
    Section = 3
}
