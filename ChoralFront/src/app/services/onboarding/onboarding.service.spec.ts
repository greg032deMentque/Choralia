import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpErrorResponse } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { OnboardingService } from './onboarding.service';
import { environment } from '@env/environment';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';

const ONBOARDING_BASE_URL = `${environment.apiUrl}onboarding`;
const AUTH_BASE_URL = `${environment.apiUrl}auth`;

describe('OnboardingService', () => {
  let service: OnboardingService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(OnboardingService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('previewCode : encode le code dans la query param "code"', () => {
    service.previewCode('AB12-CD34').subscribe();

    const req = httpMock.expectOne(r => r.url === `${ONBOARDING_BASE_URL}/PreviewCode` && r.params.get('code') === 'AB12-CD34');
    expect(req.request.method).toBe('GET');
    req.flush({ Name: 'Chorale Sainte-Cécile', SpaceType: SpaceTypeEnum.Choir });
  });

  it('previewCode : une erreur 400 remonte le message serveur tel quel, sans enrichissement', () => {
    let captured: HttpErrorResponse | undefined;

    service.previewCode('XXXX-XXXX').subscribe({
      error: err => (captured = err)
    });

    const req = httpMock.expectOne(r => r.url === `${ONBOARDING_BASE_URL}/PreviewCode`);
    req.flush({ Message: 'Code inconnu ou expiré.' }, { status: 400, statusText: 'Bad Request' });

    expect(captured?.error?.Message).toBe('Code inconnu ou expiré.');
  });

  it('demanderAdhesion : POST /onboarding/DemanderAdhesion avec le corps transmis tel quel', () => {
    service.requestMembership({ Code: 'AB12-CD34', Message: 'Bonjour' }).subscribe();

    const req = httpMock.expectOne(`${ONBOARDING_BASE_URL}/RequestMembership`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ Code: 'AB12-CD34', Message: 'Bonjour' });
    req.flush({ Id: 'demande-1', SpaceId: 'espace-1', SpaceName: 'Chorale', Status: 0, Message: 'Bonjour', CreatedAt: '2026-01-01' });
  });

  it('annulerDemande : DELETE /onboarding/MesDemandes/{id}', () => {
    service.cancelRequest('demande-1').subscribe();

    const req = httpMock.expectOne(`${ONBOARDING_BASE_URL}/MyRequests/demande-1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('creerChorale : POST CreerChorale PUIS rafraîchit la session (GET /auth/Me)', () => {
    service.createChoir({ Name: 'Chorale Sainte-Cécile' }).subscribe();

    const createReq = httpMock.expectOne(`${ONBOARDING_BASE_URL}/CreateChoir`);
    expect(createReq.request.method).toBe('POST');
    createReq.flush({ Id: 'espace-1', ClientId: 'client-1', Name: 'Chorale Sainte-Cécile', Description: null, ImageUrl: null, Status: 0 });

    const meReq = httpMock.expectOne(`${AUTH_BASE_URL}/Me`);
    expect(meReq.request.method).toBe('GET');
    meReq.flush({ Id: 'user-1', Email: 'a@a.com', Firstname: 'A', Lastname: 'B', Roles: [], SpaceRoles: [], ClientRoles: [] });
  });
});
