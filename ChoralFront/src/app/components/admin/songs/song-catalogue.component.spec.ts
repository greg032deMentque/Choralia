import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Params, convertToParamMap, provideRouter } from '@angular/router';
import { SongCatalogueComponent } from './song-catalogue.component';
import { environment } from '@env/environment';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';
import { SongStatusEnum } from '@app/enums/song-status.enum';

const ADMIN_SONGS_BASE_URL = `${environment.apiUrl}admin-songs`;

const GROUP_AVE_MARIA_GOUNOD = {
  Key: 'ave maria|gounod',
  Title: 'Ave Maria',
  Composer: 'Gounod',
  ChoirCount: 7,
  OccurrenceCount: 7
};

const GROUP_SANCTUS_WITHOUT_COMPOSER = {
  Key: 'sanctus|chant-id-1',
  Title: 'Sanctus',
  Composer: null,
  ChoirCount: 1,
  OccurrenceCount: 1
};

const EMPTY_CATALOGUE_PAGE = { Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 };

describe('ChantCatalogueComponent', () => {
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

  function flushCatalogue(fixture: ReturnType<typeof TestBed.createComponent>, items: unknown[]): void {
    httpMock
      .expectOne(r => r.url === `${ADMIN_SONGS_BASE_URL}/GetPagedCatalogue`)
      .flush({ Items: items, TotalCount: items.length, CurrentPage: 1, PageSize: 10 });
    fixture.detectChanges();
  }

  it('groupe porté par 7 chorales : le compteur affiché vaut 7', () => {
    const fixture = TestBed.createComponent(SongCatalogueComponent);
    fixture.detectChanges();
    flushCatalogue(fixture, [GROUP_AVE_MARIA_GOUNOD]);

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('7');
  });

  it('dépliement d\'une ligne : un seul appel GetChoralesDuGroupe, sans rechargement du tableau principal', () => {
    const fixture = TestBed.createComponent(SongCatalogueComponent);
    fixture.detectChanges();
    flushCatalogue(fixture, [GROUP_AVE_MARIA_GOUNOD]);

    const row = fixture.nativeElement.querySelector('tbody tr');
    row.dispatchEvent(new Event('click'));
    fixture.detectChanges();

    const detailReq = httpMock.expectOne(
      r => r.url === `${ADMIN_SONGS_BASE_URL}/GetGroupChoirs` && r.params.get('key') === GROUP_AVE_MARIA_GOUNOD.Key
    );
    detailReq.flush([
      { ChoirId: 'c1', ChoirName: 'Choir 1', ClientName: 'Client 1', SongStatus: SongStatusEnum.Active, CreationDate: '2026-01-01T00:00:00Z' }
    ]);
    fixture.detectChanges();

    httpMock.expectNone(r => r.url === `${ADMIN_SONGS_BASE_URL}/GetPagedCatalogue`);
  });

  it('repli puis re-dépliement de la même ligne : pas de second appel (mise en cache)', () => {
    const fixture = TestBed.createComponent(SongCatalogueComponent);
    fixture.detectChanges();
    flushCatalogue(fixture, [GROUP_AVE_MARIA_GOUNOD]);

    const row = fixture.nativeElement.querySelector('tbody tr');

    // Premier dépliage : un appel.
    row.dispatchEvent(new Event('click'));
    fixture.detectChanges();
    httpMock
      .expectOne(r => r.url === `${ADMIN_SONGS_BASE_URL}/GetGroupChoirs`)
      .flush([
        { ChoirId: 'c1', ChoirName: 'Choir 1', ClientName: 'Client 1', SongStatus: SongStatusEnum.Active, CreationDate: '2026-01-01T00:00:00Z' }
      ]);
    fixture.detectChanges();

    // Repli.
    row.dispatchEvent(new Event('click'));
    fixture.detectChanges();

    // Re-dépliage : aucun nouvel appel, la réponse vient du cache.
    row.dispatchEvent(new Event('click'));
    fixture.detectChanges();
    httpMock.expectNone(r => r.url === `${ADMIN_SONGS_BASE_URL}/GetGroupChoirs`);

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Choir 1');
  });

  it('chant sans composer : mention affichée, jamais présenté comme un doublon', () => {
    const fixture = TestBed.createComponent(SongCatalogueComponent);
    fixture.detectChanges();
    flushCatalogue(fixture, [GROUP_SANCTUS_WITHOUT_COMPOSER]);

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Non renseigné — non regroupé');

    // Le compteur (1 chorale) ne doit jamais recevoir le badge de mise en évidence réservé
    // aux forts effectifs — une seule chorale n'est pas un doublon.
    const badges = Array.from(fixture.nativeElement.querySelectorAll('.text-bg-primary')) as HTMLElement[];
    expect(badges.some(badge => badge.textContent?.trim().includes('1'))).toBe(false);
  });

  it('bascule "doublons uniquement" : la pagination est remise à la page 1', () => {
    const fixture = TestBed.createComponent(SongCatalogueComponent);
    fixture.detectChanges();
    flushCatalogue(fixture, [GROUP_AVE_MARIA_GOUNOD]);

    // Simule une navigation préalable en page 4 (résultat non filtré paginé).
    fixture.componentInstance.page.set(4);

    const toggle = fixture.nativeElement.querySelector('#filter-doublons-uniquement');
    toggle.checked = true;
    toggle.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const req = httpMock.expectOne(r => r.url === `${ADMIN_SONGS_BASE_URL}/GetPagedCatalogue`);
    expect(req.request.params.get('Page')).toBe('1');
    expect(req.request.params.get('DuplicatesOnly')).toBe('true');
    req.flush({ Items: [GROUP_AVE_MARIA_GOUNOD], TotalCount: 1, CurrentPage: 1, PageSize: 10 });
  });

  it('seules Titre, Composer et ChoirCount sont déclarées triables', () => {
    const fixture = TestBed.createComponent(SongCatalogueComponent);
    fixture.detectChanges();
    flushCatalogue(fixture, [GROUP_AVE_MARIA_GOUNOD]);

    const columns = fixture.componentInstance.columns();
    const sortableKeys = columns.filter(col => col.sortable).map(col => col.key);

    expect(sortableKeys.sort()).toEqual(['Composer', 'ChoirCount', 'Title'].sort());
  });

  // Bug corrigé (CORRECTION CIBLÉE) : la tuile "Groupes en doublon" du tableau de bord admin
  // navigue avec `?DuplicatesOnly=true`, mais rien ne le lisait — le premier appel partait
  // toujours non filtré.
  describe('lecture du query param DuplicatesOnly au chargement', () => {
    it('DuplicatesOnly=true dans l’URL : le premier appel GetPagedCatalogue part déjà filtré', () => {
      currentQueryParams = { DuplicatesOnly: 'true' };
      TestBed.createComponent(SongCatalogueComponent);

      const req = httpMock.expectOne(r => r.url === `${ADMIN_SONGS_BASE_URL}/GetPagedCatalogue`);
      expect(req.request.params.get('DuplicatesOnly')).toBe('true');
      req.flush(EMPTY_CATALOGUE_PAGE);
    });

    it('aucun query param : comportement inchangé (case décochée, aucun filtre transmis)', () => {
      const fixture = TestBed.createComponent(SongCatalogueComponent);
      fixture.detectChanges();

      const req = httpMock.expectOne(r => r.url === `${ADMIN_SONGS_BASE_URL}/GetPagedCatalogue`);
      expect(req.request.params.has('DuplicatesOnly')).toBe(false);
      expect(fixture.componentInstance.doublonsUniquement()).toBe(false);
      req.flush(EMPTY_CATALOGUE_PAGE);
    });

    it('DuplicatesOnly=peut-être (malformé) : ignoré silencieusement, aucune exception', () => {
      currentQueryParams = { DuplicatesOnly: 'peut-être' };

      expect(() => {
        const fixture = TestBed.createComponent(SongCatalogueComponent);
        fixture.detectChanges();
      }).not.toThrow();

      const req = httpMock.expectOne(r => r.url === `${ADMIN_SONGS_BASE_URL}/GetPagedCatalogue`);
      expect(req.request.params.has('DuplicatesOnly')).toBe(false);
      req.flush(EMPTY_CATALOGUE_PAGE);
    });
  });
});
