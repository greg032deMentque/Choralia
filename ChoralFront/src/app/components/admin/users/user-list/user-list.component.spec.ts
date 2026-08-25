import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Params, Router, convertToParamMap, provideRouter } from '@angular/router';
import { UserListComponent } from './user-list.component';
import { environment } from '@env/environment';
import { RoutePaths } from '@core/route-paths';
import { UserRoleEnum } from '@app/enums/user-role.enum';
import { VoicePartEnum } from '@app/enums/voice-part.enum';
import { MemberStatusEnum } from '@app/enums/member-status.enum';
import { IAdminChoirUserListItem } from '@models/admin-models/admin-choir-user-list-item.model';
import { IAdminUserListItem } from '@models/admin-models/admin-user-list-item.model';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';
import { verifyIgnoringIcons } from '@app/testing/verify-ignoring-icons';

const ADMIN_USERS_BASE_URL = `${environment.apiUrl}admin-users`;

const EMPTY_PAGE = { Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 };

// Ligne brute telle que reçue du back (Role en chaîne unique, pas encore convertie en
// UserRoleEnum par AdminUserService.getEventUsersPaged).
const FAKE_EVENT_ROW_RAW = {
  Id: 'rattachement-1',
  UserId: 'user-1',
  Firstname: 'Jean',
  Lastname: 'Dupont',
  Email: 'jean.dupont@exemple.fr',
  EventId: 'evenement-1',
  EventTitle: 'Concert de Noël',
  EventStartDate: '2026-12-24T20:00:00Z',
  ChoirId: null,
  ChoirName: null,
  Role: 'Participant',
  Presence: 1,
  Status: 1
};

const FAKE_CHOIR_ROW: IAdminChoirUserListItem = {
  Id: 'rattachement-2',
  UserId: 'user-2',
  Firstname: 'Marie',
  Lastname: 'Martin',
  Email: 'marie.martin@exemple.fr',
  ChoirId: 'choir-1',
  ChoirName: 'Chorale des Alpes',
  Roles: [UserRoleEnum.Singer],
  PrimaryVoicePart: VoicePartEnum.Soprano,
  Status: MemberStatusEnum.Active,
  IsActive: true,
  LastActive: null
};

const FAKE_ADMIN_ROW: IAdminUserListItem = {
  Id: 'user-3',
  Email: 'admin@exemple.fr',
  Firstname: 'Alice',
  Lastname: 'Admin',
  IsActive: true,
  LastConnection: null,
  CreatedAt: '2026-01-01T00:00:00Z',
  CreatedByUserId: null,
  CreatedByName: null
};

describe('UserListComponent', () => {
  let httpMock: HttpTestingController;
  let router: Router;
  // `queryParamMap` est un getter (lu paresseusement) plutôt qu'une valeur figée à la
  // configuration : chaque test ajuste cette variable puis crée le composant, sans jamais
  // reconfigurer TestBed en cours de test (TestBed.resetTestingModule()/overrideProvider()
  // interfère avec la réinitialisation automatique du framework entre files de specs — voir
  // décision consignée dans le rapport de la CORRECTION CIBLÉE).
  let currentQueryParams: Params = {};

  beforeEach(() => {
    currentQueryParams = {};
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { get queryParamMap() { return convertToParamMap(currentQueryParams); } } }
        }
      ]
    });
    // IconComponent (rendu par ce composant via <app-icon> et DataStateComponent) charge ses
    // SVG en HTTP sans passer par HttpTestingController une fois stubbé — voir
    // src/app/testing/icon-http-stub.ts. Le flush(() => true) ci-dessous reste utile pour les
    // appels admin-users non pertinents pour l'assertion du test, mais n'a plus besoin de
    // couvrir les requêtes /icons/.
    stubIconHttpRequests();
    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    // Balaie tout ce qui n'a pas été explicitement vérifié dans le test (tout appel
    // admin-users non pertinent pour l'assertion du test).
    httpMock.match(() => true).forEach(req => req.flush(EMPTY_PAGE));
    verifyIgnoringIcons(httpMock);
  });

  it("changement d'onglet : remet la pagination à la page 1", () => {
    const fixture = TestBed.createComponent(UserListComponent);
    const component = fixture.componentInstance;

    // Chargement initial (onglet "chorales" par défaut, constructeur).
    httpMock.expectOne(r => r.url === `${ADMIN_USERS_BASE_URL}/GetChoirUsersPaged`).flush(EMPTY_PAGE);

    component.onPageChange(4);
    httpMock
      .expectOne(r => r.url === `${ADMIN_USERS_BASE_URL}/GetChoirUsersPaged` && r.params.get('Page') === '4')
      .flush(EMPTY_PAGE);
    expect(component.page()).toBe(4);

    component.selectTab('admins');

    const req = httpMock.expectOne(r => r.url === `${ADMIN_USERS_BASE_URL}/GetPaged`);
    expect(req.request.params.get('Page')).toBe('1');
    expect(component.page()).toBe(1);
    req.flush(EMPTY_PAGE);
  });

  it("les filtres avancés d'un onglet ne sont jamais transmis à un autre onglet", () => {
    const fixture = TestBed.createComponent(UserListComponent);
    const component = fixture.componentInstance;

    httpMock.expectOne(r => r.url === `${ADMIN_USERS_BASE_URL}/GetChoirUsersPaged`).flush(EMPTY_PAGE);

    component.onChoirVoicePartChange(String(VoicePartEnum.Soprano));
    const choirReq = httpMock.expectOne(r => r.url === `${ADMIN_USERS_BASE_URL}/GetChoirUsersPaged`);
    expect(choirReq.request.params.get('Voix')).toBe(String(VoicePartEnum.Soprano));
    choirReq.flush(EMPTY_PAGE);

    component.selectTab('admins');
    const adminReq = httpMock.expectOne(r => r.url === `${ADMIN_USERS_BASE_URL}/GetPaged`);
    // L'onglet Administrateurs (PaginateViewModel nu côté back) ne doit JAMAIS recevoir un
    // paramètre Voix — même s'il a été positionné sur l'onglet Chorales juste avant.
    expect(adminReq.request.params.has('Voix')).toBe(false);
    adminReq.flush(EMPTY_PAGE);

    component.selectTab('events');
    const eventReq = httpMock.expectOne(r => r.url === `${ADMIN_USERS_BASE_URL}/GetEventUsersPaged`);
    expect(eventReq.request.params.has('Voix')).toBe(false);
    eventReq.flush(EMPTY_PAGE);
  });

  it("ligne d'événement sans chorale porteuse : affiche un repli explicite, jamais 'undefined'", async () => {
    const fixture = TestBed.createComponent(UserListComponent);
    const component = fixture.componentInstance;

    httpMock.expectOne(r => r.url === `${ADMIN_USERS_BASE_URL}/GetChoirUsersPaged`).flush(EMPTY_PAGE);

    component.selectTab('events');
    httpMock
      .expectOne(r => r.url === `${ADMIN_USERS_BASE_URL}/GetEventUsersPaged`)
      .flush({ Items: [FAKE_EVENT_ROW_RAW], TotalCount: 1, CurrentPage: 1, PageSize: 10 });

    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('undefined');
    expect(text).toContain('Événement autonome');
  });

  it('clic sur une ligne : navigue avec UserId (onglets Chorales/Événements) ou Id (onglets Administrateurs/Sans rattachement)', () => {
    const fixture = TestBed.createComponent(UserListComponent);
    const component = fixture.componentInstance;
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    httpMock.expectOne(r => r.url === `${ADMIN_USERS_BASE_URL}/GetChoirUsersPaged`).flush(EMPTY_PAGE);

    component.onRowClick(FAKE_CHOIR_ROW);
    expect(navigateSpy).toHaveBeenCalledWith(['/', RoutePaths.Admin, RoutePaths.AdminUsers, 'user-2']);

    component.onRowClick(FAKE_ADMIN_ROW);
    expect(navigateSpy).toHaveBeenCalledWith(['/', RoutePaths.Admin, RoutePaths.AdminUsers, 'user-3']);
  });

  // Bug corrigé (CORRECTION CIBLÉE) : les tuiles "Active"/"Invités non activés" du tableau de
  // bord admin naviguent vers cette liste avec `?IsActive=true`/`?IsGuestAccount=true`, mais
  // rien ne le lisait ni ne sélectionnait le bon onglet — le premier appel réseau partait
  // toujours sur l'onglet "chorales" par défaut, non filtré.
  describe('lecture des query params au chargement', () => {
    it('tab=admins dans l’URL : ouvre directement cet onglet', () => {
      currentQueryParams = { tab: 'admins' };
      const fixture = TestBed.createComponent(UserListComponent);
      const component = fixture.componentInstance;

      expect(component.activeTab()).toBe('admins');
      httpMock.expectOne(r => r.url === `${ADMIN_USERS_BASE_URL}/GetPaged`).flush(EMPTY_PAGE);
    });

    it('tab=unattached dans l’URL : ouvre directement cet onglet', () => {
      currentQueryParams = { tab: 'unattached' };
      const fixture = TestBed.createComponent(UserListComponent);
      const component = fixture.componentInstance;

      expect(component.activeTab()).toBe('unattached');
      httpMock.expectOne(r => r.url === `${ADMIN_USERS_BASE_URL}/GetUnattachedUsersPaged`).flush(EMPTY_PAGE);
    });

    it('IsGuestAccount=true sans onglet explicite : bascule sur l’onglet "sans-rattachement" et filtre dès le premier appel', () => {
      currentQueryParams = { IsGuestAccount: 'true' };
      const fixture = TestBed.createComponent(UserListComponent);
      const component = fixture.componentInstance;

      expect(component.activeTab()).toBe('unattached');
      const req = httpMock.expectOne(r => r.url === `${ADMIN_USERS_BASE_URL}/GetUnattachedUsersPaged`);
      expect(req.request.params.get('IsGuestAccount')).toBe('true');
      req.flush(EMPTY_PAGE);
    });

    it('IsActive=true seul (sans onglet ni IsGuestAccount) : bascule sur l’onglet "administrateurs" et filtre dès le premier appel', () => {
      currentQueryParams = { IsActive: 'true' };
      const fixture = TestBed.createComponent(UserListComponent);
      const component = fixture.componentInstance;

      expect(component.activeTab()).toBe('admins');
      const req = httpMock.expectOne(r => r.url === `${ADMIN_USERS_BASE_URL}/GetPaged`);
      expect(req.request.params.get('IsActive')).toBe('true');
      req.flush(EMPTY_PAGE);
    });

    it('aucun query param : comportement inchangé (onglet "chorales" par défaut, aucun filtre)', () => {
      const fixture = TestBed.createComponent(UserListComponent);
      const component = fixture.componentInstance;

      expect(component.activeTab()).toBe('choirs');
      httpMock.expectOne(r => r.url === `${ADMIN_USERS_BASE_URL}/GetChoirUsersPaged`).flush(EMPTY_PAGE);
    });

    it('tab=inconnu (malformé) : ignoré silencieusement, retombe sur "chorales", aucune exception', () => {
      currentQueryParams = { tab: 'inconnu' };

      expect(() => {
        TestBed.createComponent(UserListComponent);
      }).not.toThrow();

      httpMock.expectOne(r => r.url === `${ADMIN_USERS_BASE_URL}/GetChoirUsersPaged`).flush(EMPTY_PAGE);
    });
  });
});
