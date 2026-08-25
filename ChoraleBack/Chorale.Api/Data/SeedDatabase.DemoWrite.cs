using ChoraleBackEnd.Common.Enums;
using ChoraleBackEnd.Data;
using ChoraleBackEnd.Data.Entities;
using ChoraleBackEnd.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ChoraleBackEnd.Api.Data;

public static partial class SeedDatabase
{
    /// <summary>
    /// Jeu de demonstration enrichi (`10` recette) : deux clients independants, quatre
    /// espaces (deux chorales et un evenement autonome cote client A, un evenement autonome
    /// seul cote client B), et des comptes couvrant chaque role — dont SectionLeader, absent
    /// des versions precedentes, et ClientManager sur un compte DEDIE. Idempotent — ne fait
    /// rien si les deux clients de demonstration existent deja.
    /// </summary>
    /// <remarks>
    /// Le contenu du jeu (comptes, clients, chorale, evenements) vient de la section
    /// `Seed:Demo` d'`appsettings.json` ; les six comptes partagent le mot de passe
    /// `Seed:Demo:Password`, seul secret a configurer pour activer ce jeu — tenu hors du
    /// depot dans `appsettings.Development.json` en local, dans les Application Settings de
    /// l'App Service en Staging (avec `Seed:Demo:EnabledInStaging = true` en plus, voir
    /// `InitializeAsync`).
    /// L'affectation ClientManager suit la logique d'auto-service reelle
    /// (<see cref="Services.OnboardingServices.OnboardingCreationService"/>) : c'est le
    /// createur de la structure — le premier responsable de la premiere chorale pour le
    /// client A, l'Organizer de l'evenement pour le client B — qui en devient responsable, via
    /// <see cref="ClientMember"/> et non une appartenance d'espace. Un SECOND ClientManager,
    /// dedie, est ajoute a chaque client : sans lui la zone `/client/:clientId` est
    /// inatteignable, le front classant Management avant Client dans `resolveZone`.
    ///
    /// Decision idempotence : ce seed detecte trois etats — dataset complet (no-op),
    /// dataset partiel (un seul des deux clients existe) et ancien dataset a un seul
    /// client. Dans ces deux derniers cas, il n'ecrit RIEN et journalise un avertissement :
    /// completer ou fusionner un dataset partiel/ancien exigerait de deviner ce qui manque
    /// deja, avec le risque de laisser la base a moitie peuplee si une etape echoue en
    /// route. Un seul chemin d'ecriture existe : base vierge de ces trois noms de client
    /// -> creation complete en une transaction (un seul SaveChangesAsync final).
    ///
    /// Les enregistrements audio sont un cas particulier de cette transaction unique : leur
    /// copie disque (voir `SeedDatabase.Recordings.cs`) a lieu AVANT le `SaveChangesAsync`
    /// final, puisque le nom de fichier genere doit exister avant d'etre ecrit sur
    /// `Recording.FilePath`. Si ce `SaveChangesAsync` echoue apres qu'une ou plusieurs copies
    /// ont reussi, les fichiers deja copies restent orphelins sur disque — aucune compensation
    /// n'est prevue, meme risque accepte que les etats partiels ci-dessus (nettoyage manuel).
    /// </remarks>
    private static async Task EnsureDemoDataAsync(
        ChoraleDbContext context, UserManager<User> userManager, IWebHostEnvironment environment,
        IPathService pathService, DemoSeedOptions? options, ILogService logger)
    {
        // Nom de l'ancien jeu a un seul client : valeur de detection d'un dataset
        // historique, pas une donnee a configurer — elle ne doit jamais changer.
        const string legacyClientName = "Client de démonstration";

        var fixturesRoot = ResolveFixturesRoot(environment, options?.FixturesRoot);

        // La configuration est entierement validee AVANT la moindre ecriture : une section
        // incomplete produit un avertissement et un no-op, jamais une base a moitie peuplee.
        if (!TryBuildDemoPlan(options, fixturesRoot, logger, out var plan))
            return;

        if (await IsDemoAlreadySeededAsync(
                context, plan.ChoirClient.Name!, plan.EventClient.Name!, legacyClientName, logger))
            return;

        // Tous les comptes sont crees AVANT toute entite metier : si l'un d'eux echoue
        // (politique de mot de passe, validateur additionnel en test), l'exception remonte
        // avant le moindre context.Add, donc avant le SaveChangesAsync final — aucun client
        // ni espace partiel ne peut atteindre la base.
        var users = new Dictionary<string, User>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, account) in plan.Accounts)
            users[key] = await EnsureDemoUserAsync(userManager, plan.Password, account);

        var choirClient = BuildDemoClient(context, plan.ChoirClient.Name!);
        AssignClientManager(context, choirClient.Id, users[plan.ChoirFounderAccountKey].Id);
        AssignClientManager(context, choirClient.Id, users[DemoSeedOptions.ChoirClientManagerKey].Id);

        // Chorales dont l'activite doit etre reculee dans le temps (InactiveSinceDaysAgo) :
        // memorisees ici, appliquees APRES le SaveChangesAsync final — voir
        // BackdateChoirActivityAsync, la ligne n'existe pas encore en base tant que ce
        // SaveChanges n'a pas eu lieu.
        var choirsToBackdate = new List<(Guid ChoirId, int DaysAgo)>();
        foreach (var choir in plan.Choirs)
        {
            var choirId = await BuildDemoChoirAsync(context, pathService, fixturesRoot, choirClient.Id, choir, users);
            if (choir.InactiveSinceDaysAgo is { } daysAgo)
                choirsToBackdate.Add((choirId, daysAgo));
        }

        foreach (var probe in plan.StatusProbeClients)
            BuildStatusProbeClient(context, probe.Name, probe.Status);

        // Sonde de suppression douce sur un compte : cree normalement ci-dessus (comme tout
        // autre compte de la section Accounts), marque supprime ici avant le SaveChanges final
        // — AdminUserQueryService l'exclura alors de tous ses listings (!u.IsDeleted partout).
        users[DemoSeedOptions.DeletedAccountProbeKey].IsDeleted = true;

        BuildDemoAutonomousEvent(
            context, choirClient.Id,
            users[DemoSeedOptions.ChoirEventOrganizerKey].Id,
            users[DemoSeedOptions.ChoirEventParticipantKey].Id,
            plan.ChoirClient.Event!);

        var eventClient = BuildDemoClient(context, plan.EventClient.Name!);
        AssignClientManager(context, eventClient.Id, users[DemoSeedOptions.StandaloneOrganizerKey].Id);
        AssignClientManager(context, eventClient.Id, users[DemoSeedOptions.EventClientManagerKey].Id);
        BuildDemoAutonomousEvent(
            context, eventClient.Id,
            users[DemoSeedOptions.StandaloneOrganizerKey].Id,
            users[DemoSeedOptions.StandaloneParticipantKey].Id,
            plan.EventClient.Event!);

        await context.SaveChangesAsync();

        foreach (var (choirId, daysAgo) in choirsToBackdate)
            await BackdateChoirActivityAsync(context, choirId, daysAgo, logger);

        logger.LogInformation(
            "Seed demo created: {ClientCount} clients, {SpaceCount} spaces, {AccountCount} accounts, {RecordingCount} recordings",
            2 + plan.StatusProbeClients.Count, plan.Choirs.Count + 2, plan.Accounts.Count,
            plan.Choirs.Sum(c => c.Songs.Sum(song => song.Recordings.Count)));
    }

    /// <summary>
    /// Recule <c>CreatedAt</c>/<c>UpdatedAt</c> d'une chorale de <paramref name="daysAgo"/>
    /// jours, pour rendre observable <c>AdminChoirService.InactiveFor30Days</c> (lot audit
    /// admin). <c>ExecuteUpdateAsync</c> contourne le <c>ChangeTracker</c> — donc
    /// <c>AuditSaveChangesInterceptor</c>, qui reecrirait sinon ces deux dates a la date du
    /// jour sur TOUT SaveChanges, y compris un second passage explicite — c'est le seul moyen
    /// de simuler une inactivite ancienne sans affaiblir cette garantie d'audit pour le reste
    /// de l'application. Non supporte par le provider InMemory (<c>Chorale.Test</c>) : sans
    /// effet hors base relationnelle, jamais atteint par <c>SeedDatabaseTests</c> qui ne
    /// configure aucune chorale avec <c>InactiveSinceDaysAgo</c>.
    /// </summary>
    private static async Task BackdateChoirActivityAsync(
        ChoraleDbContext context, Guid choirId, int daysAgo, ILogService logger)
    {
        if (!context.Database.IsRelational())
        {
            logger.LogInformation(
                "Seed demo: backdating skipped for choir {ChoirId} (non-relational provider)", choirId);
            return;
        }

        var backdated = DateTime.UtcNow.AddDays(-daysAgo);
        await context.Choirs
            .Where(c => c.Id == choirId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.CreatedAt, backdated)
                .SetProperty(c => c.UpdatedAt, backdated));
    }

    /// <summary>
    /// Client "sonde" (lot audit admin) : sans chorale ni membre, seul son <c>Status</c>
    /// compte — rend observable le filtre correspondant de <c>ClientService.GetPagedAsync</c>
    /// sans toucher aux deux clients fonctionnels (qui restent <c>Active</c>).
    /// </summary>
    private static void BuildStatusProbeClient(ChoraleDbContext context, string name, ClientStatusEnum status)
    {
        context.Clients.Add(new Client
        {
            Id = ChoraleDbContext.NewIdGuid(),
            Name = name,
            Status = status,
            ChoirLimit = Client.DefaultLimits.Choirs,
            MemberLimit = Client.DefaultLimits.Members,
            StorageQuotaBytes = Client.DefaultLimits.StorageOctets,
            MaxFileSizeBytes = Client.DefaultLimits.FileSizeBytes
        });
    }

    private static async Task<bool> IsDemoAlreadySeededAsync(
        ChoraleDbContext context, string choirClientName, string eventClientName,
        string legacyClientName, ILogService logger)
    {
        var existingNames = await context.Clients
            .Where(c => c.Name == choirClientName || c.Name == eventClientName || c.Name == legacyClientName)
            .Select(c => c.Name)
            .ToListAsync();

        var choirClientExists = existingNames.Contains(choirClientName);
        var eventClientExists = existingNames.Contains(eventClientName);

        if (choirClientExists && eventClientExists)
        {
            // Seule sortie de cette methode qui ne journalisait rien. Un demarrage sur une base
            // deja peuplee restait donc entierement muet — au point de masquer qu'un jeu seede
            // par une version anterieure du seed n'a pas le contenu que la configuration decrit
            // aujourd'hui (cas des enregistrements de demonstration).
            logger.LogInformation(
                "Seed demo skipped: dataset already present ({ChoirClientName}, {EventClientName})",
                choirClientName, eventClientName);
            return true;
        }

        if (choirClientExists || eventClientExists)
        {
            logger.LogWarning(
                "Seed demo skipped: partial demo dataset detected (only one of the two demo "
                + "clients exists) — resolve manually before reseeding.");
            return true;
        }

        if (existingNames.Contains(legacyClientName))
        {
            logger.LogWarning(
                "Seed demo skipped: legacy single-client demo dataset detected ('{LegacyClientName}') — "
                + "this seed now expects the two-client dataset; clean the dev database manually before reseeding.",
                legacyClientName);
            return true;
        }

        return false;
    }

    private static async Task<User> EnsureDemoUserAsync(
        UserManager<User> userManager, string password, DemoAccountOptions account)
    {
        var email = account.Email!.Trim();

        var user = await userManager.FindByEmailAsync(email);
        if (user is not null)
            return user;

        user = new User
        {
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            Firstname = account.Firstname ?? string.Empty,
            Lastname = account.Lastname ?? string.Empty,
            IsActive = true,
            LastActive = DateTime.UtcNow
        };

        var creation = await userManager.CreateAsync(user, password);
        if (!creation.Succeeded)
            throw new InvalidOperationException(FormatErrors(creation));

        return user;
    }

    private static Client BuildDemoClient(ChoraleDbContext context, string name)
    {
        var client = new Client
        {
            Id = ChoraleDbContext.NewIdGuid(),
            Name = name,
            Status = ClientStatusEnum.Active,
            ChoirLimit = Client.DefaultLimits.Choirs,
            MemberLimit = Client.DefaultLimits.Members,
            StorageQuotaBytes = Client.DefaultLimits.StorageOctets,
            MaxFileSizeBytes = Client.DefaultLimits.FileSizeBytes
        };
        context.Clients.Add(client);
        return client;
    }

    private static void AssignClientManager(ChoraleDbContext context, Guid clientId, string userId)
    {
        context.ClientMembers.Add(new ClientMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            ClientId = clientId,
            UserId = userId,
            Role = UserRoleEnum.ClientManager
        });
    }

    private static async Task<Guid> BuildDemoChoirAsync(
        ChoraleDbContext context, IPathService pathService, string fixturesRoot, Guid clientId,
        ResolvedChoir choir, IReadOnlyDictionary<string, User> users)
    {
        var choirId = ChoraleDbContext.NewIdGuid();
        var space = new Space { Id = choirId, SpaceType = SpaceTypeEnum.Choir, ClientId = clientId };
        context.Spaces.Add(space);
        var choirEntity = new ChoraleBackEnd.Data.Entities.Choir
        {
            Id = choirId,
            ClientId = clientId,
            Name = choir.Name,
            Status = choir.Status
        };
        context.Choirs.Add(choirEntity);

        if (choir.SoftDeleted)
        {
            // Reproduit exactement ChoirService.DeleteAsync : Choir.IsDeleted ET
            // Space.IsDeleted ensemble, jamais l'un sans l'autre — sinon AdminChoirService
            // (qui verifie les deux) laisserait la chorale visible malgre la suppression.
            choirEntity.IsDeleted = true;
            space.IsDeleted = true;
        }

        // Les quatre pupitres sont crees d'un bloc et indexes par voix : `04` § Membre exige
        // une voix ET un pupitre des l'affectation, le seed ne doit produire aucun membre sans
        // les deux.
        var sectionsByVoicePart = new Dictionary<VoicePartEnum, Section>();
        foreach (var voicePart in Enum.GetValues<VoicePartEnum>())
        {
            var section = new Section { Id = ChoraleDbContext.NewIdGuid(), ChoirId = choirId, VoicePart = voicePart };
            context.Sections.Add(section);
            sectionsByVoicePart[voicePart] = section;
        }

        foreach (var managerKey in choir.ManagerAccountKeys)
            AddSpaceMemberWithRole(context, users[managerKey].Id, choirId, choirId, UserRoleEnum.Manager);

        // SectionLeader n'est PAS une ligne SpaceMemberRole : SpaceRoleResolverService le derive
        // de Section.SectionLeaderId. Le chef est aussi membre du pupitre qu'il dirige — sans
        // cela ChoirMembersService.AssignSectionLeaderRoleAsync refuserait la meme affectation.
        foreach (var leader in choir.SectionLeaders)
        {
            var leaderId = users[leader.AccountKey].Id;
            var section = sectionsByVoicePart[leader.VoicePart];
            section.SectionLeaderId = leaderId;
            AddSpaceMember(context, leaderId, choirId, choirId);
            AddSectionMember(context, leaderId, section.Id);
        }

        // Membres simples : aucune ligne SpaceMemberRole — le role de base d'un membre de
        // chorale est implicite (SpaceRoleResolverService), seuls Manager et Organizer sont
        // stockes explicitement.
        foreach (var singer in choir.Singers)
        {
            var singerId = users[singer.AccountKey].Id;
            AddSpaceMember(context, singerId, choirId, choirId);
            AddSectionMember(context, singerId, sectionsByVoicePart[singer.VoicePart].Id);
        }

        foreach (var song in choir.Songs)
            await AddDemoSongAsync(context, pathService, fixturesRoot, choirId, song, users);

        return choirId;
    }

    private static void AddSectionMember(ChoraleDbContext context, string userId, Guid sectionId)
        => context.SectionMembers.Add(new SectionMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = userId,
            SectionId = sectionId
        });

    private static async Task AddDemoSongAsync(
        ChoraleDbContext context, IPathService pathService, string fixturesRoot, Guid choirId,
        ResolvedSong song, IReadOnlyDictionary<string, User> users)
    {
        var songId = ChoraleDbContext.NewIdGuid();
        context.Songs.Add(new Song
        {
            Id = songId,
            ChoirId = choirId,
            Title = song.Title,
            Composer = song.Composer,
            Status = song.Status,
            Priority = song.Priority
        });

        foreach (var voicePart in song.VoiceParts)
            context.SongVoiceParts.Add(new SongVoicePart
            {
                Id = ChoraleDbContext.NewIdGuid(),
                SongId = songId,
                VoicePart = voicePart
            });

        foreach (var recording in song.Recordings)
            await AddDemoRecordingAsync(context, pathService, fixturesRoot, songId, choirId, users, recording);
    }

    private static void BuildDemoAutonomousEvent(
        ChoraleDbContext context, Guid clientId, string organizerId, string participantId,
        DemoEventOptions options)
    {
        var startDate = DateTime.UtcNow.AddMonths(options.StartInMonths);
        var eventId = ChoraleDbContext.NewIdGuid();
        context.Spaces.Add(new Space
        {
            Id = eventId,
            SpaceType = SpaceTypeEnum.Event,
            ClientId = clientId,
            EndDate = startDate
        });

        // ChoirId = null : evenement autonome (D39). Un evenement rattache a une chorale
        // n'a pas d'Organizer, question qui ne se pose pas ici puisqu'aucun des deux
        // evenements du jeu de demonstration n'est rattache.
        context.Events.Add(new Event
        {
            Id = eventId,
            Title = options.Title!,
            Description = options.Description,
            StartDate = startDate,
            Type = EventTypeEnum.Concert,
            Location = options.Location!,
            Status = EventStatusEnum.Published,
            ChoirId = null
        });

        AddSpaceMemberWithRole(context, organizerId, eventId, null, UserRoleEnum.Organizer, AttendanceEnum.NoReply);

        // Participant : meme raison que Singer ci-dessus, aucune ligne SpaceMemberRole.
        AddSpaceMember(context, participantId, eventId, null, AttendanceEnum.Attending);
    }

    private static SpaceMember AddSpaceMember(
        ChoraleDbContext context, string userId, Guid spaceId, Guid? choirId, AttendanceEnum? presence = null)
    {
        var member = new SpaceMember
        {
            Id = ChoraleDbContext.NewIdGuid(),
            UserId = userId,
            SpaceId = spaceId,
            ChoirId = choirId,
            Status = MemberStatusEnum.Active,
            Presence = presence
        };
        context.SpaceMembers.Add(member);
        return member;
    }

    private static SpaceMember AddSpaceMemberWithRole(
        ChoraleDbContext context, string userId, Guid spaceId, Guid? choirId,
        UserRoleEnum role, AttendanceEnum? presence = null)
    {
        var member = AddSpaceMember(context, userId, spaceId, choirId, presence);
        context.SpaceMemberRoles.Add(new SpaceMemberRole
        {
            Id = ChoraleDbContext.NewIdGuid(),
            SpaceMemberId = member.Id,
            Role = role
        });
        return member;
    }
}
