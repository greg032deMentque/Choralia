import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AdminChoirService } from './admin-choir.service';
import { environment } from '@env/environment';
import { ChoirStatusEnum } from '@app/enums/status-choir.enum';

const ADMIN_CHOIRS_BASE_URL = `${environment.apiUrl}admin-choirs`;

describe('AdminChoraleService', () => {
  let service: AdminChoirService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AdminChoirService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('GetPaged : transmet pagination ET filtre en query params (POST)', () => {
    service
      .getPaged(
        { Page: 2, PageSize: 20, SortActive: 'Nom', SortDirection: 'asc', Filter: 'ste-cecile' },
        { ClientId: 'client-1', Status: ChoirStatusEnum.Published, InactiveFor30Days: true }
      )
      .subscribe();

    const req = httpMock.expectOne(
      r =>
        r.url === `${ADMIN_CHOIRS_BASE_URL}/GetPaged` &&
        r.params.get('Page') === '2' &&
        r.params.get('PageSize') === '20' &&
        r.params.get('SortActive') === 'Nom' &&
        r.params.get('SortDirection') === 'asc' &&
        r.params.get('Filter') === 'ste-cecile' &&
        r.params.get('ClientId') === 'client-1' &&
        r.params.get('Status') === String(ChoirStatusEnum.Published) &&
        r.params.get('InactiveFor30Days') === 'true'
    );
    expect(req.request.method).toBe('POST');
    req.flush({ Items: [], TotalCount: 0, CurrentPage: 2, PageSize: 20 });
  });

  it('GetById : appelle la route par id (GET)', () => {
    service.getById('choir-1').subscribe();

    const req = httpMock.expectOne(`${ADMIN_CHOIRS_BASE_URL}/choir-1`);
    expect(req.request.method).toBe('GET');
    req.flush({});
  });

  it('GetMembres : transmet la pagination (POST) et convertit Roles (chaînes -> enum)', () => {
    let result: { Items: { Roles: unknown }[] } | undefined;
    service.getMembers('choir-1', { Page: 1, PageSize: 10 }).subscribe(res => (result = res));

    const req = httpMock.expectOne(r => r.url === `${ADMIN_CHOIRS_BASE_URL}/choir-1/GetMembers`);
    expect(req.request.method).toBe('POST');
    req.flush({
      Items: [
        {
          Id: 'membre-1',
          UserId: 'user-1',
          ChoirId: 'chorale-1',
          Status: 0,
          UserFullName: 'Jean Dupont',
          UserEmail: 'jean@exemple.fr',
          Roles: ['Manager', 'Singer'],
          SectionId: null,
          SectionVoicePart: null
        }
      ],
      TotalCount: 1,
      CurrentPage: 1,
      PageSize: 10
    });

    expect(result?.Items[0].Roles).toEqual([3, 2]);
  });

  it('GetChants : transmet la pagination (POST)', () => {
    service.getSongs('choir-1', { Page: 1, PageSize: 10 }).subscribe();
    const req = httpMock.expectOne(r => r.url === `${ADMIN_CHOIRS_BASE_URL}/choir-1/GetSongs`);
    expect(req.request.method).toBe('POST');
    req.flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 });
  });

  it('GetEvenements : transmet la pagination (POST)', () => {
    service.getEvents('choir-1', { Page: 1, PageSize: 10 }).subscribe();
    const req = httpMock.expectOne(r => r.url === `${ADMIN_CHOIRS_BASE_URL}/choir-1/GetEvents`);
    expect(req.request.method).toBe('POST');
    req.flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 });
  });

  it('Update : envoie Id/Nom/Description (PUT)', () => {
    service.update({ Id: 'chorale-1', Name: 'Sainte-Cécile', Description: 'desc' }).subscribe();
    const req = httpMock.expectOne(`${ADMIN_CHOIRS_BASE_URL}/Update`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ Id: 'chorale-1', Name: 'Sainte-Cécile', Description: 'desc' });
    req.flush({});
  });

  it('ImpactArchivage : appelle la route par id (GET)', () => {
    service.getImpactArchivage('choir-1').subscribe();
    const req = httpMock.expectOne(`${ADMIN_CHOIRS_BASE_URL}/choir-1/ArchiveImpact`);
    expect(req.request.method).toBe('GET');
    req.flush({ MemberCount: 0, SongCount: 0, EventCount: 0 });
  });

  // Exigence explicite du plan : une transition interdite (409) doit remonter telle quelle au
  // composant appelant pour afficher un message inline exploitable, pas un code brut.
  it('ChangeStatus en 409 (transition interdite) : l’erreur remonte telle quelle', () => {
    let receivedStatus: number | undefined;

    service.changeStatus({ Id: 'choir-1', Status: ChoirStatusEnum.Published }).subscribe({
      next: () => {
        throw new Error('ne doit pas réussir');
      },
      error: err => {
        receivedStatus = err.status;
      }
    });

    const req = httpMock.expectOne(`${ADMIN_CHOIRS_BASE_URL}/ChangeStatus`);
    expect(req.request.method).toBe('PUT');
    req.flush({ Message: 'Transition interdite de Archivée vers Publiée.' }, { status: 409, statusText: 'Conflict' });

    expect(receivedStatus).toBe(409);
  });
});
