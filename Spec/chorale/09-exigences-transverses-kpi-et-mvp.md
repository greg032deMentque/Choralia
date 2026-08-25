# 09 — Exigences transverses, KPI et MVP

---

## Exigences transverses

### Authentification et sécurité

- Authentification par email et mot de passe.
- Récupération d'accès par lien email à durée limitée.
- Expiration de session après inactivité (seuils définis en `10-D20`).
- API protégées contre accès non authentifiés et non autorisés — OWASP Top 10 en référence.

### Isolation des données

- Les contenus d'une chorale sont invisibles aux autres par défaut.
- L'appartenance multi-chorales n'entraîne aucune fusion de droits.
- Seuls les enregistrements explicitement partagés sont visibles entre chorales.

### Audit

Chaque trace d'audit est **non modifiable** et porte : auteur, date, objet concerné, action réalisée.

Actions auditées :

- publication et retrait d'une partition ou d'un enregistrement ;
- partage inter-chorales et retrait de partage ;
- changement de rôle sensible (chef de pupitre, responsable) ;
- tout accès de l'administration générale aux données d'un client.

### Exigences média

- Lecture continue sans interruption entre chants d'une liste.
- Reprise de lecture au point d'arrêt.
- Lecture avec écran verrouillé.
- Aucun mode hors ligne en V1 ; une connexion réseau est requise pour accéder aux contenus.
- Téléchargement conditionné à l'autorisation portée par le contenu.
- Toute nouvelle version d'une partition ou d'un enregistrement archive la précédente — pas d'écrasement silencieux.

### Notifications (V1 — canaux simples)

Déclencheurs couverts en V1 :

- invitation d'un nouveau membre ;
- publication d'un événement ;
- nouveau contenu publié sur un chant lié à un événement à venir ;
- nouvelle consigne publiée.

Canaux de notification V1 : `email` et `push mobile` (voir `10-D19`). Un moteur de préférences et d'orchestration avancées est hors scope MVP.

### Règle de priorisation des arbitrages

En cas de tension entre fonctionnalités, l'ordre de valeur métier est :

1. Lecture simple et continue.
2. Accès rapide à la bonne partition.
3. Couverture audio par voix.
4. Préparation des événements.
5. Pilotage et reporting.

---

## Indicateurs clés de suivi

### Par chorale (visible du responsable)

| Indicateur | Définition |
|---|---|
| Membres actifs (30j) | Membres connectés dans les 30 derniers jours |
| Taux actifs par voix | Part de membres actifs par pupitre |
| Taux de réponse présence | Réponses reçues / membres ciblés par événement |
| Chants avec partition de référence | Chants ayant au moins une partition `Published` |
| Chants avec enregistrement général | Chants avec un enregistrement général `Published` |
| Chants avec enregistrement par voix | Par voix : chants avec enregistrement dédié `Published` |
| Enregistrements à valider | Taille de la file d'attente de validation |
| Enregistrements partagés émis | Partages `actifs` depuis cette chorale |
| Écoutes moyennes par chant | Total écoutes / nombre de chants publiés |
| Chants jamais écoutés | Chants `Published` sans aucune écoute |
| Délai moyen création → complétude chorale | Entre création du chant et atteinte de la complétude chorale |
| Événements à venir | Événements `Published` dans le futur |

### Global (visible de l'administration générale)

| Indicateur | Définition |
|---|---|
| Clients actifs (30j) | Clients avec activité dans les 30 derniers jours |
| Clients non démarrés | Créés sans contenu ni membre actif |
| Clients à risque | Inactivité ou impayé |
| Chorales par client (moy.) | Nombre moyen de chorales par client actif |
| Membres actifs par chorale (moy.) | Nombre moyen de membres actifs par chorale |
| Taux d'activation | Clients avec au moins un événement et un contenu publié |
| Évolution membres actifs (mensuelle) | Tendance croissance / déclin |
| Taux de rétention client | Clients renouvelés / clients échus |
| MRR | Somme des abonnements actifs ramenée au mois |
| ARR | MRR × 12 |
| Abonnements actifs | Total des abonnements en cours |
| Abonnements en retard | Abonnements avec paiement dépassé |

---

## MVP recommandé

### Dans le scope

**Authentification et accès**
- Connexion, récupération d'accès, choix et changement de chorale active.

**Application mobile — Membre**
- Accueil avec chants prioritaires et prochain événement.
- Consultation et lecture de chants et listes de chants (lecture continue, écran verrouillé).
- Fiche chant : partition et enregistrements.
- Téléchargement si autorisé.
- Consultation d'un événement et confirmation de présence.
- Écran Mon pupitre.

**Application mobile — Chef de pupitre**
- Enregistrement direct ou dépôt audio pour sa voix.
- Note de référence (7 notes naturelles).
- Envoi à validation.

**Site de gestion — Responsable**
- Gestion des membres (invitation, rôle, voix, statut).
- Gestion des chants, partitions et enregistrements (création, publication, archivage).
- Validation et publication des enregistrements issus des chefs de pupitre.
- Gestion des événements (création, liste de chants, consignes, publication, présence).
- Partage manuel d'un enregistrement vers une autre chorale.
- Tableau de bord avec indicateurs de base.

**Administration générale**
- Vue clients (statut, abonnement, usage).
- Vue chorales par client.
- Tableau de bord global avec indicateurs de base.
- Accès support avec traçabilité.

### Hors scope MVP

| Sujet | Raison |
|---|---|
| Édition audio avancée | Hors positionnement produit en V1 |
| Mixage multipiste | Hors positionnement produit |
| Synchronisation partition / audio | Dépendance à librairies tierces non évaluées |
| Mode hors ligne complet | Surcharge technique prématurée |
| Annotations collaboratives sur partition | Aucun besoin V1 exprimé |
| Recommandations intelligentes | Requiert un historique d'usage suffisant |
| Gestion avancée des droits d'auteur | Cadrage légal non finalisé |
| Moteur de notification multi-canal avancé | Noyau métier à stabiliser en priorité |
| Altérations et multi-octaves (note de référence) | Extension naturelle post-MVP |
| Annotations personnelles sur chants/partitions | Valeur insuffisante en V1 |
| Voix configurables | Complexité modèle de données sans besoin V1 prouvé |
