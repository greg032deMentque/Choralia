import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Params, convertToParamMap, provideRouter } from '@angular/router';
import { EventListComponent } from './event-list.component';
import { environment } from '@env/environment';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';
import { EventStatusEnum } from '@app/enums/event-status.enum';
import { EventTypeEnum } from '@app/enums/event-type.enum';
import { EventEffectiveStateEnum } from '@app/enums/event-effective-state.enum';

const ADMIN_EVENTS_BASE_URL = `${environment.apiUrl}admin-events`;
const EMPTY_EVENTS_PAGE = { Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 };

describe('EvenementListComponent (admin)', () => {
  let httpMock: HttpTestingController;
  // `queryParamMap` est un getter (lu paresseusement) plutôt qu'une valeur figée à la
  // configuration : chaque test ajuste cette variable puis crée le composant, sans jamais
  // reconfigurer TestBed en cours de test (TestBed.resetTestingModule()/overrideProvider()
  // interfère avec la réinitialisation automatique du framework entre files de specs — voir
  // décision consignée dans le rapport de la CORRECTION CIBLÉE).
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
    httpMock.match(() => true).forEach(req => req.flush(null));
    httpMock.verify();
  });

  it('événement autonome (ChoirName null) : affiche un repli explicite, jamais "undefined"', () => {
    const fixture = TestBed.createComponent(EventListComponent);
    fixture.detectChanges();

    httpMock.expectOne(r => r.url === `${ADMIN_EVENTS_BASE_URL}/GetPaged`).flush({
      Items: [
        {
          Id: 'evenement-1',
          Title: 'Concert de Noël',
          Type: EventTypeEnum.Concert,
          StartDate: '2026-12-24T18:00:00Z',
          EndDate: null,
          Location: 'Église',
          Status: EventStatusEnum.Published,
          EffectiveState: EventEffectiveStateEnum.Published,
          ChoirId: null,
          ChoirName: null,
          ClientId: 'client-1',
          ClientName: 'Diocèse',
          ParticipantCount: 3,
          IsTechnicalClientAnomaly: false
        }
      ],
      TotalCount: 1,
      CurrentPage: 1,
      PageSize: 10
    });
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Événement autonome (sans chorale)');
    expect(text).not.toContain('undefined');
  });

  it('événement anomalie (IsTechnicalClientAnomaly) : la ligne est mise en évidence par un badge dédié', () => {
    const fixture = TestBed.createComponent(EventListComponent);
    fixture.detectChanges();

    httpMock.expectOne(r => r.url === `${ADMIN_EVENTS_BASE_URL}/GetPaged`).flush({
      Items: [
        {
          Id: 'evenement-2',
          Title: 'Événement hérité',
          Type: EventTypeEnum.Other,
          StartDate: '2026-01-01T00:00:00Z',
          EndDate: null,
          Location: 'Inconnu',
          Status: EventStatusEnum.Archived,
          EffectiveState: EventEffectiveStateEnum.Archived,
          ChoirId: null,
          ChoirName: null,
          ClientId: 'client-technique',
          ClientName: 'Sans structure',
          ParticipantCount: 0,
          IsTechnicalClientAnomaly: true
        }
      ],
      TotalCount: 1,
      CurrentPage: 1,
      PageSize: 10
    });
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('À corriger');
  });

  // Bug corrigé (CORRECTION CIBLÉE) : les tuiles du tableau de bord admin naviguent vers cette
  // liste avec un filtre en query string (ex. `?Upcoming=true`), mais rien ne le lisait — le
  // premier appel réseau partait toujours non filtré.
  describe('lecture des query params au chargement', () => {
    it('Upcoming=true dans l’URL : le premier appel GetPaged part déjà filtré', () => {
      currentQueryParams = { Upcoming: 'true' };
      TestBed.createComponent(EventListComponent);

      const req = httpMock.expectOne(r => r.url === `${ADMIN_EVENTS_BASE_URL}/GetPaged`);
      expect(req.request.params.get('Upcoming')).toBe('true');
      req.flush(EMPTY_EVENTS_PAGE);
    });

    it('Statut et Type dans l’URL : tous deux transmis convertis en entier', () => {
      currentQueryParams = { Status: String(EventStatusEnum.Published), Type: String(EventTypeEnum.Concert) };
      TestBed.createComponent(EventListComponent);

      const req = httpMock.expectOne(r => r.url === `${ADMIN_EVENTS_BASE_URL}/GetPaged`);
      expect(req.request.params.get('Status')).toBe(String(EventStatusEnum.Published));
      expect(req.request.params.get('Type')).toBe(String(EventTypeEnum.Concert));
      req.flush(EMPTY_EVENTS_PAGE);
    });

    it('aucun query param : comportement inchangé (aucun filtre transmis)', () => {
      const fixture = TestBed.createComponent(EventListComponent);
      fixture.detectChanges();

      const req = httpMock.expectOne(r => r.url === `${ADMIN_EVENTS_BASE_URL}/GetPaged`);
      expect(req.request.params.has('Status')).toBe(false);
      expect(req.request.params.has('Type')).toBe(false);
      expect(req.request.params.has('Upcoming')).toBe(false);
      req.flush(EMPTY_EVENTS_PAGE);
    });

    it('Upcoming=peut-être (malformé) : ignoré silencieusement, aucune exception', () => {
      currentQueryParams = { Upcoming: 'peut-être' };

      expect(() => {
        const fixture = TestBed.createComponent(EventListComponent);
        fixture.detectChanges();
      }).not.toThrow();

      const req = httpMock.expectOne(r => r.url === `${ADMIN_EVENTS_BASE_URL}/GetPaged`);
      expect(req.request.params.has('Upcoming')).toBe(false);
      req.flush(EMPTY_EVENTS_PAGE);
    });

    it('Statut=99 (entier syntaxiquement valide mais hors énumération) : ignoré silencieusement', () => {
      currentQueryParams = { Status: '99' };
      TestBed.createComponent(EventListComponent);

      const req = httpMock.expectOne(r => r.url === `${ADMIN_EVENTS_BASE_URL}/GetPaged`);
      expect(req.request.params.has('Status')).toBe(false);
      req.flush(EMPTY_EVENTS_PAGE);
    });
  });
});
