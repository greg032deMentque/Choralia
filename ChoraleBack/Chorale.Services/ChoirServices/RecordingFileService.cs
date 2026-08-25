using ChoraleBackEnd.Services;

namespace ChoraleBackEnd.Services.ChoirServices;

/// <remarks>
/// Interface distincte sans membre propre : voir <see cref="IScoreFileService"/>.
/// </remarks>
public interface IRecordingFileService : IContentFileService
{
}

public sealed class RecordingFileService : ContentFileServiceBase, IRecordingFileService
{
    private static readonly Dictionary<string, string[]> RecordingFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = ["audio/mpeg"],
        [".m4a"] = ["audio/mp4", "audio/x-m4a"],
        [".wav"] = ["audio/wav", "audio/x-wav"]
    };

    public RecordingFileService(IPathService pathService)
        : base(pathService)
    {
    }

    protected override IReadOnlyDictionary<string, string[]> AllowedFormats => RecordingFormats;

    protected override string MessageFormatNotAllowed =>
        "Format de fichier non autorisé. Formats acceptés : MP3 (.mp3), M4A (.m4a), WAV (.wav).";

    protected override string MessageFileNotFound => "Fichier audio introuvable.";
}
