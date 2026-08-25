# 05 — Parcours et critères de succès

Les règles métier des objets impliqués dans ces parcours sont définies dans `03` et `04`. Les droits sont dans `02`.

---

## Parcours 1 — Un membre prépare son prochain travail

### Flux nominal

1. Le membre ouvre l'application et voit sa chorale active.
2. Il identifie les chants prioritaires et le prochain événement.
3. Il lance l'écoute d'un chant ou d'une liste.
4. Il verrouille son téléphone — l'écoute continue.
5. Il ouvre la fiche du chant et consulte la partition de référence.
6. Il lit les consignes associées.
7. Il répond à l'événement si une demande de présence est en attente.

### Critères de succès

- Le membre atteint l'audio utile en **moins de trois actions** depuis l'accueil.
- Il ne se demande pas quelle version est la bonne.
- Il comprend quoi préparer pour son prochain rendez-vous sans sortir de l'application.

### Cas limites

| Situation | Comportement attendu |
|---|---|
| Membre dans plusieurs chorales | La chorale active est affichée, le changement est accessible depuis l'accueil. |
| Aucun enregistrement de voix disponible | État explicite affiché (`Enregistrement non disponible`), pas de champ vide. |
| Partition générale sans partition de voix | Les deux cas sont affichés distinctement sur la fiche chant. |

---

## Parcours 2 — Un chef de pupitre produit un enregistrement

### Flux nominal

1. Le chef de pupitre ouvre l'écran `Enregistrement pupitre`.
2. Il choisit le chant et sa voix.
3. Il écoute une note de référence si besoin.
4. Il enregistre directement ou dépose un fichier audio.
5. Il écoute la prévisualisation et recommence si nécessaire.
6. Il envoie à validation ou publie directement si la délégation est activée.

### Critères de succès

- L'enregistrement est rattaché sans ambiguïté à un chant et une voix.
- Le responsable retrouve un contenu propre dans sa file `à valider`.
- Le statut du contenu est clair pour le chef de pupitre à tout moment.

### Cas limites

| Situation | Comportement attendu |
|---|---|
| Format audio non supporté | Message d'erreur avec la liste des formats acceptés. |
| Enregistrement envoyé à validation sans suite | Reste en `à valider` indéfiniment, visible dans le tableau de bord responsable. |

---

## Parcours 3 — Un responsable prépare et publie un événement

### Flux nominal

1. Le responsable crée un événement (titre, type, date, lieu).
2. Il sélectionne les chants et les organise en liste(s).
3. Il vérifie la complétude de chaque chant (partition de référence publiée et voix attendues couvertes).
4. Il rédige les consignes.
5. Il cible les membres concernés.
6. Il publie l'événement.
7. Il suit les confirmations de présence par voix.

### Critères de succès

- L'événement est publiable sans tableur annexe.
- Les chants incomplets sont signalés avant publication (non bloquant).
- Les membres reçoivent une préparation cohérente dès la publication.

### Cas limites

| Situation | Comportement attendu |
|---|---|
| Chant sans enregistrement de voix | Signalement visible, non bloquant pour la publication. |
| Événement `mariage` avec sous-moments | Plusieurs listes de chants rattachées au même événement. |
| Événement publié puis annulé | Les membres voient l'annulation explicitement. La liste de chants reste consultable. |

---

## Parcours 4 — Un responsable partage un enregistrement avec une autre chorale

### Flux nominal

1. Le responsable choisit un enregistrement au statut `publié`.
2. Il désigne une chorale destinataire.
3. Il définit la politique de téléchargement à destination.
4. La chorale destinataire voit l'enregistrement dans son espace.
5. Le responsable destinataire rattache l'enregistrement à un chant local.

### Critères de succès

- La provenance du contenu est visible pour la chorale destinataire.
- La destination ne peut pas modifier le contenu source.
- La source peut retirer le partage à tout moment.

### Cas limites

| Situation | Comportement attendu |
|---|---|
| Chant au même nom dans la chorale destinataire | Rattachement explicite requis, aucune fusion automatique. |
| Partage retiré après usage | Contenu masqué immédiatement à la destination. Audit conservé. |

---

## Parcours 5 — L'administration générale surveille l'usage et les risques

### Flux nominal

1. L'administrateur consulte le tableau de bord global.
2. Il identifie les clients sans activité récente.
3. Il repère les abonnements en retard ou à renouveler.
4. Il ouvre la fiche d'un client à risque et priorise les actions de support.

### Critères de succès

- L'administration distingue un problème d'usage d'un problème commercial.
- Tout accès aux données d'un client est tracé automatiquement.
