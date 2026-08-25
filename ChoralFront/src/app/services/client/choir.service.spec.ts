import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChoirService } from './choir.service';
import { environment } from '@env/environment';

const CHOIRS_BASE_URL = `${environment.apiUrl}choirs`;

describe('ChoirService', () => {
  let service: ChoirService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ChoirService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('Create : POST du payload sur /choirs/Create', () => {
    service.create({ ClientId: 'structure-1', Name: 'Sainte-Cécile', ChoirMasterEmail: 'chef@exemple.fr' }).subscribe();

    const req = httpMock.expectOne(`${CHOIRS_BASE_URL}/Create`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ ClientId: 'structure-1', Name: 'Sainte-Cécile', ChoirMasterEmail: 'chef@exemple.fr' });
    req.flush({ Id: 'chorale-1', ClientId: 'structure-1', Name: 'Sainte-Cécile', Description: null, ImageUrl: null, Status: 1, ChoirMasterEmail: null });
  });

  it('GetChoirMasters : transmet la pagination (POST) et convertit Roles (chaînes -> enum)', () => {
    let result: { Items: { Roles: unknown }[] } | undefined;
    service.getChoirMasters('chorale-1', { Page: 1, PageSize: 10 }).subscribe(res => (result = res));

    const req = httpMock.expectOne(r => r.url === `${CHOIRS_BASE_URL}/chorale-1/ChoirMasters/GetPaged`);
    expect(req.request.method).toBe('POST');
    req.flush({
      Items: [
        {
          Id: 'membre-1',
          UserId: 'user-1',
          ChoirId: 'chorale-1',
          Status: 1,
          UserFullName: 'Jean Dupont',
          UserEmail: 'jean@exemple.fr',
          Roles: ['Manager'],
          SectionId: null,
          SectionVoicePart: null
        }
      ],
      TotalCount: 1,
      CurrentPage: 1,
      PageSize: 10
    });

    expect(result?.Items[0].Roles).toEqual([3]);
  });

  it('AssignChoirMaster : PUT du payload sur ChoirMasters/Assign', () => {
    service.assignChoirMaster('chorale-1', { Email: 'chef@exemple.fr' }).subscribe();

    const req = httpMock.expectOne(`${CHOIRS_BASE_URL}/chorale-1/ChoirMasters/Assign`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ Email: 'chef@exemple.fr' });
    req.flush({
      Id: 'membre-1',
      UserId: 'user-1',
      ChoirId: 'chorale-1',
      Status: 1,
      UserFullName: null,
      UserEmail: 'chef@exemple.fr',
      Roles: ['Manager'],
      SectionId: null,
      SectionVoicePart: null
    });
  });

  it('RemoveChoirMaster : DELETE sur ChoirMasters/{userId}', () => {
    service.removeChoirMaster('chorale-1', 'user-1').subscribe();

    const req = httpMock.expectOne(`${CHOIRS_BASE_URL}/chorale-1/ChoirMasters/user-1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
