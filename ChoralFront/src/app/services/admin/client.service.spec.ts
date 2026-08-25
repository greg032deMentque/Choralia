import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ClientService } from './client.service';
import { environment } from '@env/environment';
import { ClientStatusEnum } from '@app/enums/status-client.enum';

const CLIENTS_BASE_URL = `${environment.apiUrl}clients`;

describe('ClientService', () => {
  let service: ClientService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ClientService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('GetPaged : transmet la pagination (POST)', () => {
    service.getPaged({ Page: 1, PageSize: 10, Filter: 'chorale' }).subscribe();

    const req = httpMock.expectOne(
      r => r.url === `${CLIENTS_BASE_URL}/GetPaged` && r.params.get('Filter') === 'chorale' && r.params.get('Page') === '1'
    );
    expect(req.request.method).toBe('POST');
    req.flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 });
  });

  it('GetById : appelle la route par id (GET)', () => {
    service.getById('client-1').subscribe();
    const req = httpMock.expectOne(`${CLIENTS_BASE_URL}/client-1`);
    expect(req.request.method).toBe('GET');
    req.flush({});
  });

  it('GetChorales : transmet clientId et pagination (POST)', () => {
    service.getChoirs('client-1', { Page: 2, PageSize: 5 }).subscribe();
    const req = httpMock.expectOne(
      r => r.url === `${CLIENTS_BASE_URL}/client-1/GetChoirs` && r.params.get('Page') === '2' && r.params.get('PageSize') === '5'
    );
    expect(req.request.method).toBe('POST');
    req.flush({ Items: [], TotalCount: 0, CurrentPage: 2, PageSize: 5 });
  });

  it('Reactiver : POST sans corps', () => {
    service.reactivate('client-1').subscribe();
    const req = httpMock.expectOne(`${CLIENTS_BASE_URL}/client-1/Reactivate`);
    expect(req.request.method).toBe('POST');
    req.flush({});
  });

  // GetManagers est un GET (contrairement à GetPaged/GetChorales, en POST) — le distinguo
  // mérite un test dédié, c'est le genre d'erreur de verbe qui casse silencieusement en prod.
  it('GetManagers : GET avec pagination en query params', () => {
    service.getManagers('client-1', { Page: 1, PageSize: 10 }).subscribe();

    const req = httpMock.expectOne(
      r => r.url === `${CLIENTS_BASE_URL}/client-1/Managers` && r.params.get('Page') === '1' && r.params.get('PageSize') === '10'
    );
    expect(req.request.method).toBe('GET');
    req.flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 });
  });

  it('Responsables : Designer (POST) puis Retirer (DELETE)', () => {
    service.assignManager('client-1', { Email: 'a@b.fr' }).subscribe();
    const postReq = httpMock.expectOne(`${CLIENTS_BASE_URL}/client-1/Managers`);
    expect(postReq.request.method).toBe('POST');
    postReq.flush(null);

    service.removeManager('client-1', 'user-1').subscribe();
    const deleteReq = httpMock.expectOne(`${CLIENTS_BASE_URL}/client-1/Managers/user-1`);
    expect(deleteReq.request.method).toBe('DELETE');
    deleteReq.flush(null);
  });

  // Exigence explicite du plan : ChangeStatus en 409 doit remonter tel quel.
  it('ChangeStatus en 409 : l’erreur remonte telle quelle', () => {
    let receivedStatus: number | undefined;

    service.changeStatus({ Id: 'client-1', Status: ClientStatusEnum.Suspended }).subscribe({
      next: () => {
        throw new Error('ne doit pas réussir');
      },
      error: err => {
        receivedStatus = err.status;
      }
    });

    const req = httpMock.expectOne(`${CLIENTS_BASE_URL}/ChangeStatus`);
    req.flush({ Message: 'Transition interdite.' }, { status: 409, statusText: 'Conflict' });

    expect(receivedStatus).toBe(409);
  });

  // Réactivation, 409 plafond dépassé (message chiffré) — doit remonter tel quel pour que
  // client-detail.component.ts puisse l'afficher et proposer un lien vers l'onglet Plafonds.
  it('Reactivate en 409 (plafond dépassé) : l’erreur remonte telle quelle', () => {
    let receivedMessage: string | undefined;

    service.reactivate('client-1').subscribe({
      next: () => {
        throw new Error('ne doit pas réussir');
      },
      error: err => {
        receivedMessage = err.error?.Message;
      }
    });

    const req = httpMock.expectOne(`${CLIENTS_BASE_URL}/client-1/Reactivate`);
    req.flush({ Message: 'Réactivation impossible, plafond dépassé : chorales 5/3.' }, { status: 409, statusText: 'Conflict' });

    expect(receivedMessage).toBe('Réactivation impossible, plafond dépassé : chorales 5/3.');
  });
});
