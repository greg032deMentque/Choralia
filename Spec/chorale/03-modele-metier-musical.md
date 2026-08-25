# 03 — Modèle métier musical

Ce fichier définit les objets métier musicaux, leurs attributs, leurs statuts et leurs règles de cycle de vie. Les droits associés à ces objets sont définis dans le fichier `02`.

---

## Chant

Le `chant` est l'objet central du domaine. Tout contenu musical est rattaché à un chant.

### Attributs

| Attribut | Obligatoire | Type | Contrainte |
|---|---|---|---|
| Titre | Oui | Texte | Non vide |
| Statut | Oui | Enum | `Active`, `Archived` |
| Voix concernées | Oui | Liste | Au moins une voix |
| Auteur | Non | Texte | — |
| Compositeur | Non | Texte | — |
| Langue | Non | Texte | — |
| Durée approximative | Non | Durée | — |
| Tonalité de travail | Non | Texte | — |
| Niveau de priorité | Non | Enum | `High`, `Normal`, `Low` |
| Commentaires de préparation | Non | Texte libre | — |

### Relations

- Un chant peut avoir zéro ou plusieurs `partitions`.
- Un chant peut avoir zéro ou plusieurs `enregistrements`.
- Un chant peut appartenir à plusieurs `listes de chants`.
- Un chant peut être associé à plusieurs `événements`.

### Règles

- Un chant `Archived` n'apparaît pas dans les listes opérationnelles par défaut, mais reste accessible via recherche et dans l'historique.
- L'archivage d'un chant ne modifie pas le statut de ses partitions et enregistrements.

### Définition d'un chant « complet »

La complétude est évaluée à deux niveaux :

- **Complétude chorale** : le chant dispose d'une partition de référence au statut `Published` et de tous les enregistrements par voix attendus par la chorale.
- **Complétude pupitre** : pour une voix donnée, le chant dispose d'une partition de référence au statut `Published` et d'un enregistrement `Published` pour cette voix.

Un chant incomplet est signalé dans le tableau de bord du responsable, avec le détail des voix non couvertes.

---

## Partition

### Attributs

| Attribut | Obligatoire | Type | Contrainte |
|---|---|---|---|
| Chant de rattachement | Oui | Référence | — |
| Type | Oui | Enum | `General`, `ByVoicePart` |
| Voix cible | Conditionnel | Référence | Obligatoire si type `ByVoicePart` |
| Version | Oui | Texte | Non vide (ex. `v1`, `2024-05`) |
| Statut | Oui | Enum | `Draft`, `Published`, `Archived` |
| Propriétaire | Oui | Référence membre | — |
| Téléchargement autorisé | Oui | Booléen | — |

### Cycle de vie

```
Draft → Published → Archived
```

- `Draft` : visible uniquement du créateur et des responsables.
- `Published` : visible par tous les membres. Une seule partition par type (`General` ou `ByVoicePart` pour une voix donnée) peut être `Published` à la fois — la publication d'une nouvelle version archive automatiquement la précédente.
- `Archived` : masquée par défaut, accessible dans l'historique.

### Règles

- Une partition générale ne remplace pas une partition par voix — les deux types coexistent.
- Le remplacement d'une partition conserve l'historique complet des versions.
- Le statut `PendingReview` n'existe pas pour les partitions : le responsable les publie directement depuis un brouillon.

---

## Enregistrement

### Attributs

| Attribut | Obligatoire | Type | Contrainte |
|---|---|---|---|
| Chant de rattachement | Oui | Référence | — |
| Type | Oui | Enum | `General`, `ByVoicePart` |
| Voix cible | Conditionnel | Référence | Obligatoire si type `ByVoicePart` |
| Chorale propriétaire | Oui | Référence | — |
| Créateur | Oui | Référence membre | — |
| Statut | Oui | Enum | `Draft`, `PendingReview`, `Published`, `Archived` |
| Source | Oui | Enum | `RecordedInApp`, `UploadedFile`, `Shared` |
| Durée | Oui | Durée | Non nulle |
| Date de publication | Conditionnel | Date | Obligatoire si statut `Published` |
| Propriétaire du contenu | Oui | Texte | Pour traçabilité des droits |
| Téléchargement autorisé | Oui | Booléen | — |

### Formats acceptés en MVP

`mp3`, `m4a`, `wav`. Tout autre format est rejeté avec un message d'erreur explicite listant les formats acceptés.

### Cycle de vie

```
Draft → PendingReview → Published → Archived
       (ou directement Published si délégation activée)
```

| Statut | Visible par les membres | Prochaine action attendue |
|---|---|---|
| `Draft` | Non | Révision ou envoi à validation par le créateur |
| `PendingReview` | Non | Publication ou rejet par le responsable |
| `Published` | Oui | Aucune — ou remplacement par nouvelle version |
| `Archived` | Non | Consultable dans l'historique uniquement |

### Règles

- Un chant peut avoir plusieurs enregistrements `Published` simultanément si cela répond à des besoins distincts (ex. `version lente`, `version normale`).
- Un enregistrement `Archived` est conservé dans l'historique et reste traçable.
- La partition suit un cycle plus simple (pas de statut `PendingReview`) car sa production est directement sous la responsabilité du responsable.

---

## Note de référence

Outil accessible depuis l'écran d'enregistrement, à destination du chef de pupitre.

### Périmètre V1

- Sept notes naturelles : `Do`, `Ré`, `Mi`, `Fa`, `Sol`, `La`, `Si`.
- Notation française uniquement.
- Restitution audio immédiate au clic.

### Hors périmètre V1

- Altérations (dièses, bémols).
- Gestion multi-octaves.
- Accordeur en temps réel.

---

## Liste de chants

### Attributs

| Attribut | Obligatoire | Type | Contrainte |
|---|---|---|---|
| Nom | Oui | Texte | Non vide |
| Type | Oui | Enum | `Free`, `Event`, `Season`, `Section` |
| Propriétaire | Oui | Référence membre | — |
| Statut | Oui | Enum | `Draft`, `Published`, `Archived` |

### Règles

- Une liste peut exister sans être rattachée à un événement.
- Un événement peut être rattaché à plusieurs listes (ex. sous-moments d'un mariage).
- L'ordre des chants est figé à la publication — toute modification nécessite une republication.
- Une liste de type `Section` est créée par un chef de pupitre pour sa voix. Les autres types sont créés et publiés par le responsable.

---

## Partage inter-chorales

En V1, seuls les **enregistrements** sont partageables entre chorales. Les partitions et listes de chants ne le sont pas.

### Attributs

| Attribut | Obligatoire | Type | Contrainte |
|---|---|---|---|
| Chorale source | Oui | Référence | — |
| Chorale destinataire | Oui | Référence | Différente de la source |
| Enregistrement partagé | Oui | Référence | Au statut `Published` |
| Date de partage | Oui | Date | — |
| Statut | Oui | Enum | `actif`, `retiré` |
| Téléchargement autorisé à destination | Oui | Booléen | — |

### Règles

- Le partage est toujours explicite, jamais automatique.
- La chorale source reste propriétaire du contenu.
- La destination consomme via un **lien vivant** vers la source — pas une copie locale.
- La destination doit rattacher l'enregistrement partagé à un chant local existant ou nouveau. Si un chant au même nom existe, le rattachement est explicite — pas de fusion automatique.
- Le retrait de partage par la source supprime l'accès immédiatement à la destination.
- L'historique d'audit est conservé après retrait.
