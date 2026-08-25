# ChoraleHelper — Instructions agents

## Regle universelle : tout le code est en ANGLAIS

**Tout identifiant est en anglais, sans exception.** Classes, methodes, proprietes, variables,
parametres, enums et leurs valeurs, noms de fichiers et de dossiers, tables et colonnes de base
de donnees, routes d'API, cles de configuration, noms de tests.

**Seul le texte destine a l'utilisateur final reste en francais** : libelles d'ecran, messages
d'erreur affiches, contenu des emails, documentation (`Spec/`, `docs/`, `README`), et les
commentaires de code.

### Pourquoi cette regle est en tete de fichier

Une derive s'est produite : plusieurs agents ont ecrit endpoints, tables, entites et variables
en francais. Elle s'auto-entretient — chaque agent s'aligne sur ce qu'il lit dans le code
existant, donc une seule occurrence en contamine toutes les suivantes. **Ne jamais prendre le
code environnant comme reference de nommage : prendre cette regle.**

Si du code francais subsiste dans une zone que tu modifies, ne le renomme pas au passage (un
renommage non demande casse les contrats API et les migrations) : signale-le dans ton
recapitulatif et laisse l'utilisateur arbitrer.

---

## Regle universelle : preseance des fichiers d'instructions

Quatre fichiers **versionnes** portent les instructions de ce depot :

| Fichier | Perimetre | Contenu |
|---|---|---|
| `.Codex/AGENTS.md` (ce fichier) | tout le depot | protocole de travail, checklist qualite, routage agents, regles transverses |
| `ChoraleBack/AGENTS.md` | `ChoraleBack/` | specifique .NET : conventions de test NUnit, enums, prompt d'audit |
| `ChoralFront/AGENTS.md` | `ChoralFront/` | specifique Angular web : structure, tokens, zones, contrat `/auth/Me` |
| `ChoraleMobile/AGENTS.md` | `ChoraleMobile/` | specifique Ionic Angular : pages, onglets, Capacitor |

1. **Ce fichier est autosuffisant.** Un lecteur qui n'a que lui doit pouvoir travailler.
   Corollaire : **aucun chemin absolu de poste** (`C:\Users\...`) dans un fichier versionne.
   Une definition d'agent, un profil, un outil local se **nomment**, ne se localisent pas.
2. **La deduplication ne se fait qu'entre ces trois fichiers.** Le `AGENTS.md` global
   (`~/.Codex/AGENTS.md`) est local a un poste : un autre ordinateur, un agent cloud ou un
   subagent ne le chargent pas. Le recouvrement avec lui est **volontaire** — ne jamais le
   « corriger » en renvoyant au global, ni alleger ce fichier au motif que le global le repete.
   Une regle qui n'existerait que la-bas rend le depot inutilisable ailleurs.
3. **Un fichier de surface ne redit jamais une regle de ce fichier.** Il porte le specifique de
   sa surface et declare ses ecarts dans une table « Derogations ». Un ecart non declare
   n'existe pas.
4. Contradiction entre ce fichier et un fichier de surface, hors derogation declaree : **ce
   fichier gagne**, et la contradiction se corrige — elle ne s'arbitre pas au cas par cas.

---

## Regle universelle : plan avant code

Pour **toute tache impliquant des changements de code**, sans exception et sans attendre que
l'utilisateur le demande :

**Phase 1 — Plan uniquement** (prefixer la reponse de `[PHASE 1 — PLAN]`)
- Identifier les fichiers touches (controllers, services, ViewModels, composants, pages)
- Lister les changements prevus fichier par fichier
- Indiquer si des tests back sont necessaires (voir regle tests)
- Challenger le plan de maniere rigoureuse avant de le presenter : cas limites, effets de bord, consequences sur le reste du code. Le plan doit avoir le meme niveau de detail qu'un plan produit en Plan Mode. Verifier l'alignement avec l'architecture definie par les agents `dotnet-api-architect` / `angular-architect` (definitions locales au poste, hors depot : ne jamais citer leur chemin ici — voir la regle de preseance).
- Passer le plan a la checklist qualite (C1 → C12 ci-dessous) et le corriger AVANT de le presenter. Pour tout symbole modifie : lister ses appelants dans le plan (C5).
- S'arreter. Attendre la confirmation explicite de l'utilisateur avant toute generation.

**Phase 2 — Execution** (prefixer de `[PHASE 2 — EXECUTION]`, uniquement apres "ok", "go", "continue")
- Generer le code depuis le plan valide
- Emettre une ligne de progression a chaque etape majeure :
  `[1/N] Controllers...`, `[2/N] Services...`, etc.
- Avant d'annoncer la fin : rendre le tableau de verification de la checklist qualite
  (`critere / statut / preuve`). Pas de livraison annoncee sans ce tableau.

**Ne jamais commencer a ecrire du code sans avoir presente et fait valider le plan.**
L'agent decompose de lui-meme — l'utilisateur ne doit pas le demander.

**Exceptions — repondre directement sans Phase 1 :**
- Mise a jour ou correction du plan deja presente dans la conversation
  ("ajoute X au plan", "retire Y", "mets a jour le plan")
  → Modifier le plan directement dans la reponse. Ne pas aller chercher dans le code.
- Question sur le plan en cours ("pourquoi ce fichier ?", "c'est quoi l'impact ?")
  → Repondre depuis le contexte de la conversation uniquement.
- Tache purement conversationnelle, explicative ou de lecture seule.

---

## Regle universelle : checklist qualite (plan ET livraison)

Declinaison ChoraleHelper des « Criteres de qualite » du AGENTS.md global. Le recouvrement avec
le global est **volontaire** : ce fichier est versionne, le global est local a un poste. Sur une
autre machine, en agent cloud, ou pour un subagent qui ne charge que le AGENTS.md projet, cette
table est la seule source. Ne pas la reduire par renvoi au global.

Elle s'applique **deux fois**, sans que l'utilisateur ait a le demander :

- **En Phase 1** — challenger le plan contre chaque critere AVANT de le presenter. Un plan qui
  viole un critere se corrige avant presentation, pas apres.
- **En fin de Phase 2** — verifier sur le code reellement produit et rendre un tableau
  `critere / statut / preuve` (fichier:ligne, ou sortie de commande). Un critere non verifiable
  en l'etat se declare `non verifie`, jamais `OK`.

Elle s'applique aussi a **toute correction**, meme d'une ligne, meme sans Phase 1 formelle.

| # | Critere | Ce qu'on verifie |
|---|---|---|
| C1 | Aucun code mort | Rien d'ajoute qui ne soit appele. Ce que le changement remplace est supprime, pas laisse a cote (jamais un `XxxV2` cohabitant avec `Xxx`). Imports, methodes, champs, routes, fichiers devenus inutiles : retires |
| C2 | Aucune duplication | La logique existe a un seul endroit. Avant d'ecrire, chercher si un service, un helper, un pipe ou un composant fait deja le travail. Un copier-coller adapte est un refus |
| C3 | Aucun god fichier | Une responsabilite par fichier / classe / composant. Un service qui grossit se decoupe par domaine ; un composant qui cumule etat, appels HTTP et presentation se scinde. Un fichier deja trop gros qu'on touche sans le decouper doit etre signale |
| C4 | Commentaires utiles en code review | Uniquement les 4 cas de la regle commentaires globale : fonction importante, concept metier, logique particuliere, choix technique ecarte. Aucune paraphrase du code, aucun commentaire pose par symetrie |
| C5 | Appelants alignes (rupture de contrat) | Tout symbole modifie — signature, comportement, DTO, contrat API back↔front, valeur d'enum — a la **liste exhaustive de ses appelants dans le plan**, et a la livraison la **preuve** que chacun est aligne. C'est le defaut qui casse le front silencieusement : le back compile, le front non |
| C6 | Pas de logique metier hors de sa couche | Aucune verite metier dans un controller (thin), un ViewModel, un profil AutoMapper ou le front. Un service reste dans son domaine (`AuthServices`, `ChoirServices`, `ClientServices`, `OnboardingServices`, `UserServices`) : il ne persiste pas et n'arbitre pas les regles d'un autre domaine |
| C7 | Chemins d'erreur et cas nuls traites | Aucun `catch` avale, aucun `null` propage sans decision explicite, aucune exception qui remonte en 500 non intentionnel. Cote front : etat d'erreur et etat vide traites, pas seulement le cas passant |
| C8 | Aucun magic number | Seuils, durees, tailles de page, tolerances : nommes et localises (constante, enum, configuration), jamais disperses en litteraux. Deux endroits qui codent la meme valeur en dur divergent tot ou tard |
| C9 | Aucune regression de perf | Pas de N+1 EF, pas de requete dans une boucle, pas de `ToList()` avant filtrage, pas de boucle non bornee, pas de chargement complet la ou `PagedListViewModel<T>` existe |
| C10 | Migration EF signalee, jamais auto-generee | Question de livraison explicite : « ce changement touche-t-il une entite ou le `DbContext` ? ». Si oui : annoncer la migration necessaire et s'arreter. Ne jamais lancer `dotnet ef migrations add` sans instruction |
| C11 | Isolation chorale et autorisation | Toute nouvelle route et toute nouvelle requete filtrent par chorale active et par role. Aucun identifiant venu du client n'est utilise sans verifier que l'appelant y a droit (IDOR). Le soft-delete (`IsDeleted`) est respecte dans les lectures |
| C12 | Preuve d'execution | Back : `dotnet build` et `dotnet test Chorale.Test/ChoraleBackEnd.Test.csproj` verts, sortie citee. Front : build et lint OK. Une livraison sans preuve n'est pas une livraison ; un echec se signale, il ne se contourne pas |

---

## Regle tests

**Tests frontend Angular et Ionic Angular : ne pas creer.**
Ne pas generer de tests Angular/Jasmine/Karma, ne pas les suggerer.

**Tests backend (ChoraleBack) : creer uniquement si la logique est absolument essentielle.**
Un test est justifie si et seulement si :
- il prouve un comportement metier a risque de regression (calcul, regle metier, cas negatif critique)
- l'absence du test laisserait passer une regression silencieuse sans aucun signal

Un test n'est PAS justifie si :
- il teste du plumbing sans logique (getter/setter, mapping trivial, controleurs thin)
- il existe uniquement pour couvrir des lignes de code

En cas de doute : ne pas creer le test.

**Derogation declaree — `ChoralFront/` uniquement.** Les tests unitaires Angular y sont
**autorises**, sous condition de valeur ajoutee reelle : detail operationnel dans
`ChoralFront/AGENTS.md` § Regle tests. Motif : le runner est le builder natif
`@angular/build:unit-test` (moteur Vitest, `angular.json`) — Jasmine et Karma, que vise
l'interdiction ci-dessus, ne sont pas installes dans ce depot. Des fichiers `.spec.ts` existent
et sont maintenus (guards, intercepteurs, services, `zone-resolver`) : ne pas les supprimer.

`ChoraleMobile/` ne beneficie pas de cette derogation.

---

## Routage agents

### Feature full-stack ou perimetre incertain
→ `project-orchestrator`

### Backend seul (.NET)
→ `dotnet-api-architect`
Ne pas pre-collecter les fichiers — passer uniquement la description du changement.
Laisser l'architecte demander ce dont il a besoin.

### Web seul (Angular)
→ `angular-architect`

### Mobile seul (Ionic Angular)
→ `angular-architect`
Preciser dans le prompt : surface mobile Ionic Angular. L'agent applique les conventions Ionic
(IonPage, IonContent, standalone components, routing Ionic, capacitor plugins si besoin).

### Revue / conformite / gap analysis / audit (lecture seule)
→ Traiter directement, sans deleguer a `review-validator`.

### Apres implementation (corrections a router)
→ `review-validator`
Toujours **un seul perimetre a la fois**.
Passer uniquement le perimetre et le contrat API.
Laisser `review-validator` demander les fichiers dont il a besoin.

**La table ci-dessus est la regle, pas une suggestion.** Une tache qui tombe dans un perimetre
qu'elle nomme se delegue a l'agent indique, sans attendre que l'utilisateur le demande : ces
agents ont ete ecrits pour porter les conventions de leur surface, et travailler sans eux les
perd. Traiter directement une tache que la table route est un ecart, pas un raccourci.

**Jamais de dispatch hors de cette table** : pas d'agent improvise pour l'occasion, pas de
fan-out d'agents non prevu, pas de workflow multi-agents sans demande explicite de l'utilisateur.

Si j'ai besoin de contexte supplementaire : une seule question courte.

---

## Architecture

```
ChoraleBack/
  Chorale.Api          -> HTTP delivery uniquement (controllers thin)
  Chorale.Services     -> logique metier
  Chorale.Data         -> EF entities, DbContext, migrations
  Chorale.ViewModels   -> DTOs request/response, profils AutoMapper
  Chorale.Common       -> partage cross-project (garder minimal)
  Chorale.Test         -> tests back (voir regle tests ci-dessus)

ChoralFront/           -> Angular. Les 4 zones de D22/D32 sont implementees : /admin
                          (administration generale), /client/:clientId (« Ma structure »),
                          /management/:spaceId (gestion), /me (espace membre)
ChoraleMobile/         -> Ionic Angular, cible membres et chefs de pupitre (dossier sans
                          code d'application pour l'instant — AGENTS.md + pipelines CI seuls)

ChoralFront/public/icons/  -> SVG Phosphor Icons, SEULE source servie par Angular
                              (angular.json ne declare que `public` en assets).
                              ChoralFront/src/assets/icons/ est un doublon mort,
                              jamais servi et deja desynchronise — ne rien y ajouter.

Spec/
  chorale/             -> spec metier par fichier numerote
  spec-metier-application-chorale.md -> sommaire
```

## Surfaces et technologies

| Surface | Technologie | Cible utilisateur |
|---|---|---|
| API | .NET / ASP.NET Core | — |
| Site de gestion | Angular (standalone components, Signal API) | Responsable de chorale |
| Administration generale | Angular (meme app, module prevu par D22, non implemente) | Operateur interne |
| Application mobile | Ionic Angular (Capacitor), non demarree | Membres, chefs de pupitre |

## Conventions backend

- Tous les services heritent de `BaseService` — classe abstraite **non generique**
  (`Chorale.Services/BaseService.cs`)
- Soft-delete via `IsDeleted`, jamais de DELETE SQL direct
- Pagination obligatoire sur les listes : retourner `PagedListViewModel<T>`
- Un seul perimetre a la fois pour `review-validator`

## Conventions frontend (Angular et Ionic Angular)

- Standalone components partout (pas de NgModule)
- Signal API pour l'etat local (`signal`, `computed`, `effect`)
- Intercepteurs et guards fonctionnels (pas de classes)
- SCSS par composant, design tokens de `Spec/chorale/11-ux-ui.md` via `src/themes/`
- Icones : composant `IconComponent` (`<app-icon name="..." />`), qui charge en inline les SVG
  de `ChoralFront/public/icons/`. **Aucun package npm Phosphor n'est installe** — ne pas en
  ajouter. Les noms de fichiers sont sensibles a la casse en production (serveur Ubuntu).
- Tests : voir la regle tests ci-dessus. Interdits sur `ChoraleMobile/`, autorises sur
  `ChoralFront/` (derogation declaree)

## Conventions mobile (Ionic Angular specifique)

- Pages Ionic : `IonPage` + `IonContent` + `IonHeader` obligatoires
- Navigation : `IonTabs` (5 onglets, voir `12-catalogue-icones.md`)
- Plugins Capacitor privilegies sur les API web pour audio, fichiers, notifications
- Bottom sheet → `IonModal` avec `breakpoints`
- Toast → `IonToast` (durees : succes 3 s, warning 5 s, erreur persist)

## Specs du projet

- Sommaire : `Spec/spec-metier-application-chorale.md`
- UX/UI et tokens : `Spec/chorale/11-ux-ui.md`
- Icones : `Spec/chorale/12-catalogue-icones.md`
- Decisions produit : `Spec/chorale/10-decisions.md`
