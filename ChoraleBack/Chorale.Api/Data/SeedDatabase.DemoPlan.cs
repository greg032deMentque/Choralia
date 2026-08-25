using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Services;

namespace ChoraleBackEnd.Api.Data;

public static partial class SeedDatabase
{
    /// <summary>
    /// Resout la section `Seed:Demo` en un jeu de donnees complet et valide. Retourne
    /// `false` — avec un avertissement nommant la cle fautive — des qu'un element manque,
    /// afin qu'aucune ecriture ne commence sur une configuration partielle.
    /// </summary>
    private static bool TryBuildDemoPlan(
        DemoSeedOptions? options, string fixturesRoot, ILogService logger, out DemoSeedPlan plan)
    {
        plan = null!;

        if (options is null)
            return SkipDemo(logger, "configuration section 'Seed:Demo' missing");

        var password = options.Password?.Trim();
        if (string.IsNullOrWhiteSpace(password))
            return SkipDemo(logger, "'Seed:Demo:Password' missing");

        // Comptes a cles fixes : ceux dont le role ne depend d'aucune chorale.
        var usedAccountKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DemoSeedOptions.ChoirEventOrganizerKey,
            DemoSeedOptions.ChoirEventParticipantKey,
            DemoSeedOptions.StandaloneOrganizerKey,
            DemoSeedOptions.StandaloneParticipantKey,
            DemoSeedOptions.ChoirClientManagerKey,
            DemoSeedOptions.EventClientManagerKey,
            DemoSeedOptions.DeletedAccountProbeKey
        };

        var choirClient = options.ChoirClient;
        if (choirClient is null || string.IsNullOrWhiteSpace(choirClient.Name))
            return SkipDemo(logger, "'Seed:Demo:ChoirClient:Name' missing");
        if (choirClient.Choirs.Count == 0)
            return SkipDemo(logger, "'Seed:Demo:ChoirClient:Choirs' is empty (at least one choir required)");
        if (!IsEventUsable(choirClient.Event))
            return SkipDemo(logger, "'Seed:Demo:ChoirClient:Event' incomplete (Title and Location required)");

        var eventClient = options.EventClient;
        if (eventClient is null || string.IsNullOrWhiteSpace(eventClient.Name))
            return SkipDemo(logger, "'Seed:Demo:EventClient:Name' missing");
        if (!IsEventUsable(eventClient.Event))
            return SkipDemo(logger, "'Seed:Demo:EventClient:Event' incomplete (Title and Location required)");

        // Tri par cle : l'ordre d'un Dictionary issu de la configuration n'est pas garanti, et
        // le « fondateur » du client (premier responsable de la premiere chorale) doit etre
        // deterministe d'un demarrage a l'autre.
        var choirs = new List<ResolvedChoir>();
        foreach (var (choirKey, choirOptions) in choirClient.Choirs.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryResolveChoir(choirKey, choirOptions, fixturesRoot, usedAccountKeys, logger, out var resolved))
                return false;
            choirs.Add(resolved);
        }

        if (!TryResolveStatusProbeClients(options, logger, out var statusProbeClients))
            return false;

        // Toutes les cles referencees doivent exister avec un email : verifie APRES la
        // resolution des chorales, en un seul point, pour nommer la cle fautive.
        var accounts = new Dictionary<string, DemoAccountOptions>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in usedAccountKeys)
        {
            if (!TryGetAccount(options, key, logger, out var account))
                return false;
            accounts[key] = account;
        }

        plan = new DemoSeedPlan(
            password, accounts, choirClient, eventClient, choirs, statusProbeClients,
            choirs[0].ManagerAccountKeys[0]);
        return true;
    }

    /// <summary>
    /// Clients "sondes" (lot audit admin) : valides avec la meme discipline que le reste de ce
    /// fichier — une entree incomplete produit un avertissement nomme et un no-op complet,
    /// jamais une ecriture partielle.
    /// </summary>
    private static bool TryResolveStatusProbeClients(
        DemoSeedOptions options, ILogService logger, out List<ResolvedStatusProbeClient> resolved)
    {
        resolved = [];

        foreach (var (key, probe) in options.StatusProbeClients.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            var path = $"'Seed:Demo:StatusProbeClients:{key}";

            if (string.IsNullOrWhiteSpace(probe.Name))
                return SkipDemo(logger, $"{path}:Name' missing");
            if (!Enum.TryParse<ClientStatusEnum>(probe.Status, ignoreCase: true, out var status))
                return SkipDemo(logger, $"{path}:Status' is not a ClientStatusEnum value ('{probe.Status}')");

            resolved.Add(new ResolvedStatusProbeClient(probe.Name!, status));
        }

        return true;
    }

    /// <summary>
    /// Valide une chorale de la configuration et resout ses enums. Enregistre au passage
    /// toutes les cles de compte qu'elle reference dans <paramref name="usedAccountKeys"/>.
    /// </summary>
    private static bool TryResolveChoir(
        string choirKey, DemoChoirOptions options, string fixturesRoot, HashSet<string> usedAccountKeys,
        ILogService logger, out ResolvedChoir resolved)
    {
        resolved = null!;
        var path = $"'Seed:Demo:ChoirClient:Choirs:{choirKey}";

        if (string.IsNullOrWhiteSpace(options.Name))
            return SkipDemo(logger, $"{path}:Name' missing");

        // Vide/absent = Published : comportement historique avant l'ajout de ce champ,
        // verrouille par SeedDatabaseTests.InitializeAsync_DemoSeed_ChoirsCreatedAsPublished.
        var status = ChoirStatusEnum.Published;
        if (!string.IsNullOrWhiteSpace(options.Status)
            && !Enum.TryParse(options.Status, ignoreCase: true, out status))
            return SkipDemo(logger, $"{path}:Status' is not a ChoirStatusEnum value ('{options.Status}')");

        if (options.InactiveSinceDaysAgo is { } days && days <= 0)
            return SkipDemo(logger, $"{path}:InactiveSinceDaysAgo' must be a positive number of days ('{days}')");

        // Une chorale sans responsable est ingerable : aucune ecriture possible, et aucun
        // compte n'y atterrit en zone /management.
        var managerKeys = options.ManagerAccountKeys.Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
        if (managerKeys.Count == 0)
            return SkipDemo(logger, $"{path}:ManagerAccountKeys' is empty (at least one manager required)");

        foreach (var managerKey in managerKeys)
            usedAccountKeys.Add(managerKey);

        var sectionLeaders = new List<ResolvedSectionLeader>();
        foreach (var (voicePartName, accountKey) in options.SectionLeaderAccountKeys)
        {
            if (!Enum.TryParse<VoicePartEnum>(voicePartName, ignoreCase: true, out var voicePart))
                return SkipDemo(logger, $"{path}:SectionLeaderAccountKeys:{voicePartName}' is not a VoicePartEnum value");
            if (string.IsNullOrWhiteSpace(accountKey))
                return SkipDemo(logger, $"{path}:SectionLeaderAccountKeys:{voicePartName}' has no account key");
            usedAccountKeys.Add(accountKey);
            sectionLeaders.Add(new ResolvedSectionLeader(voicePart, accountKey));
        }

        var singers = new List<ResolvedSinger>();
        foreach (var (accountKey, voicePartName) in options.SingerAccountVoiceParts)
        {
            if (!Enum.TryParse<VoicePartEnum>(voicePartName, ignoreCase: true, out var voicePart))
                return SkipDemo(logger, $"{path}:SingerAccountVoiceParts:{accountKey}' is not a VoicePartEnum value ('{voicePartName}')");
            usedAccountKeys.Add(accountKey);
            singers.Add(new ResolvedSinger(accountKey, voicePart));
        }

        var songs = new List<ResolvedSong>();
        foreach (var (songKey, songOptions) in options.Songs.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryResolveSong($"{path}:Songs:{songKey}", songOptions, fixturesRoot, usedAccountKeys, logger, out var song))
                return false;
            songs.Add(song);
        }

        resolved = new ResolvedChoir(
            options.Name!, status, options.InactiveSinceDaysAgo, options.SoftDeleted,
            managerKeys, sectionLeaders, singers, songs);
        return true;
    }

    private static bool TryResolveSong(
        string path, DemoSongOptions options, string fixturesRoot, HashSet<string> usedAccountKeys,
        ILogService logger, out ResolvedSong resolved)
    {
        resolved = null!;

        if (string.IsNullOrWhiteSpace(options.Title))
            return SkipDemo(logger, $"{path}:Title' missing");
        if (!Enum.TryParse<SongStatusEnum>(options.Status, ignoreCase: true, out var status))
            return SkipDemo(logger, $"{path}:Status' is not a SongStatusEnum value ('{options.Status}')");

        SongPriorityEnum? priority = null;
        if (!string.IsNullOrWhiteSpace(options.Priority))
        {
            if (!Enum.TryParse<SongPriorityEnum>(options.Priority, ignoreCase: true, out var parsedPriority))
                return SkipDemo(logger, $"{path}:Priority' is not a SongPriorityEnum value ('{options.Priority}')");
            priority = parsedPriority;
        }

        var voiceParts = new List<VoicePartEnum>();
        foreach (var voicePartName in options.VoiceParts)
        {
            if (!Enum.TryParse<VoicePartEnum>(voicePartName, ignoreCase: true, out var voicePart))
                return SkipDemo(logger, $"{path}:VoiceParts' contains a non-VoicePartEnum value ('{voicePartName}')");
            voiceParts.Add(voicePart);
        }

        var recordings = new List<ResolvedRecording>();
        foreach (var (recordingKey, recordingOptions) in options.Recordings.OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryResolveRecording($"{path}:Recordings:{recordingKey}", recordingOptions, fixturesRoot, usedAccountKeys, logger, out var recording))
                return false;
            recordings.Add(recording);
        }

        resolved = new ResolvedSong(options.Title!, options.Composer, status, priority, voiceParts, recordings);
        return true;
    }

    private static bool TryGetAccount(
        DemoSeedOptions options, string key, ILogService logger, out DemoAccountOptions account)
    {
        if (options.Accounts.TryGetValue(key, out account!) && !string.IsNullOrWhiteSpace(account.Email))
            return true;

        account = null!;
        return SkipDemo(logger, $"'Seed:Demo:Accounts:{key}' missing or without email");
    }

    private static bool IsEventUsable(DemoEventOptions? demoEvent) =>
        demoEvent is not null
        && !string.IsNullOrWhiteSpace(demoEvent.Title)
        && !string.IsNullOrWhiteSpace(demoEvent.Location);

    // Le motif est interpole dans le message, pas passe en propriete structuree : c'est deja
    // une chaine formee pour un humain, et l'inliner est ce qui rend la cause lisible dans le
    // journal — un « Seed demo skipped: {Reason} » sans le motif n'aide personne.
    private static bool SkipDemo(ILogService logger, string reason)
    {
        logger.LogWarning($"Seed demo skipped: {reason}");
        return false;
    }

    /// <summary>Jeu de demonstration resolu et valide, pret a etre ecrit en base.</summary>
    private sealed record DemoSeedPlan(
        string Password,
        IReadOnlyDictionary<string, DemoAccountOptions> Accounts,
        DemoChoirClientOptions ChoirClient,
        DemoEventClientOptions EventClient,
        IReadOnlyList<ResolvedChoir> Choirs,
        IReadOnlyList<ResolvedStatusProbeClient> StatusProbeClients,
        string ChoirFounderAccountKey);

    private sealed record ResolvedChoir(
        string Name,
        ChoirStatusEnum Status,
        int? InactiveSinceDaysAgo,
        bool SoftDeleted,
        IReadOnlyList<string> ManagerAccountKeys,
        IReadOnlyList<ResolvedSectionLeader> SectionLeaders,
        IReadOnlyList<ResolvedSinger> Singers,
        IReadOnlyList<ResolvedSong> Songs);

    private sealed record ResolvedStatusProbeClient(string Name, ClientStatusEnum Status);

    private sealed record ResolvedSectionLeader(VoicePartEnum VoicePart, string AccountKey);

    private sealed record ResolvedSinger(string AccountKey, VoicePartEnum VoicePart);

    private sealed record ResolvedSong(
        string Title,
        string? Composer,
        SongStatusEnum Status,
        SongPriorityEnum? Priority,
        IReadOnlyList<VoicePartEnum> VoiceParts,
        IReadOnlyList<ResolvedRecording> Recordings);
}
