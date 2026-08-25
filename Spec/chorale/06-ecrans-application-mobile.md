# 06 — Application mobile

La surface mobile couvre les besoins de **tous les membres** (lecture, partition, événements) ainsi que les fonctions de production du **chef de pupitre** (enregistrement). Le responsable accède à ses fonctions de gestion via le site de gestion (`07`).

Chaque écran est décrit par son but, ses fonctions minimum et ses règles d'affichage spécifiques. Les règles métier et les droits sont dans `02`, `03` et `04`.

---

## Connexion et choix de chorale

**But** : accéder à son espace.

- Connexion par email et mot de passe.
- Récupération d'accès par lien email (durée limitée).
- Si plusieurs chorales : choix de la chorale active à la première connexion.
- Changement de chorale active accessible depuis n'importe quel écran.

---

## Accueil

**But** : afficher ce qu'il faut travailler maintenant.

- Prochain événement avec date et chants associés.
- Chants prioritaires pour la voix principale du membre.
- Reprise de la dernière écoute.
- Dernières consignes non lues.
- Accès rapide à la voix principale et aux partitions liées aux prochains événements.

**Affichage**

- La chorale active est toujours identifiable depuis cet écran.
- Si aucun événement à venir : les chants prioritaires occupent l'espace.

---

## Mes chants

**But** : retrouver rapidement un chant.

- Recherche par titre.
- Filtres : voix, événement, priorité, statut.
- Accès à toutes les voix de la chorale.
- Ouverture de la fiche chant ou lancement direct de la lecture.

---

## Fiche chant

**But** : rassembler le contenu de référence d'un chant.

- Titre et métadonnées (durée, tonalité, priorité).
- Lecture de l'enregistrement général publié.
- Lecture de l'enregistrement de la voix principale.
- Accès aux enregistrements des autres voix.
- Ouverture de la partition de référence publiée.
- Téléchargement si autorisé par le contenu.
- Consignes associées.
- Événements utilisant ce chant.

**Affichage**

- Si plusieurs partitions existent : la version `publiée` de référence est mise en avant.
- Si aucun enregistrement de voix n'existe : état explicite (`Enregistrement non disponible`), jamais vide.
- Si le contenu provient d'un partage inter-chorales : la chorale source est visible.

---

## Lecteur

**But** : écouter comme dans une application de streaming.

- Lecture / Pause — Suivant / Précédent.
- Titre et voix du chant en cours.
- Lecture maintenue avec écran verrouillé.
- File de lecture visible et navigable.
- Reprise de lecture au point d'arrêt.

---

## Listes de chants

**But** : lancer un ensemble cohérent de chants.

- Listes par événement, par saison, par pupitre.
- Lecture de la liste complète.
- Visualisation de l'ordre des chants.

---

## Événements

**But** : préparer un rendez-vous.

- Date, lieu et informations pratiques.
- Chants prévus avec accès direct à la fiche chant.
- Documents associés.
- Réponse de présence : `présent`, `absent`, `peut-être`.
- Affichage de l'état actuel de sa réponse.

**Affichage**

- Un événement `annulé` reste visible avec son statut affiché explicitement.

---

## Partition

**But** : consulter la partition de façon exploitable.

- Ouverture, zoom, pagination.
- Téléchargement si autorisé.
- Bascule entre partitions publiées si plusieurs versions existent pour le même type.

---

## Mon pupitre

**But** : recentrer le membre sur sa voix principale.

- Chants de la voix principale avec leur statut de complétude.
- Accès aux autres voix du membre.
- Identification du chef de pupitre de la voix.
- Consignes du pupitre.
- Accès direct aux enregistrements par voix.

---

## Enregistrement pupitre

**But** : permettre au chef de pupitre de produire un audio sans quitter l'application.

**Accès** : Chef de pupitre et Responsable uniquement.

- Sélection du chant et de la voix (limitée aux voix du chef de pupitre).
- Lecture d'une note de référence.
- Enregistrement direct dans l'application.
- Dépôt d'un fichier audio externe.
- Écoute de la prévisualisation.
- Suppression et nouvel essai.
- Envoi à validation — ou publication directe si délégation `publication pupitre` activée.

---

## Mon compte

**But** : consulter et modifier ses informations personnelles.

- Identité, voix principale, rôle, chorale active.
- Modification des champs autorisés.
- Déconnexion.
