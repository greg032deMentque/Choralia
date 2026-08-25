import { TestBed } from '@angular/core/testing';
import { HttpBackend, HttpEvent, HttpResponse } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { IconComponent } from '@app/components/shared/icon/icon.component';

// IconComponent charge ses SVG via un HttpClient interne construit depuis HttpBackend,
// volontairement isolé de tokenInterceptor/apiErrorInterceptor (voir icon.component.ts).
// Dans un test, ce HttpClient résout le MÊME HttpBackend que HttpClientTestingModule : sans
// ce stub, chaque <app-icon> rendu (directement ou via un composant partagé comme
// DataStateComponent) dépose une requête GET /icons/{name}.svg dans HttpTestingController —
// un aspect purement décoratif, sans rapport avec le comportement métier vérifié par
// httpMock.verify(). Pire, cette requête part via toObservable()/effect (asynchrone,
// planifiée par le scheduler d'effects), ce qui crée une course non déterministe avec les
// assertions synchrones du test (httpMock.verify(), lecture de signaux juste après
// detectChanges()) — cause du test instable documentée dans docs/reste-a-faire.md.
//
// NE PEUT PAS être neutralisé depuis `testing/global-test-setup.ts` (setupFiles) : constaté
// empiriquement, ce builder charge les setupFiles HORS du graphe de modules compilé de
// l'application. Un import d'`IconComponent` depuis un setupFile — alias @app/ (échoue à la
// résolution) ou chemin relatif (résout, mais vers une INSTANCE DE MODULE SÉPARÉE, avec sa
// propre classe IconComponent distincte de celle réellement rendue dans les specs) — ne permet
// pas d'obtenir une référence exploitable pour `TestBed.overrideComponent`. Cette fonction doit
// donc rester appelée depuis CHAQUE spec (dans son propre graphe de modules, où l'identité de
// classe est correcte) — voir `testing/global-test-setup.ts` pour le filet de sécurité qui
// limite les dégâts quand elle est malgré tout oubliée.
//
// Ce stub remplace HttpBackend UNIQUEMENT dans l'injecteur local d'IconComponent (ajouté via
// TestBed.overrideComponent) : le HttpBackend global utilisé par le HttpClient métier des
// composants/services testés n'est pas touché. Les requêtes /icons/ ne sont donc plus jamais
// transmises à HttpTestingController et se résolvent de façon synchrone, sans dépendre du
// scheduler d'effects.
class IconHttpBackendStub extends HttpBackend {
  // La requête elle-même n'est pas nécessaire : toute requête d'icône reçoit la même réponse
  // neutre, quel que soit son URL (paramètre omis — TypeScript accepte une implémentation
  // avec moins de paramètres que la méthode abstraite qu'elle surcharge).
  override handle(): Observable<HttpEvent<unknown>> {
    return of(new HttpResponse<string>({ status: 200, body: '' }));
  }
}

/**
 * À appeler dans le `beforeEach` d'un spec, juste après `TestBed.configureTestingModule(...)`
 * et avant tout `TestBed.createComponent(...)`. Rend le rendu de `<app-icon>` inoffensif pour
 * `HttpTestingController`, quel que soit le nombre d'icônes rendues (directement ou via un
 * composant partagé comme DataStateComponent) et quel que soit le nombre d'appels à
 * `fixture.detectChanges()`.
 */
export function stubIconHttpRequests(): void {
  TestBed.overrideComponent(IconComponent, {
    add: { providers: [{ provide: HttpBackend, useClass: IconHttpBackendStub }] }
  });
}
