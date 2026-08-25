# Instructions Claude Code — ChoraleMobile

Ce fichier s'applique uniquement à `ChoraleMobile/` et porte le **spécifique Ionic Angular**.
Le protocole de travail, la checklist qualité C1→C12 et le routage agents vivent dans
`.claude/CLAUDE.md` : ils ne sont pas redits ici (voir sa règle de préséance).

**Aucune dérogation déclarée au `.claude/CLAUDE.md`** — en particulier, la dérogation « tests
Angular autorisés » de `ChoralFront/` ne s'étend pas à cette surface.

## Agent Angular (application mobile Ionic)

Toute création ou modification de code dans `ChoraleMobile/` passe par l'agent
`angular-architect` (`subagent_type: "angular-architect"`), conformément au routage agents du
`.claude/CLAUDE.md`, en précisant toujours dans le prompt : **"surface mobile Ionic Angular"**
— sans quoi l'agent applique les patterns web au lieu d'IonPage/IonContent, du routing Ionic
et de Capacitor.

### Protocole — ce qui est propre à cette surface

Les préfixes `[PHASE 1 — PLAN]` / `[PHASE 2 — EXECUTION]` et la checklist qualité C1→C12 sont
définis dans `.claude/CLAUDE.md`. Trois points seulement sont spécifiques ici :

**Ce qu'on passe à l'agent en Phase 1** : le contenu de ce fichier + les specs brutes
pertinentes + un rappel explicite des patterns déjà établis côté `ChoralFront` (`AuthStore`,
`TokenInterceptor`, `ApiErrorInterceptor`, structure `models/{domain}-models/`, tokens SCSS).
L'agent retourne le plan et un `BLOC DE TRANSFERT`, recopié tel quel dans le prompt de Phase 2.

**Questions de l'agent** : si la Phase 1 contient un bloc `[QUESTIONS POUR L'ORCHESTRATEUR]`,
les présenter avec `AskUserQuestion` — jamais en prose dans la conversation.

**Phase 3 — Audit (automatique après Phase 2)**
Elle **s'ajoute** à la checklist C1→C12, elle ne s'y substitue pas. Lancer un nouvel agent
`review-validator` (jamais un ré-appel d'`angular-architect`) avec :
```
[AUDIT OWASP] [AUDIT QUALITÉ]
Périmètre : ChoraleMobile ({feature ou "socle"}, pas de backend touché)
Contrat API (reconstitué depuis ChoraleBack/Chorale.Api/Controllers/{Controller}.cs) :
{liste des routes réelles utilisées}

Fichiers à auditer : {liste complète des fichiers créés en Phase 2}

Contexte des décisions intentionnelles (ne pas signaler comme failles) :
{BLOC DE TRANSFERT copié depuis la Phase 1, incluant AmbiguitiesResolved}
```
Vérifier explicitement, à chaque audit, l'absence de toute route ou guard "Admin général"
côté mobile — c'est un point de non-régression cross-surface (voir section Rôles ci-dessous).
Cet agent est distinct du générateur — il repart de zéro et lit les fichiers produits.
Il ne doit pas signaler comme ❌ les décisions documentées dans le BLOC DE TRANSFERT.
Présenter la matrice de conformité à l'utilisateur et attendre sa validation avant
toute correction. Si ❌ critique : router la correction vers `angular-architect` avec le
préfixe `[CORRECTION CIBLÉE — {FeatureName}]` (pas Phase 1/2), puis relancer
`review-validator`. Maximum 2-3 cycles avant de remonter le blocage à l'utilisateur.

---

## Structure de dossiers imposée

Même structure que `ChoralFront` (voir `ChoralFront/CLAUDE.md`), adaptée Ionic :

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
src/theme/
```

Path aliases TypeScript obligatoires : `@app/`, `@core/`, `@models/`, `@env/` — jamais de
`../../..`.

## Design tokens (pas de Bootstrap)

Ionic a son propre système de grille et d'utilitaires CSS — ne pas ajouter Bootstrap 5
côté mobile. Les tokens de `Spec/chorale/11-ux-ui.md` (couleurs, typographie Inter,
spacing base 4px, radius, shadows, animations) sont portés dans `src/theme/variables.scss`
(variables Ionic CSS `--ion-color-*` mappées sur les tokens). Aucune valeur hexadécimale ou
pixel codée en dur dans un composant. Les tokens SCSS prévoient dès le socle une variante
`dark` pour `prefers-color-scheme` (implémentation visuelle complète hors scope de cette
itération, mais la structure doit exister).

## Icônes

Composant standalone `IconComponent` (`<app-icon name="house" />`), identique au pattern
`ChoralFront`, chargeant en inline les SVG depuis le dossier `public/icons/` de l'application
— jamais `src/assets/icons/`, qui est un doublon mort côté `ChoralFront` et ne doit pas être
reproduit ici. Aucun package npm Phosphor (inexistant pour Angular/Ionic). Table de
correspondance nom → fichier `.svg`, source : `Spec/chorale/12-catalogue-icones.md`. Les noms
de fichiers sont sensibles à la casse en production (serveur Ubuntu).

## Interdictions de dérive

- Pas de nouvelle librairie UI en plus d'Ionic (Angular Material, PrimeNG, etc.) sans
  validation explicite de l'utilisateur, mentionnée dans un plan Phase 1.
- Pas de state management externe (NgRx, NGXS, Akita) — Signal API uniquement.
- Pas de tests Angular (règle du `.claude/CLAUDE.md` — la dérogation `ChoralFront/` ne
  s'étend pas à cette surface).
- Respect strict des breakpoints de `11-ux-ui.md` §4 — pas de breakpoint ad hoc.
- Aucun nouveau package npm sans mention explicite dans un plan Phase 1 et validation
  utilisateur — en particulier tout plugin Capacitor natif avancé (enregistrement audio,
  notifications push) est **hors scope** tant que non explicitement validé. Le socle ne
  couvre que le scaffold Capacitor de base (`@capacitor/core` + `android`/`ios` si
  nécessaire pour build).

## Spécificités Ionic obligatoires

- `IonPage` + `IonContent` + `IonHeader` obligatoires sur toute page.
- `IonTabs` : exactement 5 onglets fixes, ordre imposé par `11-ux-ui.md` §5.1 et
  `12-catalogue-icones.md` : Accueil (House), Chants (MusicNotes), Événements (Calendar),
  Mon pupitre (Microphone), Compte (User).
- Sélecteur de chorale accessible depuis n'importe quel onglet (header, nom chorale +
  chevron) — composant partagé `ChoirSelectorComponent` (nom anglais, règle #1 du
  `.claude/CLAUDE.md`).
- Bottom sheet → `IonModal` avec `breakpoints`, poignée de drag visible, swipe-to-close.
- Toast → `IonToast` (durées imposées : succès 3s, warning 5s, erreur persistante jusqu'à
  dismiss manuel).
- Transitions imposées (§6.5 de `11-ux-ui.md`) : fade 100ms au changement d'onglet, slide
  depuis la droite 250ms pour une vue détail, slide depuis le bas 300ms pour un bottom sheet.
- Plugins Capacitor privilégiés sur les API web quand une fonctionnalité native équivalente
  existe (audio, fichiers, notifications) — mais voir "Interdictions de dérive" ci-dessus
  pour le périmètre autorisé dans le socle.

## Authentification

- Session : 30 jours, pas d'option "remember me" (déjà la durée par défaut sur mobile).
- Contrat API réel identique à `ChoralFront`
  (`ChoraleBack/Chorale.Api/Controllers/AuthController.cs`) :
  `POST /api/auth/Login`, `POST /api/auth/RefreshToken`, `POST /api/auth/Logout`,
  `POST /api/auth/ForgotPassword`, `POST /api/auth/ResetPassword`, `GET /api/auth/Me`.
- `GET /api/auth/Me` ne remplit jamais `AccessToken`/`RefreshToken` (restent `null`) —
  ces champs ne sont peuplés que par les réponses de `Login` et `RefreshToken`.
- `TokenInterceptor` + `ApiErrorInterceptor` — mêmes noms et responsabilités que côté
  `ChoralFront`, ne pas réinventer un pattern différent.

## Rôles, espace actif et guards

Source unique de vérité : `Spec/chorale/02-roles-droits-et-visibilite.md`. **Les rôles sont
scopés par espace (chorale ou événement) ou par client, jamais globaux — sauf `Admin`.**

### Contrat `GET /api/auth/Me` (identique à `ChoralFront`)

- `Roles` : claims JWT globaux — en pratique uniquement `Admin`.
- `SpaceRoles: List<SpaceRoleAssignmentViewModel>` (`SpaceId`, `Name`, `SpaceType`,
  `Roles: string[]`, `ClientId`, `ChoraleId`). `SpaceType` est un entier (`0` = chorale,
  `1` = événement).
- `ClientRoles: List<ClientRoleAssignmentViewModel>` — source de la zone web « Ma structure ».
  **Sans usage côté mobile** : cette zone est réservée au web (`02` §121).
- `ChoraleRoles` / `ChoirRoles` : **retirés du contrat**, le serveur ne les émet plus. Ce
  fichier les a documentés jusqu'au 2026-08-17 — ne pas les réintroduire.
- Les espaces dont le client n'est pas `Actif` sont déjà exclus par le serveur : ne pas
  réimplémenter ce filtre côté mobile.

### Espace actif

Aucune notion d'espace actif côté serveur : l'application en sélectionne un dans `SpaceRoles`
(`ChoirSelectorComponent` + `AuthStore`) et transmet son identifiant via le header
**`X-Space-Id`**, posé dans `TokenInterceptor` — même mécanisme que `ChoralFront`.
`X-Chorale-Id` n'est qu'un repli de compatibilité côté back : ne pas s'en servir pour du code
neuf. Header absent ou invalide sur une route protégée par une policy scopée → `403 Forbidden`.

Un utilisateur **sans aucun rattachement** est une situation normale : écran dédié, jamais une
page blanche ni une boucle de `403`.

| Rôle | Valeur `UserRoleEnum` | Guard |
|---|---|---|
| Membre | `Singer` (chorale) / `Participant` (événement) | guard d'authentification de base, implicite à toute route protégée |
| Chef de pupitre | `SectionLeader` | guard de rôle scopé sur l'**espace actif**, jamais « sur un espace quelconque » ; étend Membre |
| Chef de chœur | `Manager` | guard de rôle scopé sur l'espace actif ; étend Membre |
| Responsable de structure | `ClientManager` | **jamais de route ni de guard côté mobile — web uniquement** |
| Admin général | `Admin` | **jamais de route ni de guard côté mobile — web uniquement** |

L'application décide seulement **quoi afficher** ; le serveur décide **quoi autoriser**. Aucune
règle de permission n'est réécrite côté mobile.

## Specs de référence

- `Spec/chorale/11-ux-ui.md` — design tokens, layout, breakpoints, accessibilité,
  transitions
- `Spec/chorale/02-roles-droits-et-visibilite.md` — rôles, matrice d'actions
- `Spec/chorale/06-ecrans-application-mobile.md` — écrans de l'application mobile
  (extraits seulement, selon l'itération en cours — ne pas tout implémenter d'un coup)
- `Spec/chorale/12-catalogue-icones.md` — catalogue des icônes

## Cohérence avec ChoralFront

`ChoralFront` sert de référence : mêmes noms de patterns (`AuthStore`, `TokenInterceptor`,
`ApiErrorInterceptor`), même structure `models/{domain}-models/`, mêmes tokens (adaptés au
format Ionic). Toute divergence de pattern doit être documentée explicitement ici, jamais
silencieuse.
