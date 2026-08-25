# Architecture web et rôles

> Contenu prêt à publier dans le wiki Azure DevOps (`ChoraleHelper.wiki`).
> Reflète les décisions D22 et D23 (`Spec/chorale/10-decisions.md`).

## Vue d'ensemble

ChoraleHelper expose trois surfaces autour d'une API .NET unique :

- **API** — ASP.NET Core (`ChoraleBack/`)
- **Web** — Angular (`ChoralFront/`), application **unique** à **quatre zones par rôle**
- **Mobile** — Ionic Angular (`ChoraleMobile/`), pour les membres et chefs de pupitre (dossier
  sans code d'application pour l'instant, voir `docs/reste-a-faire.md`)

## Les quatre zones web

Une seule application Angular, avec **redirection post-connexion selon le rôle**. Aucune
logique de permission dupliquée entre zones : la source de vérité reste les rôles scopés par
espace, le rôle scopé par client et le claim global `Admin`.

**Point conceptuel** : la zone n'est pas une propriété de l'utilisateur seul, mais du couple
**(utilisateur, espace actif)** — changer d'espace actif peut changer de zone (un
`ClientManager` qui est aussi `Manager` d'une chorale bascule en `/gestion` sur cette
chorale, jamais en `/client`). La règle d'aiguillage est centralisée dans un seul fichier,
`ChoralFront/src/app/core/zone-resolver.ts` (`resolveZone`), réutilisé tel quel par les guards
de route et par la bascule d'espace de la barre de navigation.

| Zone | Route | Habilitation | Périmètre |
|---|---|---|---|
| **Administration** | `/admin` | Claim global `Admin` | Clients, chorales (support, lecture seule sur le contenu), événements (lecture seule), utilisateurs, catalogue de chants transverse, audit. |
| **Ma structure** | `/client/:clientId` | Rôle `ClientManager`, scopé au client de la route | Chorales du client, plafonds de service (lecture), désignation de responsables. Le mot « Client » n'apparaît jamais hors de `/admin` — l'écran se nomme « Ma structure ». |
| **Gestion** | `/gestion/:espaceId` | `Manager`/`SectionLeader`/`Organizer` sur l'espace actif | Membres, chants, partitions, enregistrements, listes de chants, événements, consignes d'un espace (chorale ou événement). C'est le site de gestion existant. |
| **Espace membre** | `/moi` | Appartenance simple (au moins un espace) | Consultation + participation — **route et guard existent, le contenu est un écran de substitution** (voir `docs/reste-a-faire.md`). |

Ordre de priorité de l'aiguillage post-connexion (`resolveZone`), du plus large au plus
spécifique : `Admin` d'abord (un admin qui est aussi membre reste redirigé vers `/admin`), puis
`Gestion` (un `ClientManager` qui est aussi `Manager` d'une chorale part en `/gestion`,
jamais en `/client`), puis `Client`, puis `Espace membre`, puis `/aucun-espace` si l'utilisateur
n'a ni rôle ni appartenance.

## Surfaces d'accès par rôle

| Rôle | Mobile | Espace membre web | Gestion web | Ma structure web | Admin web |
|---|---|---|---|---|---|
| Membre | ✓ | ✓ (contenu non construit) | — | — | — |
| Chef de pupitre | ✓ | — | ✓ | — | — |
| Responsable | ✓ | — | ✓ | — | — |
| Responsable client | — | — | — | ✓ | — |
| Admin générale | — | — | — | — | ✓ |

Les chefs de pupitre et responsables exercent leurs fonctions de membre sur le mobile
(décision D17) et leurs fonctions de gestion sur la zone Gestion du web.

`ClientManager` dispose désormais d'une surface (« Ma structure », `MyStructureComponent`) :
onglets Chorales / Plafonds (lecture) / Responsables (désignation, seule écriture disponible ici
— `Update`/`UpdateLimits` restent `Admin`-only côté back).

## Scope chorale et sécurité

- Les rôles `Manager`/`SectionLeader`/`Organizer` sont **scopés par espace** ;
  `ClientManager` est **scopé par client** (troisième niveau de scope, à côté de
  l'espace) ; `Admin` est un claim JWT global.
- L'espace actif (chorale ou événement — un événement est aussi un espace) est transmis à
  l'API via le header **`X-Space-Id`** sur chaque requête à scope espace. `X-Chorale-Id`
  reste accepté en repli, pour compatibilité.
- L'autorisation serveur (`SpaceRoleAuthorizationHandler`) valide le rôle pour l'espace **du
  header**. Le scope client (`ClientController`) est validé séparément par
  `ClientRoleAuthorizationHandler`, à partir du `clientId` porté par la route — pas par un
  header.
- **Résolution du `clientId` par `ClientRoleAuthorizationHandler`**, dans cet ordre, jamais
  mélangé pour une même requête : (1) la route (`clientId`) ; (2) la ressource chorale
  **existante** visée par la requête (`id` en route ou en query string — cas d'`Update` et de
  `Delete`, où le client se déduit de ce qui est déjà stocké, jamais de ce que l'appelant
  déclare) ; (3) en dernier repli, le corps de la requête, uniquement pour une action qui crée
  une ressource qui n'existe pas encore (`Create`). L'ordre (2) avant (3) est le point de
  sécurité : lire le `ClientId` du corps pour une action qui porte sur une chorale existante
  permettrait à un `ClientManager` de modifier la chorale d'un autre client en déclarant le
  sien dans le corps.
- Tout filtrage de données à scope espace doit dériver du **même** identifiant que celui
  validé par l'autorisation (voir la note de sécurité sur `GetPaged`).

## Modèle d'administration : palier Client (D23)

D23 est **tranchée et implémentée** : le palier `Client` est conservé, entre l'administration
générale et les chorales. Une entité `Client` regroupe une ou plusieurs chorales et porte trois
responsabilités :

1. **Identité et activation** — désactiver/suspendre un client coupe l'accès à toutes ses
   chorales d'un seul geste (vérifié à la fois côté résolution de rôle et côté lecture de
   contenu).
2. **Autonomie du client** — le rôle `ClientManager`, scopé au client, permet de
   créer/fermer ses propres chorales et d'y nommer les responsables, sans détenir le claim
   global `Admin`.
3. **Limites de service** — plafonds explicites : nombre de chorales, nombre de membres,
   quota de stockage, taille max par fichier.

**Écart D23 résorbé (constaté le 2026-07-29, corrigé le 2026-07-31)** : le point 2 n'était que
partiellement câblé — `ChoirController.Create` restait restreint au rôle global `Admin`, la
policy `ClientManager` n'y étant pas utilisée. `ChoirController.Create`/`Update`/`Delete`
utilisent désormais la policy `AdminOrClientManager` (satisfaite par le claim `Admin` ou
par `ClientManager` sur le client visé) ; `AddMember`/`RemoveMember` utilisent
`SpaceManager`. Le rôle `ClientManager` exerce maintenant la responsabilité qui le
justifie.

**Nuance non résolue par ce chantier** : une chorale créée est toujours immédiatement publiée
(`Status = Published`), jamais créée en `Draft` — le parcours d'inscription auto-service qui
rendrait ce statut utile (lot 6 de `docs/reste-a-faire.md`) n'existe pas encore.

`Client.Name` n'est plus une clé d'unicité (levée par ce chantier — voir `Spec/chorale/04` et
`10-decisions.md`) : c'est un libellé d'exploitation, comparable au nom d'une chorale.

La gestion des clients (activation, plafonds) reste possible uniquement par `Admin`, via
`/admin/clients` ou appel direct à l'API ; la désignation de responsables est en plus ouverte
au `ClientManager` via `/client/:clientId`.

## Publier dans le wiki Azure DevOps

Le wiki est un dépôt Git distinct (`…/ChoraleHelper.wiki`). Pour publier : cloner ce dépôt, y
copier ce fichier (ou coller son contenu via l'éditeur wiki), commit + push. Ce fichier
`docs/Architecture-web-et-roles.md` sert de source dans le dépôt principal.
