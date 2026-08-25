import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AdminDashboardService } from './admin-dashboard.service';
import { environment } from '@env/environment';

const ADMIN_DASHBOARD_BASE_URL = `${environment.apiUrl}admin-dashboard`;
const ADMIN_GUEST_ACCOUNTS_BASE_URL = `${environment.apiUrl}admin-guest-accounts`;

describe('AdminDashboardService', () => {
  let service: AdminDashboardService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AdminDashboardService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('GetKpi : appelle la bonne route (GET, admin-dashboard)', () => {
    service.getKpi().subscribe();

    const req = httpMock.expectOne(`${ADMIN_DASHBOARD_BASE_URL}/GetKpi`);
    expect(req.request.method).toBe('GET');
    req.flush({});
  });

  // GetPurgeCandidates/PurgeInactive appartiennent côté back à AdminGuestAccountsController,
  // pas à AdminDashboardController — base URL différente de GetKpi, à ne pas confondre.
  it('GetPurgeCandidates : appelle la route admin-guest-accounts (GET), pas admin-dashboard', () => {
    service.getPurgeCandidates().subscribe();

    const req = httpMock.expectOne(`${ADMIN_GUEST_ACCOUNTS_BASE_URL}/GetPurgeCandidates`);
    expect(req.request.method).toBe('GET');
    req.flush({ Count: 0, HasMore: false, Candidates: [] });
  });

  it('PurgeInactive : appelle la route admin-guest-accounts (POST)', () => {
    let result: { AnonymizedCount: number } | undefined;
    service.purgeInactive().subscribe(res => (result = res));

    const req = httpMock.expectOne(`${ADMIN_GUEST_ACCOUNTS_BASE_URL}/PurgeInactive`);
    expect(req.request.method).toBe('POST');
    req.flush({ AnonymizedCount: 3, HasMore: false });

    expect(result?.AnonymizedCount).toBe(3);
  });
});
