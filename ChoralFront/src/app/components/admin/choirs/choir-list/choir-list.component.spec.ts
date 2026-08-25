import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Params, convertToParamMap, provideRouter } from '@angular/router';
import { ChoirListComponent } from './choir-list.component';
import { environment } from '@env/environment';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';
import { ChoirStatusEnum } from '@app/enums/status-choir.enum';

const ADMIN_CHOIRS_BASE_URL = `${environment.apiUrl}admin-choirs`;
const EMPTY_PAGE = { Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 };

// Bug corrigé (CORRECTION CIBLÉE) : les tuiles du tableau de bord admin naviguent vers cette
// liste avec un filtre en query string (ex. `?InactiveFor30Days=true`), mais rien ne les
// lisait — la liste s'affichait toujours non filtrée. Ces tests vérifient que le PREMIER appel
// réseau part déjà filtré, pas seulement que l'affichage finit par se corriger.
describe('ChoirListComponent — lecture des query params au chargement', () => {
  let httpMock: HttpTestingController;
  // `queryParamMap` est un getter (lu paresseusement) plutôt qu'une valeur figée à la
  // configuration : chaque test ajuste cette variable puis crée le composant, sans jamais
  // reconfigurer TestBed (TestBed.resetTestingModule()/overrideProvider() en cours de test
  // interfère avec la réinitialisation automatique du framework entre files de specs).
  let currentQueryParams: Params = {};

  beforeEach(() => {
    currentQueryParams = {};
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { get queryParamMap() { return convertToParamMap(currentQueryParams); } } }
        }
      ]
    });
    stubIconHttpRequests();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.match(() => true).forEach(req => req.flush(EMPTY_PAGE));
    httpMock.verify();
  });

  it('InactiveFor30Days=true dans l’URL : le premier appel GetPaged part déjà filtré', () => {
    currentQueryParams = { InactiveFor30Days: 'true' };
    TestBed.createComponent(ChoirListComponent);

    const req = httpMock.expectOne(r => r.url === `${ADMIN_CHOIRS_BASE_URL}/GetPaged`);
    expect(req.request.params.get('InactiveFor30Days')).toBe('true');
    req.flush(EMPTY_PAGE);
  });

  it('Statut=1 dans l’URL : le premier appel GetPaged transmet Statut converti en entier', () => {
    currentQueryParams = { Status: String(ChoirStatusEnum.Published) };
    TestBed.createComponent(ChoirListComponent);

    const req = httpMock.expectOne(r => r.url === `${ADMIN_CHOIRS_BASE_URL}/GetPaged`);
    expect(req.request.params.get('Status')).toBe(String(ChoirStatusEnum.Published));
    req.flush(EMPTY_PAGE);
  });

  it('aucun query param : comportement inchangé (aucun filtre transmis)', () => {
    const fixture = TestBed.createComponent(ChoirListComponent);
    fixture.detectChanges();

    const req = httpMock.expectOne(r => r.url === `${ADMIN_CHOIRS_BASE_URL}/GetPaged`);
    expect(req.request.params.has('Status')).toBe(false);
    expect(req.request.params.has('InactiveFor30Days')).toBe(false);
    expect(req.request.params.has('ClientId')).toBe(false);
    req.flush(EMPTY_PAGE);
  });

  it('Statut=abc (malformé) : ignoré silencieusement, la page se charge normalement', () => {
    currentQueryParams = { Status: 'abc' };

    expect(() => {
      const fixture = TestBed.createComponent(ChoirListComponent);
      fixture.detectChanges();
    }).not.toThrow();

    const req = httpMock.expectOne(r => r.url === `${ADMIN_CHOIRS_BASE_URL}/GetPaged`);
    expect(req.request.params.has('Status')).toBe(false);
    req.flush(EMPTY_PAGE);
  });

  it('InactiveFor30Days=peut-être (malformé) : ignoré silencieusement, aucune exception', () => {
    currentQueryParams = { InactiveFor30Days: 'peut-être' };

    expect(() => {
      const fixture = TestBed.createComponent(ChoirListComponent);
      fixture.detectChanges();
    }).not.toThrow();

    const req = httpMock.expectOne(r => r.url === `${ADMIN_CHOIRS_BASE_URL}/GetPaged`);
    expect(req.request.params.has('InactiveFor30Days')).toBe(false);
    req.flush(EMPTY_PAGE);
  });

  it('filtre initial reste modifiable ensuite par l’utilisateur (pas un verrouillage)', () => {
    currentQueryParams = { InactiveFor30Days: 'true' };
    const fixture = TestBed.createComponent(ChoirListComponent);
    const component = fixture.componentInstance;

    httpMock.expectOne(r => r.url === `${ADMIN_CHOIRS_BASE_URL}/GetPaged`).flush(EMPTY_PAGE);

    component.onInactive30jChange('false');
    const req = httpMock.expectOne(r => r.url === `${ADMIN_CHOIRS_BASE_URL}/GetPaged`);
    expect(req.request.params.get('InactiveFor30Days')).toBe('false');
    req.flush(EMPTY_PAGE);
  });
});
