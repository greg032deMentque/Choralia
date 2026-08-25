import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Params, convertToParamMap, provideRouter } from '@angular/router';
import { provideToastr } from 'ngx-toastr';
import { ClientListComponent } from './client-list.component';
import { environment } from '@env/environment';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';
import { ClientStatusEnum } from '@app/enums/status-client.enum';

const CLIENTS_BASE_URL = `${environment.apiUrl}clients`;
const EMPTY_PAGE = { Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 };

// Bug corrigé (CORRECTION CIBLÉE) : les tuiles "Active"/"Suspended"/"Archivés" du tableau de
// bord admin naviguent vers cette liste avec `?Status=...`, mais rien ne le lisait. Écart
// assumé : ClientController.GetPaged n'accepte pas encore Statut/ClientIds/ProcheDuPlafond côté
// back au moment de ce correctif (voir client.service.ts) — ces tests vérifient uniquement que
// le FRONT transmet déjà ces paramètres dès le premier appel, prêt pour quand le back les lira.
describe('ClientListComponent — lecture des query params au chargement', () => {
  let httpMock: HttpTestingController;
  // `queryParamMap` est un getter (lu paresseusement) plutôt qu'une valeur figée à la
  // configuration : chaque test ajuste cette variable puis crée le composant, sans jamais
  // reconfigurer TestBed en cours de test.
  let currentQueryParams: Params = {};

  beforeEach(() => {
    currentQueryParams = {};
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideToastr(),
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

  it('Statut=1 dans l’URL : le premier appel GetPaged transmet Statut converti en entier', () => {
    currentQueryParams = { Status: String(ClientStatusEnum.Suspended) };
    TestBed.createComponent(ClientListComponent);

    const req = httpMock.expectOne(r => r.url === `${CLIENTS_BASE_URL}/GetPaged`);
    expect(req.request.params.get('Status')).toBe(String(ClientStatusEnum.Suspended));
    req.flush(EMPTY_PAGE);
  });

  it('ClientIds avec plusieurs identifiants : tous transmis au serveur', () => {
    const id1 = '11111111-1111-1111-1111-111111111111';
    const id2 = '22222222-2222-2222-2222-222222222222';
    currentQueryParams = { ClientIds: [id1, id2] };
    TestBed.createComponent(ClientListComponent);

    const req = httpMock.expectOne(r => r.url === `${CLIENTS_BASE_URL}/GetPaged`);
    expect(req.request.params.getAll('ClientIds')).toEqual([id1, id2]);
    req.flush(EMPTY_PAGE);
  });

  it('aucun query param : comportement inchangé (aucun filtre transmis)', () => {
    const fixture = TestBed.createComponent(ClientListComponent);
    fixture.detectChanges();

    const req = httpMock.expectOne(r => r.url === `${CLIENTS_BASE_URL}/GetPaged`);
    expect(req.request.params.has('Status')).toBe(false);
    expect(req.request.params.has('ClientIds')).toBe(false);
    expect(req.request.params.has('ProcheDuPlafond')).toBe(false);
    req.flush(EMPTY_PAGE);
  });

  it('Statut=abc (malformé) : ignoré silencieusement, la page se charge normalement', () => {
    currentQueryParams = { Status: 'abc' };

    expect(() => {
      const fixture = TestBed.createComponent(ClientListComponent);
      fixture.detectChanges();
    }).not.toThrow();

    const req = httpMock.expectOne(r => r.url === `${CLIENTS_BASE_URL}/GetPaged`);
    expect(req.request.params.has('Status')).toBe(false);
    req.flush(EMPTY_PAGE);
  });

  it('ProcheDuPlafond=peut-être (malformé) : ignoré silencieusement, aucune exception', () => {
    currentQueryParams = { NearCap: 'peut-être' };

    expect(() => {
      const fixture = TestBed.createComponent(ClientListComponent);
      fixture.detectChanges();
    }).not.toThrow();

    const req = httpMock.expectOne(r => r.url === `${CLIENTS_BASE_URL}/GetPaged`);
    expect(req.request.params.has('ProcheDuPlafond')).toBe(false);
    req.flush(EMPTY_PAGE);
  });

  it('ClientIds avec un identifiant invalide mêlé à un valide : seul le valide est transmis', () => {
    const validId = '11111111-1111-1111-1111-111111111111';
    currentQueryParams = { ClientIds: [validId, 'pas-un-guid'] };
    TestBed.createComponent(ClientListComponent);

    const req = httpMock.expectOne(r => r.url === `${CLIENTS_BASE_URL}/GetPaged`);
    expect(req.request.params.getAll('ClientIds')).toEqual([validId]);
    req.flush(EMPTY_PAGE);
  });
});
