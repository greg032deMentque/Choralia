using System.Text;

namespace ChoraleBackEnd.Common.Helpers;

/// <summary>
/// Encodage base64url des jetons transportes dans une URL de lien email (invitation,
/// activation de compte, reinitialisation de mot de passe, verification d'email).
/// </summary>
/// <remarks>
/// Point unique des DEUX sens. La regle etait auparavant recopiee sur six sites d'encodage
/// pour un seul decodage correct : <c>AccountService.ResetPassword</c> appelait
/// <c>Convert.FromBase64String</c> sur une chaine dont le remplissage avait ete retire a
/// l'emission, ce qui levait une <see cref="FormatException"/> des que la longueur du jeton
/// n'etait pas un multiple de 3 octets — soit deux cas sur trois. Le lien d'invitation
/// echouait alors en 400 indistinguable d'une faute de frappe.
///
/// Le remplissage (<c>=</c>) est retire a l'emission parce qu'il s'echappe en <c>%3D</c> dans
/// une URL ; il doit donc etre restaure a la lecture, ce que <see cref="Decode"/> fait.
/// </remarks>
public static class UrlTokenHelper
{
    /// <summary>
    /// Encode des octets bruts en base64url sans remplissage — cas d'un jeton deja
    /// chiffre (<c>IDataProtector.Protect</c>), qui n'est PAS du texte : le passer par
    /// <see cref="Encode(string)"/> le corromprait par un aller-retour UTF-8 parasite.
    /// </summary>
    public static string EncodeBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>
    /// Decode en octets bruts un jeton produit par <see cref="EncodeBytes"/>.
    /// </summary>
    /// <exception cref="FormatException">Jeton illisible.</exception>
    public static byte[] DecodeBytes(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        var padded = token.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            // 1 est une longueur impossible en base64 : laisser Convert lever plutot que
            // completer silencieusement une chaine tronquee.
            _ => string.Empty
        };

        return Convert.FromBase64String(padded);
    }

    /// <summary>
    /// Encode un jeton textuel en base64url sans remplissage. Le resultat ne contient que
    /// des caracteres surs en query string.
    /// </summary>
    public static string Encode(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        return EncodeBytes(Encoding.UTF8.GetBytes(token));
    }

    /// <summary>
    /// Decode un jeton produit par <see cref="Encode"/>, remplissage restaure.
    /// </summary>
    /// <exception cref="FormatException">
    /// Jeton illisible (caractere hors base64url, longueur impossible). L'appelant decide :
    /// <see cref="TryDecode"/> pour un refus silencieux, cette surcharge pour laisser
    /// remonter — <c>ExceptionMiddleware</c> mappe <see cref="FormatException"/> en 400.
    /// </exception>
    public static string Decode(string token)
        => Encoding.UTF8.GetString(DecodeBytes(token));

    /// <summary>
    /// Variante non levante, pour les chemins ou un jeton illisible et un jeton invalide
    /// doivent produire la meme reponse (anti-enumeration).
    /// </summary>
    public static bool TryDecode(string? token, out string decoded)
    {
        decoded = string.Empty;
        if (string.IsNullOrWhiteSpace(token)) return false;

        try
        {
            decoded = Decode(token);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
