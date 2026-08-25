using System;
using System.IO;
using ChoraleBackEnd.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using NUnit.Framework;

namespace ChoraleBackEnd.Test.Services;

/// <summary>
/// `PathService` decide ou vivent les partitions et les enregistrements, et quel nom de
/// fichier part dans un en-tete HTTP. Deux garanties y sont verifiees.
///
/// La racine de stockage doit rester HORS wwwroot : elle y etait, et un `UseStaticFiles()`
/// ajoute plus tard aurait rendu tout le contenu de toutes les chorales telechargeable sans
/// authentification, par simple Guid. Rien dans le code ne signalerait la regression.
///
/// Et `OriginalFileName` est une donnee utilisateur : renvoyee brute dans
/// Contenu-Disposition, elle permet une injection d'en-tete.
/// </summary>
[TestFixture]
public sealed class PathServiceTests
{
    private string _contentRoot = string.Empty;
    private PathService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), "choir-pathservice", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_contentRoot);

        _service = new PathService(
            new ConfigurationBuilder().Build(),
            new FakeEnvironment(_contentRoot));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_contentRoot))
            Directory.Delete(_contentRoot, recursive: true);
    }

    [Test]
    public void StorageRoot_IsOutsideWwwroot()
    {
        var path = _service.GetFilePath("fichier.pdf");
        var wwwroot = Path.Combine(_contentRoot, "wwwroot");

        Assert.That(path.StartsWith(wwwroot, StringComparison.OrdinalIgnoreCase), Is.False,
            "Le contenu de chorale ne doit jamais etre atteignable par URL statique.");
    }

    [TestCase("../secrets.txt")]
    [TestCase("..\\secrets.txt")]
    [TestCase("sous/dossier.pdf")]
    [TestCase("sous\\dossier.pdf")]
    [TestCase("")]
    [TestCase("   ")]
    public void GetFilePath_RejectsANameThatEscapesTheDirectory(string name)
        => Assert.Throws<ArgumentException>(() => _service.GetFilePath(name));

    [Test]
    public void GetFilePath_AcceptsASimpleName()
    {
        var path = _service.GetFilePath("a1b2c3.pdf");

        Assert.That(Path.GetFileName(path), Is.EqualTo("a1b2c3.pdf"));
        Assert.That(Path.IsPathFullyQualified(path), Is.True);
    }

    [TestCase("normal.pdf", "normal.pdf")]
    [TestCase("avec \"guillemets\".pdf", "avec guillemets.pdf")]
    [TestCase("path/traverse.pdf", "pathtraverse.pdf")]
    [TestCase("path\\traverse.pdf", "pathtraverse.pdf")]
    public void SanitizeFileName_RemovesWhatWouldBreakTheHeader(string entry, string expected)
        => Assert.That(_service.SanitizeFileName(entry), Is.EqualTo(expected));

    [Test]
    public void SanitizeFileName_RemovesLineBreaks()
    {
        // Une injection d'en-tete HTTP passe par un CRLF suivi d'un en-tete forge.
        var result = _service.SanitizeFileName("innocent.pdf\r\nX-Injecte: oui");

        Assert.That(result, Does.Not.Contain("\r"));
        Assert.That(result, Does.Not.Contain("\n"));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\"\"")]
    public void SanitizeFileName_NeverReturnsAnEmptyString(string entry)
        => Assert.That(_service.SanitizeFileName(entry), Is.EqualTo("fichier"));

    private sealed class FakeEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.Combine(contentRoot, "wwwroot");
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Choir.Test";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRoot;
        public string EnvironmentName { get; set; } = "Test";
    }
}
