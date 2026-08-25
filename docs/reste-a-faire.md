# Reste à faire — ChoraleHelper

> Point d'étape au 2026-07-31. Remplace le point d'étape du 2026-07-29 (palier `Client`,
> statuts d'événement, purge de dette de sécurité). Chantier livré : partie administration
> (4 zones web, statut de chorale, `ClientId` sur `Space`, 4 correctifs d'habilitation,
> écrans d'administration générale, socle front partagé).

## ✅ Fait

### Backend + Front — anglicisation complète du code (chantier du 2026-07-31, option B)

> Les entrées historiques ci-dessous citent les anciens noms (`ChantController`,
> `api/chants`, `StatutChoraleEnum`…). Elles décrivent l'état au moment de leur livraison
> et n'ont volontairement pas été réécrites.

| Sujet | Contenu | Vérification |
|---|---|---|
| **Vocabulaire** | Tout le code passe en anglais : 22 entités (`Chorale`→`Choir`, `Chant`→`Song`, `Evenement`→`Event`, `Pupitre`→`Section`, `Enregistrement`→`Recording`, `Partition`→`Score`, `Consigne`→`Instruction`, `Espace`→`Space`, `ListeChants`→`SongList`, `DemandeAdhesion`→`MembershipRequest`, `EspaceCodeRattachement`→`SpaceJoinCode`…), ~130 propriétés, 21 enums (types **et** membres), ~105 méthodes de service, 21 contrôleurs. **Seuls restent en français** : les libellés d'interface, les messages utilisateur, les commentaires, et les chemins d'URL Angular (`/chants`, `/evenements`, `/gestion`) | `dotnet build` + 564 tests verts, `ng build` vert |
| **Ordinaux d'enum inchangés** | Les 21 enums changent de nom (type et membres) sans qu'aucun ordinal ne bouge : `VoixEnum`→`VoicePartEnum` (`Basse`→`Bass=2`), `StatutEnregistrementEnum`→`RecordingStatusEnum` (`AValider`→`PendingReview=1`)… Le SQL en dur (`[Status] = 1` sur l'index `Scores`, `[Scope] BETWEEN 0 AND 3` sur `Instructions`) désigne les mêmes valeurs qu'avant | `EnumOrdinauxTests` (fichier non renommé malgré l'anglicisation du reste du code — voir écart signalé), réécrit avec les nouveaux noms d'enum et **les mêmes ordinaux attendus** |
| **Base repartie de zéro** | Les 12 migrations historiques étaient déjà supprimées du working tree : une unique migration `InitialCreate` est générée depuis le modèle anglicisé. 30 tables, 6 contraintes `CHECK` (`CK_Choir_Status`, `CK_Instruction_Scope`…) et 10 index filtrés (`[Status] = 1 AND [IsDeleted] = 0`…), tous en anglais. **Aucune reprise de données** — décision assumée, aucune base à préserver | `dotnet ef database update` sur base neuve |
| **41 routes renommées** | Bases : `api/chants`→`api/songs`, `api/chorales`→`api/choirs`, `api/evenements`→`api/events`, `api/enregistrements`→`api/recordings`, `api/partitions`→`api/scores`, `api/consignes`→`api/instructions`, `api/listes-chants`→`api/song-lists`, `api/membres-chorale`→`api/choir-members`, `api/pupitres`→`api/sections`, `api/espaces`→`api/spaces`. Actions : `Publier`→`Publish`, `ChangerStatut`→`ChangeStatus`, `AjouterMembre`→`AddMember`, `EnvoyerAValidation`→`SubmitForReview`, `RepasserEnBrouillon`→`RevertToDraft`… **Rupture de contrat : back et front doivent partir ensemble** | routes listées par `grep -rhoE '\[(Route\|Http[A-Za-z]+)\("[^"]*"\)\]'` |
| **Contrats par chaîne alignés** | En-tête `X-Espace-Id`→`X-Space-Id` ; policies `EspaceResponsable`→`SpaceManager`, `ChoraleResponsable`→`ChoirManager`, `ClientResponsable`→`ClientManager`, `AdminOuClientResponsable`→`AdminOrClientManager` ; rôles JWT `Responsable`→`Manager`, `ChefPupitre`→`SectionLeader`, `Chanteur`→`Singer`, `ResponsableClient`→`ClientManager` ; codes d'action d'audit (`AdminChoraleModifiee`→`AdminChoirUpdated`…) | tests d'autorisation verts |
| **Clés de tri** | Les 18 listes blanches `*ColonnesTriables`→`*SortableColumns` et **leurs clés** (`"Titre"`→`"Title"`, `"Nom"`→`"Name"`, `"ChoraleNom"`→`"ChoirName"`…), alignées avec les `key:` des colonnes front. Contrat par chaîne : **non protégé par le compilateur des deux côtés** — un désalignement fait retomber silencieusement sur le tri par défaut | recette manuelle du tri par colonne |

### Backend — partie administration (chantier du 2026-07-31)

| Sujet | Contenu | Vérification |
|---|---|---|
| **4 zones web** (D22 étendue) | `/admin` (claim global `Admin`), `/client/:clientId` (« Ma structure », `ResponsableClient`), `/gestion/:espaceId` (`Responsable`/`ChefPupitre`/`Organisateur` sur l'espace actif), `/moi` (appartenance simple). La zone est une propriété du couple (utilisateur, espace actif), pas de l'utilisateur seul : changer d'espace actif peut changer de zone. Aiguillage centralisé dans `ChoralFront/src/app/core/zone-resolver.ts` (`resolveZone`), ordre de priorité Admin > Gestion > Client > Membre > Aucun espace | `zone-resolver.spec.ts` et guards associés |
| **Contrat `GET /api/auth/Me` élargi** | `EspaceRoles` (`EspaceId`, `Nom`, `TypeEspace`, `Roles`, `ClientId`, `ChoraleId`) couvre chorales et événements ; `ClientRoles` ajouté. `ChoraleRoles` **déprécié** (sous-ensemble d'`EspaceRoles` restreint au type Chorale) — conservé le temps de la bascule front, sera retiré | `Chorale.ViewModels/AuthenticatedUserViewModel.cs` |
| **Statut métier sur `Chorale`** (migration 13, `AjouteStatutChorale`) | `StatutChoraleEnum { Brouillon=0, Publie=1, Annule=2, Archive=3 }`, distinct de `EvenementStatutEnum` malgré des ordinaux identiques (verrouillé séparément par `EnumOrdinauxTests`). Transitions dans `ChoraleEtatHelper.TransitionAutorisee` : Brouillon→Publie\|Archive, Publie→Annule\|Archive, Annule→Publie\|Archive, Archive→Publie (seule réactivation). `IsDeleted` retrouve son seul rôle : la suppression | `AjouteStatutChoraleTests` |
| **`ClientId` porté par `Espace`** (migration 12, `AjouteClientSurEspace`) | `Espace.ClientId` obligatoire, chorale et événement confondus. `ServiceLimitService.ResoudreClientAsync` résout le client par un chemin unique. Migration de données : client technique « Événements sans structure — à rattacher » (`Client.ClientTechnique.SansStructureId`) pour les événements autonomes préexistants ; ils remontent en anomalie sur le tableau de bord (`AdminDashboardKpiViewModel.AnomalieEvenementsSansStructure`) et sur la liste des événements admin (`EstAnomalieClientTechnique`) | `AdminDashboardTests`, `AdminEvenementListeTests` |
| **4 trous de sécurité fermés (habilitation), tous antérieurs au chantier** | `ClientController.GetPaged` : `Admin` seul (était accessible à tout compte authentifié). `ChoraleController.GetPaged`/`GetById` : filtrage par appartenance ou par rôle `ResponsableClient` sur le client de la chorale (était : toutes les chorales de tous les clients). `ChoraleController.Update`/`ClientRoleAuthorizationHandler` : le client est déduit de la ressource existante (`id` en route/query), plus du corps de la requête — sinon un `ResponsableClient` pouvait modifier la chorale d'un autre client en déclarant son propre `ClientId`. `EspaceRoleResolverService.ResolveRolesAsync` filtre désormais sur `MembreStatutEnum.Actif` (un `Responsable` archivé gardait ses droits d'écriture) | — |
| **Front — redirection ouverte fermée** | `isAllowedReturnUrl()` (`ChoralFront/src/app/core/route-paths.ts`) restreint le `returnUrl` post-connexion aux segments connus sous `/gestion/:espaceId` | — |
| **Création de chorale — écart D23 résorbé** | `ChoraleController.Create`/`Update`/`Delete` passent de `[Authorize(Roles = "Admin")]` à la policy `AdminOuClientResponsable`. `AjouterMembre`/`RetirerMembre` passent à `EspaceResponsable` (le responsable d'une chorale peut désormais y ajouter un membre, ce qu'il ne pouvait pas faire avant). **Nuance non documentée par l'ancien état d'étape** : une chorale créée est immédiatement `Statut = Publie`, jamais `Brouillon` — le parcours d'inscription auto-service qui rendrait `Brouillon` utile (lot 6) n'existe pas ; une chorale invisible de ses propres membres à la création casserait le parcours actuel (voir commentaire `ChoraleService.CreateAsync`) | — |
| **7 contrôleurs d'administration générale** | `AdminUserController` (`api/admin-users` : `GetPaged`, `Create`, 3 listes onglets — `GetChoraleUsersPaged`/`GetEvenementUsersPaged`/`GetSansRattachementUsersPaged` —, `GetUserDetail` fiche agrégée, `UpdateIdentity`, `SetActive`, `ResetPassword`, `ResendInvitation`, `Delete`), `AdminChoraleController` (`api/admin-chorales` : liste, détail, onglets membres/chants/événements **en lecture seule**, `Update` restreint à `Nom`/`Description` — jamais `ClientId` ni le contenu —, `ImpactArchivage`, `ChangerStatut`), `AdminEvenementController` (liste + détail, lecture seule), `AdminChantController` (catalogue transverse, lecture seule), `AdminDashboardController` (`GetKpi`), `AdminAuditController` (`GetPaged`, lecture seule, aucun endpoint d'écriture), `ClientController` étendu | `AdminChoraleTests`, `AdminUserTests`, `AdminAuditTests` (noms indicatifs — voir `Chorale.Test/Services/`) |
| **Catalogue de chants regroupé par clé normalisée** | `ChantCleHelper.CalculerCle` : regroupement d'**affichage seul**, aucune entité « œuvre » créée en base — chaque chorale garde son propre `Chant`. Clé = titre normalisé (`ToLowerInvariant` + suppression diacritiques/ponctuation, jamais délégué à la collation SQL Server car le poste de dev est sous NLS/Windows et la prod sous ICU/Ubuntu) + compositeur normalisé ; un chant **sans compositeur** n'est jamais fusionné (clé unique par `chantId`) | — |
| **Tableau de bord d'administration générale** | 9 indicateurs actionnables, tous à source réelle (D30) : Clients (Total/Actifs/Suspendus/Archivés), Chorales (Total/Brouillon/Publiées/Annulées/Archivées + Inactives depuis 30j), Utilisateurs (Total/Actifs/Invités non activés), Chants (Total/Groupes en doublon), Événements à venir (30j), Anomalie « événements sans structure ». Un 10ᵉ champ, `StockageTotalOctets`, est informatif et volontairement **non actionnable** (pas de liste filtrée associée) — il n'est pas compté dans les « 9 indicateurs ». Aucun indicateur financier (D30 : Stripe/abonnements/impayés sans source) | `AdminDashboardTests` |
| **Trois familles de défauts fermées** | **Tri** : `SortActive`/`SortDirection` étaient déclarés mais lus par aucun service — corrigé par `TriHelper.ApplyTri` (liste blanche par appelant, aucune réflexion, `ThenBy(Id)` pour un départage déterministe). **Pagination non déterministe** : méthodes paginées sans `OrderBy` corrigées avec départage sur l'identifiant. **Statut jamais vérifié** : `PeutEcrireAsync` (`AppartenanceService`) câblé sur 7 services (`ChantService`, `ChoraleService`, `ConsigneService`, `EnregistrementService`, `EvenementService`, `ListeChantsService`, `MembresChoraleService`, `PartitionService`) | — |
| **Projets renommés** | Fichiers projet `ChoraleBackEnd.{Api,Common,Data,Services,Test,ViewModels}.csproj`, solution `ChoraleBackEnd.slnx` — **les dossiers restent `Chorale.*`**. Commande de test : `dotnet test Chorale.Test/ChoraleBackEnd.Test.csproj` | — |

### Frontend — socle partagé et écrans d'administration (chantier du 2026-07-31)

| Sujet | Contenu |
|---|---|
| **Socle partagé** | `ConfirmService` (`sweetalert2`, seul point d'import de la librairie dans l'application — nouveau package npm, seul ajout du chantier), `DataTableComponent<T>`, `SubmitOnceDirective` (anti double-clic, réactivation sur erreur), `FormFieldComponent`, `PageHeaderComponent`, `DataStateComponent` étendu (`skeleton`/`spinner`/`overlay`), `stubIconHttpRequests()` (utilitaire de test, `src/app/testing/icon-http-stub.ts`) |
| **Écrans `/admin`** | Tableau de bord (9 tuiles + bloc purge RGPD indépendant), Clients (liste + fiche), Chorales (liste + fiche à onglets membres/chants/événements en lecture seule), Événements (liste + fiche), Utilisateurs (liste à onglets + fiche + modale de création d'administrateur), Chants (catalogue transverse), Audit (liste, lecture seule) |
| **Écran `/client/:clientId` (« Ma structure »)** | `MaStructureComponent` — onglets Chorales / Plafonds / Responsables. Le mot « Client » n'apparaît dans aucun texte visible de cet écran (le libellé est « structure ») — critère de recette explicite en commentaire. Le `ResponsableClient` ne modifie ni les informations ni les plafonds (`Update`/`ModifierLimites` restent `Admin`-only côté back) ; seule la désignation/le retrait de responsables est une écriture disponible ici |
| **Test front instable corrigé** | `chant-detail.component.spec.ts` : `IconComponent` construit son propre `HttpClient` via `HttpBackend` pour contourner les intercepteurs ; en test, ces requêtes atteignaient `HttpTestingController` de façon asynchrone et faisaient échouer `verify()` par intermittence. Corrigé par un stub local d'`HttpBackend` limité à l'injecteur d'`IconComponent`. Quatre autres specs portaient la même fragilité et ont été corrigées de la même façon |

### Backend — palier Client et cycle de vie événement (2026-07-27/29)

| Lot | Contenu | Vérification |
|---|---|---|
| **Palier `Client`** (D23, `Spec/chorale/10-decisions.md`) | Entité `Client` + `MembreClient` ; rôle `ResponsableClient` scopé client (policy `ClientResponsable`, `clientId` lu dans la route — policy et service lisent la même valeur) ; `ClientController` complet (`Create`, `Update`, `ModifierLimites`, `ChangerStatut`, `ImpactSuspension`, `Responsables` POST/DELETE) | `ChangerStatutClientTests`, `SuspensionClientTests` |
| **Limites de service** | 4 plafonds portés par `Client` (chorales, membres, quota de stockage, taille max par fichier), appliqués à l'écriture par `ServiceLimitService` — seul point où ils sont vérifiés | `ServiceLimitServiceTests` |
| **Suspension propagée** | Un client `Suspendu`/`Archivé` coupe l'accès à toutes ses chorales, vérifié à deux endroits : `EspaceRoleResolverService` (résolution des rôles) et `AppartenanceService` (lecture de contenu) | — |
| **Renommage `Dossier` → `ListeChants`** | Entité, service, contrôleur, route (`api/listes-chants`) sur les deux surfaces ; migration écrite à la main (`RenameTable`/`RenameColumn`, pas de `Drop`/`Create`) pour ne pas perdre de données | commit `449440c` |
| **Événement** | Champs `Lieu` (obligatoire à la publication) et `Statut` stockés ; `Termine` volontairement **non stocké**, calculé par `EvenementEtatHelper.StatutEffectif` à partir de `Statut` + dates ; endpoint `POST /api/evenements/ChangerStatut` + table de transitions (`TransitionAutorisee`) ; `CK_Evenement_PublieAvecLieu` en base | `EvenementEtatHelperTests` |
| **Consigne** | ~~4 portées (Chorale / Voix / Chant / Evenement)~~ → **cible unique : le chant** depuis `10-D43` et la migration `InstructionsSongScopeOnly`. Colonnes `Scope`/`ChoirId`/`EventId` et `CK_Instruction_Scope` supprimées, `SongId` obligatoire, `VoicePart` conservé comme restriction de pupitre *à l'intérieur* du chant. `CK_Instruction_Published` inchangée ; `InstructionController` | `InstructionServiceTests` |

### Backend — purge de dette du 2026-07-29 (commit `8d2ea0d`)

| Sujet | Contenu | Vérification |
|---|---|---|
| **Ordinaux d'enum verrouillés** | Membres numérotés explicitement (`= 0`, `= 1`…) avec bandeau d'avertissement par fichier ; `EnumOrdinauxTests` fait échouer tout réordonnancement. Enums stockés et sérialisés en **entier** (EF sans `HasConversion`, pas de `StringEnumConverter`) : l'ordinal est une donnée persistée, dupliquée au front et écrite en dur dans des filtres d'index et des `CHECK` | `EnumOrdinauxTests` |
| **Écriture des chants conforme à la matrice `02`** | Créer/modifier réservé au Responsable (`EnsureResponsableAsync`) ; archiver ouvert aussi au chef de pupitre pour les chants liés à sa voix (`EstChefDeVoixConcerneeAsync`) | — |
| **Appartenance unifiée** | `AppartenanceService` remplace 7 contrôles divergents. 3 conditions systématiques : appartenance à la chorale + statut membre `Actif` + client `Actif` | `AppartenanceServiceTests` |
| **Brouillons d'événement invisibles** | `GetPaged` filtre les `Brouillon`/`Archive` pour qui n'est ni Responsable, ni Organisateur, ni créateur ; `GetById` répond `404` (pas `403`) sur un contenu non publié | — |
| **Logs — niveau selon le statut** | 4xx en `Warning` sans stack trace ; 5xx en `Error` avec pile filtrée au namespace `Chorale` ; `UserId` présent via `LogService.WithContext` | `ExceptionMiddleware.cs` |
| **ViewModels séparés des profils AutoMapper** | 17 fichiers : DTO et `{ViewModel}MappingProfile : Profile` séparés — les internes d'AutoMapper ne partent plus dans le JSON | 26/26 `CreateMap` conservés |
| **Quota de stockage** | `IgnoreQueryFilters` sur le calcul de consommation : un contenu soft-delete occupe toujours le disque en V1 | `ServiceLimitServiceTests` |
| **Dates UTC de bout en bout** | Convertisseur EF global : écriture ramenée en UTC, lecture reposée en `Kind=Utc` | `ChoraleDbContext.cs` |
| **Secrets hors dépôt** | `JWTToken:Secret`, `Seed:Admin:Password`, `Seed:Demo:Password`, `Analytics:IpSalt`, `AutomapperLicense` hors des fichiers versionnés | `git check-ignore` couvre les 3 `appsettings.{Env}.json` |
| **Filtres de requête parent/enfant** | Alignement sur `ChantVoix`, `ListeChantsChant`, `MembreEspaceRole`, `MembrePupitre`, `RefreshToken`, `Chorale` (son propre `IsDeleted`) | — |
| **11ᵉ migration** | `DurcitContraintesMetier` : `CK_Evenement_PublieAvecLieu`, `CK_MembreClient_RoleClient`, index `Consignes` filtré | — |
| **Seed entièrement en configuration** | Section `Seed` d'`appsettings.json` : `Seed:Admin` (super admin, tous environnements) et `Seed:Demo` (2 clients, 3 espaces, 6 comptes, idempotent — actif en `Development`, et en `Staging` si `Seed:Demo:EnabledInStaging = true`, jamais en `Production`). Seuls les deux mots de passe sortent du dépôt | `SeedDatabase.cs`, `SeedOptions.cs` |

### Tests

> Compteurs revérifiés le 2026-07-31 par exécution directe (`dotnet test` / `npm test`) —
> les chiffres ci-dessous ne reflètent pas nécessairement ceux annoncés au moment de la
> livraison des chantiers listés plus haut, du travail ayant continué depuis.

| Suite | Résultat | Commande |
|---|---|---|
| Backend (NUnit + EF InMemory) | **564 tests, 0 échec** | `dotnet test Chorale.Test/ChoraleBackEnd.Test.csproj` |
| Front (Vitest) | **215 tests, 0 échec** | `npm test` dans `ChoralFront/` |

### Outillage agents
8 agents de cadrage versionnés dans `.claude/agents/` du dépôt (`design-system-guardian`, `devops-release`, `domain-data-architect`, `product-owner`, `project-manager`, `qa-strategist`, `security-auditor`, `ux-ui-designer`). `dotnet-api-architect`, `angular-architect`, `review-validator`, `project-orchestrator` (niveau utilisateur, `~/.claude/agents`) disponibles sur ce poste.

---

## ⏳ À faire

### Frontend — contenu des zones `/gestion` et `/moi`
La **zone** `/moi` (« Espace membre ») existe : route, guard d'authentification, redirection post-connexion. Son **contenu** est un unique écran de substitution (`PlaceholderComponent`) — aucune des fonctions prévues par D22 (consultation + participation : voir événements, chants, enregistrements, consignes, présence) n'est construite.

Dans `/gestion/:espaceId`, deux routes restent des substituts : `MemberDetail` (fiche membre) et `Activity`. La route `Instructions` n'existe plus : les consignes se gèrent dans l'écran du chant (`10-D43`). Les écrans événement (liste/détail/formulaire, publication/annulation/archivage) sont construits, mais **l'UI d'invitation** d'un participant à un événement n'existe pas — l'API (`EventParticipantService`) n'a aucune surface.

`Activity` n'a par ailleurs **aucune source de données** : `AdminAuditLog` ne porte ni `ChoirId` ni `SpaceId`, et le seul endpoint (`api/admin-audit`) est réservé au claim `Admin` et alimenté uniquement par les accès support hors périmètre. Construire cet écran suppose d'abord un journal d'activité scopé par espace (nouvelle entité ou colonne + points d'écriture dans les services), pas seulement une page.

### Frontend — navigation unifiée
« Mon accueil » agrégé cross-espaces (D29) et le switcher chorales/événements ne sont pas construits. Un utilisateur avec plusieurs espaces de gestion atterrit sur le premier de la liste (`zone-resolver.ts`, `managementSpaces[0]`), sans écran de sélection.

### Backend — aucun écran d'affectation des membres à une chorale
L'API existe (`ChoirController.AddMember`/`RemoveMember`, `ChoirMembersController.Invite`), aucune UI ne l'expose — y compris dans les nouveaux écrans admin (l'onglet Membres d'`AdminChoirController` est volontairement lecture seule). Le seed de démonstration contourne le problème en dev, pas en usage réel.

### Lot 6 — inscription auto-service (non réalisé)
Aucune route `/inscription`, `/rejoindre` ou `/demarrer`, aucun code de rattachement, aucune demande d'adhésion n'existe côté front ou back. C'est la pièce manquante qui rendrait le statut `Draft` d'une chorale réellement utile (voir la nuance « Création de chorale » ci-dessus) : tant que ce parcours n'existe pas, rien ne peut créer de chorale hors du seed de démonstration ou d'un appel direct à l'API par un `Admin`/`ClientManager`.

### Backend — dette assumée
- **A4 — Contract** *(différé, après une fenêtre de production stable)* : supprimer les colonnes legacy (`SpaceMember.ChoirId` et équivalents une fois tous les usages migrés vers `SpaceId`). Irréversible → pas en plein dev.
- **Purge RGPD** : déclenchement manuel via `POST /api/admin-guest-accounts/PurgeInactive` (pas de scheduler — contrainte projet « pas de worker »), désormais exposé par un bouton sur le tableau de bord admin. La procédure opérationnelle (qui la lance, à quelle fréquence) reste à écrire.
- **Migration de conversion des enums** (historiquement `EnumsEnEntiersEtStatutEvenementEtConsignes`, supprimée avec le reste de l'historique — voir « Base repartie de zéro » ci-dessus) n'avait jamais rencontré de données réelles (base vide au moment de son exécution). Sans objet désormais : plus aucune base pré-conversion n'existe à faire remonter.
- **Filtre `IsActive`/`IsGuestAccount` absent d'`AdminUserController.GetPaged`** : le tableau de bord admin transmet déjà ces paramètres de navigation par anticipation (voir commentaire `dashboard.component.ts`), sans effet réel tant que le filtre serveur n'existe pas.
- **`ClientController.GetPaged` sans filtre** : les tuiles « Clients non démarrés » / « Clients proches d'un plafond » du tableau de bord n'ouvrent donc pas de liste filtrée — seuls des liens directs vers chaque fiche client, à partir des identifiants transmis par le KPI.

### Sécurité / dette assumée
- Partage inter-chorales : hors périmètre, non spécifié.
- Notifications (email/push) : hors périmètre V1.
- Upload et quota de stockage : logique testée unitairement (`ServiceLimitServiceTests`), pas de test bout en bout.
- N+1 résiduels éventuels ailleurs que sur la liste des clients (le seul confirmé et corrigé) — pas d'audit systématique mené.

### ChoraleMobile
Le dossier ne contient aucun code d'application : `CLAUDE.md` seulement. Aucun projet Ionic scaffoldé. Les deux pipelines CI qui s'y trouvaient ont été supprimés lors de la mise en place du déploiement Azure : ils ciblaient App Center (retiré par Microsoft en mars 2025) et un dossier `chorale-mobile/` inexistant.

### Migration — historique réinitialisé
Les 13 migrations historiques (listées ci-dessous à titre d'archive) n'existent plus : le
chantier d'anglicisation du 2026-07-31 (voir « Backend + Front — anglicisation complète du
code » ci-dessus) les a remplacées par une unique migration `InitialCreate`
(`20260731185831_InitialCreate`), générée depuis le modèle anglicisé. Décision assumée :
**aucune reprise de données**, pas de base à préserver au moment du chantier. Un lecteur qui
cherche une migration nommée ci-dessous (ex. `AjouteClientSurEspace`) ne la trouvera donc pas
dans `Chorale.Data/Migrations/` — c'est attendu, pas une perte accidentelle. La base de
développement locale est désormais `ChoraleBdd` (`appsettings.Development.json`, non
versionné).

Anciennes migrations (13, pour mémoire) : `ImportVoixEtPartitions` · `InitialImportVoixPartitionsEvenements` · `ExpandEspaceModel` · `MigrateEspaceModel` · `EvenementsGerablesA2` · `AddEvenementClotureAt` · `DurcissementStockageEtIndexPartition` · `RenommeDossierEnListeChants` · `AjoutePalierClient` · `EnumsEnEntiersEtStatutEvenementEtConsignes` · `DurcitContraintesMetier` · `AjouteClientSurEspace` · `AjouteStatutChorale`

---

## Repères d'architecture

- **Hiérarchie** : `Client` (facturation/quota, rôle `ClientManager`) → `Choir`/`Event` (deux types de `Space`, rôles `Manager`/`SectionLeader`/`Organizer`/`Participant`) → `Song`/`SongList`/`Score`/`Recording`/`Instruction`.
- **Scope requête** : en-tête **`X-Space-Id`** (repli `X-Chorale-Id` accepté pour compatibilité, cf. `SpaceRoleAuthorizationHandler`) ; `clientId` porté par la route pour les endpoints scopés client (`ClientController`, résolution en 3 temps — route, puis ressource existante, puis corps de requête en dernier repli — voir `ClientRoleAuthorizationHandler`) ; `Admin` reste un claim JWT global.
- **Rôles** (`UserRoleEnum`, 7 valeurs, ordinal persisté) : `Admin` = 0, `SectionLeader` = 1, `Singer` = 2, `Manager` = 3, `Organizer` = 4, `Participant` = 5, `ClientManager` = 6.
- **Zones web** (4, D22 étendue) : `/admin`, `/client/:clientId`, `/gestion/:espaceId`, `/moi` — aiguillage dans `zone-resolver.ts`, la zone dépend du couple (utilisateur, espace actif).
- **Autorisation contenu** : `MembershipService` est la source unique pour « cet utilisateur peut-il lire cette chorale » (appartenance + statut Active + client Active). Ne pas réintroduire de contrôle ad hoc dans un service métier.
- **Modèle événement** : `Status` est la décision humaine (`Draft`/`Published`/`Cancelled`/`Archived`) ; `EffectiveStatus` (dont `Finished`) est calculé à la volée par `EventStateHelper`, jamais stocké. Le rattachement `ChoirId` se décide exclusivement à la création et ne peut plus être modifié ensuite (`EventStateHelper.IsChoirIdChangeRequested`, appliqué dans `EventService.UpdateAsync`).
- **Modèle chorale** : `Status` (`Draft`/`Published`/`Cancelled`/`Archived`, `ChoirStateHelper.IsTransitionAllowed`) est un enum distinct de celui de l'événement — pas d'`EffectiveStatus` pour la chorale (pas de date de fin).
- Détail : `docs/Architecture-web-et-roles.md`, décisions `Spec/chorale/10-decisions.md`.
