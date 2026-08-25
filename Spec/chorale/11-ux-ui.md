# 11 — Spécification UX/UI

Ce document définit les principes, le système visuel et les patterns d'interaction applicables à l'ensemble des surfaces de l'application : mobile (`06`), site de gestion (`07`) et administration générale (`08`). Il est normatif — toute dérogation doit être documentée dans `10`.

---

## 0. Brief — pour qui et dans quel registre

**Choralia** est un outil pour des chefs de chœur bénévoles de chorales classiques et liturgiques, de 25 à 150 choristes, qui l'utilisent en sessions longues (30 minutes et plus), sur ordinateur portable ou tablette, parfois pendant la répétition. L'interface est **sobre et chaleureuse**, jamais technique : elle porte le nom de la chorale, pas le sien.

Conséquences directes, à opposer à toute proposition qui les contredirait :

| Ce qui est vrai du public | Ce que ça impose |
|---|---|
| Bénévoles, aisance informatique moyenne | Libellés explicites plutôt qu'icônes seules ; pas de raccourci sans équivalent visible |
| Sessions longues | Densité confortable, pas de compression de l'information |
| Chorales classiques et liturgiques | Registre visuel du répertoire : serif pour les titres, neutres chauds, accent laiton |
| Portable, tablette, téléphone | Le site de gestion est **responsive de 375 px à 1440 px** — ce n'est pas un site desktop |
| L'utilisateur est chez lui | Le nom de la chorale domine ; le nom du produit s'efface une fois connecté |

Les écrans où l'utilisateur passe le plus de temps — **chants, partitions, enregistrements, événements** — sont prioritaires sur tous les autres en cas d'arbitrage.

---

## 1. Principes de design

### 1.1 Clarté avant densité
Chaque écran expose une action principale. Les informations secondaires sont accessibles par progression (expand, modal, page de détail), jamais empilées sur la vue initiale.

### 1.2 Feedback immédiat
Toute action utilisateur déclenche une réponse visible en moins de 100 ms (state change, spinner, toast). Aucune action ne reste silencieuse.

### 1.3 Contenu d'abord
Les contenus audio et les partitions sont le cœur du produit. Toute UI qui concurrence visuellement le contenu est réduite (iconographie légère, chrome minimal en mode lecture).

### 1.4 Progression visible
L'état d'avancement (chant complet / incomplet, enregistrement publié / à valider) est toujours représenté par un indicateur visuel, pas seulement par du texte.

### 1.5 Dépendance réseau explicite
En V1, aucun mode hors ligne n'est promis. En cas de perte de réseau, l'interface affiche un état d'indisponibilité explicite et désactive les actions impossibles, jamais silencieusement.

### 1.6 Accessibilité par défaut
Les critères WCAG 2.1 niveau AA s'appliquent dès la conception, non en post-traitement (voir section 7).

---

## 2. Design tokens

Les tokens sont la source de vérité partagée entre mobile (Ionic Angular) et web (Angular). Toute valeur codée en dur dans un composant est une violation.

Ils se lisent sur **deux niveaux**, et cette séparation est normative :

1. la **palette** (`ink-*`, `brass-*`, `stone-*`) : les couleurs brutes. **Aucun composant ne la référence directement.**
2. les tokens **sémantiques** (`bg-*`, `text-*`, `border-*`, `action-*`, `nav-*`) : ce que les composants consomment.

C'est cette indirection qui permet de rhabiller une zone, d'introduire un co-branding client ou un mode sombre sans rouvrir le style des composants. Un composant qui pointe sur la palette est une violation au même titre qu'une valeur en dur.

### 2.1 Palette — « Encre & laiton »

**Encre** — primaire, actions, texte :

| Token | Valeur | | Token | Valeur |
|---|---|---|---|---|
| `ink-50` | `#F2F4F8` | | `ink-500` | `#43537A` |
| `ink-100` | `#E2E6EF` | | `ink-600` | `#2C3A5B` — **primaire** |
| `ink-200` | `#C4CBDA` | | `ink-700` | `#222E49` |
| `ink-300` | `#9BA6BF` | | `ink-800` | `#1B2438` — **texte principal** |
| `ink-400` | `#6C7B9C` | | `ink-900` | `#141C2E` |

**Laiton** — accent de marque **uniquement** (logo, marqueur d'élément actif, priorité haute) :

| Token | Valeur | Usage |
|---|---|---|
| `brass-300` | `#E3C88E` | Fond d'accent très clair |
| `brass-400` | `#D6B06B` | Bordure, filet |
| `brass-500` | `#C89A4E` | Accent de marque |
| `brass-600` | `#A87C33` | Icône, trait épais |
| `brass-700` | `#8A6323` | **Seule teinte laiton autorisée pour du texte** |

> **Règle absolue** : le laiton ne porte jamais de texte blanc (2,6:1). Il n'apparaît jamais non plus dans un contexte d'état — c'est une couleur d'identité, pas de signalisation.

**Papier** — neutres chauds, ils portent le registre chaleureux du brief :

| Token | Valeur | Usage |
|---|---|---|
| `paper-0` | `#FFFFFF` | Fond de carte |
| `stone-50` | `#FAF9F7` | Fond de page |
| `stone-100` | `#F3F1ED` | Fond creusé, survol |
| `stone-200` | `#E6E2DA` | Séparateurs, bordures |
| `stone-300` | `#D2CCC0` | Bordure appuyée |
| `stone-400` | `#A39C8D` | Texte désactivé |
| `stone-500` | `#6B6558` | Texte secondaire |
| `stone-600` | `#4F4A40` | Texte sur fond neutre |

**États** — conventions universelles, inchangées :

| Token | Valeur | Usage |
|---|---|---|
| `state-success` | `#2E7D32` | Publié, confirmation |
| `state-warning` | `#F57C00` | À valider, alerte |
| `state-error` | `#C62828` | Rejeté, erreur, impayé |
| `overlay` | `rgba(20,28,46,0.48)` | Fond de modal |

### 2.1 bis Tokens sémantiques

| Token | Pointe sur | Usage |
|---|---|---|
| `bg-page` / `bg-surface` / `bg-sunken` | `stone-50` / `paper-0` / `stone-100` | Fonds |
| `bg-hover` / `bg-selected` | `stone-100` / `ink-50` | États d'interaction |
| `text-primary` / `text-muted` / `text-inverse` | `ink-800` / `stone-500` / `paper-0` | Texte |
| `text-accent` / `text-link` | `brass-700` / `ink-600` | Texte d'accent et liens |
| `border-subtle` / `border-strong` / `focus-ring` | `stone-200` / `stone-300` / `ink-500` | Traits |
| `action-bg` / `action-bg-hover` / `action-fg` | `ink-600` / `ink-700` / `paper-0` | Bouton primaire |
| `action-danger-bg` / `action-danger-fg` | `state-error` / `paper-0` | Bouton destructif |
| `accent` / `accent-strong` | `brass-500` / `brass-600` | Marque |
| `nav-bg` / `nav-fg` / `nav-fg-active` / `nav-bg-active` / `nav-border` | surface / `text-muted` / `action-bg` / `ink-50` / `border-subtle` | Navigation — **seul point de rhabillage d'une zone** |

### 2.2 Typographie

**Deux familles.** **Inter** (variable) pour toute l'interface ; **Source Serif 4** (variable) pour les titres, du `display` au `heading-2` inclus. La serif ne descend jamais plus bas : aux petites tailles elle nuit à la lisibilité. Les deux sont sous licence SIL OFL et destinées à être auto-hébergées.

Familles de secours : `system-ui, -apple-system, sans-serif` et `Georgia, 'Times New Roman', serif`.

| Token | Taille | Poids | Interligne | Usage |
|---|---|---|---|---|
| `text-display` | 28 px | 600 | 1.2 | Titre de page, nom de chorale — *serif* |
| `text-heading-1` | 22 px | 600 | 1.3 | Titre de section — *serif* |
| `text-heading-2` | 18 px | 600 | 1.4 | Titre de carte, nom de chant — *serif* |
| `text-body` | 15 px | 400 | 1.6 | Corps de texte |
| `text-body-medium` | 15 px | 500 | 1.6 | Label de champ, navigation |
| `text-caption` | 12 px | 400 | 1.5 | Métadonnées, timestamps |
| `text-overline` | 11 px | 600 | 1.4 | Étiquettes de section (uppercase) |

Taille minimale lisible : **12 px**. Aucun texte fonctionnel sous ce seuil.

### 2.3 Espacements

Échelle à base 4 px :

| Token | Valeur | Usage type |
|---|---|---|
| `space-1` | 4 px | Gap interne d'icône |
| `space-2` | 8 px | Padding inline d'un badge |
| `space-3` | 12 px | Gap entre icône et label |
| `space-4` | 16 px | Padding standard d'une carte |
| `space-5` | 20 px | Espacement entre items de liste |
| `space-6` | 24 px | Marge de section |
| `space-8` | 32 px | Espacement entre blocs majeurs |
| `space-12` | 48 px | Marges de page (desktop) |

### 2.4 Coins arrondis

| Token | Valeur | Usage |
|---|---|---|
| `radius-sm` | 4 px | Badges, tags |
| `radius-md` | 8 px | Cartes, inputs |
| `radius-lg` | 16 px | Modales, bottom sheets |
| `radius-full` | 9999 px | Avatars, boutons pill |

### 2.5 Élévations (ombres)

| Token | Valeur CSS | Usage |
|---|---|---|
| `shadow-sm` | `0 1px 3px rgba(0,0,0,0.08)` | Carte au repos |
| `shadow-md` | `0 4px 12px rgba(0,0,0,0.12)` | Carte survolée, dropdown |
| `shadow-lg` | `0 8px 32px rgba(0,0,0,0.18)` | Modal, bottom sheet |
| `shadow-none` | `none` | État désactivé |

### 2.6 Durées d'animation

| Token | Valeur | Usage |
|---|---|---|
| `duration-fast` | 100 ms | Feedback immédiat (press, focus) |
| `duration-base` | 200 ms | Transitions de state |
| `duration-slow` | 350 ms | Entrée de modal, bottom sheet |
| `easing-standard` | `cubic-bezier(0.4, 0, 0.2, 1)` | Transition générale |
| `easing-enter` | `cubic-bezier(0, 0, 0.2, 1)` | Élément entrant |
| `easing-exit` | `cubic-bezier(0.4, 0, 1, 1)` | Élément sortant |

---

## 3. Système de composants

### 3.1 Atomes

#### Boutons

| Variante | Fond | Texte | Usage |
|---|---|---|---|
| Primary | `color-primary` | Blanc | Action principale unique par vue |
| Secondary | Transparent | `color-primary` + bordure | Action secondaire |
| Destructive | `color-error` | Blanc | Suppression, désactivation |
| Ghost | Transparent | `color-neutral-600` | Action tertiaire, annulation |
| Icon-only | Transparent | — | Lecteur, toolbar |

États obligatoires sur tous les boutons : `default`, `hover`, `pressed`, `disabled`, `loading` (spinner inline).

Taille minimale de zone tactile : **44 × 44 px** (voir section 7).

#### Champs de saisie

Structure : `label` (au-dessus) → `input` → `helper text` ou `error message` (en-dessous).

- Hauteur : 48 px sur mobile, 40 px sur web.
- Bordure repos : `color-neutral-200`. Focus : `focus-ring` (2 px). Erreur : `color-error` (2 px).
- Le label ne disparaît jamais (pas de placeholder-as-label).
- Le message d'erreur est explicite et actionnable : *"Email invalide — vérifiez le format"*, pas seulement *"Erreur"*.

#### Badges / tags de statut

| Statut | Fond | Texte | Contraste |
|---|---|---|---|
| Publié | `status-published-bg` `#E8F5E9` | `status-published-fg` `#276B2A` | 5,8:1 |
| À valider | `status-pending-bg` `#FFF3E0` | `status-pending-fg` `#8A4B00` | 6,2:1 |
| Rejeté | `status-rejected-bg` `#FFEBEE` | `state-error` | 4,9:1 |
| Brouillon | `status-neutral-bg` `stone-200` | `status-neutral-fg` `stone-600` | 6,8:1 |
| Archivé | `status-neutral-bg` | `status-neutral-fg` (italique) | 6,8:1 |
| Annulé | `status-neutral-bg` | `status-neutral-fg` (barré) | 6,8:1 |

Le texte d'un badge est **plus sombre que la couleur d'état correspondante** : `state-success` et `state-warning` échouent tous deux sur leur propre fond pastel. C'est la raison d'être des tokens `status-*-fg`.

Tous les badges incluent un texte lisible — jamais de couleur seule comme seul indicateur de statut. Archivé et annulé partagent le fond neutre du brouillon : c'est la graphie qui les distingue, pas une couleur supplémentaire.

#### Icônes

Bibliothèque unique : **Phosphor Icons** (poids `Regular` par défaut, `Bold` pour les actions primaires). Taille standard : 24 px (nav), 20 px (inline), 16 px (dense).  
Fichiers SVG : `ChoralFront/public/icons/`. Catalogue complet et règles d'utilisation : `12-catalogue-icones.md`.

Chaque icône fonctionnelle est accompagnée d'un label ou d'un `aria-label`.

### 3.2 Molécules

#### Carte de chant

```
┌──────────────────────────────────────────────┐
│  [Icône musique]  Titre du chant             │
│                   Voix • Durée               │
│                                  [Badge statut]│
│  ──────────────────────────────────────────  │
│  [▶ Écouter]  [Partition]  [Consigne ●]      │
└──────────────────────────────────────────────┘
```

- Le point rouge sur "Consigne" indique une consigne non lue.
- Hauteur fixe : 88 px (liste dense), extensible en mode détail.

#### Carte événement

```
┌──────────────────────────────────────────────┐
│  MAR               Titre de l'événement      │
│  14 JAN            Lieu — 14:00              │
│                    N chants • Taux : XX %    │
│                                  [Ma réponse]│
└──────────────────────────────────────────────┘
```

- Le taux de réponse est affiché uniquement pour les rôles Responsable et Chef de pupitre.

#### Item de liste membre

```
┌──────────────────────────────────────────────┐
│  [Avatar initiales]  Prénom Nom              │
│                      Voix principale • Rôle  │
│                                   [Statut]   │
└──────────────────────────────────────────────┘
```

#### Lecteur audio (persistant — mobile)

Barre fixe en bas, au-dessus de la navigation. Hauteur : 64 px.

```
┌──────────────────────────────────────────────┐
│ [Miniature] Titre du chant — Voix   [◀][▶][⏸]│
│             ━━━━━━━━━━━━━━━━━━━━━━           │
└──────────────────────────────────────────────┘
```

Tap sur la barre ouvre le lecteur plein écran.

### 3.3 Organismes

#### Tableau de données (web)

- En-tête de colonne triable avec indicateur visuel de direction.
- Pagination : 25 / 50 / 100 lignes par page.
- Barre de recherche + filtres actifs matérialisés sous forme de chips supprimables.
- Sélection multiple avec barre d'actions contextuelles flottante.
- Ligne en état `loading` : skeleton de même hauteur que la ligne réelle.
- Tableau vide : illustration + texte explicatif + CTA si action disponible.

#### Modal de confirmation

Déclenchée pour toute action irréversible (archiver, désactiver, révoquer).

```
┌────────────────────────────────────┐
│  Titre de l'action                 │
│                                    │
│  Conséquence explicite (1-2 ph.)   │
│                                    │
│  [Annuler]        [Confirmer]      │
└────────────────────────────────────┘
```

- Largeur : 480 px (desktop), pleine largeur −32 px (mobile).
- Le bouton destructif est à droite, visuellement distingué.
- Pas de modal pour les actions réversibles (ex. mettre en brouillon).

#### Bottom sheet (mobile)

Remplace les modales de confirmation et les menus contextuels sur mobile. Glisser vers le bas pour fermer. Poignée visible (drag handle) en haut.

#### Toast / notification in-app

- Positionnement : centre-haut (mobile), bas-droite (desktop).
- Durée : succès 3 s, avertissement 5 s, erreur persistant jusqu'à dismiss manuel.
- Empilage : max 3 toasts simultanés, les suivants poussent la file.

| Type | Icône | Couleur de bord gauche |
|---|---|---|
| Succès | Check circle | `color-success` |
| Avertissement | Warning | `color-warning` |
| Erreur | X circle | `color-error` |
| Info | Info | `color-primary` |

---

## 4. Grilles et breakpoints

### 4.1 Breakpoints

| Nom | Plage | Surface concernée |
|---|---|---|
| `mobile-sm` | 320 – 374 px | Support minimal |
| `mobile` | 375 – 428 px | Cible principale mobile |
| `mobile-lg` | 429 – 767 px | Grands téléphones, paysage |
| `tablet` | 768 – 1023 px | iPad, mode split |
| `desktop` | 1024 – 1279 px | Portables |
| `desktop-lg` | 1280 px + | Postes fixes, admin |

### 4.2 Grille desktop (site de gestion et admin)

- **Colonnes** : 12 colonnes, gouttière 24 px, marges latérales 48 px.
- **Largeur max du contenu** : 1440 px (centré).
- **Sidebar** : 256 px fixe, contenu sur le reste.
- Zones courantes : pleine largeur (tableau), 8/12 + 4/12 (formulaire + résumé), 6/6 (deux colonnes équivalentes).

### 4.3 Grille mobile

- Disposition en colonne unique.
- Padding horizontal : 16 px.
- Pas de grille multi-colonnes sauf exception documentée.

---

## 5. Navigation et architecture de l'information

### 5.1 Application mobile — bottom navigation

5 onglets fixes, visibles en permanence hors mode lecteur plein écran.

| Position | Onglet | Icône | Visible pour |
|---|---|---|---|
| 1 | Accueil | House | Tous |
| 2 | Chants | Music notes | Tous |
| 3 | Événements | Calendar | Tous |
| 4 | Mon pupitre | Microphone | Tous |
| 5 | Compte | User | Tous |

- L'onglet actif est mis en valeur par `color-primary` + label visible.
- Un badge numérique sur "Accueil" indique les consignes non lues.
- Le changement de chorale active est accessible depuis n'importe quel onglet via le sélecteur en haut de l'écran (nom de la chorale + chevron).

### 5.2 Site de gestion — sidebar

Sidebar fixe à gauche, rétractable sur `desktop` (icônes seules si rétractée).

```
┌────────────────────┐
│ [Logo] ChoraleHelper│
│ Nom de la chorale  │
├────────────────────┤
│ ▪ Tableau de bord  │
│ ▪ Membres          │
│ ▪ Chants           │
│   ▸ Partitions     │
│   ▸ Enregistrements│
│ ▪ Événements       │
│   ▸ Listes         │
│ ▪ Consignes        │
│ ▪ Activité         │
├────────────────────┤
│ [Avatar] Mon compte│
└────────────────────┘
```

- La section active est mise en avant (fond `color-primary` à 8 % d'opacité + texte `color-primary`).
- Les sous-sections sont indentées et masquées par défaut, déployées au survol ou au clic de la section parente.

### 5.3 Administration générale — sidebar

Même structure que le site de gestion, avec une couleur de chrome distincte (fond `color-neutral-900`) pour différencier visuellement la surface d'administration.

```
┌────────────────────┐
│ [Logo] Admin       │
├────────────────────┤
│ ▪ Tableau de bord  │
│ ▪ Clients          │
│ ▪ Chorales         │
│ ▪ Suivi commercial │
│ ▪ Abonnements      │
│ ▪ Support          │
└────────────────────┘
```

### 5.4 Fil d'Ariane (breadcrumb)

Obligatoire sur toute page de détail (web). Format : `Section > Sous-section > Élément actif`. L'élément actif n'est pas un lien.

### 5.5 Retour en arrière (mobile)

Toute vue de détail expose un chevron gauche en haut à gauche. Aucun fond perdu ou geste glisser sans fallback bouton.

---

## 6. Micro-interactions et états

### 6.1 États de chargement

| Contexte | Pattern |
|---|---|
| Chargement initial d'une liste | Skeleton (même structure que la liste réelle) |
| Chargement d'une action | Spinner inline dans le bouton (remplace le label) |
| Chargement audio (buffering) | Barre de progression indéterminée sous le lecteur |
| Chargement de partition (PDF) | Skeleton de la taille de la page |

Les spinners ne bloquent jamais l'interface entière sauf si l'action rend toute interaction impossible (rare).

### 6.2 États vides

Chaque liste peut être vide. L'état vide comprend :

- Une illustration légère (ligne, pas de couleur pleine).
- Une phrase explicative au ton neutre (*"Aucun chant pour le moment"*).
- Un bouton d'action principal si l'utilisateur a les droits pour créer (*"Ajouter un chant"*).

Jamais un fond blanc vide sans contenu.

### 6.3 États d'erreur

| Type | Comportement |
|---|---|
| Erreur de chargement (réseau) | Message inline + bouton "Réessayer" |
| Erreur de formulaire | Message sous le champ concerné, jamais en alert global seul |
| Erreur serveur 5xx | Page d'erreur dédiée avec code d'erreur, lien retour |
| Session expirée | Redirection login avec message contextuel |

### 6.4 Confirmations et feedback positif

**Confirmer ou permettre d'annuler — voir `10-D42`.** Une modale de confirmation n'est ouverte que pour une action **réellement irréversible**, c'est-à-dire dont l'API n'expose aucune action inverse. Si l'inverse existe, l'action s'exécute immédiatement et son annulation est portée par le toast pendant 5 secondes. Le `confirm()` natif du navigateur est proscrit sur toutes les surfaces.

- Après publication : toast succès + badge de la ressource mis à jour instantanément (optimistic UI si applicable).
- Après envoi d'invitation : toast succès + membre apparaît en statut `Invited` dans la liste.
- Après enregistrement d'un formulaire : toast succès, pas de rechargement complet de page.

### 6.5 Transitions de navigation

| Surface | Transition |
|---|---|
| Mobile — changement d'onglet | Fade (100 ms) |
| Mobile — ouverture d'une vue de détail | Slide depuis la droite (250 ms) |
| Mobile — fermeture / retour | Slide vers la droite (200 ms) |
| Mobile — bottom sheet | Slide depuis le bas (300 ms) |
| Web — navigation sidebar | Fade du contenu (150 ms) |
| Web — ouverture de modal | Fade + légère montée (200 ms) |

---

## 7. Accessibilité

### 7.1 Norme cible

WCAG 2.1, niveau AA. Applicable à toutes les surfaces.

### 7.2 Contraste de couleur

Ratios mesurés sur les tokens réels de la charge « Encre & laiton » :

| Paire | Ratio | Verdict |
|---|---|---|
| `text-primary` sur `bg-page` (`ink-800` / `stone-50`) | 10,7:1 | ✅ AAA |
| `text-muted` sur `bg-page` (`stone-500` / `stone-50`) | 5,5:1 | ✅ AA |
| `action-fg` sur `action-bg` (blanc / `ink-600`) | 11,3:1 | ✅ AAA |
| Blanc sur `state-error` | 5,6:1 | ✅ AA |
| Blanc sur `state-success` | 5,1:1 | ✅ AA |
| `text-accent` sur `bg-page` (`brass-700` / `stone-50`) | 5,1:1 | ✅ AA |
| Badges de statut, toutes paires du §3.1 | 4,9 – 6,8:1 | ✅ AA |
| **Blanc sur `state-warning`** | **2,7:1** | ❌ **interdit** — texte `ink-800` obligatoire (6,3:1) |
| **Blanc sur `brass-500`** | **2,6:1** | ❌ **interdit** — le laiton ne porte pas de texte |

Les deux dernières lignes sont des interdictions, pas des avertissements. Elles sont tenues côté implémentation par `$min-contrast-ratio: 4.5` dans le thème Bootstrap, qui force le texte sombre sur ces deux couleurs.

Toute nouvelle paire est vérifiée avec les tokens réels, pas avec les valeurs nominales.

### 7.3 Navigation clavier (web)

- Tous les éléments interactifs sont atteignables par `Tab`.
- Ordre de tabulation suit l'ordre visuel.
- Focus visible sur tous les éléments (outline `focus-ring`, 2 px, offset 2 px).
- Les modales piègent le focus jusqu'à leur fermeture (`focus trap`).
- Fermeture des modales par `Escape`.

### 7.4 Taille des zones tactiles (mobile)

- Zone tactile minimale : **44 × 44 px** (recommandation Apple / Google).
- Les éléments plus petits visuellement (icônes 20 px) bénéficient d'un padding invisible pour atteindre 44 px.

### 7.5 Lecteurs d'écran

- Chaque image décorative porte `aria-hidden="true"`.
- Chaque image fonctionnelle porte un `alt` descriptif.
- Les icônes seules portent un `aria-label`.
- Les états dynamiques (chargement, erreur, succès) sont annoncés via `aria-live="polite"` (succès, info) ou `aria-live="assertive"` (erreur bloquante).
- Les listes de lecture audio exposent leurs métadonnées (titre, durée, voix) aux lecteurs d'écran.

### 7.6 Réduction de mouvement

Les animations respectent `prefers-reduced-motion` : si activé, toutes les transitions sont remplacées par un fade instantané (≤ 100 ms).

---

## 8. Spécificités par surface

### 8.1 Application mobile

**Mode lecteur plein écran**
Le chrome de navigation (bottom nav, header) disparaît. Seul le contenu et les contrôles du lecteur sont visibles. Un swipe vers le bas ou un tap sur la croix ferme le mode plein écran.

**Mode sombre**
Supporté via `prefers-color-scheme`. Les tokens de couleur ont une variante `dark` définie. Le mode sombre est prioritaire si activé au niveau OS.

**Gestes natifs**
- Swipe horizontal sur une carte de chant : actions rapides (Écouter, Favori).
- Pull-to-refresh sur toute liste.
- Pinch-to-zoom sur la partition.
- Long press sur un élément de liste : sélection multiple.

**Illustrations vide et onboarding**
Style ligne monochrome, couleur `color-primary` à 60 % d'opacité. Format SVG uniquement.

### 8.2 Site de gestion (web)

**Densité d'information**
Les tableaux affichent 25 lignes par défaut. Les cartes de tableau de bord affichent un résumé ; le détail s'ouvre dans un panneau latéral (`drawer`) sans changer d'URL.

**Édition inline**
Les champs éditables fréquemment (statut, priorité) bénéficient d'une édition inline (clic sur le champ → édition directe → save au blur ou à `Enter`). Les formulaires complets s'ouvrent en page dédiée ou en modal.

**Glisser-déposer**
Réordonner les chants dans une liste et les voix dans un chant par drag-and-drop. L'indicateur de position cible est une ligne de 2 px `color-primary`. L'animation de déplacement suit `duration-base`.

### 8.3 Administration générale (web)

**Différenciation visuelle** — *révisé, voir `10-D41`*
L'administration partage l'habillage du site de gestion. La frontière est marquée par le fil d'Ariane (« Administration / … ») et par un libellé de zone dans la barre latérale, pas par un chrome sombre. Le chrome distinct prévu initialement est abandonné : l'opérateur est unique et connaît la zone où il se trouve.

**Données sensibles** — *révisé, voir `10-D41`*
Pas de masquage « cliquer pour révéler ». Le pattern coûte un clic sur chaque consultation pour protéger d'un risque — le partage d'écran accidentel — qui ne se pose pas avec un opérateur unique.

**Densité**
L'administration est le seul écran où la densité prime sur le confort de lecture : tableaux compacts, 25 lignes par défaut. Le site de gestion suit la règle inverse (§0).

**Accès support**
Il n'existe pas de mode support : l'administration **consulte** les données d'une chorale, elle n'agit jamais sur son contenu. Aucune bascule d'identité, aucun bandeau de session support (`10-D41`).

---

## 9. Livrables attendus du designer

| Livrable | Format | Contenu minimum |
|---|---|---|
| Design tokens | JSON (W3C Design Tokens format) | Toutes les valeurs §2 |
| Composants | Figma (Auto Layout, Variables) | Tous les atomes et molécules §3 |
| Maquettes | Figma, par surface | Mobile (375 px) + Web (1280 px) |
| Prototype de navigation | Figma Interactive | Parcours critiques définis dans `05` |
| Spécifications de remise | Figma Inspect ou Zeplin | Mesures, tokens, exports d'actifs |
| Rapport d'accessibilité | Figma Contrast Checker | Toutes les paires de couleurs §7.2 |

Les maquettes mobiles couvrent a minima : Accueil, Fiche chant, Lecteur, Mon pupitre, Événements.  
Les maquettes web couvrent a minima : Tableau de bord (gestion), Liste de chants (gestion), Tableau de bord (admin), Fiche client (admin).
