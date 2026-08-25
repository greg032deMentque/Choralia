using ChoraleBackEnd.Services;

namespace ChoraleBackEnd.Test.Fakes;

/// <summary>
/// Racine de stockage jetable, propre a chaque instance : deux fixtures qui tournent en
/// parallele ne peuvent pas se marcher dessus sur un meme nom de fichier.
/// </summary>
/// <remarks>
/// <c>SanitizeFileName</c> est volontairement l'identite. Les tests qui posent un fichier le
/// nomment eux-memes (souvent un GUID) et veulent le retrouver tel quel ; assainir ici
/// masquerait le nom attendu sans rien prouver du service teste.
/// </remarks>
public sealed class FakePathService : IPathService
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "choir-tests", Guid.NewGuid().ToString());

    public FakePathService() => Directory.CreateDirectory(_root);

    public string GetFilePath(string fileName) => Path.Combine(_root, fileName);

    public string SanitizeFileName(string name) => name;
}
