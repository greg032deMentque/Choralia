using System.Text;
using ChoraleBackEnd.Common.Helpers;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Common;

/// <summary>
/// Aller-retour base64url sur les trois longueurs modulo 3.
/// </summary>
/// <remarks>
/// C'est le cœur du défaut corrigé : le remplissage <c>=</c> était retiré à l'émission et
/// jamais restauré à la lecture. <c>Convert.FromBase64String</c> levait donc une
/// <see cref="FormatException"/> dès que la longueur du jeton n'était pas un multiple de
/// 3 octets — deux cas sur trois, sans aucun signal : le lien d'invitation échouait en 400
/// indistinguable d'une faute de frappe.
/// </remarks>
[TestFixture]
public sealed class UrlTokenHelperTests
{
    // 3 octets -> aucun remplissage, 4 -> deux '=', 5 -> un '='. Les trois restes du
    // modulo 3 sont couverts, donc les trois longueurs de remplissage possibles.
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    public void EncodeDecode_AnyLength_RestoresTheValue(int byteLength)
    {
        var value = new string('a', byteLength);

        var encoded = UrlTokenHelper.Encode(value);

        Assert.Multiple(() =>
        {
            Assert.That(encoded, Does.Not.Contain("="), "Le remplissage doit être retiré à l'émission.");
            Assert.That(UrlTokenHelper.Decode(encoded), Is.EqualTo(value));
        });
    }

    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    public void EncodeBytesDecodeBytes_BinaryToken_RestoresTheBytes(int byteLength)
    {
        // Un jeton DataProtector est binaire, pas textuel : le passer par Encode(string) le
        // corromprait par un aller-retour UTF-8 parasite.
        var bytes = Enumerable.Range(200, byteLength).Select(b => (byte)b).ToArray();

        var restored = UrlTokenHelper.DecodeBytes(UrlTokenHelper.EncodeBytes(bytes));

        Assert.That(restored, Is.EqualTo(bytes));
    }

    [Test]
    public void Encode_ProducesTokenWithoutQueryStringReservedCharacter()
    {
        // '+' et '/' du base64 standard sont réinterprétés dans une URL ('+' devient une
        // espace) : c'est ce qui impose la variante base64url.
        var encoded = UrlTokenHelper.Encode(Encoding.UTF8.GetString([251, 255, 254, 253]));

        Assert.That(encoded, Does.Not.Contain("+").And.Not.Contain("/"));
    }

    [Test]
    public void TryDecode_UnreadableToken_RejectsWithoutThrowing()
    {
        var ok = UrlTokenHelper.TryDecode("ceci n'est pas du base64url", out var decoded);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(decoded, Is.Empty);
        });
    }

    [Test]
    public void TryDecode_EmptyToken_RejectsWithoutThrowing()
        => Assert.That(UrlTokenHelper.TryDecode(null, out _), Is.False);
}
