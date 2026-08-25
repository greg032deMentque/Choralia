using System.Globalization;
using System.Text;

namespace ChoraleBackEnd.Common.Helpers;

/// <summary>
/// Calcule la cle de regroupement d'AFFICHAGE utilisee par le catalogue chants de
/// l'administration (`AdminSongService`). Chaque chorale cree ses propres <c>Song</c> —
/// le meme chant depose par sept chorales produit sept lignes distinctes en base ; cette
/// classe ne fusionne rien en base, elle calcule seulement la cle qui permet de les regroup
/// a l'affichage.
/// </summary>
/// <remarks>
/// Le serveur de production tourne sous Ubuntu (ICU), le poste de developpement sous
/// Windows (NLS) : <c>ToLower()</c> sans culture invariante ne produit pas le meme result
/// sur certains caracteres selon la plateforme. La cle est donc calculee explicitement en
/// <c>ToLowerInvariant()</c> avec normalisation Unicode explicite, et n'est <b>jamais</b>
/// delegue a la collation SQL Server — sinon le regroupement differerait entre le poste de
/// dev et la production, silencieusement.
///
/// Regle de non-fusion : un composer absent, nul ou blanc ne fusionne jamais deux chants
/// entre eux (« Ave Maria » de Schubert et celui de Gounod ne doivent jamais devenir une
/// seule ligne) — dans ce cas, la cle integre l'identifiant du chant pour garantir un groupe
/// a lui seul.
/// </remarks>
public static class SongKeyHelper
{
    public static string ComputeKey(Guid songId, string? title, string? composer)
    {
        var titleNormalise = Normalize(title);

        return string.IsNullOrWhiteSpace(composer)
            ? $"{titleNormalise}|{songId:N}"
            : $"{titleNormalise}|{Normalize(composer)}";
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var minuscule = value.Trim().ToLowerInvariant();

        var formeDecomposee = minuscule.Normalize(NormalizationForm.FormD);
        var withoutDiacriticsOrPunctuation = new StringBuilder(formeDecomposee.Length);

        foreach (var caractere in formeDecomposee)
        {
            var categorie = CharUnicodeInfo.GetUnicodeCategory(caractere);
            if (categorie == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsPunctuation(caractere)) continue;

            withoutDiacriticsOrPunctuation.Append(caractere);
        }

        return ReduireSpaces(withoutDiacriticsOrPunctuation.ToString().Normalize(NormalizationForm.FormC));
    }

    /// <summary>
    /// Reduit toute sequence d'espaces (y compris insecables, categorie Unicode
    /// <c>SpaceSeparator</c>) a un seul espace, et retire les espaces en debut/fin.
    /// </summary>
    private static string ReduireSpaces(string value)
    {
        var result = new StringBuilder(value.Length);
        var precedentEstSpace = false;

        foreach (var caractere in value)
        {
            if (char.IsWhiteSpace(caractere))
            {
                if (!precedentEstSpace) result.Append(' ');
                precedentEstSpace = true;
            }
            else
            {
                result.Append(caractere);
                precedentEstSpace = false;
            }
        }

        return result.ToString().Trim();
    }
}
