using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace ChoraleBackEnd.Services;

public interface IPathService
{
    /// <summary>
    /// Chemin absolu du fichier stocke. Refuse tout nom contenant un separateur de
    /// chemin ou une remontee de repertoire.
    /// </summary>
    string GetFilePath(string fileName);

    /// <summary>
    /// Retire d'un nom de fichier tout ce qui n'a pas sa place dans un en-tete
    /// Contenu-Disposition : caracteres de controle, guillemets, separateurs de chemin.
    /// </summary>
    string SanitizeFileName(string name);
}

public sealed class PathService : IPathService
{
    private readonly string _uploadsPath;

    public PathService(IConfiguration configuration, IWebHostEnvironment env)
    {
        // Racine de stockage HORS wwwroot. Les partitions et les enregistrements sont du
        // contenu de chorale : ils ne doivent jamais etre atteignables par URL statique.
        // Un `UseStaticFiles()` ajoute plus tard, ou un reverse proxy exposant /uploads,
        // rendrait sinon tout le contenu de toutes les chorales telechargeable sans
        // authentification, par simple Guid.
        var configuredRoot = configuration["Storage:Root"];

        _uploadsPath = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "storage", "uploads"))
            : Path.GetFullPath(configuredRoot);

        Directory.CreateDirectory(_uploadsPath);
    }

    public string GetFilePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("Nom de fichier vide.", nameof(fileName));

        // Le nom vient de FilePath en base, genere par le serveur — mais on ne s'en
        // remet pas a cette provenance : defense en profondeur contre la traversee.
        if (fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains(".."))
            throw new ArgumentException("Nom de fichier invalide.", nameof(fileName));

        var path = Path.GetFullPath(Path.Combine(_uploadsPath, fileName));

        if (!path.StartsWith(_uploadsPath, StringComparison.Ordinal))
            throw new ArgumentException("Nom de fichier invalide.", nameof(fileName));

        return path;
    }

    public string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "fichier";

        var nettoye = new string(name
            .Where(c => !char.IsControl(c) && c != '"' && c != '\\' && c != '/')
            .ToArray())
            .Trim();

        return string.IsNullOrWhiteSpace(nettoye) ? "fichier" : nettoye;
    }
}
