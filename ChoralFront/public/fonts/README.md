# Polices auto-hébergées

Les deux familles de la charte (`Spec/chorale/11-ux-ui.md` §2.2) sont servies depuis ce
dossier. Aucune requête vers un tiers, aucun clignotement dû à une feuille de style externe.

| Fichier | Famille | Axe | Sous-ensemble | Poids |
|---|---|---|---|---|
| `inter-latin-variable.woff2` | Inter — interface | `wght` 100–900 | latin | 48 ko |
| `source-serif-4-latin-variable.woff2` | Source Serif 4 — titres | `wght` 200–900 | latin | 50 ko |

Le sous-ensemble latin couvre le français, ligature `œ` comprise (U+0152–0153).

## Provenance et licence

Fichiers issus des paquets `@fontsource-variable/inter` et
`@fontsource-variable/source-serif-4` (rééditions des sources officielles
[rsms/inter](https://github.com/rsms/inter) et
[adobe-fonts/source-serif](https://github.com/adobe-fonts/source-serif)).

Les deux sont sous **SIL Open Font License 1.1** — redistribution dans un dépôt applicatif
autorisée. Textes de licence conservés ici : `OFL-Inter.txt`, `OFL-SourceSerif4.txt`.

Ce sont des fichiers déposés, pas une dépendance npm : rien à installer, rien à mettre à jour
automatiquement.

## Où c'est branché

- `@font-face` : `src/styles.scss`, en tête de fichier.
- `<link rel="preload">` : `src/index.html` — les deux polices bloquent le premier rendu du
  texte, elles sont préchargées plutôt que découvertes à la lecture du CSS.
- Noms de familles : `--font-family-base` et `--font-family-display` dans
  `src/themes/tokens.scss`.

## Variante à axe optique

Source Serif 4 existe aussi avec l'axe `opsz` (122 ko au lieu de 50). Non retenue : la charte
n'utilise qu'une seule graisse de titre, l'axe optique ne serait jamais piloté.
