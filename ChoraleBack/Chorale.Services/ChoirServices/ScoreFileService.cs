using ChoraleBackEnd.Services;

namespace ChoraleBackEnd.Services.ChoirServices;

/// <remarks>
/// Interface distincte sans membre propre : elle ne sert qu'a distinguer les deux
/// enregistrements DI de <see cref="IContentFileService"/> (partitions et enregistrements).
/// L'alternative serait des services par cle, qui changeraient tous les points d'injection.
/// </remarks>
public interface IScoreFileService : IContentFileService
{
}

public sealed class ScoreFileService : ContentFileServiceBase, IScoreFileService
{
    private static readonly Dictionary<string, string[]> ScoreFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ["application/pdf"],
        [".png"] = ["image/png"],
        [".jpg"] = ["image/jpeg"],
        [".jpeg"] = ["image/jpeg"]
    };

    public ScoreFileService(IPathService pathService)
        : base(pathService)
    {
    }

    protected override IReadOnlyDictionary<string, string[]> AllowedFormats => ScoreFormats;

    protected override string MessageFormatNotAllowed =>
        "Format de fichier non autorisé. Formats acceptés : PDF (.pdf), PNG (.png), JPEG (.jpg, .jpeg).";

    protected override string MessageFileNotFound => "Fichier de partition introuvable.";
}
