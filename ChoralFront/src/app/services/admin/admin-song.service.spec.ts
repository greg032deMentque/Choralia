import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AdminSongService } from './admin-song.service';
import { environment } from '@env/environment';

const ADMIN_SONGS_BASE_URL = `${environment.apiUrl}admin-songs`;

describe('AdminChantService', () => {
  let service: AdminSongService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AdminSongService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('GetPagedCatalogue : transmet pagination, filtre et bascule doublons uniquement (POST)', () => {
    service
      .getPagedCatalogue(
        { Page: 2, PageSize: 10, SortActive: 'NombreChorales', SortDirection: 'desc', Filter: 'ave maria' },
        { DuplicatesOnly: true }
      )
      .subscribe();

    const req = httpMock.expectOne(
      r =>
        r.url === `${ADMIN_SONGS_BASE_URL}/GetPagedCatalogue` &&
        r.params.get('Page') === '2' &&
        r.params.get('PageSize') === '10' &&
        r.params.get('SortActive') === 'NombreChorales' &&
        r.params.get('SortDirection') === 'desc' &&
        r.params.get('Filter') === 'ave maria' &&
        r.params.get('DuplicatesOnly') === 'true'
    );
    expect(req.request.method).toBe('POST');
    req.flush({ Items: [], TotalCount: 0, CurrentPage: 2, PageSize: 10 });
  });

  it('GetPagedCatalogue : sans bascule doublons, ne transmet pas le paramètre (repli "tous")', () => {
    service.getPagedCatalogue({ Page: 1, PageSize: 10 }, {}).subscribe();

    const req = httpMock.expectOne(r => r.url === `${ADMIN_SONGS_BASE_URL}/GetPagedCatalogue`);
    expect(req.request.params.has('DuplicatesOnly')).toBe(false);
    req.flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 });
  });

  it('GetChoralesDuGroupe : encode correctement une clé contenant espaces et caractères spéciaux', () => {
    // Le piège de ce contrat : `cle` est une chaîne opaque transmise en query string. Un '&'
    // non encodé couperait la query string en deux paramètres côté serveur.
    const key = 'ave maria & gounod / n°2 (chœur)';
    service.getChoirsDuGroup(key).subscribe();

    const req = httpMock.expectOne(r => r.url === `${ADMIN_SONGS_BASE_URL}/GetGroupChoirs`);
    expect(req.request.method).toBe('GET');
    // Round-trip : HttpParams doit reconstituer exactement la valeur d'origine.
    expect(req.request.params.get('key')).toBe(key);
    // La requête réellement émise ne doit contenir qu'un seul paramètre — le '&' de la clé a
    // bien été percent-encodé, pas transmis en clair.
    const [, rawQuery] = req.request.urlWithParams.split('?');
    expect(rawQuery.split('&')).toHaveLength(1);
    req.flush([]);
  });

  it('GetChoralesDuGroupe : une réponse 400 (clé manquante ou vide) remonte une erreur exploitable', () => {
    let receivedError: unknown;
    service.getChoirsDuGroup('').subscribe({ error: err => (receivedError = err) });

    const req = httpMock.expectOne(r => r.url === `${ADMIN_SONGS_BASE_URL}/GetGroupChoirs`);
    req.flush({ Message: 'Le paramètre cle est requis.' }, { status: 400, statusText: 'Bad Request' });

    expect(receivedError).toBeInstanceOf(HttpErrorResponse);
    expect((receivedError as HttpErrorResponse).status).toBe(400);
  });
});
