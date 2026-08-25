import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { MembershipRequestsListComponent } from './membership-requests-list.component';
import { ConfirmService } from '@app/services/confirm.service';
import { AuthStore } from '@core/auth.store';
import { environment } from '@env/environment';
import { MembershipRequestStatusEnum } from '@app/enums/status-membership-request.enum';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';

const SPACES_BASE_URL = `${environment.apiUrl}spaces`;
const SPACE_ID = 'espace-1';

function flushInitialLoad(httpMock: HttpTestingController, items: unknown[] = []): void {
  const req = httpMock.expectOne(r => r.url === `${SPACES_BASE_URL}/${SPACE_ID}/MembershipRequests/GetPaged`);
  req.flush({ Items: items, TotalCount: items.length, CurrentPage: 1, PageSize: 50 });
}

function aPendingRequest(id = 'demande-1') {
  return {
    Id: id,
    SpaceId: SPACE_ID,
    UserId: 'user-1',
    UserFullName: 'Ada Lovelace',
    UserEmail: 'ada@example.com',
    Status: MembershipRequestStatusEnum.Pending,
    Message: 'Bonjour !',
    DeclineReason: null,
    CreatedAt: new Date().toISOString(),
    HandledAt: null
  };
}

describe('DemandesAdhesionListComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ConfirmService, useValue: { confirm: () => Promise.resolve(true) } }
      ]
    });
    stubIconHttpRequests();
    TestBed.inject(AuthStore).setActiveSpace(SPACE_ID);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('409 (plafond atteint) : affiche un bandeau persistant, désactive Admettre, et les demandes restent visibles', () => {
    const fixture = TestBed.createComponent(MembershipRequestsListComponent);
    fixture.detectChanges();
    flushInitialLoad(httpMock, [aPendingRequest()]);
    fixture.detectChanges();

    fixture.componentInstance.openApproval(aPendingRequest());
    fixture.componentInstance.confirmApproval({ PrimaryVoicePart: 0, Role: 2 });

    const req = httpMock.expectOne(`${SPACES_BASE_URL}/${SPACE_ID}/MembershipRequests/demande-1/Approve`);
    req.flush({ Message: 'Plafond atteint' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(fixture.componentInstance.capAtteint()).toBe(true);
    expect(fixture.componentInstance.items().length).toBe(1);
    expect(fixture.nativeElement.textContent).toContain('Ada Lovelace');

    const approveBtn = Array.from(fixture.nativeElement.querySelectorAll('button')).find(
      (b): b is HTMLButtonElement => (b as HTMLElement).textContent?.trim() === 'Admettre'
    );
    expect(approveBtn?.disabled).toBe(true);
  });

  it('Refuser : demande confirmation PUIS appelle Refuser', async () => {
    const fixture = TestBed.createComponent(MembershipRequestsListComponent);
    fixture.detectChanges();
    flushInitialLoad(httpMock, [aPendingRequest()]);
    fixture.detectChanges();

    await fixture.componentInstance.decline(aPendingRequest());

    const req = httpMock.expectOne(`${SPACES_BASE_URL}/${SPACE_ID}/MembershipRequests/demande-1/Decline`);
    expect(req.request.method).toBe('POST');
    req.flush({ ...aPendingRequest(), Status: MembershipRequestStatusEnum.Declined });
  });
});
