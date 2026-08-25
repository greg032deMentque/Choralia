import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MembershipRequestManagementService } from './membership-request-management.service';
import { environment } from '@env/environment';
import { VoicePartEnum } from '@app/enums/voice-part.enum';
import { UserRoleEnum } from '@app/enums/user-role.enum';
import { MembershipRequestStatusEnum } from '@app/enums/status-membership-request.enum';

const SPACES_BASE_URL = `${environment.apiUrl}spaces`;

describe('DemandeAdhesionGestionService', () => {
  let service: MembershipRequestManagementService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(MembershipRequestManagementService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('getPaged : POST /espaces/{espaceId}/Demandes/GetPaged avec la pagination en query', () => {
    service.getPaged('espace-1', { Page: 1, PageSize: 50 }).subscribe();

    const req = httpMock.expectOne(
      r => r.url === `${SPACES_BASE_URL}/espace-1/MembershipRequests/GetPaged` && r.params.get('Page') === '1' && r.params.get('PageSize') === '50'
    );
    expect(req.request.method).toBe('POST');
    req.flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 50 });
  });

  it('admettre : POST .../Demandes/{id}/Admettre avec VoixPrincipale et Role', () => {
    service.approve('espace-1', 'demande-1', { PrimaryVoicePart: VoicePartEnum.Alto, Role: UserRoleEnum.Singer }).subscribe();

    const req = httpMock.expectOne(`${SPACES_BASE_URL}/espace-1/MembershipRequests/demande-1/Approve`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ PrimaryVoicePart: VoicePartEnum.Alto, Role: UserRoleEnum.Singer });
    req.flush({});
  });

  it('refuser : POST .../Demandes/{id}/Refuser avec le motif interne', () => {
    service.decline('espace-1', 'demande-1', { DeclineReason: 'Pupitre complet' }).subscribe();

    const req = httpMock.expectOne(`${SPACES_BASE_URL}/espace-1/MembershipRequests/demande-1/Decline`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ DeclineReason: 'Pupitre complet' });
    req.flush({});
  });

  it('getPendingCount : ne compte que les demandes EnAttente de la page chargée', () => {
    let count: number | undefined;
    service.getPendingCount('espace-1').subscribe(c => (count = c));

    const req = httpMock.expectOne(r => r.url === `${SPACES_BASE_URL}/espace-1/MembershipRequests/GetPaged`);
    req.flush({
      Items: [
        { Id: '1', SpaceId: 'espace-1', UserId: 'u1', UserFullName: 'A', UserEmail: null, Status: MembershipRequestStatusEnum.Pending, Message: null, DeclineReason: null, CreatedAt: '2026-01-01', HandledAt: null },
        { Id: '2', SpaceId: 'espace-1', UserId: 'u2', UserFullName: 'B', UserEmail: null, Status: MembershipRequestStatusEnum.Approved, Message: null, DeclineReason: null, CreatedAt: '2026-01-01', HandledAt: null },
        { Id: '3', SpaceId: 'espace-1', UserId: 'u3', UserFullName: 'C', UserEmail: null, Status: MembershipRequestStatusEnum.Pending, Message: null, DeclineReason: null, CreatedAt: '2026-01-01', HandledAt: null }
      ],
      TotalCount: 3,
      CurrentPage: 1,
      PageSize: 100
    });

    expect(count).toBe(2);
  });
});
