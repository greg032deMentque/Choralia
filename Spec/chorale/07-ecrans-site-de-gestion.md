# 07 — Site de gestion responsable

Surface web dédiée au **Responsable de chorale**. Elle couvre la publication, la gestion des membres, l'organisation des événements et le suivi d'activité. Les droits et règles métier sont dans `02`, `03` et `04`.

---

## Tableau de bord

**But** : vue d'ensemble de la santé de la chorale.

- Prochains événements et taux de réponse de présence par voix.
- Chants incomplets (partition ou enregistrement manquants).
- Membres en statut `invité` non encore connectés.
- Enregistrements `à valider` en attente d'action.
- Alertes : événement sans liste de chants, voix sans enregistrement pour un événement à venir.
- Indicateurs clés : membres actifs, chants complets, taux d'écoute moyen.

---

## Gestion des membres

- Inviter un membre (par email).
- Modifier les informations d'un membre.
- Affecter une ou plusieurs voix et définir la voix principale.
- Attribuer ou modifier un rôle (`membre`, `chef de pupitre`, `responsable`).
- Désigner ou changer le chef de pupitre d'une voix (l'ancien conserve son rôle de membre).
- Passer un membre en `inactif` ou `archivé`.
- Rechercher et filtrer par statut, voix, rôle.

---

## Gestion des chants

- Créer un chant et renseigner ses métadonnées.
- Associer les voix concernées et définir le niveau de priorité.
- Voir l'état de complétude : partitions manquantes, voix non couvertes en enregistrement.
- Archiver un chant.

---

## Gestion des partitions

- Ajouter une partition (brouillon).
- Publier une partition — archive automatiquement la version précédente du même type.
- Remplacer une version existante.
- Archiver une version manuellement.
- Filtrer par chant, voix, statut.
- Identifier sans ambiguïté la version de référence visible par les membres.

---

## Gestion des enregistrements

- Valider et publier un enregistrement `à valider`.
- Rejeter un enregistrement `à valider` (repasse en `brouillon`).
- Ajouter directement un enregistrement (déposé ou enregistré dans l'app).
- Archiver un enregistrement.
- Filtrer par chant, voix, statut, source.
- Gérer les enregistrements partagés **reçus** : voir la source, rattacher à un chant local.
- Gérer les enregistrements partagés **émis** : voir les destinataires, retirer un partage.
- Consulter l'historique : qui a publié quoi et quand.

---

## Gestion des listes de chants

- Créer une liste, nommer et choisir son type.
- Ajouter et ordonner les chants.
- Modifier l'ordre avant publication.
- Dupliquer une liste existante.
- Publier une liste (fige l'ordre).
- Rattacher une liste à un événement.

---

## Gestion des événements

- Créer un événement (titre, type, date, lieu).
- Rattacher une ou plusieurs listes de chants.
- Définir les membres ou groupes ciblés.
- Rédiger et publier des consignes.
- Publier l'événement.
- Suivre les confirmations de présence par voix et calculer le taux de réponse.
- Annuler ou archiver un événement.

---

## Consignes

- Écrire une consigne générale à toute la chorale.
- Écrire une consigne par voix.
- Écrire une consigne par chant.
- Écrire une consigne par événement.
- Consulter les consignes en cours avec leur date de publication.

---

## Suivi d'activité

- Écoutes par chant (nombre et tendance).
- Membres actifs sur 30 jours.
- Contenus peu ou jamais écoutés.
- Chants sans contenu complet.
- Indicateurs clés définis dans le fichier `09`.
