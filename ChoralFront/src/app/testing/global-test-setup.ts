import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, onTestFinished } from 'vitest';

// Fichier de configuration globale de tests, chargé par le builder `@angular/build:unit-test`
// via `architect.test.options.setupFiles` (voir angular.json). Exécuté avant CHAQUE fichier de
// spec.
//
// Contrainte architecturale constatée empiriquement (et non supposée) en construisant ce
// fichier : le builder Angular exécute les `setupFiles` HORS du graphe de modules compilé de
// l'application. Un import d'un fichier `@app/**` depuis ce fichier — que ce soit via l'alias
// `@app/` (échoue à la résolution) ou via un chemin relatif (résout, mais vers une INSTANCE DE
// MODULE SÉPARÉE, distincte de celle utilisée par les composants réellement rendus dans les
// specs) — ne permet donc PAS d'obtenir une référence utilisable à `IconComponent` :
// `TestBed.overrideComponent(IconComponent, ...)` appelé avec cette référence dupliquée
// n'intercepte rien (vérifié : le backend de substitution n'était jamais invoqué). Seules les
// classes de `@angular/*` (packages npm, résolution stable) sont sûres à référencer ici.
//
// Stratégie retenue en conséquence : au lieu d'empêcher la requête GET /icons/{name}.svg
// d'atteindre `HttpTestingController` (impossible à froid depuis ce fichier), on la laisse
// partir normalement puis on la neutralise systématiquement à la fin de chaque test, avant que
// `verify()` du spec ne s'exécute — voir `onTestFinished` ci-dessous. `TestBed` et
// `HttpTestingController` sont des classes framework (@angular/core/testing,
// @angular/common/http/testing) : contrairement à IconComponent, il n'en existe qu'une seule
// instance pour tout le worker, donc sûres à référencer ici.

beforeEach(() => {
  // Filet de sécurité : si l'afterEach d'un spec précédent lève (ex. httpMock.verify() sur une
  // requête fuyante) avant que le TestBed ne se réinitialise, `TestBed` reste instancié. Avec
  // `isolate: false` (config Vitest par défaut du builder Angular — un seul module JS partagé
  // par tous les fichiers d'un même worker), cet état fuit vers le PROCHAIN test, potentiellement
  // dans un fichier de spec totalement différent et innocent, qui échoue alors avec :
  // "Cannot configure the test module when the test module has already been instantiated."
  // Ce beforeEach repart toujours d'un TestBed propre avant le prochain test, quelle que soit
  // la cause de l'état précédent — un échec doit rester local à son fichier.
  try {
    TestBed.resetTestingModule();
  } catch {
    // Best effort : ce filet de sécurité ne doit jamais lui-même faire échouer un test.
  }

  // `sessionStorage` et `localStorage` survivent à `resetTestingModule()` : ils appartiennent à
  // l'environnement jsdom, partagé par tous les fichiers de spec d'un même worker. Sans ce
  // nettoyage, un spec qui pose un espace actif (via StorageService) le lègue au suivant :
  // `AuthStore` le relit à sa construction (rehydrate) et des composants déclenchent alors des
  // appels qu'ils auraient dû éviter. Constaté : `song-list-detail.component.spec.ts` échouait
  // 1 fois sur 10 sur un `POST /songs/GetPagedByChoir` que son test interdit explicitement —
  // `loadSongOptions()` sortait tôt (`if (!choirId) return`) sauf quand un identifiant d'espace
  // traînait dans sessionStorage. Défaut d'ordonnancement, pas de composant.
  try {
    sessionStorage.clear();
    localStorage.clear();
  } catch {
    // Best effort : idem.
  }

  // Neutralise toute requête GET /icons/*.svg encore ouverte dans HttpTestingController à la
  // fin du test, quel que soit le composant qui l'a émise (IconComponent directement, ou
  // indirectement via un composant partagé comme DataStateComponent) — AVANT que le
  // `httpMock.verify()` propre au spec ne s'exécute.
  //
  // Constaté empiriquement (et non supposé) : un `afterEach` enregistré ici, dans ce setupFile,
  // ne suffit PAS — Vitest n'exécute PAS les afterEach suivants dans la chaîne dès qu'un premier
  // lève (contrairement à Jest). Le `afterEach(() => httpMock.verify())` local du spec, imbriqué
  // dans son describe(), s'exécute AVANT le nôtre (plus profondément imbriqué = exécuté en
  // premier) ; s'il lève à cause d'une requête d'icône non flushée, notre `afterEach` global
  // n'est alors jamais invoqué pour ce test. `onTestFinished()` est en revanche garanti de
  // s'exécuter quel que soit l'état des autres hooks — c'est le mécanisme prévu par Vitest
  // précisément pour ce cas ; il doit être enregistré pendant qu'un test est en cours, donc ici,
  // dans ce beforeEach global (qui s'exécute pour chaque test).
  onTestFinished(() => {
    let httpMock: HttpTestingController | null = null;
    try {
      httpMock = TestBed.inject(HttpTestingController, null);
    } catch {
      // Best effort : un TestBed dans un état inhabituel ne doit pas faire échouer ce hook.
    }
    if (!httpMock) {
      return;
    }
    // `flush()` (plutôt qu'une simple extraction via `match()`) fait aboutir l'Observable
    // toObservable()/toSignal() d'IconComponent : sans ça, un Observable resterait indéfiniment
    // en attente dans son cache SVG module-level (voir icon.component.ts) et polluerait le
    // rendu de <app-icon> dans un test ultérieur, même si HttpTestingController ne le voit plus.
    for (const req of httpMock.match(request => request.url.includes('/icons/'))) {
      req.flush('', { status: 200, statusText: 'OK' });
    }
  });
});
