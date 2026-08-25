using System.Globalization;
using ChoraleBackEnd.Common.Helpers;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Common;

/// <summary>
/// <see cref="SongKeyHelper"/> calcule la cle de regroupement d'affichage du catalogue
/// chants (lot 4). Point de vigilance numero un : la cle doit etre strictement identique
/// entre le poste de dev (Windows/NLS) et la production (Ubuntu/ICU) — voir
/// <see cref="ComputeKey_TurkishCasing_ResultIndependentOfCurrentCulture"/>.
/// </summary>
[TestFixture]
public sealed class SongKeyHelperTests
{
    [Test]
    public void ComputeKey_SameTitleDifferentCase_ProducesSameKey()
    {
        var key1 = SongKeyHelper.ComputeKey(Guid.NewGuid(), "Ave Verum", "Mozart");
        var key2 = SongKeyHelper.ComputeKey(Guid.NewGuid(), "ave verum", "Mozart");

        Assert.That(key1, Is.EqualTo(key2));
    }

    [Test]
    public void ComputeKey_Accents_NoelEqualsNoel()
    {
        var key1 = SongKeyHelper.ComputeKey(Guid.NewGuid(), "Noël", "Traditionnel");
        var key2 = SongKeyHelper.ComputeKey(Guid.NewGuid(), "noel", "Traditionnel");

        Assert.That(key1, Is.EqualTo(key2));
    }

    [Test]
    public void ComputeKey_MultipleAndNonBreakingSpaces_ReducedToSingleSpace()
    {
        var key1 = SongKeyHelper.ComputeKey(Guid.NewGuid(), "Ave   Verum", "Mozart");
        var key2 = SongKeyHelper.ComputeKey(Guid.NewGuid(), "Ave Verum", "Mozart");
        var key3 = SongKeyHelper.ComputeKey(Guid.NewGuid(), "Ave Verum", "Mozart");

        Assert.That(key1, Is.EqualTo(key3));
        Assert.That(key2, Is.EqualTo(key3));
    }

    [Test]
    public void ComputeKey_Punctuation_AveVerumEqualsAveVerumWithoutPunctuation()
    {
        var key1 = SongKeyHelper.ComputeKey(Guid.NewGuid(), "Ave, Verum !", "Mozart");
        var key2 = SongKeyHelper.ComputeKey(Guid.NewGuid(), "Ave Verum", "Mozart");

        Assert.That(key1, Is.EqualTo(key2));
    }

    [Test]
    public void ComputeKey_ComposerEmptyNullOrBlank_NeverMerges()
    {
        var songId1 = Guid.NewGuid();
        var songId2 = Guid.NewGuid();
        var songId3 = Guid.NewGuid();

        var emptyKey = SongKeyHelper.ComputeKey(songId1, "Ave Maria", "");
        var nullKey = SongKeyHelper.ComputeKey(songId2, "Ave Maria", null);
        var blankKey = SongKeyHelper.ComputeKey(songId3, "Ave Maria", "   ");

        Assert.That(emptyKey, Is.Not.EqualTo(nullKey));
        Assert.That(nullKey, Is.Not.EqualTo(blankKey));
        Assert.That(emptyKey, Is.Not.EqualTo(blankKey));
    }

    [Test]
    public void ComputeKey_TurkishCasing_ResultIndependentOfCurrentCulture()
    {
        // Sous la culture turque, "I".ToLower() produit "ı" (i sans point) et non "i" —
        // une divergence classique entre plateformes/cultures qui existe justement parce que
        // ToLower() (sans suffixe) suit la culture du thread courant. Ce test ne verifie pas
        // un mapping Unicode precis (CalculerCle ne s'engage sur aucun), mais que le
        // changement de CultureInfo.CurrentCulture ne fait PAS bouger le result — c'est la
        // garantie qui protege contre une divergence Windows (dev, NLS) / Ubuntu (prod, ICU).
        // Si CalculerCle utilisait ToLower() sans le suffixe Invariant, ce test échouerait.
        const string title = "Istanbul'da Bir Gece";
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var invariantKey = SongKeyHelper.ComputeKey(Guid.Empty, title, "Composer");

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var keyUnderTurkishCulture = SongKeyHelper.ComputeKey(Guid.Empty, title, "Composer");

            Assert.That(keyUnderTurkishCulture, Is.EqualTo(invariantKey));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Test]
    public void ComputeKey_Title200Characters_NoSilentTruncation()
    {
        var titleLong = new string('a', 200);

        var key = SongKeyHelper.ComputeKey(Guid.NewGuid(), titleLong, "Composer");

        Assert.That(key, Does.StartWith(titleLong));
    }

    [Test]
    public void ComputeKey_TwoDifferentComposers_ProduceTwoDistinctKeys()
    {
        var key1 = SongKeyHelper.ComputeKey(Guid.NewGuid(), "Ave Maria", "Schubert");
        var key2 = SongKeyHelper.ComputeKey(Guid.NewGuid(), "Ave Maria", "Gounod");

        Assert.That(key1, Is.Not.EqualTo(key2));
    }
}
