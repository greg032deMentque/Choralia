using ChoraleBackEnd.Common.Helpers;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Common;

[TestFixture]
public sealed class SortHelperTests
{
    private sealed class Person
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    private static readonly IReadOnlyDictionary<string, System.Linq.Expressions.Expression<Func<Person, object?>>> ColumnsAllowed =
        new Dictionary<string, System.Linq.Expressions.Expression<Func<Person, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = p => p.Name,
            ["Age"] = p => p.Age
        };

    private static List<Person> Dataset() =>
    [
        new Person { Id = 1, Name = "Charlie", Age = 40 },
        new Person { Id = 2, Name = "Alice", Age = 30 },
        new Person { Id = 3, Name = "Bob", Age = 20 }
    ];

    [Test]
    public void SortActiveAllowed_Ascending_SortsOnRequestedColumn()
    {
        var result = Dataset().AsQueryable()
            .ApplySort("Name", "asc", ColumnsAllowed, p => p.Id, q => q.OrderBy(p => p.Id))
            .ToList();

        Assert.That(result.Select(p => p.Name), Is.EqualTo(new[] { "Alice", "Bob", "Charlie" }));
    }

    [Test]
    public void SortActiveAllowed_Descending_SortsInReverseOrder()
    {
        var result = Dataset().AsQueryable()
            .ApplySort("Name", "desc", ColumnsAllowed, p => p.Id, q => q.OrderBy(p => p.Id))
            .ToList();

        Assert.That(result.Select(p => p.Name), Is.EqualTo(new[] { "Charlie", "Bob", "Alice" }));
    }

    [Test]
    public void SortActiveUnknown_FallsBackToDefaultSort_WithoutException()
    {
        List<Person> result = null!;

        Assert.DoesNotThrow(() =>
        {
            result = Dataset().AsQueryable()
                .ApplySort("ColonneQuiNexistePas", "asc", ColumnsAllowed, p => p.Id, q => q.OrderBy(p => p.Id))
                .ToList();
        });

        Assert.That(result.Select(p => p.Id), Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void SortActiveNullOrEmpty_FallsBackToDefaultSort()
    {
        var nullResult = Dataset().AsQueryable()
            .ApplySort(null, "asc", ColumnsAllowed, p => p.Id, q => q.OrderBy(p => p.Id))
            .ToList();

        var emptyResult = Dataset().AsQueryable()
            .ApplySort("   ", "asc", ColumnsAllowed, p => p.Id, q => q.OrderBy(p => p.Id))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(nullResult.Select(p => p.Id), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(emptyResult.Select(p => p.Id), Is.EqualTo(new[] { 1, 2, 3 }));
        });
    }

    [TestCase("DESC")]
    [TestCase("Desc")]
    [TestCase("desc")]
    public void SortDirection_MixedCase_IsInterpretedAsDescending(string sortDirection)
    {
        var result = Dataset().AsQueryable()
            .ApplySort("Age", sortDirection, ColumnsAllowed, p => p.Id, q => q.OrderBy(p => p.Id))
            .ToList();

        Assert.That(result.Select(p => p.Age), Is.EqualTo(new[] { 40, 30, 20 }));
    }

    [Test]
    public void SortDirection_InvalidValue_IsTreatedAsAscending()
    {
        var result = Dataset().AsQueryable()
            .ApplySort("Age", "xyz", ColumnsAllowed, p => p.Id, q => q.OrderBy(p => p.Id))
            .ToList();

        Assert.That(result.Select(p => p.Age), Is.EqualTo(new[] { 20, 30, 40 }));
    }

    [TestCase("p => p.Nom")]
    [TestCase("Nom; DROP TABLE Personnes;")]
    [TestCase("NomInexistant")]
    [TestCase("Id")]
    public void InjectionAttempt_SortActiveOutsideWhitelist_FallsBackToDefaultSort_WithoutException(string maliciousSortActive)
    {
        List<Person> result = null!;

        Assert.DoesNotThrow(() =>
        {
            result = Dataset().AsQueryable()
                .ApplySort(maliciousSortActive, "asc", ColumnsAllowed, p => p.Id, q => q.OrderBy(p => p.Id))
                .ToList();
        });

        // "Id" n'est volontairement pas dans ColumnsAllowed : seule une colonne
        // explicitement liste doit pouvoir servir de tri, jamais une propriete existante
        // devinee par le client.
        Assert.That(result.Select(p => p.Id), Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void Determinism_IdenticalSortValues_TwoConsecutivePagesDoNotOverlapOrLoseAnyRow()
    {
        var dataSet = Enumerable.Range(1, 6)
            .Select(i => new Person { Id = i, Name = "Meme", Age = 25 })
            .ToList();

        var sorted = dataSet.AsQueryable()
            .ApplySort("Name", "asc", ColumnsAllowed, p => p.Id, q => q.OrderBy(p => p.Id));

        var page1 = sorted.Skip(0).Take(3).Select(p => p.Id).ToList();
        var page2 = sorted.Skip(3).Take(3).Select(p => p.Id).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(page1, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(page2, Is.EqualTo(new[] { 4, 5, 6 }));
            Assert.That(page1.Intersect(page2), Is.Empty);
            Assert.That(page1.Concat(page2).Distinct().Count(), Is.EqualTo(6));
        });
    }
}
