# 12 — Catalogue d'icônes

Source : **Phosphor Icons** — poids `Regular` par défaut, `Bold` pour les actions primaires.  
Fichiers SVG : `ChoralFront/public/icons/{nom}.svg` (servis à `/icons/{nom}.svg`, dupliqués dans `ChoralFront/src/assets/icons/`).  
Usage dans le code : composant standalone `IconComponent` (`<app-icon name="house" />`,
`ChoralFront/src/app/components/shared/icon/icon.component.ts`), qui charge le SVG en inline —
aucun package npm Phosphor n'est installé.

> Toute icône ajoutée en cours de développement doit être ajoutée ici et son SVG déposé dans `ChoralFront/public/icons/`.

---

## Navigation principale (bottom nav mobile / sidebar web)

| Icône | Fichier | Onglet / Section | Surface |
|---|---|---|---|
| House | `house.svg` | Accueil | Mobile, Web |
| MusicNotes | `music-notes.svg` | Chants | Mobile, Web |
| Calendar | `calendar.svg` | Événements | Mobile, Web |
| Microphone | `microphone.svg` | Mon pupitre | Mobile |
| User | `user.svg` | Compte / Mon compte | Mobile, Web |

---

## Audio et lecteur

| Icône | Fichier | Usage |
|---|---|---|
| Play | `play.svg` | Lancer la lecture |
| Pause | `pause.svg` | Mettre en pause |
| SkipBack | `skip-back.svg` | Piste précédente |
| SkipForward | `skip-forward.svg` | Piste suivante |
| SpeakerHigh | `speaker-high.svg` | Volume actif |
| SpeakerX | `speaker-x.svg` | Volume coupé (mute) |
| Queue | `queue.svg` | File de lecture |
| Headphones | `headphones.svg` | Mode écoute, accès audio |

---

## Chants et partitions

| Icône | Fichier | Usage |
|---|---|---|
| MusicNote | `music-note.svg` | Carte chant, icône générique chant |
| MusicNotes | `music-notes.svg` | Liste de chants |
| FilePdf | `file-pdf.svg` | Partition PDF |
| FileMusic | `file-music.svg` | Fichier audio (enregistrement) |

---

## Membres

| Icône | Fichier | Usage |
|---|---|---|
| Users | `users.svg` | Liste des membres, section membres |
| User | `user.svg` | Profil individuel, onglet compte |
| UserPlus | `user-plus.svg` | Inviter un membre |
| UserMinus | `user-minus.svg` | Désactiver / retirer un membre |
| IdentificationCard | `identification-card.svg` | Fiche membre, rôle |

---

## Événements

| Icône | Fichier | Usage |
|---|---|---|
| Calendar | `calendar.svg` | Événement, date |
| MapPin | `map-pin.svg` | Lieu de l'événement |
| Clock | `clock.svg` | Heure, durée |
| CheckSquare | `check-square.svg` | Réponse présent |
| XSquare | `x-square.svg` | Réponse absent |
| Question | `question.svg` | Réponse peut-être |

---

## Actions générales

| Icône | Fichier | Usage |
|---|---|---|
| Plus | `plus.svg` | Créer, ajouter |
| Pencil | `pencil.svg` | Modifier, éditer |
| Trash | `trash.svg` | Supprimer (action destructive) |
| Archive | `archive.svg` | Archiver |
| Upload | `upload.svg` | Déposer un fichier |
| Download | `download.svg` | Télécharger |
| ShareNetwork | `share-network.svg` | Partager (inter-chorales) |
| Eye | `eye.svg` | Voir, prévisualiser |
| EyeSlash | `eye-slash.svg` | Masquer (données sensibles admin) |
| ArrowLeft | `arrow-left.svg` | Retour (navigation mobile) |
| ArrowRight | `arrow-right.svg` | Suivant, détail |
| Check | `check.svg` | Valider, confirmer |
| X | `x.svg` | Fermer, annuler |
| DotsThreeVertical | `dots-three-vertical.svg` | Menu contextuel (kebab) |

---

## Feedback et statuts

| Icône | Fichier | Usage | Couleur associée |
|---|---|---|---|
| CheckCircle | `check-circle.svg` | Toast succès, statut publié | `color-success` |
| Warning | `warning.svg` | Toast avertissement, statut à valider | `color-warning` |
| XCircle | `x-circle.svg` | Toast erreur, statut rejeté | `color-error` |
| Info | `info.svg` | Toast info | `color-primary` |

---

## Gestion et administration

| Icône | Fichier | Usage |
|---|---|---|
| ChartBar | `chart-bar.svg` | Tableau de bord, statistiques |
| CurrencyEur | `currency-eur.svg` | Abonnements, paiements |
| Gear | `gear.svg` | Paramètres |
| Lock | `lock.svg` | Accès restreint, session support admin |
| ShieldCheck | `shield-check.svg` | Sécurité, accès tracé |
| Bell | `bell.svg` | Notifications, alertes |
| MagnifyingGlass | `magnifying-glass.svg` | Recherche |
| Funnel | `funnel.svg` | Filtres |
| List | `list.svg` | Vue liste |
| Rows | `rows.svg` | Vue tableau dense |
| Columns | `columns.svg` | Vue multi-colonnes |
| Buildings | `buildings.svg` | Client (organisation), admin |

---

## UI divers

| Icône | Fichier | Usage |
|---|---|---|
| CaretDown | `caret-down.svg` | Selector déroulant, accordion fermé |
| CaretRight | `caret-right.svg` | Sous-section sidebar, item navigable |
| CaretLeft | `caret-left.svg` | Retour, pagination précédente |
| Spinner | `spinner.svg` | Chargement (animé par CSS `animate-spin`) |
| WifiSlash | `wifi-slash.svg` | Indisponibilité réseau |
| Star | `star.svg` | Priorité haute, favori |

---

## Règles d'utilisation

### Tailles
| Contexte | Taille |
|---|---|
| Navigation (bottom nav, sidebar) | 24 px |
| Actions inline (bouton, liste) | 20 px |
| Dense (tableau, badge) | 16 px |
| Illustration vide state | 48 px |

### Poids
- **Regular** : usage courant (lecture, navigation, labels).
- **Bold** : action primaire unique par écran (bouton principal, CTA).
- **Fill** : état actif d'un onglet de navigation.

### Couleur
Les icônes héritent de la couleur du texte parent (`currentColor`). Ne pas coder une couleur fixe directement sur l'icône — utiliser les tokens du §2.1 de `11-ux-ui.md`.

### Accessibilité
- Icône seule (sans label textuel visible) → `aria-label` obligatoire.
- Icône décorative accompagnant un texte → `aria-hidden="true"`.

### Interdictions
- Ne pas utiliser une icône d'une autre bibliothèque sans décision documentée dans `10-decisions.md`.
- Ne pas redimensionner en dehors des tailles ci-dessus sans raison documentée.
- Ne pas changer le poids d'une icône pour raison esthétique seule.
