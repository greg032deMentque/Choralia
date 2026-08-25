using System.Linq.Expressions;

namespace ChoraleBackEnd.Common.Helpers;

/// <summary>
/// Applique un tri demande par le client (`SortActive`/`SortDirection`) a une requete, sans
/// jamais interpreter la chaine recue : chaque appelant fournit sa propre liste blanche
/// explicite (colonne autorisee -> expression de tri fortement typee). Aucune reflexion,
/// aucun LINQ dynamique, aucune construction d'expression a partir de la valeur brute.
/// </summary>
/// <remarks>
/// `SortActive` inconnu, absent ou vide retombe silencieusement sur le tri par defaut de
/// l'appelant — jamais d'exception, jamais d'interpretation de la valeur recue (le client ne
/// doit jamais pouvoir provoquer une erreur serveur avec une valeur de tri invalide).
///
/// Le tri par defaut est fourni tel quel par l'appelant et doit deja porter son propre
/// departage deterministe s'il en avait un avant cette correction : cette methode ne l'altere
/// pas, pour garantir qu'un appel sans `SortActive` produit EXACTEMENT le meme result
/// qu'avant l'introduction du tri dynamique. Seule la branche « colonne choisie » ajoute un
/// departage sur `Id` : sans lui, deux lignes de meme valeur de tri produiraient une
/// pagination non deterministe (lignes dupliquees ou manquantes d'une page a l'autre).
/// </remarks>
public static class SortHelper
{
    public static IOrderedQueryable<T> ApplySort<T, TId>(
        this IQueryable<T> query,
        string? sortActive,
        string? sortDirection,
        IReadOnlyDictionary<string, Expression<Func<T, object?>>> allowedColumns,
        Expression<Func<T, TId>> idSelector,
        Func<IQueryable<T>, IOrderedQueryable<T>> defaultSort)
    {
        ArgumentNullException.ThrowIfNull(allowedColumns);
        ArgumentNullException.ThrowIfNull(idSelector);
        ArgumentNullException.ThrowIfNull(defaultSort);

        if (string.IsNullOrWhiteSpace(sortActive) || !allowedColumns.TryGetValue(sortActive, out var column))
            return defaultSort(query);

        // Insensible a la casse mais invariant de culture : le serveur de production tourne
        // sous Ubuntu (ICU), le poste de dev sous Windows (NLS) — une comparaison dependante
        // de la culture ne se comporte pas pareil des deux cotes. Toute valeur autre que
        // "desc" vaut "asc".
        var descendant = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        var trie = descendant ? query.OrderByDescending(column) : query.OrderBy(column);
        return trie.ThenBy(idSelector);
    }
}
