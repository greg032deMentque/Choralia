# 01 — Vision et périmètre

## Problème résolu

La plupart des chorales dispersent leurs contenus entre messagerie, dossiers partagés, PDF, notes vocales et liens audio.

Le produit remplace ce bricolage par un espace unique qui permet :

- de retrouver le bon chant et la bonne version de partition ;
- d'écouter le bon enregistrement pour sa voix ;
- de savoir quoi préparer pour un événement ;
- de publier rapidement un contenu fiable pour tout le groupe.

## Positionnement

Le produit est :

- une application musicale **privée** — pas un Spotify public ;
- un outil de travail choral orienté voix ;
- un outil de pilotage pour responsables ;
- une plateforme multi-chorales pour un ou plusieurs clients.

Le produit n'est **pas**, en V1 :

- un catalogue public de musique ;
- un studio audio complet de montage multipiste ;
- un outil de transcription automatique de partition ;
- un réseau social public.

## Contextes d'usage

| Contexte | Description |
|---|---|
| `Mode saison` | Une chorale vit toute l'année avec répétitions, répertoire et événements successifs. |
| `Mode événement` | Un groupe se forme pour un objectif ponctuel (ex. mariage). |

Le produit ne doit pas obliger une chorale à changer de modèle d'organisation.

## Surfaces du produit

| Surface | Cible | Canal |
|---|---|---|
| Application mobile | Membres, chefs de pupitre | iOS et Android |
| Site de gestion | Responsables de chorale | Navigateur web |
| Administration générale | Équipe interne — pilotage multi-clients | Navigateur web |

## Principes produit

**Mobile first** — Le cas d'usage principal est l'écoute sur téléphone. Toute fonctionnalité est utilisable sur mobile avant d'être pensée sur desktop.

**Une priorité par voix** — Chaque membre voit d'abord les contenus utiles à sa voix principale, sans perdre l'accès au reste.

**Un chant centralisé** — Le `chant` est l'objet central qui relie audio, partition, voix, listes et événements. Aucun contenu musical n'existe hors d'un chant.

**Un contenu publié est fiable** — Un contenu visible par les membres porte un statut clair et une version identifiable. Aucune publication n'écrase silencieusement une version précédente.

**Le partage inter-chorales est contrôlé** — Rien n'est partagé par défaut entre deux chorales. Tout partage est explicite, traçable et révocable.

**Le produit aide à décider** — Les responsables voient ce qui manque : partition absente, audio manquant, voix non couverte, événement incomplet.

## Définition de « expérience proche de Spotify »

Dans cette spec, cela signifie **au minimum** :

- lecture continue sans interruption entre deux chants d'une liste ;
- reprise de lecture au point d'arrêt ;
- lecture avec écran verrouillé ;
- commandes simples (lecture, pause, suivant, précédent) ;
- accès rapide au contenu en cours depuis l'écran de lecture.

Cela **ne signifie pas** : recommandations algorithmiques, radio automatique, catalogue public.
