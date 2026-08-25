# 13 — Ma structure

Surface web dédiée au rôle `ClientManager` (`/client/:clientId`, policy `ClientManager`), introduite par
`10-D32` pour lui donner une surface d'exercice à son droit réel (`10-D23`) : créer et fermer les chorales
de sa structure, y désigner des responsables, suivre les plafonds fixés par l'administration générale.

Le mot « client » n'apparaît dans aucun texte visible de cette zone (`10-D32`) — on dit toujours
« structure ». Cette zone ne donne **jamais** accès au contenu d'une chorale (chants, membres,
enregistrements) : c'est une exclusion volontaire, voir § Hors périmètre.

---

## Accueil de la structure

Trois onglets, en-tête portant le nom et le statut de la structure (`Active`/`Suspended`/`Archived`) :

| Onglet | Contenu |
|---|---|
| Chorales | Liste des chorales de la structure — nom, nombre de membres, nombre de chants, événements à venir. Action : créer une chorale. |
| Plafonds | Lecture seule (`10` § Client, seul rôle habilité à les fixer : Admin générale) : chorales, membres, stockage — utilisation vs plafond, alerte visuelle si le plafond est bientôt atteint. |
| Responsables | Désignation et retrait d'un `Manager` sur une chorale de la structure (`10` matrice scope `client`). |

Cliquer une ligne de l'onglet Chorales ouvre la fiche chorale ci-dessous.

---

## Fiche chorale

Écran de lecture, au niveau structure — jamais au niveau contenu (voir § Hors périmètre).

Contenu :
- En-tête : nom, statut de la chorale (`ChoirStatusEnum`, `10-D33`).
- Les trois indicateurs consolidés de la liste (membres, chants, événements à venir), affichés en
  **lecture seule, non cliquables** : ce sont des indicateurs de volumétrie de la structure
  (« combien de chorales, combien de membres au total »), pas des points d'entrée vers le contenu
  d'une chorale précise. Conforme à `10-D30` : chaque indicateur n'est affiché que si sa source
  réelle existe.
- Liste des chefs de chœur (`UserRoleEnum.Manager`, libellé figé par `10-D40` — à ne pas confondre
  avec `SectionLeader`, le chef de **pupitre**) de la chorale, avec désignation et retrait — reprend
  le contenu actuellement porté par l'écran « Chefs de chœur », replacé dans ce cadre.
- Actions de cycle de vie autorisées par la matrice scope `client` (`02`) : fermer, archiver,
  republier, selon les transitions bornées par `10-D33`.
- Si et seulement si le `ClientManager` détient par ailleurs le rôle `Manager` sur cette chorale
  précise : un bouton « Ouvrir la gestion de cette chorale » vers `/management/:choirId`. Ce lien
  n'est qu'un raccourci vers un rôle déjà détenu — il n'étend aucun droit et n'apparaît jamais pour
  un `ClientManager` sans ce rôle.

---

## Hors périmètre

- **Contenu d'une chorale** (liste nominative des membres, chants, enregistrements, événements en
  détail) : refusé par `02` § Matrice des actions — scope `client`, note 4 — *« Pour agir dans une
  chorale, un responsable client doit y détenir le rôle `Manager`. Le rôle client ne donne jamais
  accès au contenu — c'est ce qui empêche un cumul silencieux de droits par simple appartenance au
  client. »* Un `ClientManager` sans rôle de gestion n'a donc, pour le contenu d'une chorale, ni
  plus ni moins d'accès qu'un visiteur externe. Ouvrir cet accès nécessiterait une décision produit
  explicite révisant `02`, avec un régime de traçabilité au moins équivalent à celui de
  l'administration générale (`10-D35` : accès support, toujours tracé) — l'administration elle-même
  n'a pas un accès plus large.
- **Modification des plafonds de service** : lecture seule ici, écriture réservée à l'administration
  générale (`08` § Clients).
- **Autres structures** : la zone ne montre jamais que la structure de l'utilisateur connecté.
