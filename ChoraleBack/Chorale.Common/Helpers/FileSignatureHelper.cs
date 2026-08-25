namespace ChoraleBackEnd.Common.Helpers;

/// <summary>
/// Verifie qu'un fichier televerse est bien du type que son extension annonce, en lisant ses
/// premiers octets (« magic bytes »). Point de passage unique de <c>ScoreService</c> et
/// <c>RecordingService</c> : une seule table de signatures pour les deux surfaces, sinon
/// elles divergent des le premier format ajoute d'un cote seulement.
/// </summary>
/// <remarks>
/// L'extension du nom de fichier et le <c>Content-Type</c> d'un upload viennent tous deux du
/// client : un executable renomme en <c>.pdf</c> et envoye avec
/// <c>Content-Type: application/pdf</c> passe les deux controles sans en etre un. Les octets
/// sont la seule donnee que le client ne choisit pas librement s'il veut que son fichier reste
/// exploitable. C'est ce qui compte ici : ces fichiers sont ensuite reservis <b>inline</b> par
/// <c>ScoreController.Stream</c> et affiches dans une iframe du front — un HTML deguise en PDF
/// y deviendrait un XSS stocke (OWASP A03).
///
/// Tous les cas indecidables retournent <c>false</c>, donc un refus : extension inconnue, flux
/// non repositionnable, en-tete trop court. Sur un point d'entree de fichier utilisateur,
/// l'incertitude se traite comme un rejet, jamais comme une acceptation.
///
/// La position du flux est restauree telle qu'elle etait avant lecture — c'est une garantie
/// du contrat, verifiee par <c>FileSignatureHelperTests</c>. Les deux appelants actuels n'en
/// dependent pas (ils passent un <c>IFormFile.OpenReadStream()</c> et laissent
/// <c>CopyToAsync</c> en rouvrir un autre, independant), mais un appelant qui reutiliserait le
/// meme flux pour ecrire ecrirait un fichier ampute de son en-tete, sans erreur ni exception.
/// </remarks>
public static class FileSignatureHelper
{
    // 12 octets couvrent la verification la plus longue : RIFF (0-3) puis WAVE (8-11).
    private const int HeaderLength = 12;

    // Une trame MPEG brute (MP3 sans tag ID3) commence par 11 bits de synchronisation a 1 :
    // 0xFF, puis un octet dont les 3 bits de poids fort sont a 1.
    private const byte MpegSyncFirstByte = 0xFF;
    private const byte MpegSyncSecondByteMask = 0xE0;

    private static readonly byte[] PdfSignature = "%PDF"u8.ToArray();
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Id3Signature = "ID3"u8.ToArray();
    private static readonly byte[] FtypSignature = "ftyp"u8.ToArray();
    private static readonly byte[] RiffSignature = "RIFF"u8.ToArray();
    private static readonly byte[] WaveSignature = "WAVE"u8.ToArray();

    private static readonly Dictionary<string, Func<byte[], bool>> ValidatorsByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = header => MatchesAt(header, 0, PdfSignature),
            [".png"] = header => MatchesAt(header, 0, PngSignature),
            [".jpg"] = header => MatchesAt(header, 0, JpegSignature),
            [".jpeg"] = header => MatchesAt(header, 0, JpegSignature),
            [".mp3"] = IsMp3,
            [".m4a"] = IsM4a,
            [".wav"] = IsWav
        };

    /// <summary>
    /// Indique si les premiers octets de <paramref name="content"/> correspondent a
    /// <paramref name="extension"/> (point inclus, casse indifferente). Rend le flux a la
    /// position ou il l'a trouve.
    /// </summary>
    public static bool MatchesExtension(string? extension, Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (string.IsNullOrWhiteSpace(extension)) return false;
        if (!ValidatorsByExtension.TryGetValue(extension, out var validator)) return false;
        if (!content.CanSeek) return false;

        var initialPosition = content.Position;
        try
        {
            var buffer = new byte[HeaderLength];
            var readCount = content.ReadAtLeast(buffer, HeaderLength, throwOnEndOfStream: false);
            return validator(readCount == HeaderLength ? buffer : buffer[..readCount]);
        }
        finally
        {
            content.Position = initialPosition;
        }
    }

    private static bool IsMp3(byte[] header)
        => MatchesAt(header, 0, Id3Signature)
           || header.Length >= 2
              && header[0] == MpegSyncFirstByte
              && (header[1] & MpegSyncSecondByteMask) == MpegSyncSecondByteMask;

    // Conteneur ISO-BMFF : la taille de la boite occupe les 4 premiers octets, le type `ftyp`
    // ne commence qu'au cinquieme.
    private static bool IsM4a(byte[] header)
        => MatchesAt(header, 4, FtypSignature);

    private static bool IsWav(byte[] header)
        => MatchesAt(header, 0, RiffSignature) && MatchesAt(header, 8, WaveSignature);

    private static bool MatchesAt(byte[] header, int offset, byte[] signature)
        => header.Length >= offset + signature.Length
           && header.AsSpan(offset, signature.Length).SequenceEqual(signature);
}
