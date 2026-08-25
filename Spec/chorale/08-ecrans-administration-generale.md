# 08 — Administration générale

Surface web dédiée aux opérateurs internes (`/admin`, claim global `Admin`). Elle pilote
plusieurs clients et n'intervient dans une chorale qu'en mode support, en lecture seule sur le
contenu — tout accès est tracé (voir `02`).

---

## Tableau de bord

9 indicateurs actionnables, tous à source réelle (`10-D30` : pas d'indicateur sans appel de
données réel derrière). Chaque tuile ouvre la liste filtrée correspondante, sauf les deux
indicateurs qui n'exposent qu'une liste d'identifiants (voir plus bas).

| Section | Indicateurs |
|---|---|
| Clients | Total, Actifs, Suspendus, Archivés |
| Chorales | Total, Brouillon, Publiées, Annulées, Archivées, Inactives depuis 30 jours |
| Utilisateurs | Total, Actifs, Invités non activés |
| Chants | Total au catalogue, Groupes en doublon |
| Suivi | Événements à venir (30 jours) |
| Anomalie | Événements sans structure — à rattacher (voir `04` § Client, client technique de migration) |

Un dixième champ, le stockage total consommé, est affiché de façon informative sans être
cliquable : aucune liste ne correspond à un total agrégé.

« Clients non démarrés » et « Clients proches d'un plafond » n'ouvrent pas de liste filtrée :
`ClientController.GetPaged` n'accepte aucun filtre de ce type aujourd'hui. Le tableau de bord
n'affiche que les identifiants transmis par l'indicateur, sous forme de liens directs vers
chaque fiche client — écart assumé, pas un bug (voir `docs/reste-a-faire.md`).

Un bloc distinct, indépendant du chargement des indicateurs, permet de déclencher la **purge
RGPD** des comptes invités inactifs : aperçu des comptes concernés puis confirmation explicite
(`10-D28`, purge manuelle, jamais de tâche planifiée).

Hors périmètre construit : tout indicateur commercial ou financier (MRR, ARR, impayés,
renouvellements, taux de rétention) — aucune source n'existe (`10-D30`, `10-D31`). Ils ne
figurent pas à l'écran tant que leur source n'existe pas.

---

## Clients

- Créer un client (nom, contact) — le nom n'est plus une clé d'unicité (`04` § Client).
- Modifier les informations du client (nom, contact).
- Activer, suspendre ou archiver un client. Suspendre refuse l'accès à **toutes** ses
  chorales.
- Voir les chorales rattachées et leur niveau d'usage.
- Consulter et modifier les **limites de service** du client : nombre de chorales, nombre de
  membres, quota de stockage, taille maximale de fichier.
- Désigner un `ClientManager`, qui peut ensuite créer des chorales pour ce client et
  gérer ses plafonds en lecture depuis « Ma structure » (voir `13`).

Hors périmètre construit, en attente de `10-D31` : statut d'abonnement, état des paiements, et
les indicateurs financiers associés. Conformément à `10-D30`, ils n'apparaissent pas à l'écran
tant que leur source n'existe pas.

---

## Chorales

- Lister toutes les chorales (tous clients confondus), avec filtre par statut
  (`Draft`/`Published`/`Cancelled`/`Archived`) et par inactivité (30 jours).
- Ouvrir une chorale : onglets Membres, Chants, Événements — **lecture seule**, aucune
  écriture sur le contenu depuis cette surface.
- Modifier le nom et la description de la chorale (pas son contenu, pas son `ClientId` —
  l'administration générale ne déplace pas une chorale d'un client à l'autre).
- Changer le statut de la chorale, selon les transitions autorisées (`02` § Statut de la
  chorale). Un écran d'impact (`ImpactArchivage`) précède l'archivage.

---

## Événements

- Lister tous les événements (tous clients confondus, tous statuts — pas de filtre implicite
  sur les brouillons, contrairement à la vue membre).
- Ouvrir un événement en détail, lecture seule.
- Repérer les événements rattachés au client technique de migration (« sans structure »),
  signalés en anomalie.

---

## Utilisateurs

Un écran à 4 onglets, tous listés depuis le même contrôleur (`AdminUserController`) :

| Onglet | Contenu |
|---|---|
| Chorales | Utilisateurs rattachés à au moins une chorale, avec rôle(s) et voix |
| Événements | Participants et organisateurs d'événements |
| Administrateurs | Comptes portant le claim `Admin` — créés depuis cet écran |
| Sans rattachement | Comptes sans aucun `SpaceMember` (typiquement, des comptes administrateur) |

Une fiche utilisateur agrégée (`GetUserDetail`) permet de modifier l'identité, d'activer ou
désactiver le compte, de réinitialiser le mot de passe, de renvoyer une invitation, ou de
supprimer le compte.

---

## Chants

Catalogue transverse à tous les clients, **regroupé à l'affichage** par titre et compositeur
normalisés (`SongKeyHelper`) — aucune entité « œuvre » créée en base : chaque chorale garde
son propre `Song`. Un chant sans compositeur n'est jamais fusionné avec un autre. Lecture
seule, sans accès au contenu (partitions, enregistrements) des chants regroupés.

---

## Audit

Journal d'audit, lecture seule volontaire — aucun endpoint d'écriture ni de suppression
n'existe sur cet écran, un journal modifiable ne vaut rien. Filtrable par utilisateur, type
d'entité, action et période.

---

## Hors périmètre (aucune source, aucun écran)

Conformément à `10-D30` (aucun indicateur sans source réelle) et `10-D31` (calendrier de
facturation non tranché), les surfaces suivantes ne sont pas construites et ne doivent pas
apparaître avant qu'une source existe :

- Suivi commercial et activité (taux d'engagement, clients à risque, renouvellements).
- Abonnements et paiements (montant, périodicité, échéances, retards, Stripe).
- Tout indicateur financier (MRR, ARR, impayés, taux de rétention).
- Support et contrôle au sens d'un écran dédié (problèmes déclarés par client, contexte
  rapide) — l'audit et la fiche client couvrent aujourd'hui un sous-ensemble de ce besoin.

L'inscription auto-service (lot 6 : `/inscription`, `/rejoindre`, `/demarrer`, codes de
rattachement, demandes d'adhésion) n'est pas réalisée (voir `docs/reste-a-faire.md`) : aucune
chorale ne peut être créée hors du seed de démonstration ou d'un appel direct par `Admin`/
`ClientManager`.
