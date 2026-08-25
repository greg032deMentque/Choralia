# 10 — Décisions tranchées et ouvertes

Registre des décisions produit. Chaque décision est soit **tranchée** (intégrée dans la spec, ne nécessite plus d'arbitrage), soit **ouverte** (à trancher avant la fin de la conception).

---

## Décisions tranchées

### D1 — Voix fixes en V1
**Décision** : quatre voix fixes — `Soprano`, `Alto`, `Tenor`, `Bass`.
**Raison** : simplifie le modèle de données et les filtres. Voix configurables = extension post-MVP.
**Impact** : toute demande de voix personnalisée est renvoyée à une V2.

---

### D2 — Gouvernance publication des enregistrements
**Décision** : validation par responsable par défaut. La délégation `publication pupitre` est activable par chorale ; lorsqu'elle est active, le chef de pupitre peut créer, publier, remplacer et archiver directement les enregistrements de son pupitre.
**Raison** : conserver un mode sécurisé par défaut tout en permettant une autonomie complète locale quand la chorale le souhaite.
**Impact** : le tableau de bord responsable affiche la file `PendingReview` uniquement pour les chorales ou pupitres non délégués.

---

### D3 — Périmètre du partage inter-chorales
**Décision** : V1 — enregistrements uniquement. Partitions et listes non partageables.
**Raison** : limite l'explosion des règles de droit et de provenance.
**Impact** : toute demande de partage de partition → V2.

---

### D4 — Mode de partage inter-chorales
**Décision** : lien vivant en lecture seule. Pas de copie locale.
**Raison** : pas de conflit de version, traçabilité conservée.
**Impact** : si la source est indisponible ou le partage retiré, le contenu disparaît immédiatement à la destination.

---

### D5 — Droit de téléchargement
**Décision** : contrôle par contenu — chaque partition et enregistrement porte un indicateur de téléchargement autorisé.
**Raison** : risque juridique et besoins différenciés des responsables.
**Impact** : indicateur obligatoire à la création de tout contenu.

---

### D6 — Partitions générales et par voix coexistantes
**Décision** : les deux types sont gérés dès V1 et coexistent.
**Raison** : besoin métier fréquent, nécessaire pour définir la complétude d'un chant.
**Impact** : la complétude d'un chant dépend du type de partition attendu par la chorale.

---

### D7 — Statut `PendingReview` sur enregistrements uniquement
**Décision** : les enregistrements ont un statut `PendingReview` intercalé, pas les partitions. Ce statut s'applique quand la délégation `publication pupitre` n'est pas activée.
**Raison** : les enregistrements peuvent être produits hors responsable ; les partitions restent publiées directement par le responsable via `Draft` → `Published`.
**Impact** : les workflows diffèrent selon le type de contenu et selon la configuration de délégation de la chorale.

---

### D8 — Note de référence (périmètre V1)
**Décision** : 7 notes naturelles (`Do` à `Si`) en notation française, restitution immédiate. Altérations et multi-octaves hors V1.
**Raison** : périmètre suffisant sans surcharge de la V1.
**Impact** : communiquer aux chefs de pupitre que les altérations arriveront en V2.

---

### D9 — Mode hors ligne
**Décision** : aucun mode hors ligne en V1. La lecture avec écran verrouillé est supportée, mais une connexion réseau reste requise pour accéder aux contenus.
**Raison** : supprimer toute ambiguïté produit et éviter une dette technique prématurée.
**Impact** : aucune promesse de consultation ni de lecture sans réseau n'est faite en V1.

---

### D10 — Définition d'un chant « complet »
**Décision** : la complétude est évaluée à deux niveaux. Pour la chorale : une partition de référence `Published` et tous les enregistrements par voix attendus sont requis. Pour un pupitre : une partition de référence `Published` et l'enregistrement `Published` de la voix du pupitre sont requis.
**Raison** : distinguer le besoin global de préparation de la chorale et le besoin opérationnel de chaque pupitre.
**Impact** : les tableaux de bord, alertes et indicateurs doivent afficher séparément la complétude chorale et la complétude par pupitre.

---

### D11 — Rattachement d'un partage à un chant local
**Décision** : rattachement toujours explicite. Aucune fusion automatique si un chant au même nom existe déjà.
**Raison** : éviter doublons et erreurs de rapprochement.
**Impact** : l'interface propose les chants existants + option de création d'un nouveau chant.

---

### D12 — Sous-moments d'un événement (mariage, office)
**Décision** : gérés comme des listes de chants distinctes rattachées au même événement.
**Raison** : pas de nouveau niveau de modèle — extensibilité via les listes existantes.
**Impact** : un événement `Wedding` peut porter plusieurs listes de chants.

---

### D13 — Annotations personnelles
**Décision** : hors scope MVP.
**Raison** : valeur insuffisante en V1.
**Impact** : les membres ne peuvent pas annoter chants ou partitions en V1.

---

### D14 — Notifications V1
**Décision** : niveau simple — invitation, publication d'événement, nouveau contenu, nouvelle consigne. Les notifications V1 utilisent `email` et `push mobile` ; préférences fines, ciblage avancé et orchestration multi-canal restent hors MVP.
**Raison** : combiner la fiabilité de l'email et l'immédiateté du push sans construire un moteur complexe.
**Impact** : chaque déclencheur V1 doit être prévu sur les deux canaux, avec fallback naturel sur l'email si le push n'est pas disponible.

---

### D15 — Droits d'auteur
**Décision** : stocker un indicateur de téléchargement autorisé et un propriétaire de contenu. Gestion avancée hors V1.
**Raison** : couvrir le risque légal minimal dès la mise en production.
**Impact** : propriétaire et indicateur obligatoires à la création de tout contenu.

---

### D16 — Politique d'archivage
**Décision** : archivage manuel et réversible pour tous les objets. Aucun objet supprimé physiquement en V1.
**Raison** : reporting fiable, historique préservé.
**Impact** : les objets archivés sont exclus des vues actives par défaut mais restent visibles dans l'historique. Suppression définitive → V2.

---

### D17 — Le responsable est aussi membre
**Décision** : le responsable hérite de toutes les capacités du membre (écoute, présence, consultation). Il n'a pas d'écran spécifique sur l'application mobile pour ses fonctions de gestion — il utilise le site de gestion.
**Raison** : éviter la duplication des fonctions de gestion sur deux surfaces.
**Impact** : si un responsable veut enregistrer sa voix, il utilise l'écran `Enregistrement pupitre` comme chef de pupitre.

---

### D18 — Seuil d'inactivité client
**Décision** : un client est signalé comme `à risque` après 30 jours sans activité authentifiée.
**Raison** : seuil assez court pour déclencher une action support ou commerciale avant décrochage durable.
**Impact** : les alertes d'administration et les KPI `clients actifs (30j)` / `clients à risque` utilisent ce seuil.

---

### D19 — Canal de notification V1
**Décision** : les déclencheurs de notification V1 sont envoyés par `email` et par `push mobile` lorsque l'appareil du membre est enregistré et autorisé.
**Raison** : garantir une couverture fonctionnelle sans dépendre d'un seul canal.
**Impact** : l'intégration technique doit prévoir les deux canaux dès le MVP, sans préférences utilisateur avancées.

---

### D20 — Durée d'expiration de session
**Décision** : expiration après 30 jours d'inactivité sur l'application mobile et 8 heures d'inactivité sur le site de gestion et l'administration. L'option `se souvenir de moi` est autorisée sur le web pour un appareil de confiance.
**Raison** : équilibre entre confort d'usage et niveau de sécurité attendu sur les surfaces d'administration et de gestion.
**Impact** : le web doit distinguer la durée de session applicative et la persistance locale du choix `se souvenir de moi`.

---

### D21 — Produit et tarification V1
**Décision** : un seul produit en V1, au même prix pour tous les clients. Il n'existe pas de catalogue de formules ni de paliers différenciés.
**Raison** : simplifier la commercialisation, la facturation et les écrans d'administration tant que le produit n'a pas encore validé ses segments ni sa stratégie de pricing.
**Impact** : les écrans `Abonnements et paiements` gèrent un abonnement unique par client (statut, périodicité, échéances, paiements), sans choix de formule ni logique de limites commerciales variables.

---

### D22 — Architecture web en trois zones et accès web des membres
**Décision** : le web est **une seule application** découpée en **trois zones différenciées par rôle**, avec redirection post-connexion selon le rôle :
- **Administration** (rôle global `Admin`) : création et gestion des chorales, gestion des utilisateurs administrateurs. **Aucune** gestion de contenu chorale (membres, chants, événements) dans cette zone.
- **Gestion chorale** (`Manager`, `SectionLeader`) : membres, chants, partitions, enregistrements, événements, consignes d'une chorale. C'est le site de gestion existant.
- **Espace membre web** (`Singer` simple) : **consultation + participation** (voir événements, chants, enregistrements, consignes, et y participer — présence, dépôt d'enregistrement selon droits) — équivalent web de l'application mobile membre.

**Raison** : séparer clairement les responsabilités par rôle, centraliser les fonctions dans une application unique (authentification, tokens, thème et composants partagés), et ouvrir un accès web aux membres simples auparavant réservés au mobile.

**Impact** :
- Évolution du modèle « membre = mobile uniquement » : le membre simple dispose désormais d'une surface web.
- La zone Administration opère **directement au niveau des chorales** (le rôle `Admin` crée et gère les chorales) — cohérent avec l'implémentation backend actuelle, mais divergent du palier `Client` décrit dans `02-roles-droits-et-visibilite.md` (voir décision ouverte D23).
- Le routage et les guards distinguent les trois zones sans dupliquer la logique de permission : source unique = rôles scopés par chorale (`Manager`/`SectionLeader`) + claim global `Admin`.
- Voir la table « Surfaces d'accès par rôle » dans `02-roles-droits-et-visibilite.md`.
- La zone Administration gère les clients depuis la décision `D23` : le point ci-dessus (« opère directement au niveau des chorales ») est révisé en conséquence.

---

### D23 — Palier `Client` : conservé, avec rôle client et limites de service
**Décision** : le palier `Client` est **conservé et implémenté**. Une entité `Client` regroupe une ou plusieurs chorales et porte trois responsabilités, et trois seulement :

1. **Identité et activation** — nom, contact, indicateur d'activité. Désactiver un client refuse l'accès à toutes ses chorales d'un seul geste.
2. **Autonomie du client** — un rôle `ClientManager`, scopé **au client** (troisième niveau de scope, à côté de l'espace), permet à une personne côté client de créer et fermer ses propres chorales, d'y nommer les responsables et de consulter les indicateurs consolidés de son périmètre — **sans détenir le claim global `Admin`**.
3. **Limites de service** — plafonds explicites portés par le client : nombre de chorales, nombre de membres, quota de stockage, taille maximale de fichier. `D21` posant un produit unique au même prix, ces plafonds sont des valeurs par défaut uniformes, surchargeables à la marge.

**Raison** :
- Sans palier, six des indicateurs globaux de `09` n'ont pas de dimension d'agrégation : « clients actifs (30j) », « clients non démarrés », « chorales par client », « membres actifs par chorale », « taux d'activation », « clients à risque ».
- Sans rôle client, **seul un opérateur interne peut créer une chorale**. C'est un plafond de croissance dès quelques dizaines de clients, et cela pousse à distribuer le claim `Admin` à des personnes qui ne devraient pas l'avoir — or ce claim court-circuite les contrôles d'appartenance par espace.
- Sans limites de service, il n'existe **aucun quota** : le dépôt est plafonné par fichier mais illimité en nombre, dans un stockage partagé par toutes les chorales. Un seul client peut saturer le service pour tous les autres. Et rétro-ajouter des quotas oblige à décider quoi faire des clients déjà au-dessus — poser les colonnes maintenant coûte quasi rien.

**Écarté explicitement** :
- **`Client` comme troisième type de `Space`.** Tentant, puisqu'il hériterait gratuitement des membres et des rôles via `SpaceMember`. Mais un client n'est pas un lieu où l'on chante : il hériterait de `Presence`, du RSVP et de `EndDate`, dénués de sens pour lui, et `Space` cesserait d'avoir une signification unique. C'est le mécanisme même qui a produit la collision de vocabulaire sur `Dossier`.
- **`Client` hiérarchique** (diocèse → paroisses → chorales). Réel dans le domaine, mais non demandé, et une hiérarchie auto-référencée rend récursive chaque agrégation et chaque contrôle de droit.

**Différé, sans être écarté** : faire du client une véritable **frontière d'isolation** (tout scope remontant au client, accès support et export RGPD scopés par client). Souhaitable, mais pas dans la même fenêtre qu'une migration `Space` fraîche — cela se construit au-dessus de la présente décision sans rien casser.

**Impact** :
- `Choir.ClientId` devient obligatoire. La liste des chorales doit être scopée par client — elle est aujourd'hui lisible par tout compte authentifié.
- La création d'une chorale passe de `Admin` global à `ClientManager` **ou** `Admin`.
- La matrice de `02` gagne un scope `client` ; `08` gagne la gestion des clients et de leurs plafonds ; `04` gagne la définition de l'objet `Client`, aujourd'hui absente partout.
- **Hors de cette décision** : abonnements et paiements. Le `Client` porte la FK d'accueil d'un futur `Abonnement`, pas la facturation. `D21` reste la cible et n'est pas révoquée ; le calendrier de la facturation reste à trancher.

---

### D24 — Modèle unifié « Espace » : chorale et événement
**Décision** : une chorale et un événement sont deux instances d'une même abstraction, l'`Espace` (`Space`, un espace géré, avec membres et rôles). La chorale est **permanente** ; l'événement est **daté** (`EndDate`) et peut exister **sans chorale** (autonome). Techniquement : entité `Space` en relation 1:1 à **clé primaire partagée** (`Space.Id == Choir.Id`, `Space.Id == Event.Id`), sans héritage C#.
**Raison** : un seul modèle mental et un seul jeu de composants ; le header `X-Chorale-Id` reste valide comme `SpaceId` sans traduction ; migration douce.
**Impact** : `MembreChorale` → `SpaceMember`, rôles scopés à l'espace, `Event.ChoirId` devient nullable, autorisation généralisée (`X-Space-Id`, policy `SpaceManager`). Trajectoire de migration expand → migrate → contract.

---

### D25 — Création d'événement et rôle Organisateur
**Décision** : **tout utilisateur authentifié** peut créer un événement et en devient automatiquement l'**Organisateur** (équivalent du Responsable pour cet espace). Les participants sont des utilisateurs avec un compte ; un invité sans compte se voit créer un **compte latent**.
**Raison** : ouvrir l'événementiel au-delà des responsables de chorale (mariages, prestations externes non rattachées à une chorale).
**Impact** : nouveaux rôles `Organizer` / `Participant` (ajoutés en fin d'`UserRoleEnum`). Rate limiting requis contre l'abus de création de comptes tiers.

---

### D26 — Présence : RSVP en V1
**Décision** : une seule dimension de présence en V1, le **RSVP d'intention** : `NoReply / Attending / Maybe / NotAttending`, modifiable tant que l'événement n'est pas terminé. Le pointage réel « jour-J » est hors V1.
**Raison** : couvre le besoin de planification sans surcharge ; extensible plus tard via un champ distinct.
**Impact** : champ `Presence` nullable sur `SpaceMember` (pertinent pour les espaces de type événement).

---

### D27 — Cycle de vie d'un événement unidirectionnel
**Décision** : la `EndDate` d'un événement est modifiable **tant qu'il n'est pas terminé** ; une fois **terminé, il est figé** (pas de réouverture). Un besoin récurrent (festival annuel) = un **nouvel événement** à chaque occurrence.
**Raison** : cycle de vie simple et non ambigu ; évite de « rallumer » des comptes invités et des présences déjà clôturés.
**Impact** : pas de fonction « prolonger un terminé » ; la clôture est définitive.

---

### D28 — Comptes invités : veille, réactivation et RGPD
**Décision** : un participant invité sans compte reçoit un **compte latent**. La désactivation de fin d'événement est déclenchée par l'**action explicite « Clôturer »** de l'organisateur (jamais un worker de fond), complétée par un garde-fou au login. À la clôture, un invité **jamais revendiqué** (pas de mot de passe défini, `EmailConfirmed=false`, aucune autre appartenance) est **anonymisé** (PII effacée, participation conservée en anonyme) ; un invité **converti** garde son compte, simplement « en veille ». Réactivation transparente à la ré-invitation (recherche par email). Collecte minimale (email + prénom). **Rétention** : les données personnelles d'un compte invité jamais réactivé sont anonymisées après **12 mois** d'inactivité, via une purge **déclenchée manuellement** (opération d'administration), jamais un worker de fond.
**Raison** : conformité RGPD sans job planifié (contrainte projet « pas de worker »), en s'appuyant sur l'action de clôture existante.
**Impact** : champ `User.IsGuestAccount` ; flux d'anonymisation à la clôture ; login déjà bloqué si `IsDeleted`.

---

### D29 — Vocabulaire et atterrissage
**Décision** : un compte invité désactivé est présenté comme **« accès en veille »** (jamais « désactivé » / « supprimé »). À la connexion, l'utilisateur atterrit sur **« Mon accueil »** (agrégat cross-espaces), avec un raccourci « reprendre » ; exception **mono-contexte** (un seul espace → accès direct).
**Raison** : réversibilité perçue, point d'entrée stable en contexte multiple.
**Impact** : à figer dans `11-ux-ui.md` (tokens et vocabulaire).

---

### D30 — Aucun indicateur sans source réelle
**Décision** : un indicateur ne s'affiche que si un appel de données réel l'alimente. Pas de valeur en dur, pas de tuile à zéro, pas de squelette permanent : l'indicateur est **absent** de l'écran tant que sa source n'existe pas. Un écran dont aucun indicateur n'est disponible est retiré de la navigation plutôt que présenté vide.
**Raison** : le tableau de bord a porté des chiffres inventés, avec des noms de personnes et des taux de réponse plausibles. Un responsable les croit — un indicateur faux est plus nuisible qu'un indicateur absent.
**Impact** : les mocks ont été retirés du tableau de bord. Les indicateurs calculables sur l'existant peuvent être branchés immédiatement (chants au répertoire, chants incomplets, enregistrements à valider, membres et invités, prochains événements, taux de réponse de présence via `SpaceMember.Presence`). Les autres restent hors écran, inscrits dans un backlog nommant l'endpoint attendu pour chacun. Aucun écran ne porte de mention « bientôt disponible » : c'est une impasse pour l'utilisateur.

---

### D32 — Architecture web étendue à quatre zones : « Ma structure »
**Décision** : `D22` posait trois zones (Administration, Gestion chorale, Espace membre). Une quatrième zone, **Ma structure** (`/client/:clientId`), est ajoutée pour le rôle `ClientManager` : chorales de son client, plafonds de service en lecture, désignation de responsables. Le mot « Client » n'apparaît dans aucun texte visible hors de `/admin` — le libellé utilisateur est « Ma structure ». Point conceptuel qui reste valable pour les quatre zones : la zone affichée est une propriété du couple **(utilisateur, espace actif)**, pas de l'utilisateur seul — changer d'espace actif peut changer de zone.
**Raison** : `D23` a doté `ClientManager` d'un droit réel (créer/fermer ses chorales, y nommer des responsables) sans lui donner de surface pour l'exercer ; sans écran dédié, ce droit ne restait accessible que par appel direct à l'API.
**Impact** : l'ordre de priorité Admin > Gestion > Ma structure > Espace membre > aucun espace régit la **zone par défaut** — redirection post-connexion, cible de repli des guards, route racine `/` —, centralisée dans un seul module (`ChoralFront/src/app/core/zone-resolver.ts`). Il ne gouverne jamais la zone affichée pendant la navigation : celle-ci dérive de la route courante et du rôle sur l'espace actif (voir `02` § Surfaces d'accès par rôle). Naviguer explicitement vers une zone non prioritaire est un usage normal ; le menu et le contexte affichés suivent alors cette zone. La table « Surfaces d'accès par rôle » de `02` gagne une colonne. `D22` n'est pas annulée, elle est étendue — voir `docs/Architecture-web-et-roles.md`.

---

### D33 — Statut métier sur `Choir`, distinct de celui de l'événement
**Décision** : `Choir` porte un statut (`Draft`/`Published`/`Cancelled`/`Archived`, `ChoirStatusEnum`), enum **distinct** de `EventStatusEnum` malgré des ordinaux et une forme identiques. `IsDeleted` retrouve son seul rôle : la suppression — l'archivage (réversible) et la suppression (définitive) étaient auparavant confondus sur ce seul champ.
**Raison** : avant ce statut, une chorale interrompue ou fermée n'avait que `IsDeleted` pour s'exprimer, ce qui ne permettait ni annulation temporaire ni fermeture réversible. Fusionner le statut avec celui de l'événement ferait qu'une évolution du cycle de vie de l'un modifierait silencieusement celui de l'autre — les deux entités ont des besoins voisins mais pas identiques (une chorale n'a pas de date de fin, donc pas d'état effectif calculé).
**Impact** : migration historique `AjouteStatutChorale` (squashée depuis, voir `docs/reste-a-faire.md` § « Migration — historique réinitialisé »), transitions bornées par `ChoirStateHelper.IsTransitionAllowed` (`Draft`→`Published`\|`Archived`, `Published`→`Cancelled`\|`Archived`, `Cancelled`→`Published`\|`Archived`, `Archived`→`Published`). `02` et `04` documentent la visibilité et le cycle de vie associés. Une chorale créée aujourd'hui est toujours immédiatement `Published` : `Draft` reste inatteignable tant que l'inscription auto-service n'existe pas (voir `docs/reste-a-faire.md`).

---

### D34 — `ClientId` porté par `Space`, chorale et événement confondus
**Décision** : `Space.ClientId` devient obligatoire pour toute chorale et tout événement, y compris un événement autonome (sans chorale porteuse). `ServiceLimitService` résout désormais le client d'un espace par un chemin unique, quel que soit son type.
**Raison** : avant cette décision, un événement autonome n'avait aucun rattachement client et échappait donc à tous les plafonds de service (`D23` : nombre d'espaces, quota de stockage, taille de fichier) — un seul organisateur pouvait saturer le service sans qu'aucun plafond ne s'applique à lui.
**Impact** : migration historique `AjouteClientSurEspace` (squashée depuis, voir `docs/reste-a-faire.md` § « Migration — historique réinitialisé »), avec création d'un client technique de migration (« Événements sans structure — à rattacher ») pour rattacher les événements autonomes préexistants. Ces événements remontent en anomalie sur le tableau de bord admin et sur la liste des événements admin, jusqu'à un rattachement manuel à un vrai client — aucun mécanisme automatique de rattachement n'est construit.

---

### D35 — Administration générale : lecture sur le contenu, jamais l'écriture
**Décision** : l'administration générale peut désormais lire, en mode support tracé, la liste des membres, des chants et des événements d'une chorale (`AdminChoirController`), ainsi qu'un catalogue de chants transverse à tous les clients (`AdminSongController`). Elle ne modifie **jamais** ce contenu — aucun endpoint d'écriture n'existe sur les membres, chants ou événements d'une chorale depuis l'administration générale. Elle continue de modifier le nom, la description et le statut de la chorale elle-même (pas son contenu).
**Raison** : le support (diagnostiquer un problème signalé par un client) nécessite de voir le contenu d'une chorale sans nécessiter d'y écrire ; ouvrir l'écriture aurait recréé, par un autre chemin, le court-circuit des contrôles d'appartenance par espace que `D23` cherche justement à éviter en dotant le client de son propre rôle.
**Impact** : `02` (matrice des actions) distingue désormais les lignes de lecture support (`✓ tracé`) des lignes d'écriture (`—`) pour l'administration générale. Partitions, enregistrements et consignes restent totalement hors d'atteinte de l'administration générale, y compris en lecture.

---

### D36 — L'administrateur ne crée plus de chorale
**Décision** : `ChoirController.Create`/`Update`/`Delete` passent de `[Authorize(Roles = "Admin")]` à la policy `AdminOrClientManager` (satisfaite par le claim `Admin` ou par `ClientManager` sur le client visé). `AddMember`/`RemoveMember` passent à `SpaceManager`. Ceci résorbe l'écart constaté le 2026-07-29 sur `D23` (le rôle `ClientManager` existait sans pouvoir exercer la responsabilité qui le justifie).
**Raison** : `D23` posait que le `ClientManager` devait pouvoir créer et fermer ses propres chorales sans intervention d'un opérateur interne ; tant que seul `Admin` le pouvait, ce rôle ne servait à rien et la distribution du claim `Admin` à des personnes côté client restait la seule voie de contournement — ce que `D23` visait justement à éliminer.
**Impact** : conséquence à documenter explicitement (`docs/reste-a-faire.md`) : tant que l'inscription auto-service (lot 6) n'existe pas, cette policy élargie reste peu visible en pratique — rien ne crée de chorale hors du seed de démonstration ou d'un appel direct à l'API par `Admin`/`ClientManager`. Une chorale créée est toujours publiée immédiatement (voir `D33`).

---

### D37 — Regroupement d'affichage des chants, sans entité « Œuvre »
**Décision** : le catalogue de chants transverse de l'administration générale regroupe les chants par titre et compositeur normalisés (`SongKeyHelper`), à l'**affichage seulement** — aucune entité « Œuvre » n'est créée en base. Chaque chorale garde son propre `Song`. Un chant sans compositeur n'est jamais fusionné avec un autre.
**Raison** : une entité « Œuvre » aurait nécessité un rapprochement fiable entre chorales (le même titre, déposé par des chorales différentes, ne désigne pas toujours la même œuvre) et une migration de données pour tout l'historique existant — sans bénéfice fonctionnel immédiat, le seul besoin exprimé étant la lecture consolidée côté administration.
**Impact** : la clé de regroupement est calculée explicitement en `ToLowerInvariant`, jamais déléguée à la collation SQL Server (le poste de développement tourne sous Windows/NLS, la production sous Ubuntu/ICU — un même regroupement doit produire le même résultat des deux côtés).

---

### D38 — Unicité du nom de client levée
**Décision** : `Client.Name` n'est plus contraint à l'unicité parmi les clients actifs, contrairement à ce que prévoyait initialement `04`.
**Raison** : le nom est un libellé d'exploitation destiné à l'affichage, pas une clé d'identification — l'identité d'un client repose sur son `Id`, jamais sur son nom.
**Impact** : `04` § Client reflète la contrainte levée. Aucune validation d'unicité n'est effectuée à la création (`ClientService.CreateAsync`) ni à la modification (`ClientService.UpdateAsync`) du nom.

---

### D39 — Le rôle `Organizer` n'existe que sur un événement autonome
**Décision** : un `Organizer` ne peut être affecté qu'à un événement **non rattaché à une chorale**. Un événement de chorale est géré par les `Manager` de sa chorale porteuse, qui y exercent les mêmes capacités ; aucun organisateur n'y est affectable. Le rattachement d'un événement à une chorale se décide à sa création et est **définitif** : aucune action ne permet de rattacher après coup un événement autonome à une chorale, ni l'inverse.

**Raison** : deux chemins d'autorité concurrents sur un même espace, sans règle pour les départager, est une ambiguïté qu'aucun écran ne peut résoudre. Un événement de chorale appartient à la chorale — c'est elle qui décide, et ses responsables sont déjà identifiés. `D25` posait la création d'événement et le rôle `Organizer` sans distinguer les deux cas de figure ; cette décision comble ce vide, elle ne la contredit pas.

**Impact** : `02` documente désormais `Organizer` et `Participant`, absents jusqu'ici de la source unique de vérité sur les rôles, ainsi que le tableau de partage de responsabilité entre événement de chorale et événement autonome. Côté code, l'affectation d'un `Organizer` sur un événement portant un `ChoirId` est refusée (`EventAuthorizationService.EnsureOrganizerAssignable`), et le rattachement figé est fait respecter par `EventStateHelper.IsChoirIdChangeRequested` dans `EventService.UpdateAsync` — vérifié dans le code au 2026-07-31. `EventParticipantService` continue d'accepter « organisateur de l'événement **ou** responsable de la chorale porteuse » : cette formulation reste correcte, les deux branches devenant mutuellement exclusives par construction.

---

### D40 — Identité produit « Choralia » et charte « Encre & laiton »
**Décision** : le produit s'appelle **Choralia**. Sa charte repose sur une primaire encre `#2C3A5B`, un accent laiton `#C89A4E` et des neutres chauds sur fond crème `#FAF9F7` ; les titres sont composés en serif (Source Serif 4), l'interface en Inter. Le lexique affiché est figé : **chorale**, **choriste**, **chef de chœur**, **chef de pupitre**, **organisateur** (sur un événement autonome, voir `D39`), **Ma structure** (pour un client, hors `/admin`). Le mot « espace » est un terme de code : il ne s'affiche jamais.

**Raison** : la charte précédente (`#4A6FA5` / `#E8A838`) posait douze couleurs plates sans échelle ni couche sémantique — chaque composant inventait ses nuances, et deux orangés voisins servaient à la fois d'accent de marque et d'alerte, donc confondables. Le registre encre et laiton correspond au public réel du produit (chorales classiques et liturgiques), que le bleu-gris d'origine n'évoquait pas. Le nom « ChoraleHelper » était un nom de travail franglais dans une interface entièrement française.

**Impact** : `11` §0 (nouveau brief), §2.1, §2.1 bis, §2.2, §3.1 et §7.2 sont réécrits. Les tokens sont réorganisés en deux niveaux — palette brute et tokens sémantiques — et **un composant qui référence la palette directement est une violation**, au même titre qu'une valeur en dur. Les anciens noms `--color-*` survivent en alias le temps de la migration des composants. Le renommage ne touche ni le dépôt, ni les namespaces `ChoraleBackEnd.*`, ni les dossiers : le nom produit n'est pas un identifiant de code. Côté back, le nom apparaît dans `appsettings.json`, `appsettings.Development.example.json` et `RegistrationService.cs` (emails).

---

### D41 — L'administration générale n'a pas de chrome distinct ni de masquage
**Décision** : trois dispositions de `11` §8.3 sont abandonnées — la barre latérale sombre censée distinguer la surface opérateur, le masquage « cliquer pour révéler » des données sensibles, et le mode support tracé. L'administration partage l'habillage du site de gestion ; la frontière passe par le fil d'Ariane et un libellé de zone. L'administration **consulte** les données d'une chorale et n'agit jamais sur son contenu.

**Raison** : ces trois dispositions protègent contre des risques d'équipe — un opérateur qui ne sait pas dans quelle surface il se trouve, un partage d'écran devant témoin, une action non tracée attribuée à la mauvaise personne. L'opérateur est unique et c'est l'éditeur lui-même. Maintenir un second habillage complet et un clic de révélation sur chaque donnée coûterait un effort permanent pour un risque nul, au détriment des écrans réellement utilisés (`11` §0).

**Impact** : `11` §8.3 est révisé en conséquence. Cette décision est à rouvrir si l'administration s'ouvre un jour à une équipe de support : le chrome distinct et la traçabilité redeviendraient alors nécessaires, et la couche sémantique de tokens introduite par `D40` permet de les rétablir en surchargeant les cinq variables `nav-*`, sans toucher au style des composants.

---

### D42 — Confirmation ou annulation : la réversibilité tranche, pas la gravité ressentie
**Décision** : une action destructive n'ouvre une modale de confirmation que si elle est **réellement irréversible**. Si l'API expose l'action inverse, l'action s'exécute immédiatement et son annulation est portée par le toast pendant 5 secondes (`ToastService.undoable`). Le `confirm()` natif du navigateur est proscrit sur toutes les surfaces.

Application au périmètre actuel :

| Action | Inverse disponible | Traitement |
|---|---|---|
| Archiver une partition, un enregistrement | `restore` | Toast d'annulation |
| Retirer un chant d'une liste | `addSong` (avec position) | Toast d'annulation |
| Archiver un chant | *aucun* | Modale |
| Archiver un événement | *aucun* (transition terminale) | Modale |
| Annuler un événement | *aucun* | Modale — conséquence visible des participants |
| Rejeter un enregistrement | *aucun* | Modale — conséquence visible de son auteur |
| Supprimer (chant, événement, liste, partition, enregistrement) | *aucun* | Modale |

**Raison** : une modale sur une action annulable en un clic est de la friction sans bénéfice — l'utilisateur apprend à cliquer « Confirmer » sans lire, ce qui dégrade la protection là où elle compte vraiment. Inversement, un bouton « Annuler » sur une action sans inverse serait un mensonge. Le critère est donc technique et vérifiable — l'endpoint inverse existe ou non — et non une appréciation de gravité, qui varie d'un développeur à l'autre et produit l'incohérence constatée entre écrans.

**Impact** : `11` §6.4. Ajouter une action destructive impose de vérifier d'abord si son inverse existe côté API ; si oui, c'est un toast d'annulation, pas une modale. `ToastService.undoable` ne déclenche l'annulation que sur clic explicite : l'expiration du délai vaut acceptation. Les actions à impact chiffré (suspension d'un client, purge RGPD) conservent en plus le mot-clé de confirmation à saisir, indépendamment de cette règle.

---

### D43 — Une consigne n'a qu'une cible : le chant
**Décision** : les consignes ne portent plus que sur un **chant**. Les trois autres portées prévues à l'origine — générale (toute la chorale), par voix (un pupitre), par événement — sont **supprimées du modèle**, pas seulement dépourvues d'écran. Il n'existe donc plus d'écran « Consignes » transverse : une consigne se crée, se publie, s'archive et se supprime depuis l'écran de son chant.

Le ciblage d'un pupitre subsiste, mais **à l'intérieur du chant** : le champ `VoicePart` d'une consigne est optionnel — vide, la consigne s'adresse à tout le chœur sur ce chant (responsable seul) ; renseigné, c'est une consigne de pupitre sur ce chant, seul cas ouvert au chef de pupitre, et uniquement sur sa propre voix.

**Raison** : une consigne détachée de son chant n'a pas de lieu naturel où être lue. Concrètement, aucun filtre de l'API ne pouvait exprimer « toutes les consignes de la chorale active, toutes portées » : filtrer par chorale excluait les portées chant et événement (leur `ChoirId` était nul), ne pas filtrer mélangeait les chorales d'un responsable multi-chorales. La portée événement était par ailleurs **inatteignable** — la policy d'écriture exige `Manager`/`SectionLeader` sur l'espace actif, ce qu'un organisateur d'événement n'a jamais, et seuls les événements rattachés à une chorale auraient abouti.

**Impact** :
- `04` § Types de consignes réduit à une seule ligne.
- Migration `InstructionsSongScopeOnly` : colonnes `Scope`, `ChoirId`, `EventId` supprimées, `SongId` rendu obligatoire, contrainte `CK_Instruction_Scope` retirée. **Perte de données assumée** — les consignes des trois portées supprimées sont effacées ; `Down()` recrée le schéma, jamais les lignes.
- `InstructionScopeEnum` supprimé, et avec lui ses cas dans `EnumOrdinauxTests`.
- Front : route `/gestion/:espaceId/instructions`, entrée de barre latérale « Consignes » et `RoutePaths.Instructions` supprimés ; le panneau vit dans `SongDetailComponent`.
- Réintroduire une portée suppose de rouvrir cette décision, pas d'ajouter une valeur d'énumération.

---

## Décisions ouvertes

### D31 — Calendrier de la facturation
**Question** : `D21` fixe le produit et la tarification (un produit unique au même prix), et `D23` conserve le palier `Client` mais **sans** facturation — le client porte l'identité, l'activation et les plafonds de service, pas les abonnements.
**À trancher** : construire les abonnements et paiements dans la foulée du palier `Client`, ou les reporter explicitement après la première mise en service.
**Impact tant que non tranché** : les sections « Suivi commercial et activité » et « Abonnements et paiements » de `08` restent non construites, et six indicateurs globaux de `09` restent sans source — `MRR`, `ARR`, `abonnements actifs`, `abonnements en retard`, `taux de rétention`, `clients à risque (impayé)`. Conformément à `D30`, ils ne doivent pas s'afficher à vide : ils restent hors écran jusqu'à ce que leur source existe.
