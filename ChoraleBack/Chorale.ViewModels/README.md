# Chorale.ViewModels — conventions

DTO de requête et de réponse de l'API. Aucune logique métier : ce projet ne contient que
des porteurs de données et leurs profils AutoMapper.

## Rangement

Un dossier par domaine, aligné sur le contrôleur qui expose les DTO. La racine du projet
ne contient que les deux primitives transverses :

| Fichier racine | Rôle |
|---|---|
| `PaginateViewModel.cs` | Base de tous les filtres paginés (`Page`, `PageSize`, tri, `Filter`) |
| `PagedListViewModel.cs` | Enveloppe de réponse `{ Items, TotalCount, CurrentPage, PageSize }` |

Elles restent à la racine volontairement : les namespaces enfants
(`ChoraleBackEnd.ViewModels.Songs`, etc.) voient le namespace parent sans `using`, donc
les ~20 filtres qui héritent de `PaginateViewModel` n'ont aucune directive à déclarer.

Dossiers actuels : `AdminAudit`, `AdminChoirs`, `AdminDashboard`, `AdminEvents`,
`AdminSongs`, `AdminUsers`, `Auth`, `ChoirMembers`, `Choirs`, `Clients`, `Dashboard`,
`Events`, `Guests`, `Instructions`, `Onboarding`, `Recordings`, `Scores`, `SongLists`,
`Songs`, `Users`.

## Un fichier = une classe

Sauf le profil AutoMapper d'un DTO, qui reste dans le fichier de ce DTO.

## Pourquoi le profil est une classe séparée du DTO

Un profil AutoMapper hérite de `Profile`, qui expose des propriétés publiques
(`ProfileName`, etc.). Faire hériter le DTO lui-même de `Profile` ferait partir ces
internes dans le JSON de réponse. D'où la paire `XxxViewModel` /
`XxxViewModelMappingProfile` dans un même fichier.

Les profils sont découverts par scan d'assembly — voir `Program.cs`,
`AddAutoMapper(typeof(LoginViewModel).Assembly)`. Aucun enregistrement manuel à faire en
ajoutant un profil.

## Nommage

Tout identifiant en anglais (règle racine du dépôt). Seuls les messages d'erreur de
`DataAnnotations` et les commentaires restent en français.
