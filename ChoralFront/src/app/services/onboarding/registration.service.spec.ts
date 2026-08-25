import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { RegistrationService } from './registration.service';
import { environment } from '@env/environment';

const AUTH_BASE_URL = `${environment.apiUrl}auth`;

describe('RegistrationService', () => {
  let service: RegistrationService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(RegistrationService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('register : POST /auth/Register avec le corps transmis tel quel', () => {
    service.register({ Firstname: 'Ada', Lastname: 'Lovelace', Email: 'ada@example.com', Password: 'Sup3r!Secret' }).subscribe();

    const req = httpMock.expectOne(`${AUTH_BASE_URL}/Register`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ Firstname: 'Ada', Lastname: 'Lovelace', Email: 'ada@example.com', Password: 'Sup3r!Secret' });
    req.flush({ Message: 'ok' });
  });

  it('verifyEmail : GET /auth/VerifyEmail avec userId et token en query params', () => {
    service.verifyEmail('user-1', 'tok=en+special/chars').subscribe();

    const req = httpMock.expectOne(
      r => r.url === `${AUTH_BASE_URL}/VerifyEmail` && r.params.get('userId') === 'user-1' && r.params.get('token') === 'tok=en+special/chars'
    );
    expect(req.request.method).toBe('GET');
    req.flush(null);
  });

  it('resendVerification : POST /auth/ResendVerification avec Email dans le corps', () => {
    service.resendVerification({ Email: 'ada@example.com' }).subscribe();

    const req = httpMock.expectOne(`${AUTH_BASE_URL}/ResendVerification`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ Email: 'ada@example.com' });
    req.flush(null);
  });
});
