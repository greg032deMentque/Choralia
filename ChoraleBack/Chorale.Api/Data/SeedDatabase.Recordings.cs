using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;

namespace ChoraleBackEnd.Api.Data;

public static partial class SeedDatabase
{
    // Sous-dossier fixe sous la racine configurable : un seul type de contenu fixture existe
    // aujourd'hui (Recording), la racine elle-meme reste generique pour absorber un futur
    // sous-dossier Scores sans nouvelle cle de configuration.
    private const string RecordingFixturesSubfolder = "Recordings";

    /// <summary>
    /// Resout la racine des fixtures de seed. Meme motif que <see cref="PathService"/> pour
    /// `Storage:Root` (valeur vide -> chemin calcule depuis `ContentRootPath`, valeur fournie
    /// prise telle quelle), sans reutiliser son code : `IPathService` resout une destination de
    /// stockage pour du contenu utilisateur avec une garantie anti-traversee de chemin, alors
    /// que ce chemin-ci est une source de lecture seule, developpeur-controlee, propre au seed
    /// de demonstration — etendre `IPathService` y ferait fuiter une connaissance de
    /// `Chorale.Api` dans `Chorale.Services`, consomme par la production.
    /// </summary>
    /// <remarks>
    /// Le chemin par defaut (calcule depuis <c>ContentRootPath</c>) fonctionne aussi bien en
    /// `dotnet run` qu'apres un `dotnet publish` : les fichiers sous <c>Data/SeedFixtures/</c>
    /// suivent desormais le publish via la regle <c>Content</c> (<c>CopyToOutputDirectory</c> /
    /// <c>CopyToPublishDirectory</c>) de <c>ChoraleBackEnd.Api.csproj</c> — sans elle, seul
    /// `dotnet run` les trouvait, car le SDK Web n'embarque automatiquement que <c>wwwroot/</c>
    /// dans une publication. `Seed:Demo:FixturesRoot` reste donc un override optionnel (volume
    /// monte, dossier externe au deploiement), pas une necessite pour le cas courant. Aucune
    /// de ces deux voies n'a d'effet hors `Development` et `Staging` (ce dernier seulement si
    /// `Seed:Demo:EnabledInStaging = true`, en plus du mot de passe) : ce seed entier est garde
    /// par `environment.IsDevelopment() || (environment.IsStaging() &amp;&amp; EnabledInStaging)`
    /// dans `SeedDatabase.cs` — jamais en `Production`.
    /// </remarks>
    private static string ResolveFixturesRoot(IWebHostEnvironment environment, string? configuredRoot)
    {
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(environment.ContentRootPath, "Data", "SeedFixtures")
            : Path.GetFullPath(configuredRoot);

        return Path.Combine(root, RecordingFixturesSubfolder);
    }

    /// <summary>
    /// Valide un enregistrement de demonstration et resout ses enums. Reproduit la regle de
    /// <see cref="Services.ChoirServices.RecordingService"/> (`TargetVoicePart` obligatoire si
    /// et seulement si `Type = ByVoicePart`) : le seed ecrit l'entite directement, sans passer
    /// par le service, donc sans en heriter la validation.
    /// </summary>
    private static bool TryResolveRecording(
        string path, DemoRecordingOptions options, string fixturesRoot, HashSet<string> usedAccountKeys,
        ILogService logger, out ResolvedRecording resolved)
    {
        resolved = null!;

        if (!Enum.TryParse<RecordingTypeEnum>(options.Type, ignoreCase: true, out var type))
            return SkipDemo(logger, $"{path}:Type' is not a RecordingTypeEnum value ('{options.Type}')");

        VoicePartEnum? targetVoicePart = null;
        if (type == RecordingTypeEnum.ByVoicePart)
        {
            if (!Enum.TryParse<VoicePartEnum>(options.TargetVoicePart, ignoreCase: true, out var voicePart))
                return SkipDemo(logger, $"{path}:TargetVoicePart' is required and must be a VoicePartEnum value for a ByVoicePart recording ('{options.TargetVoicePart}')");
            targetVoicePart = voicePart;
        }
        else if (!string.IsNullOrWhiteSpace(options.TargetVoicePart))
        {
            return SkipDemo(logger, $"{path}:TargetVoicePart' must be empty for a General recording");
        }

        if (!Enum.TryParse<RecordingStatusEnum>(options.Status, ignoreCase: true, out var status))
            return SkipDemo(logger, $"{path}:Status' is not a RecordingStatusEnum value ('{options.Status}')");

        if (!Enum.TryParse<RecordingSourceEnum>(options.Source, ignoreCase: true, out var source))
            return SkipDemo(logger, $"{path}:Source' is not a RecordingSourceEnum value ('{options.Source}')");

        if (string.IsNullOrWhiteSpace(options.CreatorAccountKey))
            return SkipDemo(logger, $"{path}:CreatorAccountKey' missing");
        usedAccountKeys.Add(options.CreatorAccountKey);

        if (string.IsNullOrWhiteSpace(options.ContentOwner))
            return SkipDemo(logger, $"{path}:ContentOwner' missing");

        if (options.DurationSeconds <= 0)
            return SkipDemo(logger, $"{path}:DurationSeconds' must be positive");

        // Le nom de fichier vient d'appsettings.json, pas d'une entree utilisateur, mais reste
        // controle par defense en profondeur : aucun separateur de chemin admis.
        if (string.IsNullOrWhiteSpace(options.FixtureFileName)
            || options.FixtureFileName.Contains('/') || options.FixtureFileName.Contains('\\'))
            return SkipDemo(logger, $"{path}:FixtureFileName' missing or invalid");

        if (!File.Exists(Path.Combine(fixturesRoot, options.FixtureFileName)))
            return SkipDemo(logger, $"{path}:FixtureFileName' not found on disk ('{options.FixtureFileName}')");

        resolved = new ResolvedRecording(
            type, targetVoicePart, status, source, options.CreatorAccountKey!, options.ContentOwner!,
            options.DownloadAllowed, options.DurationSeconds, options.FixtureFileName!);
        return true;
    }

    /// <summary>
    /// Copie le fichier fixture vers le stockage reel — meme convention de nommage que
    /// <see cref="Services.ChoirServices.ContentFileServiceBase.SaveAsync"/>, destination
    /// resolue via <see cref="IPathService.GetFilePath"/>, aucune logique de racine de
    /// stockage dupliquee — puis ecrit l'entite <see cref="Recording"/>.
    /// </summary>
    private static async Task AddDemoRecordingAsync(
        ChoraleDbContext context, IPathService pathService, string fixturesRoot, Guid songId, Guid choirId,
        IReadOnlyDictionary<string, User> users, ResolvedRecording recording)
    {
        var extension = Path.GetExtension(recording.FixtureFileName);
        var storedFileName = $"{ChoraleDbContext.NewIdGuid()}{extension}";
        var sourcePath = Path.Combine(fixturesRoot, recording.FixtureFileName);
        var destinationPath = pathService.GetFilePath(storedFileName);

        await using (var source = File.OpenRead(sourcePath))
        await using (var destination = File.Create(destinationPath))
        {
            await source.CopyToAsync(destination);
        }

        context.Recordings.Add(new Recording
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SongId = songId,
            Type = recording.Type,
            TargetVoicePart = recording.TargetVoicePart,
            ChoirOwnerId = choirId,
            CreatorUserId = users[recording.CreatorAccountKey].Id,
            Status = recording.Status,
            Source = recording.Source,
            DurationSeconds = recording.DurationSeconds,
            PublicationDate = recording.Status == RecordingStatusEnum.Published ? DateTime.UtcNow : null,
            ContentOwner = recording.ContentOwner,
            DownloadAllowed = recording.DownloadAllowed,
            FilePath = storedFileName,
            OriginalFileName = recording.FixtureFileName,
            SizeBytes = new FileInfo(sourcePath).Length,
            IsDeleted = false
        });
    }

    private sealed record ResolvedRecording(
        RecordingTypeEnum Type,
        VoicePartEnum? TargetVoicePart,
        RecordingStatusEnum Status,
        RecordingSourceEnum Source,
        string CreatorAccountKey,
        string ContentOwner,
        bool DownloadAllowed,
        int DurationSeconds,
        string FixtureFileName);
}
