import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AdminAuditService } from './admin-audit.service';
import { environment } from '@env/environment';

const ADMIN_AUDIT_BASE_URL = `${environment.apiUrl}admin-audit`;

describe('AdminAuditService', () => {
  let service: AdminAuditService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AdminAuditService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('GetPaged : transmet pagination ET filtre en query params (POST)', () => {
    service
      .getPaged(
        { Page: 2, PageSize: 20, SortActive: 'OccurredAt', SortDirection: 'desc' },
        { UserId: 'user-1', EntityType: 'Chorale', Action: 'ChangerStatut', StartDate: '2026-07-01T00:00:00', EndDate: '2026-07-31T23:59:59' }
      )
      .subscribe();

    const req = httpMock.expectOne(
      r =>
        r.url === `${ADMIN_AUDIT_BASE_URL}/GetPaged` &&
        r.params.get('Page') === '2' &&
        r.params.get('PageSize') === '20' &&
        r.params.get('SortActive') === 'OccurredAt' &&
        r.params.get('SortDirection') === 'desc' &&
        r.params.get('UserId') === 'user-1' &&
        r.params.get('EntityType') === 'Chorale' &&
        r.params.get('Action') === 'ChangerStatut' &&
        r.params.get('StartDate') === '2026-07-01T00:00:00' &&
        r.params.get('EndDate') === '2026-07-31T23:59:59'
    );
    expect(req.request.method).toBe('POST');
    req.flush({ Items: [], TotalCount: 0, CurrentPage: 2, PageSize: 20 });
  });

  it('GetPaged sans filtre de période : aucun StartDate/EndDate transmis', () => {
    service.getPaged({ Page: 1, PageSize: 10 }, {}).subscribe();

    const req = httpMock.expectOne(r => r.url === `${ADMIN_AUDIT_BASE_URL}/GetPaged`);
    expect(req.request.params.has('StartDate')).toBe(false);
    expect(req.request.params.has('EndDate')).toBe(false);
    req.flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 });
  });
});
