import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideToastr } from 'ngx-toastr';
import { provideRouter } from '@angular/router';
import { EventDetailComponent } from './event-detail.component';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';
import { verifyIgnoringIcons } from '@app/testing/verify-ignoring-icons';

// load() valide l'id de route (regex UUID) avant tout appel HTTP (OWASP A01). Sans id lié
// (pas de navigation réelle dans ce test unitaire), l'input `id` reste à sa valeur par défaut
// (undefined) — isValidUuid(undefined) est invalide, exactement comme un segment d'URL
// malformé : load() doit se comporter de la même façon, sans appel HTTP. L'effect() du
// constructeur n'est flushé que par detectChanges() (pas de zone.js ici) ; le rendu qui en
// découle instancie aussi IconComponent — sa requête HTTP de chargement de SVG est stubbée
// (stubIconHttpRequests) pour ne jamais atteindre HttpTestingController, voir
// src/app/testing/icon-http-stub.ts pour le détail de la course évitée.
describe('EventDetailComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideToastr()]
    });
    stubIconHttpRequests();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    verifyIgnoringIcons(httpMock);
  });

  it("n'effectue aucun appel HTTP et affiche une erreur inline quand l'id de route est absent ou invalide", () => {
    const fixture = TestBed.createComponent(EventDetailComponent);
    const component = fixture.componentInstance;

    fixture.detectChanges();

    httpMock.expectNone(req => req.url.includes('/events'));
    expect(component.loading()).toBe(false);
    expect(component.error()).toBe("Identifiant d'événement invalide.");
    expect(component.evt()).toBeNull();
  });
});
