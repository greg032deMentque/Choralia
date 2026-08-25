import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AdminUserService } from './admin-user.service';
import { environment } from '@env/environment';
import { UserRoleEnum } from '@app/enums/user-role.enum';
import { MemberStatusEnum } from '@app/enums/member-status.enum';
import { VoicePartEnum } from '@app/enums/voice-part.enum';
import { AttendanceEnum } from '@app/enums/presence.enum';

const ADMIN_USERS_BASE_URL = `${environment.apiUrl}admin-users`;

describe('AdminUserService', () => {
  let service: AdminUserService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(AdminUserService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('GetChoraleUsersPaged : transmet pagination ET filtre en query params (POST), ChoirIds en paramètres répétés', () => {
    service
      .getChoirUsersPaged(
        { Page: 2, PageSize: 20, SortActive: 'Lastname', SortDirection: 'asc', Filter: 'dupont' },
        {
          ChoirIds: ['chorale-1', 'chorale-2'],
          Role: UserRoleEnum.Manager,
          Status: MemberStatusEnum.Active,
          VoicePart: VoicePartEnum.Soprano,
          IsActive: true
        }
      )
      .subscribe();

    const req = httpMock.expectOne(
      r =>
        r.url === `${ADMIN_USERS_BASE_URL}/GetChoirUsersPaged` &&
        r.params.get('Page') === '2' &&
        r.params.get('PageSize') === '20' &&
        r.params.get('SortActive') === 'Lastname' &&
        r.params.get('SortDirection') === 'asc' &&
        r.params.get('Filter') === 'dupont' &&
        JSON.stringify(r.params.getAll('ChoirIds')) === JSON.stringify(['chorale-1', 'chorale-2']) &&
        r.params.get('Role') === String(UserRoleEnum.Manager) &&
        r.params.get('Status') === String(MemberStatusEnum.Active) &&
        r.params.get('Voix') === String(VoicePartEnum.Soprano) &&
        r.params.get('IsActive') === 'true'
    );
    expect(req.request.method).toBe('POST');
    req.flush({ Items: [], TotalCount: 0, CurrentPage: 2, PageSize: 20 });
  });

  it('GetChoraleUsersPaged : ChoirIds absent → aucun paramètre ChoirIds envoyé', () => {
    service.getChoirUsersPaged({ Page: 1, PageSize: 10 }, {}).subscribe();

    const req = httpMock.expectOne(r => r.url === `${ADMIN_USERS_BASE_URL}/GetChoirUsersPaged`);
    expect(req.request.params.getAll('ChoirIds')).toBeNull();
    req.flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 });
  });

  it('GetEvenementUsersPaged : transmet pagination ET filtre en query params (POST), EventIds en paramètres répétés', () => {
    service
      .getEventUsersPaged(
        { Page: 1, PageSize: 10 },
        { EventIds: ['evenement-1', 'evenement-2'], Role: UserRoleEnum.Organizer, Presence: AttendanceEnum.Attending, Upcoming: false }
      )
      .subscribe();

    const req = httpMock.expectOne(
      r =>
        r.url === `${ADMIN_USERS_BASE_URL}/GetEventUsersPaged` &&
        JSON.stringify(r.params.getAll('EventIds')) === JSON.stringify(['evenement-1', 'evenement-2']) &&
        r.params.get('Role') === String(UserRoleEnum.Organizer) &&
        r.params.get('Presence') === String(AttendanceEnum.Attending) &&
        r.params.get('Upcoming') === 'false'
    );
    expect(req.request.method).toBe('POST');
    req.flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 });
  });

  it('GetPaged (administrateurs) : transmet uniquement la pagination', () => {
    service.getPaged({ Page: 1, PageSize: 10 }).subscribe();

    const req = httpMock.expectOne(r => r.url === `${ADMIN_USERS_BASE_URL}/GetPaged`);
    expect(req.request.method).toBe('POST');
    expect(req.request.params.get('Page')).toBe('1');
    req.flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 });
  });

  it('GetSansRattachementUsersPaged : transmet uniquement la pagination', () => {
    service.getUnattachedUsersPaged({ Page: 1, PageSize: 10 }).subscribe();

    const req = httpMock.expectOne(r => r.url === `${ADMIN_USERS_BASE_URL}/GetUnattachedUsersPaged`);
    expect(req.request.method).toBe('POST');
    req.flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 });
  });

  it('GetUserDetail : appelle la route avec userId en query param (GET)', () => {
    service.getUserDetail('user-1').subscribe();

    const req = httpMock.expectOne(r => r.url === `${ADMIN_USERS_BASE_URL}/GetUserDetail` && r.params.get('userId') === 'user-1');
    expect(req.request.method).toBe('GET');
    req.flush({
      Id: 'user-1',
      Email: 'a@b.c',
      Firstname: 'A',
      Lastname: 'B',
      IsActive: true,
      IsGuestAccount: false,
      CreatedAt: '2026-01-01T00:00:00Z',
      LastConnection: null,
      LastActive: null,
      Choirs: [],
      Events: [],
      ClientAttachments: []
    });
  });

  // Exigence explicite du plan : l'erreur 409 (email déjà pris) doit remonter telle quelle
  // au composant appelant — jamais avalée par le service — pour que la fiche puisse afficher
  // un message inline exploitable au lieu d'un code brut.
  it('UpdateIdentity en 409 (email déjà pris) : l’erreur remonte telle quelle', () => {
    let receivedStatus: number | undefined;
    let receivedError: unknown;

    service.updateIdentity({ Id: 'user-1', Firstname: 'A', Lastname: 'B', Email: 'deja-pris@exemple.fr' }).subscribe({
      next: () => {
        throw new Error('ne doit pas réussir');
      },
      error: err => {
        receivedStatus = err.status;
        receivedError = err;
      }
    });

    const req = httpMock.expectOne(`${ADMIN_USERS_BASE_URL}/UpdateIdentity`);
    expect(req.request.method).toBe('PUT');
    req.flush({ Message: 'Cette adresse e-mail est déjà utilisée.' }, { status: 409, statusText: 'Conflict' });

    expect(receivedStatus).toBe(409);
    expect(receivedError).toBeTruthy();
  });

  it('Delete : envoie userId en query param (DELETE)', () => {
    service.delete('user-1').subscribe();

    const req = httpMock.expectOne(r => r.url === `${ADMIN_USERS_BASE_URL}/Delete` && r.params.get('userId') === 'user-1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });
});
