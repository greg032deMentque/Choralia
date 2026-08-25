# 04 — Modèle métier organisationnel

Ce fichier définit les objets organisationnels, leurs attributs et leurs cycles de vie. Les droits associés sont dans le fichier `02`.

---

## Client

Structure qui souscrit au service. Regroupe une ou plusieurs chorales. Décision de référence : `10-D23`.

### Attributs

| Attribut | Obligatoire | Type | Contrainte |
|---|---|---|---|
| Nom | Oui | Texte | Non vide. Libellé d'exploitation, pas une clé : l'unicité parmi les clients actifs, prévue initialement, est **levée**. |
| Contact — nom | Non | Texte | — |
| Contact — email | Non | Email | — |
| Statut | Oui | Enum | `Active`, `Suspended`, `Archived` |
| Limite — nombre de chorales | Oui | Entier | Valeur par défaut uniforme, surchargeable |
| Limite — nombre de membres | Oui | Entier | Idem |
| Limite — quota de stockage | Oui | Octets | Idem |
| Limite — taille maximale de fichier | Oui | Octets | Idem |

Les quatre limites sont fixées par l'administration générale seule. `D21` posant un produit unique au même prix, elles portent des valeurs par défaut identiques pour tous les clients et ne se surchargent qu'à la marge.

### Cycle de vie

| Statut | Accès aux chorales du client | Réversible |
|---|---|---|
| `Active` | Ouvert | — |
| `Suspended` | **Refusé pour toutes ses chorales** | Oui → `Active` |
| `Archived` | Refusé. Présent dans l'historique uniquement | Non en V1 |

### Règles

- Toute chorale appartient à exactement un client. Il n'existe pas de chorale sans client.
- Suspendre un client refuse l'accès à **toutes** ses chorales en un seul geste — c'est la raison d'être du palier.
- Un client ne voit jamais les chorales, les membres ni les contenus d'un autre client.
- Le franchissement d'une limite de service refuse l'opération avec un message explicite nommant la limite atteinte. Il ne dégrade jamais silencieusement le service.
- Un client existant au-dessus d'une limite abaissée n'est pas amputé : l'existant est conservé, seules les créations nouvelles sont refusées.
- La facturation n'est pas portée par le client en l'état (`10-D23`, calendrier en `10-D31`).

---

## Chorale

Chaque chorale est un espace de travail strictement isolé contenant : membres, chants, partitions, enregistrements, listes de chants, événements, consignes et documents.

Toute chorale appartient à un `Client`.

### Statut

| Attribut | Obligatoire | Type | Contrainte |
|---|---|---|---|
| Statut | Oui | Enum | `Draft`, `Published`, `Cancelled`, `Archived` |

Distinct du statut de l'`Événement` (`10-D33`) : les deux enums partagent la même forme mais
restent verrouillés séparément, pour qu'une évolution du cycle de vie de l'un ne modifie pas
silencieusement celui de l'autre. Contrairement à un événement, une chorale n'a pas de date de
fin : il n'existe donc pas d'état effectif calculé.

#### Cycle de vie

| Statut | Visible des membres | Écriture sur le contenu | Réversible |
|---|---|---|---|
| `Draft` | Non — créateur et `Manager` seuls | Oui | → `Published` ou `Archived` |
| `Published` | Oui | Oui | → `Cancelled` ou `Archived` |
| `Cancelled` | Oui, statut affiché | Non — lecture seule | → `Published` ou `Archived` |
| `Archived` | Non | Non | → `Published` (seule réactivation) |

`IsDeleted` ne porte que la suppression — l'archivage (`Status = Archived`) est réversible et
distinct : avant l'introduction de ce statut, les deux notions étaient confondues sur
`IsDeleted` seul.

Une chorale créée aujourd'hui est toujours immédiatement `Published` : le statut `Draft`
existe dans le modèle mais n'est atteignable par aucun parcours utilisateur tant que
l'inscription auto-service (hors périmètre actuel, voir `docs/reste-a-faire.md`) n'existe pas.

---

## Voix

En V1, structure vocale fixe : `Soprano`, `Alto`, `Tenor`, `Bass`.

- Une chorale possède ces quatre voix en standard.
- Un membre peut être rattaché à plusieurs voix dans une chorale, avec une seule voix principale.
- Un seul chef de pupitre est désigné par voix et par chorale.

---

## Membre

Le tableau ci-dessous décrit le **rattachement** d'un compte à une chorale (`SpaceMember`
dans le modèle unifié `Space`, `10-D24`), pas le compte utilisateur lui-même : un compte peut
exister sans rattachement (compte administrateur créé par l'administration générale, par
exemple), auquel cas ce tableau ne s'applique à aucune chorale pour ce compte. C'est le
rattachement, quand il existe, qui porte toujours une voix et un rôle — jamais le compte seul.

### Attributs

| Attribut | Obligatoire | Type | Contrainte |
|---|---|---|---|
| Prénom et nom | Oui | Texte | Non vides |
| Email | Oui | Email | Unique dans le système |
| Statut | Oui | Enum | `Invited`, `Active`, `Inactive`, `Archived` |
| Voix affectées | Oui | Liste par chorale | Au moins une voix par chorale active |
| Rôle(s) | Oui | Liste par chorale | Au moins un rôle par chorale |

### Cycle de vie

| Statut | Description | Réversible |
|---|---|---|
| `Invited` | Créé avant sa première connexion. | Oui → `Active` à la connexion |
| `Active` | Connecté et opérationnel. | — |
| `Inactive` | Masqué des listes opérationnelles. Accès révoqué. | Oui → `Active` |
| `Archived` | Accès révoqué. Présent dans l'historique uniquement. | Non en V1 |

### Règles

- L'appartenance multi-chorales n'entraîne aucune fusion de droits entre chorales.
- Un membre `Archived` n'est pas supprimé — ses actions restent dans l'historique d'audit.

---

## Événement

### Attributs

| Attribut | Obligatoire | Type | Contrainte |
|---|---|---|---|
| Titre | Oui | Texte | Non vide |
| Type | Oui | Enum | `Rehearsal`, `Concert`, `Wedding`, `Mass`, `Other` |
| Date ou plage de dates | Oui | Date | — |
| Lieu | Oui | Texte | — |
| Statut | Oui | Enum | `Draft`, `Published`, `Finished`, `Cancelled`, `Archived` |
| Membres ou groupes ciblés | Oui | Liste | Toute la chorale ou sous-ensemble |
| Liste(s) de chants | Conditionnel | Référence(s) | Optionnel à la création, obligatoire à la publication |
| Informations pratiques | Non | Texte libre | — |
| Documents associés | Non | Fichiers | — |

### Sous-moments (mariage, office)

Les sous-moments (entrée, communion, sortie) sont gérés comme des **listes de chants distinctes** rattachées au même événement — pas comme un niveau de modèle supplémentaire.

### Cycle de vie

| Statut | Visible membres | Modifiable |
|---|---|---|
| `Draft` | Non | Oui (tout) |
| `Published` | Oui | Partiellement (informations pratiques, consignes) |
| `Finished` | Oui (historique) | Non |
| `Cancelled` | Oui (état affiché explicitement) | Non |
| `Archived` | Non | Non |

### Règles

- Un événement `Cancelled` reste visible des membres avec son statut affiché — il n'est pas supprimé.
- Un événement passe automatiquement à `Finished` une fois sa date passée — ou manuellement par le responsable.

---

## Présence

Suivi de présence par événement et par membre ciblé.

| Statut | Description |
|---|---|
| `NoReply` | État initial pour tout membre ciblé |
| `Attending` | Confirmé |
| `NotAttending` | Décliné |
| `Maybe` | Incertain |

### Règles

- Un membre peut modifier sa réponse tant que l'événement n'est pas au statut `Finished`.
- Le taux de réponse est calculable à tout moment : réponses reçues / membres ciblés.

---

## Saison

Une saison regroupe répertoire et événements sur une période.

### Règles

- Une chorale peut fonctionner sans saison si elle est en mode événement ponctuel.
- Un chant peut exister dans plusieurs saisons.
- L'archivage d'une saison masque la saison sans supprimer ses contenus.

---

## Consignes et documents

### Types de consignes

Une consigne porte toujours sur un **chant** — voir `10-D43`. Elle peut en outre viser un pupitre précis de ce chant.

| Cible | Peut être écrit par |
|---|---|
| Un chant, pour tout le chœur | Responsable |
| Un chant, restreint à une voix | Chef de pupitre (sa voix), Responsable |

Les portées « générale (toute la chorale) », « par voix » (hors chant) et « par événement » ont été retirées du modèle (`10-D43`) : elles n'avaient aucun écran où être lues, et la portée événement était inatteignable côté autorisations.

### Documents hors partition

Texte, paroles, prononciation, planning, consignes scéniques. Secondaires par rapport au couple `audio + partition`.

---

## Politique d'archivage (tous objets)

| Objet | Déclencheur | Réversible |
|---|---|---|
| Chant | Manuelle (responsable) | Oui |
| Partition | Manuelle ou remplacement automatique | Oui |
| Enregistrement | Manuelle (responsable) | Oui |
| Événement | Manuelle ou passage automatique à `Finished` | Oui |
| Membre | Manuelle (responsable) | Partiel (`Archived` → non en V1) |
| Saison | Manuelle (responsable) | Oui |

Aucun objet n'est supprimé physiquement en V1.
