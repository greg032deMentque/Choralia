namespace ChoraleBackEnd.Common.Enums;

// ATTENTION : les ordinaux sont PERSISTES en base (stockage entier) et exposes
// au front avec les memes valeurs. Ne jamais reorder ni inserer au milieu :
// toute evolution se fait en FIN de liste, sinon migration de donnees obligatoire.

// InstructionScopeEnum a ete SUPPRIME : une consigne n'a plus qu'une seule cible, le chant
// auquel elle est attachee (decision produit, voir Spec/chorale/10-decisions.md). Les trois
// autres portees — chorale entiere, pupitre, evenement — ont disparu du modele avec la
// migration `InstructionsSongScopeOnly`, qui supprime aussi les lignes correspondantes. Une
// enumeration a une seule valeur ne denote rien : la colonne `Scope` a donc ete supprimee, pas
// reduite. Ne pas la reintroduire sans rouvrir la decision.

/// <summary>
/// Cycle de vie d'une consigne. Aligne sur celui des lists de chants : une consigne en
/// brouillon n'est pas visible des membres.
/// </summary>
public enum InstructionStatusEnum
{
    Draft = 0,
    Published = 1,
    Archived = 2
}
