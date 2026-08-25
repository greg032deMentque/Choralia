import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { DashboardComponent } from './dashboard.component';
import { AuthStore } from '@core/auth.store';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';
import { IAuthenticatedUser } from '@models/auth-models/authenticated-user.model';
import { IChoirKpi } from '@models/common-models/dashboard-summary.model';
import { environment } from '@env/environment';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';
import { MemberHomeComponent } from '@app/components/me/member-home/member-home.component';
import { RoutePaths } from '@core/route-paths';

const DASHBOARD_BASE_URL = `${environment.apiUrl}dashboard`;

function buildUser(partial: Partial<IAuthenticatedUser>): IAuthenticatedUser {
  return {
    Id: 'user-1',
    Email: 'user@choralehelper.fr',
    Firstname: 'Jean',
    Lastname: 'Dupont',
    Roles: [],
    SpaceRoles: [],
    ClientRoles: [],
    ...partial
  };
}

const FAKE_KPI: IChoirKpi = {
  SongsInRepertoire: 12,
  IncompleteSongs: 2,
  RecordingsPendingReview: 1,
  Members: 20,
  InvitedMembers: 3,
  UpcomingEvents: []
};

// GET /api/dashboard/ChoirKpi n'existe que pour un espace de type Chorale
// (DashboardController.ChoirKpi interroge la chorale du scope) : sur un espace Événement,
// l'appel renvoie 403 (constaté en navigateur avec organisateur.structure@chorale.local).
// Le composant ne doit donc l'appeler que si l'espace actif est une chorale, et ne jamais
// afficher de message d'erreur permanent pour un espace Événement (D30 — pas d'indicateur
// fabriqué en repli).
describe('DashboardComponent', () => {
  let httpMock: HttpTestingController;
  let authStore: AuthStore;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    stubIconHttpRequests();
    httpMock = TestBed.inject(HttpTestingController);
    authStore = TestBed.inject(AuthStore);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it("espace actif de type Événement : n'appelle jamais ChoirKpi et n'affiche aucun message d'erreur d'indicateurs", async () => {
    authStore.setCurrentUser(
      buildUser({
        SpaceRoles: [
          {
            SpaceId: 'evt-1',
            Name: 'Concert de Noël',
            SpaceType: SpaceTypeEnum.Event,
            Roles: ['Organizer'],
            ClientId: null,
            ChoirId: null,
            PrimaryVoicePart: null
          }
        ]
      })
    );

    const fixture = TestBed.createComponent(DashboardComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    httpMock.expectNone(r => r.url === `${DASHBOARD_BASE_URL}/ChoirKpi`);
    expect(fixture.componentInstance.error()).toBeNull();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('Impossible de charger les indicateurs.');
    expect(text).toContain('Aucun indicateur disponible pour un événement.');
  });

  it('espace actif de type Chorale : appelle ChoirKpi comme avant (non-régression)', () => {
    authStore.setCurrentUser(
      buildUser({
        SpaceRoles: [
          {
            SpaceId: 'choir-1',
            Name: 'Chorale A',
            SpaceType: SpaceTypeEnum.Choir,
            Roles: ['Manager'],
            ClientId: null,
            ChoirId: null,
            PrimaryVoicePart: null
          }
        ]
      })
    );

    const fixture = TestBed.createComponent(DashboardComponent);

    const req = httpMock.expectOne(r => r.url === `${DASHBOARD_BASE_URL}/ChoirKpi`);
    req.flush(FAKE_KPI);

    expect(fixture.componentInstance.kpi()).toEqual(FAKE_KPI);
  });

  it("accueil membre sur un espace Événement : masque l'accès aux chants", () => {
    authStore.setCurrentUser(
      buildUser({
        SpaceRoles: [
          {
            SpaceId: 'event-1',
            Name: 'Concert',
            SpaceType: SpaceTypeEnum.Event,
            Roles: ['Participant'],
            ClientId: null,
            ChoirId: null,
            PrimaryVoicePart: null
          }
        ]
      })
    );
    const fixture = TestBed.createComponent(MemberHomeComponent);
    httpMock.expectOne(request => request.url === `${environment.apiUrl}events/GetPaged`).flush({
      Items: [],
      TotalCount: 0,
      Page: 1,
      PageSize: 1
    });
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).not.toContain('Mes chants');
    expect(root.querySelector(`a[href="/${RoutePaths.Me}/${RoutePaths.Songs}"]`)).toBeNull();
  });
});
