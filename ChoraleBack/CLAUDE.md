# Instructions Claude Code — ChoraleBack

Ce fichier s'applique uniquement à `ChoraleBack/` et porte le **spécifique .NET**. Le protocole
de travail, la checklist qualité C1→C12 et le routage agents vivent dans `.claude/CLAUDE.md` :
ils ne sont pas redits ici (voir sa règle de préséance).

## Dérogations déclarées au `.claude/CLAUDE.md`

| Règle racine | Écart | Raison |
|---|---|---|
| Routage : « après implémentation → `review-validator` » | L'audit de Phase 3 relance un **nouvel** `dotnet-api-architect`, pas `review-validator` | L'audit back est OWASP + Sonar sur une seule couche, pas un contrôle d'alignement de contrat back↔front. `review-validator` reste l'agent des corrections cross-stack |

Tout écart non listé ici n'existe pas : en cas de contradiction, `.claude/CLAUDE.md` gagne.

## Agent .NET API

Toute création ou modification de code dans `ChoraleBack/` passe par l'agent
`dotnet-api-architect` (`subagent_type: "dotnet-api-architect"`), conformément au routage
agents du `.claude/CLAUDE.md`.

### Protocole — ce qui est propre à cette surface

Les préfixes `[PHASE 1 — PLAN]` / `[PHASE 2 — EXECUTION]` et la checklist qualité C1→C12 sont
définis dans `.claude/CLAUDE.md`. Deux points seulement sont spécifiques ici :

**Ce qu'on passe à l'agent en Phase 1** : la description du changement, **sans pré-collecter
les fichiers** — laisser l'architecte demander ce dont il a besoin. Il retourne le plan et un
`BLOC DE TRANSFERT`, recopié tel quel dans le prompt de Phase 2. Si la Phase 1 contient un bloc
`[QUESTIONS POUR L'ORCHESTRATEUR]`, les présenter avec `AskUserQuestion` — jamais en prose.

**Phase 3 — Audit (automatique après Phase 2)**
Elle **s'ajoute** à la checklist C1→C12, elle ne s'y substitue pas. Relancer un nouvel agent
`dotnet-api-architect` (voir la dérogation ci-dessus) avec :
```
[AUDIT OWASP]
[AUDIT SONAR]
Projet : {ProjectName}
Répertoire : {répertoire cible}
Fichiers à auditer : {liste complète des fichiers créés en Phase 2}

Contexte des décisions intentionnelles (ne pas signaler comme failles) :
{BLOC DE TRANSFERT copié depuis la Phase 1, incluant AmbiguitiesResolved}
```
Cet agent est distinct du générateur — il repart de zéro et lit les fichiers produits.
Il ne doit pas signaler comme ❌ les décisions de sécurité documentées dans le BLOC DE TRANSFERT.
Présenter la matrice de conformité à l'utilisateur et attendre sa validation avant
toute correction.

---

## Règle tests — cadre technique (le critère de création est dans `.claude/CLAUDE.md`)

Framework et conventions existantes (voir `Chorale.Test/Services/`) : NUnit
(`[TestFixture]`/`[SetUp]`/`[Test]`), EF Core InMemory pour les tests de services
(`UseInMemoryDatabase(Guid.NewGuid().ToString())`), un fichier `{Service}Tests.cs` par
service dans `Chorale.Test/Services/{Domain}/`, namespace miroir.

### À tester en priorité
- Règles métier dans `Chorale.Services` : calculs, validations, transitions d'état,
  filtrage/visibilité selon rôle et chorale active
- Cas négatifs critiques : accès refusé (rôle/chorale non autorisé), entité soft-deleted
  qui ne doit pas remonter, ressource inexistante, conflit d'unicité
- Tout bug corrigé : un test qui reproduit le bug avant fix et le valide après
- Handlers d'autorisation (`SpaceRoleAuthorizationHandler`, `ClientRoleAuthorizationHandler` et équivalents)

### À ne pas tester
- Contrôleurs thin (délégation pure vers un service, sans logique)
- Mapping AutoMapper 1:1 sans transformation
- Getters/setters, DTOs, entités EF sans comportement
- Tests écrits uniquement pour faire monter la couverture

### Exécution obligatoire avant toute livraison
- Exécuter `dotnet test Chorale.Test/ChoraleBackEnd.Test.csproj` après toute modification
  touchant un service testé ou une de ses dépendances directes. Les fichiers projet ont été
  renommés en `ChoraleBackEnd.{Api,Common,Data,Services,Test,ViewModels}.csproj` et la
  solution en `ChoraleBackEnd.slnx` — les dossiers restent `Chorale.*`.
- Une livraison n'est valide que si **tous** les tests sont verts, y compris les tests
  préexistants impactés indirectement par le changement
- Si un test existant échoue à cause du changement : corriger le test s'il est devenu
  obsolète (changement de comportement voulu), sinon corriger le code — ne jamais
  supprimer ou ignorer un test pour faire passer la suite au vert sans justification
  explicite à l'utilisateur

---

## Convention — enums

Tous les enums (`Chorale.Common.Enums`) sont stockés et sérialisés en **entier** : pas de
`HasConversion` côté EF, pas de `StringEnumConverter` côté JSON. L'ordinal est une donnée
persistée (colonnes en base, filtres d'index, contraintes `CHECK`) et dupliquée côté front —
le réordonner change silencieusement le sens des lignes existantes. Règle : toute nouvelle
valeur se numérote explicitement (`= N`) et s'ajoute en **fin** d'enum, jamais insérée au
milieu ni laissée à la numérotation implicite. `EnumOrdinalsTests` (`Chorale.Test`) fait
échouer le build si un ordinal existant change — ne pas contourner ce test.
