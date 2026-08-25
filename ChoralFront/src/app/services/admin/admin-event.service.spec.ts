import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AdminEventService } from './admin-event.service';
import { environment } from '@env/environment';
import { EventStatusEnum } from '@app/enums/event-status.enum';
import { EventTypeEnum } from '@app/enums/event-type.enum';

const ADMIN_EVENTS_BASE_URL = `${environment.apiUrl}admin-events`;

describe('AdminEvenementService', () => {
  let service: AdminEventService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AdminEventService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('GetPaged : transmet pagination ET filtre en query params (POST)', () => {
    service
      .getPaged(
        { Page: 1, PageSize: 10, SortActive: 'DateDebut', SortDirection: 'desc' },
        { ClientId: 'client-1', ChoirId: 'chorale-1', Status: EventStatusEnum.Published, Type: EventTypeEnum.Concert, Upcoming: true }
      )
      .subscribe();

    const req = httpMock.expectOne(
      r =>
        r.url === `${ADMIN_EVENTS_BASE_URL}/GetPaged` &&
        r.params.get('SortActive') === 'DateDebut' &&
        r.params.get('SortDirection') === 'desc' &&
        r.params.get('ClientId') === 'client-1' &&
        r.params.get('ChoirId') === 'chorale-1' &&
        r.params.get('Status') === String(EventStatusEnum.Published) &&
        r.params.get('Type') === String(EventTypeEnum.Concert) &&
        r.params.get('Upcoming') === 'true'
    );
    expect(req.request.method).toBe('POST');
    req.flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 });
  });

  it('GetPaged : sans filtre, ne transmet que la pagination', () => {
    service.getPaged({ Page: 1, PageSize: 10 }, {}).subscribe();

    const req = httpMock.expectOne(r => r.url === `${ADMIN_EVENTS_BASE_URL}/GetPaged`);
    expect(req.request.params.has('ClientId')).toBe(false);
    expect(req.request.params.has('ChoirId')).toBe(false);
    req.flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 });
  });

  it('GetById : appelle la route par id (GET)', () => {
    service.getById('event-1').subscribe();
    const req = httpMock.expectOne(`${ADMIN_EVENTS_BASE_URL}/event-1`);
    expect(req.request.method).toBe('GET');
    req.flush({});
  });
});
