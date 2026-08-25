using ChoraleBackEnd.Common.Helpers;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Common;

[TestFixture]
public sealed class FileSignatureHelperTests
{
    /// <summary>
    /// Le contrat annonce par <see cref="FileSignatureHelper"/> : le flux est rendu a la
    /// position ou il a ete trouve.
    /// </summary>
    /// <remarks>
    /// Ce test existe parce que la violation serait silencieuse. Les deux appelants actuels
    /// (<c>ScoreService</c> et <c>RecordingService</c>) ne la verraient pas : ils passent un
    /// <c>IFormFile.OpenReadStream()</c> puis laissent <c>CopyToAsync</c> en rouvrir un autre,
    /// independant. Le jour ou un appelant reutilise le meme flux — un hoisting de variable
    /// suffit — il ecrirait un fichier ampute de ses premiers octets, sans erreur ni
    /// exception, et le defaut ne se verrait qu'a la relecture du fichier stocke.
    /// </remarks>
    [Test]
    public void MatchesExtension_ReturnsTheStreamToThePositionWhereItWasFound()
    {
        using var content = new MemoryStream([.. "%PDF-1.7\n"u8, .. new byte[64]]);

        var matches = FileSignatureHelper.MatchesExtension(".pdf", content);

        Assert.That(matches, Is.True);
        Assert.That(content.Position, Is.Zero);
    }

    [Test]
    public void MatchesExtension_NonSeekableStream_IsRejected()
    {
        // Cas indecidable : sans repositionnement, valider consommerait l'en-tete. Sur un
        // point d'entree de fichier utilisateur, l'incertitude se traite comme un rejet.
        using var content = new NonSeekableStream([.. "%PDF-1.7\n"u8, .. new byte[64]]);

        Assert.That(FileSignatureHelper.MatchesExtension(".pdf", content), Is.False);
    }

    private sealed class NonSeekableStream(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;
    }
}
