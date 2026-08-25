# 02 — Rôles, droits et visibilité

Ce fichier est la **source unique de vérité** pour tout ce qui concerne les rôles, les actions autorisées et les règles de visibilité. Les autres fichiers y font référence sans redéfinir ces règles.

---

## Glossaire

| Terme | Définition |
|---|---|
| `Client` | Structure qui souscrit au service. Regroupe une ou plusieurs chorales, porte l'activation de l'accès et les limites de service (nombre de chorales, de membres, quota de stockage). Ne porte pas la facturation en l'état — voir `10-D23`. |
| `Chorale` | Groupe de chanteurs travaillant dans un espace dédié, strictement isolé des autres chorales. |
| `Membre` | Personne rattachée à au moins une chorale. Porte un rôle par chorale. |
| `Voix` | Catégorie musicale de travail. En V1 : `Soprano`, `Alto`, `Tenor`, `Bass` (fixe). |
| `Voix principale` | Voix de référence d'un membre dans une chorale donnée. Unique par membre et par chorale. |
| `Pupitre` | Ensemble des membres d'une même voix dans une chorale. |
| `Chant` | Œuvre ou morceau travaillé par la chorale. Objet central du domaine. |
| `Liste de chants` | Liste ordonnée de chants, libre ou rattachée à un événement. |
| `Partition` | Document musical rattaché à un chant, général ou spécifique à une voix. |
| `Enregistrement` | Audio rattaché à un chant, général ou spécifique à une voix. |
| `Événement` | Répétition, concert, mariage, office ou autre prestation planifiée. |
| `Saison` | Période de travail regroupant répertoire et événements d'une chorale. |

---

## Rôles

Les rôles sont définis **par chorale**, jamais globalement. Un membre peut avoir des rôles différents dans des chorales différentes.

### Membre (`Singer`)

Rôle de base. Présent dans toute chorale.

Objectif : travailler ses chants, écouter les bons audios, retrouver sa partition, suivre les consignes et événements.

### Chef de pupitre (`SectionLeader`)

Extension du rôle `Singer`. Attaché à **une voix** dans **une chorale**.

Objectif : faire progresser sa voix, produire des enregistrements utiles, diffuser des consignes ciblées à son pupitre, vérifier la couverture audio de sa voix.

Contraintes :
- Un seul chef de pupitre par voix et par chorale.
- Il reste `Singer` de la chorale — il hérite de toutes les capacités du membre.
- Un membre peut être chef de pupitre sur une voix différente dans une autre chorale.
- Gère la composition de son pupitre (ajout/retrait de membres) et peut archiver les chants, partitions et enregistrements liés à sa voix — capacités détaillées dans la matrice ci-dessous.

### Responsable de chorale (`Manager`)

Extension du rôle `Singer`. Peut cumuler avec `SectionLeader`.

Objectif : structurer le répertoire, publier les contenus de référence, organiser les événements, gérer les membres, suivre activité et complétude.

Contraintes :
- Peut être plusieurs responsables dans une même chorale.
- Il reste `Singer` — il hérite de toutes les capacités du membre.

### Organisateur (`Organizer`)

Rôle scopé à un **événement autonome**, c'est-à-dire un événement qui n'est rattaché à aucune chorale. Équivalent du `Manager` pour cet espace (`10-D25`).

Objectif : monter une prestation ponctuelle — un mariage, un concert, un office — sans passer par la création d'une chorale, en y invitant des participants et en y préparant un répertoire.

Contraintes :
- **N'existe que sur un événement autonome.** Un événement rattaché à une chorale n'a pas d'organisateur : il est géré par les `Manager` de la chorale porteuse, qui y exercent les mêmes capacités. Affecter un `Organizer` à un événement de chorale n'a aucun sens métier et est refusé.
- Ne détient aucun droit sur une chorale du seul fait de ce rôle, y compris sur la chorale qui partagerait le même client.
- Le créateur d'un événement autonome en devient automatiquement `Organizer` (`10-D25`), et devient `ClientManager` de la structure créée pour le porter (`10-D23` : un espace n'existe pas sans client).

### Participant (`Participant`)

Rôle de base sur un événement, équivalent du `Singer` pour cet espace.

Objectif : consulter le répertoire et les consignes de l'événement, et répondre à l'appel de présence.

Contraintes :
- N'a pas de voix principale ni de pupitre : ces notions sont propres à la chorale.
- Son appartenance à un événement ne lui donne **aucun accès** au contenu de la chorale porteuse, quand il y en a une.

### Responsable client (`ClientManager`)

Rôle scopé **au client**, pas à une chorale. Personne côté client, pas opérateur interne.

Objectif : rendre le client autonome sur son périmètre — ouvrir et fermer ses chorales, y désigner les responsables, suivre ses indicateurs consolidés.

Contraintes :
- Peut créer une chorale **dans son client uniquement**, et y nommer un responsable.
- Ne voit que les chorales de son client, jamais celles d'un autre.
- **Ne détient aucun droit sur le contenu** d'une chorale (chants, partitions, enregistrements, membres) du seul fait de ce rôle : pour agir dans une chorale, il doit y être `Manager`. Le rôle client ouvre la porte, il n'entre pas dans la pièce.
- N'accède pas aux plafonds de service en écriture : les limites sont fixées par l'administration générale.
- Ce rôle existe pour que la création d'une chorale ne dépende plus d'un opérateur interne, et pour retirer toute raison de distribuer le claim global `Admin` à un client — ce claim court-circuitant les contrôles d'appartenance par espace.

### Administration générale (`Admin`)

Rôle transverse, hors chorale et hors client. Opérateurs internes uniquement.

Objectif : gérer les clients et leurs limites de service, contrôler l'accès au service, gérer les comptes utilisateurs, suivre usage et risque.

Contraintes :
- N'appartient à aucune chorale ni à aucun client.
- Son accès aux données d'une chorale est limité au support et est **toujours tracé**.
- Seul rôle habilité à fixer les plafonds de service d'un client.

---

## Surfaces d'accès par rôle

Le service expose plusieurs surfaces. Le web est **une application unique découpée en quatre zones** (Administration, Ma structure, Gestion chorale, Espace membre) ; la zone affichée est déterminée par la route courante et le rôle **de l'espace actif** (voir `10-D22`). La redirection post-connexion, elle, suit l'ordre de priorité de `10-D32` — les deux ne se confondent jamais : naviguer vers une zone non prioritaire est un usage normal, pas une anomalie corrigée par un repli silencieux.

| Rôle | Application mobile | Espace membre web | Gestion chorale (web) | Ma structure (web) | Administration (web) |
|---|---|---|---|---|---|
| Membre | ✓ | ✓ | — | — | — |
| Chef de pupitre | ✓ | —¹ | ✓ | — | — |
| Responsable | ✓ | —¹ | ✓ | — | — |
| Responsable client | — | — | — | ✓ | — |
| Admin générale | — | — | — | — | ✓ |

¹ *Les chefs de pupitre et responsables exercent leurs fonctions de membre sur le mobile (voir D17) et leurs fonctions de gestion sur la zone Gestion chorale du web ; ils ne passent pas par l'espace membre web.*

- L'espace membre web offre le même périmètre fonctionnel que l'application mobile membre : **consultation + participation** (voir D22). Non construit à ce jour côté web (voir `docs/reste-a-faire.md`) : la zone et son guard existent, l'écran est un substitut.
- La zone **Ma structure** (`/client/:clientId`) est le point d'entrée du `ClientManager` : chorales de son client, plafonds de service en lecture, désignation de responsables (`10-D23`). Elle reste atteignable par une entrée de navigation permanente, y compris pour un `ClientManager` détenant par ailleurs un rôle de gestion sur une chorale — voir `13` pour le détail des écrans.
- Les zones Gestion chorale, Ma structure et Administration sont réservées au web.
- L'accès à une zone n'implique aucune duplication de logique de permission : la source de vérité reste les rôles scopés par espace, le rôle scopé par client, et le claim global `Admin`.

---

## Règles de rattachement

- Un compte utilisateur peut exister **sans aucun espace** (chorale ou événement) : un compte
  administrateur créé par l'administration générale n'appartient à aucune chorale, et
  l'écran `Utilisateurs` de l'administration générale porte un onglet dédié aux comptes « sans
  rattachement ». Ce n'est donc plus une invariante du compte, mais du **rattachement** : c'est
  la ligne `SpaceMember` qui porte toujours une voix et un rôle, jamais le compte seul.
- Un membre peut appartenir à plusieurs chorales avec des rôles différents.
- Un membre peut avoir une voix principale différente par chorale.
- Un membre peut être rattaché à plusieurs voix dans une même chorale, mais n'a qu'une seule voix principale par chorale.

### Rattachement à un événement

- Un événement est un `Space` au même titre qu'une chorale, mais ses rôles sont `Organizer` et `Participant`, jamais `Manager`/`SectionLeader`/`Singer`.
- Un événement est soit **rattaché à une chorale**, soit **autonome**. Cette distinction commande qui le gère :

| | Événement de chorale | Événement autonome |
|---|---|---|
| Rattachement | une chorale porteuse | aucune chorale |
| Qui le gère | les `Manager` de la chorale porteuse | un `Organizer` |
| `Organizer` affectable | **non** | oui |
| Client de rattachement | celui de la chorale porteuse | le sien propre |

- **Un `Organizer` ne peut être affecté qu'à un événement autonome.** Sur un événement de chorale, la gestion revient aux responsables de la chorale : y ajouter un organisateur créerait un second chemin d'autorité sur le même espace, sans que rien ne les départage.
- **Le rattachement à une chorale se décide à la création de l'événement et est définitif.** Aucune action ne permet de rattacher après coup un événement autonome à une chorale, ni de détacher un événement de sa chorale porteuse pour le rendre autonome.
- Un participant n'a ni voix ni pupitre — ces notions n'existent que dans une chorale.
- L'appartenance à un événement, quel que soit le rôle, ne donne **aucun droit** sur la chorale porteuse ni sur les autres espaces du même client.

---

## Matrice des actions par rôle

La colonne `Chef de pupitre` indique uniquement les capacités **supplémentaires** par rapport au `Membre`. Le responsable cumule tout.

| Domaine | Action | Membre | Chef de pupitre | Responsable | Admin générale |
|---|---|---|---|---|---|
| **Contenu** | Écouter un contenu publié | ✓ | ✓ | ✓ | — |
| | Télécharger un contenu si autorisé | ✓ | ✓ | ✓ | — |
| | Voir toutes les voix de sa chorale | ✓ | ✓ | ✓ | — |
| **Enregistrement** | Créer un enregistrement brouillon (ses voix) | — | ✓ | ✓ | — |
| | Déposer un fichier audio (ses voix) | — | ✓ | ✓ | — |
| | Envoyer un enregistrement à validation | — | ✓ | ✓ | — |
| | Publier un enregistrement | — | ✗ par défaut¹ | ✓ | — |
| | Archiver / supprimer un enregistrement (CRUD complet) | — | ✓ | ✓ | — |
| | Partager un enregistrement à une autre chorale | — | — | ✓ | — |
| | Retirer un partage | — | — | ✓ | — |
| **Partition** | Ajouter une partition (brouillon) | — | — | ✓ | — |
| | Publier une partition | — | — | ✓ | — |
| | Archiver une partition | — | ✓ | ✓ | — |
| **Chant** | Voir la liste des chants d'une chorale (support) | — | — | — | ✓ (tracé) ³ |
| | Créer un chant | — | — | ✓ | — |
| | Modifier un chant | — | — | ✓ | — |
| | Archiver un chant | — | ✓ | ✓ | — |
| **Liste de chants** | Créer une liste de chants | — | ✓ (type `pupitre` uniquement) | ✓ | — |
| | Publier une liste de chants | — | — | ✓ | — |
| | Archiver une liste de chants | — | — | ✓ | — |
| **Événement** | Voir les événements publiés | ✓ | ✓ | ✓ | — |
| | Voir tous les événements d'une chorale, quel que soit leur statut (support) | — | — | — | ✓ (tracé) |
| | Répondre à un événement (présence) | ✓ | ✓ | ✓ | — |
| | Créer / modifier un événement | — | — | ✓ | — |
| | Publier un événement | — | — | ✓ | — |
| | Annuler / archiver un événement | — | — | ✓ | — |
| **Consigne** | Lire les consignes publiées | ✓ | ✓ | ✓ | — |
| | Écrire une consigne (voix, chant) | — | ✓ (sa voix uniquement) | ✓ | — |
| | Écrire une consigne générale ou par événement | — | — | ✓ | — |
| **Membres** | Voir la liste des membres d'une chorale (support) | — | — | — | ✓ (tracé) |
| | Inviter un membre | — | — | ✓ | — |
| | Modifier un membre | — | — | ✓ | — |
| | Affecter voix et rôle | — | — | ✓ | — |
| | Ajouter / retirer un membre d'un pupitre (sa voix) | — | ✓ (sa voix uniquement) | ✓ | — |
| | Désactiver / archiver un membre | — | — | ✓ | — |
| **Chorale** | Modifier le nom / la description d'une chorale | — | — | — | ✓ |
| | Changer le statut d'une chorale (`Draft`/`Published`/`Cancelled`/`Archived`) | — | — | — | ✓ |
| **Clients** | Créer / modifier un client | — | — | — | ✓ |
| | Activer / désactiver un client | — | — | — | ✓ |
| | Fixer les limites de service d'un client | — | — | — | ✓ |
| | Voir toutes les chorales (tous clients) | — | — | — | ✓ |
| | Accéder aux données d'une chorale (support) | — | — | — | ✓ (tracé) |
| | Gérer abonnements et paiements | — | — | — | ² |

¹ *La délégation `publication pupitre` peut être activée par chorale pour autoriser un chef de pupitre à publier directement sans validation du responsable.*

² *Non construit : le palier `Client` ne porte pas la facturation en l'état (`10-D23`). Calendrier à trancher — `10-D31`.*

³ *Vue de type catalogue transverse, tous clients confondus, regroupée par titre + compositeur normalisés — pas un accès au contenu d'une chorale précise. « Accéder aux données d'une chorale (support) », ci-dessous, couvre l'accès chorale par chorale.*

L'administration générale n'a **aucun** accès — ni lecture ni écriture — aux partitions, aux
enregistrements (fichiers ou métadonnées) ni aux consignes d'une chorale : son accès en lecture
(support, tracé) se limite à la liste des membres, des chants et des événements d'une chorale,
et au catalogue transverse des chants. Elle ne modifie jamais le contenu d'une chorale — seuls
le nom, la description et le statut de la chorale elle-même (pas son contenu) sont modifiables
par l'administration générale.

---

## Matrice des actions — scope `client`

Le rôle `ClientManager` est scopé au client et n'apparaît pas dans la matrice ci-dessus : il ne détient aucun droit de chorale de ce fait. Les deux matrices ne se lisent jamais ensemble.

| Action | Responsable client | Admin générale |
|---|---|---|
| Voir les chorales de son client | ✓ | ✓ (tous clients) |
| Créer une chorale dans son client | ✓ | ✓ |
| Fermer / archiver une chorale de son client | ✓ | ✓ |
| Désigner un `Manager` dans une chorale de son client | ✓ | ✓ |
| Voir les indicateurs consolidés de son client | ✓ | ✓ (tous clients) |
| Voir ou modifier le contenu d'une chorale (chants, membres, contenus) | ✗ ⁴ | ✗ (support tracé uniquement) |
| Consulter les limites de service de son client | ✓ (lecture) | ✓ |
| Modifier les limites de service | ✗ | ✓ |
| Créer / modifier / désactiver un client | ✗ | ✓ |
| Voir les autres clients | ✗ | ✓ |

⁴ *Pour agir dans une chorale, un responsable client doit y détenir le rôle `Manager`. Le rôle client ne donne jamais accès au contenu — c'est ce qui empêche un cumul silencieux de droits par simple appartenance au client.*

---

## Règles de visibilité

Ces règles s'appliquent à tous les rôles dans une chorale.

- Un membre ne voit que les contenus au statut `Published` de sa chorale active.
- Un contenu `Draft` ou `PendingReview` est visible uniquement par son créateur et les responsables de la chorale.
- Une chorale ne voit jamais les contenus d'une autre chorale sans partage explicite.
- Un enregistrement partagé est accessible en lecture seule à la chorale destinataire.
- La chorale destinataire rattache l'enregistrement partagé à un chant local, sans modifier le contenu source.
- Si un partage est retiré, le contenu disparaît immédiatement de la chorale destinataire. L'historique d'audit est conservé.

### Statut de la chorale elle-même

Distinct du statut de son contenu (`Published`/`Draft`/`PendingReview`, ci-dessus), la chorale porte son propre statut (`10-D23`, `04` § Chorale) qui conditionne l'accès à **tout** son contenu :

| Statut de la chorale | Visible des membres | Écriture sur le contenu |
|---|---|---|
| `Draft` | Non — visible du seul créateur et des `Manager` | Oui |
| `Published` | Oui | Oui |
| `Cancelled` | Oui, avec son statut affiché | Non — lecture seule |
| `Archived` | Non | Non |

Les transitions autorisées sont : `Draft` → `Published` ou `Archived` ; `Published` → `Cancelled` ou `Archived` ; `Cancelled` → `Published` ou `Archived` ; `Archived` → `Published` (seule réactivation possible). Aujourd'hui, une chorale créée est toujours immédiatement `Published` (voir `docs/reste-a-faire.md`, nuance sur la création de chorale) : `Draft` existe dans le modèle mais n'est pas encore atteignable par un parcours utilisateur.

---

## Règle de gouvernance sur la publication des enregistrements

Par défaut :
1. Le chef de pupitre produit un enregistrement et l'envoie à validation (`PendingReview`).
2. Le responsable valide et publie.

La délégation `publication pupitre` est activable par chorale. Elle permet au chef de pupitre de publier directement, sans passer par la validation du responsable.

Cette règle évite la diffusion d'un audio de travail non finalisé aux membres.
