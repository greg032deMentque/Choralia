import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { JoinCodeService } from './join-code.service';
import { environment } from '@env/environment';

const SPACES_BASE_URL = `${environment.apiUrl}spaces`;

describe('CodeRattachementService', () => {
  let service: JoinCodeService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(JoinCodeService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('getActif : GET /espaces/{espaceId}/CodeRattachement', () => {
    service.getActive('espace-1').subscribe();
    const req = httpMock.expectOne(`${SPACES_BASE_URL}/espace-1/JoinCode`);
    expect(req.request.method).toBe('GET');
    req.flush({ Code: null, ExpiresAt: null, IsActive: false });
  });

  it('genererOuRotator : POST avec durationDays en query quand fourni', () => {
    service.generateOuRotator('espace-1', 30).subscribe();
    const req = httpMock.expectOne(r => r.url === `${SPACES_BASE_URL}/espace-1/JoinCode` && r.params.get('durationDays') === '30');
    expect(req.request.method).toBe('POST');
    req.flush({ Code: 'AB12-CD34', ExpiresAt: '2026-08-30', IsActive: true });
  });

  it('genererOuRotator : POST sans durationDays quand non fourni', () => {
    service.generateOuRotator('espace-1').subscribe();
    const req = httpMock.expectOne(r => r.url === `${SPACES_BASE_URL}/espace-1/JoinCode`);
    expect(req.request.params.has('durationDays')).toBe(false);
    req.flush({ Code: 'AB12-CD34', ExpiresAt: '2026-08-30', IsActive: true });
  });

  it('desactiver : DELETE /espaces/{espaceId}/CodeRattachement', () => {
    service.desactiver('espace-1').subscribe();
    const req = httpMock.expectOne(`${SPACES_BASE_URL}/espace-1/JoinCode`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
