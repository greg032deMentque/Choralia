# Instructions Claude Code — ChoralFront

Ce fichier s'applique uniquement à `ChoralFront/` et porte le **spécifique Angular web**. Le
protocole de travail, la checklist qualité C1→C12 et le routage agents vivent dans
`.claude/CLAUDE.md` : ils ne sont pas redits ici (voir sa règle de préséance).

## Dérogations déclarées au `.claude/CLAUDE.md`

| Règle racine | Écart | Raison |
|---|---|---|
| « Tests frontend Angular : ne pas créer » | **Tests unitaires autorisés**, sous condition de valeur ajoutée réelle (§ Règle tests ci-dessous) | Le runner est `@angular/build:unit-test` (moteur Vitest), pas Jasmine/Karma que vise l'interdiction. Des `.spec.ts` maintenus couvrent guards, intercepteurs, services et `zone-resolver` |

Tout écart non listé ici n'existe pas : en cas de contradiction, `.claude/CLAUDE.md` gagne.

## Agent Angular (site de gestion)

Toute création ou modification de code dans `ChoralFront/` passe par l'agent
`angular-architect` (`subagent_type: "angular-architect"`), conformément au routage agents du
`.claude/CLAUDE.md`.

### Protocole — ce qui est propre à cette surface

Les préfixes `[PHASE 1 — PLAN]` / `[PHASE 2 — EXECUTION]` et la checklist qualité C1→C12 sont
définis dans `.claude/CLAUDE.md`. Trois points seulement sont spécifiques ici :

**Ce qu'on passe à l'agent en Phase 1** : le contenu de ce fichier + les specs brutes
pertinentes. L'agent retourne le plan et un `BLOC DE TRANSFERT`, recopié tel quel dans le
prompt de Phase 2.

**Questions de l'agent** : si la Phase 1 contient un bloc `[QUESTIONS POUR L'ORCHESTRATEUR]`,
les présenter avec `AskUserQuestion` — jamais en prose dans la conversation.

**Phase 3 — Audit (automatique après Phase 2)**
Elle **s'ajoute** à la checklist C1→C12, elle ne s'y substitue pas : la checklist reste due
dans tous les cas. Lancer un nouvel agent `review-validator` (jamais un ré-appel
d'`angular-architect`) avec :
```
[AUDIT OWASP] [AUDIT QUALITÉ]
Périmètre : ChoralFront ({feature ou "socle"}, pas de backend touché)
Contrat API (reconstitué depuis ChoraleBack/Chorale.Api/Controllers/{Controller}.cs) :
{liste des routes réelles utilisées}

Fichiers à auditer : {liste complète des fichiers créés en Phase 2}

Contexte des décisions intentionnelles (ne pas signaler comme failles) :
{BLOC DE TRANSFERT copié depuis la Phase 1, incluant AmbiguitiesResolved}
```
Cet agent est distinct du générateur — il repart de zéro et lit les fichiers produits.
Il ne doit pas signaler comme ❌ les décisions documentées dans le BLOC DE TRANSFERT.
Présenter la matrice de conformité à l'utilisateur et attendre sa validation avant
toute correction. Si ❌ critique : router la correction vers `angular-architect` avec le
préfixe `[CORRECTION CIBLÉE — {FeatureName}]` (pas Phase 1/2), puis relancer
`review-validator`. Maximum 2-3 cycles avant de remonter le blocage à l'utilisateur.

---

## Structure de dossiers imposée

Reprendre exactement la structure native de l'agent `angular-architect` — ne pas en inventer
une autre :

```
src/app/
  components/
    auth/
    layout/
    shared/
    {feature}/
  core/
    guards/
    auth.store.ts
  enums/
  interceptor/
  models/
    {domain}-models/
  pipe/
  services/
    {domain}/
src/environments/
src/themes/
```

Path aliases TypeScript obligatoires : `@app/`, `@core/`, `@models/`, `@env/` — jamais de
`../../..`.

## Bootstrap 5 et design tokens

Bootstrap 5 reste utilisé nativement par l'agent (`d-flex`, `gap-*`, `row`/`col-md-*`,
grid, utilitaires en priorité). Les tokens de `Spec/chorale/11-ux-ui.md` (couleurs,
typographie Inter, spacing base 4px, radius, shadows, animations, breakpoints) sont
injectés en surcouche via variables SCSS / thème Bootstrap (`src/themes/`) — appliqués
partout où visible (couleurs de boutons/liens/états, typographie, radius, ombres).
Aucune valeur hexadécimale ou pixel codée en dur dans un composant : toujours passer par
une variable de thème.

## Icônes

Composant standalone `IconComponent` (`<app-icon name="house" />`) qui charge en inline
les SVG de **`ChoralFront/public/icons/`** — seule source servie par Angular (`angular.json`
ne déclare que `public` en assets). `src/assets/icons/` est un doublon mort, jamais servi et
déjà désynchronisé : ne rien y ajouter. Aucun package npm Phosphor pour Angular n'existe — ne
pas en installer. Table de correspondance nom logique → fichier `.svg` centralisée (const ou
enum), source : `Spec/chorale/12-catalogue-icones.md`. Les noms de fichiers sont sensibles à
la casse en production (serveur Ubuntu).

## Interdictions de dérive

- Pas de nouvelle librairie UI (Angular Material, PrimeNG, ng-bootstrap, etc.) sans
  validation explicite de l'utilisateur, mentionnée dans un plan Phase 1.
- Exception validée : `ngx-toastr` est autorisé pour le `ToastService` (décision
  utilisateur du 2026-07-02, socle initial) — durées imposées : succès 3s, warning 5s,
  erreur persistante jusqu'à dismiss, max 3 toasts empilés.
- Pas de state management externe (NgRx, NGXS, Akita) — Signal API uniquement
  (`signal`, `computed`, `effect`), sauf demande explicite future.
- Tests Angular autorisés — dérogation déclarée en tête de fichier, détail en section
  « Règle tests » ci-dessous.
- Respect strict des breakpoints de `11-ux-ui.md` §4 (mobile-sm 320-374, mobile 375-428,
  mobile-lg 429-767, tablet 768-1023, desktop 1024-1279, desktop-lg 1280+) — pas de
  breakpoint ad hoc.
- Sidebar de navigation : 256px fixe, rétractable à icônes seules à partir de `desktop`
  — pas de largeur arbitraire.
- Aucun nouveau package npm sans mention explicite dans un plan Phase 1 et validation
  utilisateur.

## Règle tests (dérogation déclarée — voir la table en tête de fichier)

Les tests unitaires Angular sont autorisés ici, sous conditions strictes de valeur ajoutée.
Un test n'est utile que s'il valide qu'une évolution ou une correction fonctionne réellement
et protège contre une régression future — pas pour faire du chiffre de couverture.

Runner : builder natif Angular 21 `@angular/build:unit-test` (moteur Vitest — voir
`angular.json` et `package.json`, devDependency `vitest`). Pas de Jasmine/Karma dans ce
projet — c'est ce qui rend la dérogation légitime plutôt qu'arbitraire.

### À tester en priorité
- Logique métier non triviale : services (`services/{domain}/`), guards
  (`core/guards/`), intercepteurs (`interceptor/`), pipes personnalisées
- `computed`/`signal` porteurs de règles métier (permissions selon rôle/chorale active,
  agrégations, formatage conditionnel)
- Cas limites et cas d'erreur qui casseraient silencieusement une fonctionnalité sans
  aucun signal d'échec (token expiré, 401/403, rôle manquant, chorale non sélectionnée,
  pagination vide ou dernière page)
- Tout bug corrigé : un test qui reproduit le bug avant le fix et le valide après

### À ne pas tester
- Composants purement présentationnels sans logique (binding de template seul)
- Getters/setters triviaux, mapping 1:1 sans transformation
- Tests écrits uniquement pour faire monter la couverture

### Exigences de qualité
- Un test valide un comportement observable, pas un détail d'implémentation qui changera
  au premier refactor
- Un cas testé par test, message d'échec explicite
- En cas de doute sur la pertinence d'un test : ne pas le créer

### Exécution obligatoire avant toute livraison
- Exécuter `npm test` dans `ChoralFront/` après chaque modification touchant un fichier
  testé ou l'une de ses dépendances directes
- Une livraison n'est valide que si **tous** les tests sont verts, y compris les tests
  préexistants impactés indirectement par le changement
- Si un test existant échoue à cause du changement : corriger le test s'il est devenu
  obsolète (changement de comportement voulu), sinon corriger le code — ne jamais
  supprimer ou ignorer un test pour faire passer la suite au vert sans justification
  explicite à l'utilisateur

## Authentification

- Session : 8 heures, avec option "remember me" (flag UI côté login, sans effet sur le
  mécanisme de stockage — le token reste toujours en `sessionStorage`).
- Contrat API réel (`ChoraleBack/Chorale.Api/Controllers/AuthController.cs`) :
  `POST /api/auth/Login`, `POST /api/auth/RefreshToken`, `POST /api/auth/Logout`,
  `POST /api/auth/ForgotPassword`, `POST /api/auth/ResetPassword`, `GET /api/auth/Me`.
- `GET /api/auth/Me` ne remplit jamais `AccessToken`/`RefreshToken` (restent `null`) —
  ces champs ne sont peuplés que par les réponses de `Login` et `RefreshToken`.
- `TokenInterceptor` (attache le Bearer token, refresh automatique avant expiry) +
  `ApiErrorInterceptor` (déconnexion sur 401) — noms exacts à conserver, ce sont les
  patterns repris tels quels côté `ChoraleMobile`.
- Token stocké en `sessionStorage` (ou storage équivalent non persistant côté navigateur
  fermé) via un `StorageService` dédié — jamais en `localStorage` brut (OWASP A02).

## Zones, espace actif, rôles et guards

Source unique de vérité : `Spec/chorale/02-roles-droits-et-visibilite.md`. **Les rôles sont
scopés par espace (chorale ou événement) ou par client, jamais globaux — sauf `Admin`.**

### Les 4 zones

L'application est découpée en **quatre** zones, avec redirection post-connexion :

| Zone | Route | Habilitation | Scope transmis |
|---|---|---|---|
| Administration | `/admin` | claim global `Admin` | aucun |
| Ma structure | `/client/:clientId` | `ClientManager` | `clientId` dans la route |
| Gestion | `/management/:spaceId` | `Manager` / `SectionLeader` / `Organizer` sur l'espace actif | `X-Space-Id` |
| Espace membre | `/me` | appartenance simple | `X-Space-Id` |

**La zone est une propriété du couple (utilisateur, espace actif), pas de l'utilisateur
seul.** Une même personne peut être `Manager` d'une chorale, simple `Singer` d'une
autre et `Participant` d'un événement : changer d'espace actif change de zone. La règle
d'aiguillage est centralisée dans `core/zone-resolver.ts` — un seul endroit, ne pas la
dupliquer dans un guard ou un composant.

Le mot « Client » ne doit **jamais** apparaître dans un template hors de `/admin` ; le
libellé utilisateur de la zone `/client` est « Ma structure ».

### Contrat `GET /api/auth/Me`

- `Roles` : claims JWT globaux — en pratique uniquement `Admin`.
- `SpaceRoles: List<SpaceRoleAssignmentViewModel>` (`SpaceId`, `Name`, `SpaceType`,
  `Roles: string[]`, `ClientId`, `ChoraleId`, `PrimaryVoicePart`) — **le contrat courant**.
  Couvre chorales ET événements. `SpaceType` est un entier (`0` = Chorale, `1` = Evenement).
  `ChoraleId` est nul pour une chorale (l'espace *est* la chorale) et pour un événement
  autonome ; il porte la chorale porteuse pour un événement rattaché. `PrimaryVoicePart`
  (entier nullable, `VoicePartEnum`) porte la voix principale du membre sur cet espace :
  toujours nul pour un espace de type Evenement, et nul pour un choriste sans voix
  assignée sur une chorale.
- `ClientRoles: List<ClientRoleAssignmentViewModel>` (`ClientId`, `Name`, `Roles`) — source
  de la zone `/client`. Toujours une liste, jamais `null`.
- `ChoirRoles` : **retiré du contrat**. Ce champ n'était qu'un sous-ensemble de
  `SpaceRoles` filtré sur `SpaceType === Chorale` ; le serveur ne l'émet plus et
  `IAuthenticatedUser` ne le déclare plus. Utiliser `SpaceRoles`.
- Les espaces dont le client n'est pas `Actif` sont **déjà exclus** par le serveur : ne pas
  réimplémenter ce filtre côté front.

### Espace actif

Aucune notion d'espace actif côté serveur : le front en sélectionne un dans `SpaceRoles`
(`AuthStore.activeSpaceId`) et transmet son identifiant via le header **`X-Space-Id`**
(repli `X-Chorale-Id` accepté côté back pour compatibilité — ne pas s'y fier pour du code
neuf), posé par `TokenInterceptor`. Header absent ou invalide sur une route protégée par
une policy scopée → `403 Forbidden`. La zone `/admin` n'envoie aucun header de scope.

Un utilisateur **sans aucun rattachement** est une situation normale et doit atterrir sur
un écran dédié — jamais une page blanche ni une boucle de `403`.

| Rôle | Guard |
|---|---|
| Membre / Participant | `authGuard` (implicite à toute route protégée) |
| Chef de pupitre | `spaceRoleGuard` — vérifie le rôle sur l'**espace actif**, jamais « sur un espace quelconque » |
| Responsable | `spaceRoleGuard` |
| Organisateur | `spaceRoleGuard` (espace de type Evenement) |
| Responsable client | `clientRoleGuard` — vérifie le rattachement au `clientId` de la route |
| Admin général | `adminGuard` — claim JWT global, **web uniquement** |

Le front décide seulement **quoi afficher** ; le serveur décide **quoi autoriser**. Aucune
règle de permission ne doit être réécrite côté front.

## Specs de référence

- `Spec/chorale/11-ux-ui.md` — design tokens, layout, breakpoints, accessibilité
- `Spec/chorale/02-roles-droits-et-visibilite.md` — rôles, matrice d'actions
- `Spec/chorale/07-ecrans-site-de-gestion.md` — écrans du site de gestion (extraits
  seulement, selon l'itération en cours — ne pas tout implémenter d'un coup)
- `Spec/chorale/12-catalogue-icones.md` — catalogue des icônes

## Cohérence avec ChoraleMobile

Ce projet sert de référence pour `ChoraleMobile/CLAUDE.md` : structure de dossiers,
noms de patterns (`AuthStore`, `TokenInterceptor`, `ApiErrorInterceptor`), tokens SCSS.
Toute divergence de pattern entre les deux surfaces doit être documentée explicitement
dans le CLAUDE.md correspondant, jamais silencieuse.

## Convention — enums

Les enums TypeScript (`src/app/enums/`) sont numériques et alignés ordinal pour ordinal sur
les enums back (`Chorale.Common.Enums`, stockés et sérialisés en entier — jamais de string,
jamais de `HasConversion` côté EF). Ne jamais réordonner une valeur existante ni l'insérer au
milieu ; toute valeur ajoutée côté back se reflète en fin d'enum front, avec le même entier
des deux côtés. Un commentaire d'avertissement en tête de fichier (voir
`evenement-statut.enum.ts`, `evenement-etat-effectif.enum.ts`) est la convention à reprendre
pour tout nouvel enum. Les fonctions `xxxFromString` ne se justifient que pour un champ que
le back transmet réellement en chaîne (ex. les rôles issus des claims JWT) — ne pas en écrire
pour un champ déjà entier côté back.
