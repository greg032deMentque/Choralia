namespace ChoraleBackEnd.Api.Data;

/// <summary>
/// Jeu de donnees du seed, lu depuis la section `Seed` de la configuration.
/// </summary>
/// <remarks>
/// Les valeurs non sensibles vivent dans `appsettings.json` (versionne) ; les deux secrets —
/// <see cref="AdminSeedOptions.Password"/> et <see cref="DemoSeedOptions.Password"/> —
/// viennent de `appsettings.Development.json` (non versionne) en local, et des Application
/// Settings Azure en staging et production. Ils se fusionnent par cle sur les memes blocs.
///
/// Les comptes sont un dictionnaire et non un tableau : la configuration .NET fusionne les
/// tableaux par index entre fichiers, ce qui rendrait tout ajout dans un fichier de
/// surcharge dependant de l'ordre de declaration.
/// </remarks>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public AdminSeedOptions? Admin { get; set; }

    public DemoSeedOptions? Demo { get; set; }
}

/// <summary>
/// Super administrateur, seede dans tous les environnements. Contrairement au jeu de
/// demonstration, son absence est bloquante : demarrer sans admin produirait un
/// environnement injoignable sans aucune trace visible.
/// </summary>
public sealed class AdminSeedOptions
{
    public const string EmailKey = "Seed:Admin:Email";
    public const string PasswordKey = "Seed:Admin:Password";

    public string? Email { get; set; }
    public string? Firstname { get; set; }
    public string? Lastname { get; set; }

    /// <summary>
    /// Doit respecter la politique Identity de `Program.cs`. Jamais dans un fichier
    /// versionne : `appsettings.Development.json` en local, Application Settings Azure
    /// (`Seed__Admin__Password`) ailleurs.
    /// </summary>
    public string? Password { get; set; }
}

/// <summary>
/// Jeu de demonstration, seede en environnement `Development` uniquement.
/// </summary>
public sealed class DemoSeedOptions
{
    // Cles attendues dans Accounts : contrat entre la configuration et le seeder, qui
    // rattache chaque compte a un role precis du jeu de demonstration. Chefs de choeur,
    // chefs de pupitre et choristes, eux, ne sont PAS des cles fixes : ils sont designes par
    // chorale dans `Choirs`, pour qu'ajouter une chorale reste de la configuration.
    public const string ChoirEventOrganizerKey = "ChoirEventOrganizer";
    public const string ChoirEventParticipantKey = "ChoirEventParticipant";
    public const string StandaloneOrganizerKey = "StandaloneOrganizer";
    public const string StandaloneParticipantKey = "StandaloneParticipant";

    /// <summary>
    /// Compte dont le SEUL rattachement est ClientManager sur le client concerne. Sans lui la
    /// zone « Ma structure » (`/client/:clientId`) est inatteignable : `resolveZone` (front)
    /// classe Management AVANT Client, donc un responsable de chorale qui est aussi
    /// ClientManager part toujours en `/management`, et la topbar ne liste que les espaces.
    /// </summary>
    public const string ChoirClientManagerKey = "ChoirClientManager";
    public const string EventClientManagerKey = "EventClientManager";

    /// <summary>
    /// Sonde de suppression douce (lot audit admin) : compte cree normalement puis marque
    /// <c>IsDeleted = true</c> avant le SaveChanges final, sans aucun rattachement. Verifie
    /// que <c>AdminUserQueryService</c> l'exclut bien de tous ses listings (<c>!u.IsDeleted</c>
    /// partout) — jamais visible d'un ecran, uniquement verifiable en base.
    /// </summary>
    public const string DeletedAccountProbeKey = "DeletedAccountProbe";

    /// <summary>
    /// Mot de passe partage par tous les comptes de demonstration : un seul secret a
    /// configurer pour activer ce jeu — poste de developpement local (`Development`), ou
    /// Application Settings de l'App Service (`Staging`, si <see cref="EnabledInStaging"/>
    /// vaut aussi <c>true</c>). Doit respecter la politique Identity de `Program.cs`.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Second garde-fou, requis en plus de <see cref="Password"/> pour activer ce seed en
    /// environnement `Staging` — jamais en `Development` (toujours actif si le mot de passe
    /// est configure, comportement inchange) ni en `Production` (aucune voie d'activation,
    /// quels que soient les flags : <c>IsStaging()</c> y est toujours faux). Meme motif a deux
    /// facteurs que `Swagger:Enabled` combine a `IsDevelopment() || IsStaging()` dans
    /// `Program.cs` : la seule presence du mot de passe ne suffit pas, car il pourrait avoir
    /// ete configure sur l'App Service Staging pour une autre raison sans intention d'y
    /// peupler des comptes de demonstration. Faux par defaut — absence de cle = desactive.
    /// </summary>
    public bool EnabledInStaging { get; set; }

    /// <summary>
    /// Racine des fixtures de seed (actuellement les enregistrements audio de demonstration),
    /// meme esprit que `Storage:Root` pour <see cref="Services.PathService"/> : une valeur
    /// vide ou absente retombe sur un chemin calcule depuis `ContentRootPath`
    /// (`Data/SeedFixtures`), une valeur fournie la remplace telle quelle. Reste ici plutot
    /// que dans une cle `IConfiguration` lue en direct : tout `Seed:*` passe par
    /// <see cref="SeedOptions"/>, un seul chemin de configuration. N'a d'effet qu'en
    /// environnement `Development` ou `Staging` (si <see cref="EnabledInStaging"/>) — les
    /// seuls ou <see cref="Api.Data.SeedDatabase"/> consulte cette section.
    /// </summary>
    public string? FixturesRoot { get; set; }

    public Dictionary<string, DemoAccountOptions> Accounts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public DemoChoirClientOptions? ChoirClient { get; set; }

    public DemoEventClientOptions? EventClient { get; set; }

    /// <summary>
    /// Clients "sondes", sans chorale ni membre, dont le seul but est de rendre le filtre
    /// <c>Status</c> de <c>ClientService.GetPagedAsync</c> observable (lot audit admin) — les
    /// deux clients fonctionnels ci-dessus restent volontairement <c>Active</c>. Dictionnaire
    /// et non tableau, meme raison que <see cref="Accounts"/>.
    /// </summary>
    public Dictionary<string, DemoStatusProbeClientOptions> StatusProbeClients { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DemoStatusProbeClientOptions
{
    public string? Name { get; set; }

    /// <summary>Valeur de <see cref="Common.Enums.ClientStatusEnum"/>, en clair.</summary>
    public string? Status { get; set; }
}

public sealed class DemoAccountOptions
{
    public string? Email { get; set; }
    public string? Firstname { get; set; }
    public string? Lastname { get; set; }
}

/// <summary>Client A : une ou plusieurs chorales, et son evenement autonome.</summary>
public sealed class DemoChoirClientOptions
{
    public string? Name { get; set; }

    /// <summary>
    /// Chorales du client, indexees par une cle libre. Dictionnaire et non tableau : la
    /// configuration .NET fusionne les tableaux par index entre fichiers, ce qui rendrait tout
    /// ajout dans un fichier de surcharge dependant de l'ordre de declaration (meme raison que
    /// <see cref="DemoSeedOptions.Accounts"/>). Ajouter une chorale ne demande donc aucun code.
    /// </summary>
    public Dictionary<string, DemoChoirOptions> Choirs { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public DemoEventOptions? Event { get; set; }
}

/// <summary>Client B : un evenement autonome seul, sans chorale prealable.</summary>
public sealed class DemoEventClientOptions
{
    public string? Name { get; set; }
    public DemoEventOptions? Event { get; set; }
}

public sealed class DemoChoirOptions
{
    public string? Name { get; set; }

    /// <summary>
    /// Valeur de <see cref="Common.Enums.ChoirStatusEnum"/>, en clair. Optionnel — vide ou
    /// absent retombe sur <c>Published</c> (comportement historique, avant l'ajout de ce
    /// champ — verrouille par <c>SeedDatabaseTests.InitializeAsync_DemoSeed_ChoirsCreatedAsPublished</c>).
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Sonde d'inactivite (lot audit admin) : quand renseigne, <c>CreatedAt</c> et
    /// <c>UpdatedAt</c> de la chorale sont recules de ce nombre de jours APRES le seed normal,
    /// pour rendre observable <c>AdminChoirService.InactiveFor30Days</c>. Voir
    /// <see cref="Api.Data.SeedDatabase.BackdateChoirActivityAsync"/> pour le pourquoi du
    /// contournement (l'interceptor d'audit ecraserait sinon ces deux dates a la date du jour).
    /// </summary>
    public int? InactiveSinceDaysAgo { get; set; }

    /// <summary>
    /// Sonde de suppression douce (lot audit admin) : quand vrai, la chorale ET son espace
    /// sont crees directement avec <c>IsDeleted = true</c> — memes deux champs, ensemble, que
    /// <c>ChoirService.DeleteAsync</c>. Faux par defaut : aucune chorale existante n'est
    /// affectee par l'ajout de ce champ.
    /// </summary>
    public bool SoftDeleted { get; set; }

    /// <summary>
    /// Cles (dans <see cref="DemoSeedOptions.Accounts"/>) des comptes responsables de cette
    /// chorale. Plusieurs sont admis : une chorale reelle peut avoir plusieurs responsables.
    /// Au moins un est exige.
    /// </summary>
    public List<string> ManagerAccountKeys { get; set; } = [];

    /// <summary>
    /// Chef de pupitre par voix : cle de <see cref="Common.Enums.VoicePartEnum"/> (nom en
    /// clair, relu par un humain) vers la cle du compte dans
    /// <see cref="DemoSeedOptions.Accounts"/>. Le compte designe devient membre de la chorale,
    /// est rattache a ce pupitre, et en devient le chef (`Section.SectionLeaderId` — le role
    /// SectionLeader n'est PAS une ligne SpaceMemberRole, il en est derive par
    /// SpaceRoleResolverService).
    /// </summary>
    public Dictionary<string, string> SectionLeaderAccountKeys { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Membres simples de la chorale : cle du compte vers le nom de sa voix (valeur de
    /// <see cref="Common.Enums.VoicePartEnum"/>). `04` § Membre exige une voix et un pupitre
    /// des l'affectation — le seed ne doit jamais produire un membre sans les deux.
    /// </summary>
    public Dictionary<string, string> SingerAccountVoiceParts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Repertoire de demonstration, indexe par une cle libre (dictionnaire, meme raison que
    /// <see cref="DemoChoirClientOptions.Choirs"/>). Sans chants, les ecrans Partitions,
    /// Enregistrements et les consignes de chant restent inutilisables.
    /// </summary>
    public Dictionary<string, DemoSongOptions> Songs { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DemoSongOptions
{
    public string? Title { get; set; }
    public string? Composer { get; set; }

    /// <summary>Valeur de <see cref="Common.Enums.SongStatusEnum"/>, en clair.</summary>
    public string? Status { get; set; }

    /// <summary>Valeur de <see cref="Common.Enums.SongPriorityEnum"/>, en clair. Optionnel.</summary>
    public string? Priority { get; set; }

    /// <summary>Voix concernees : valeurs de <see cref="Common.Enums.VoicePartEnum"/>, en clair.</summary>
    public List<string> VoiceParts { get; set; } = [];

    /// <summary>
    /// Enregistrements de demonstration, indexes par une cle libre (dictionnaire, meme raison
    /// que <see cref="DemoChoirClientOptions.Choirs"/>). Sans eux, les ecrans d'ecoute et de
    /// playlist par evenement restent inutilisables en recette.
    /// </summary>
    public Dictionary<string, DemoRecordingOptions> Recordings { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DemoRecordingOptions
{
    /// <summary>Valeur de <see cref="Common.Enums.RecordingTypeEnum"/>, en clair.</summary>
    public string? Type { get; set; }

    /// <summary>
    /// Valeur de <see cref="Common.Enums.VoicePartEnum"/>, en clair. Obligatoire si et
    /// seulement si <see cref="Type"/> vaut <c>ByVoicePart</c> — meme regle que
    /// <see cref="Services.ChoirServices.RecordingService"/>.
    /// </summary>
    public string? TargetVoicePart { get; set; }

    /// <summary>Valeur de <see cref="Common.Enums.RecordingStatusEnum"/>, en clair.</summary>
    public string? Status { get; set; }

    /// <summary>Valeur de <see cref="Common.Enums.RecordingSourceEnum"/>, en clair.</summary>
    public string? Source { get; set; }

    /// <summary>Cle (dans <see cref="DemoSeedOptions.Accounts"/>) du compte createur.</summary>
    public string? CreatorAccountKey { get; set; }

    public string? ContentOwner { get; set; }

    public bool DownloadAllowed { get; set; }

    public int DurationSeconds { get; set; }

    /// <summary>
    /// Nom du fichier fixture (avec extension) sous la racine resolue par
    /// <see cref="DemoSeedOptions.FixturesRoot"/> (sous-dossier <c>Recordings</c>). Bare
    /// filename uniquement, aucun separateur de chemin admis.
    /// </summary>
    public string? FixtureFileName { get; set; }
}

public sealed class DemoEventOptions
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }

    /// <summary>
    /// Nombre de mois a ajouter a la date du seed pour obtenir la date de debut :
    /// une date absolue en configuration deviendrait passee, et un evenement passe rend
    /// le jeu de demonstration inutilisable.
    /// </summary>
    public int StartInMonths { get; set; }
}
