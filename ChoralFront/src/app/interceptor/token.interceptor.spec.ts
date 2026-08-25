import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { provideToastr } from 'ngx-toastr';
import { tokenInterceptor } from './token.interceptor';
import { AuthStore } from '@core/auth.store';
import { StorageService } from '@app/services/storage.service';
import { environment } from '@env/environment';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';
import { IAuthenticatedUser } from '@models/auth-models/authenticated-user.model';
import { IClientRoleAssignment } from '@models/auth-models/client-role-assignment.model';

const SPACE_ID = '11111111-1111-1111-1111-111111111111';
const CLIENT_ID = '33333333-3333-3333-3333-333333333333';

// Route générique : les tests n'ont besoin que d'une navigation qui aboutisse (NavigationEnd),
// jamais d'un composant réel — DisplayedZoneStore ne lit que l'URL, pas l'arbre de routes.
@Component({ selector: 'app-blank-test', template: '', standalone: true })
class BlankTestComponent {
  protected readonly isTestStub = true;
}

// Jeton non signé : seule la charge utile `exp` est lue côté front (jwt.util), jamais la
// signature — le back reste seul juge de la validité réelle.
function buildToken(expiresInSeconds: number): string {
  const payload = { exp: Math.floor(Date.now() / 1000) + expiresInSeconds };
  const base64 = btoa(JSON.stringify(payload)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `header.${base64}.signature`;
}

function buildUser(roles: string[], globalRoles: string[] = [], clientRoles: IClientRoleAssignment[] = []): IAuthenticatedUser {
  return {
    Id: 'user-1',
    Email: 'user@choralehelper.fr',
    Firstname: 'Jean',
    Lastname: 'Dupont',
    Roles: globalRoles,
    SpaceRoles: [
      { SpaceId: SPACE_ID, Name: 'Chorale A', SpaceType: SpaceTypeEnum.Choir, Roles: roles, ClientId: null, ChoirId: null, PrimaryVoicePart: null }
    ],
    ClientRoles: clientRoles
  };
}

// Admin global sans aucun SpaceRoles : reproduit le cas réel du bug — spaceRoleGuard court-
// circuite AuthStore.setActiveSpace pour un admin (allowedRoles n'est même pas vérifié), donc
// AuthStore.activeSpaceId reste null même en naviguant dans /management/:spaceId.
function buildAdminOnlyUser(): IAuthenticatedUser {
  return {
    Id: 'admin-1',
    Email: 'admin@choralehelper.fr',
    Firstname: 'Ada',
    Lastname: 'Admin',
    Roles: ['Admin'],
    SpaceRoles: [],
    ClientRoles: []
  };
}

describe('tokenInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let storage: StorageService;
  let authStore: AuthStore;
  let router: Router;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([tokenInterceptor])),
        provideHttpClientTesting(),
        provideRouter([{ path: '**', component: BlankTestComponent }]),
        provideToastr()
      ]
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    storage = TestBed.inject(StorageService);
    authStore = TestBed.inject(AuthStore);
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    httpMock.verify();
    sessionStorage.clear();
  });

  /**
   * Non-régression du défaut principal : /api/auth/Logout était classé « endpoint d'auth » et
   * partait donc SANS Bearer, alors que AuthController.Logout porte [Authorize(Bearer)]. La
   * requête repartait en 401, accountService.Logout n'était jamais exécuté, et le refresh token
   * restait valide côté serveur — une déconnexion qui ne déconnecte rien (OWASP A07).
   */
  it('attache le Bearer sur POST /api/auth/Logout', () => {
    storage.SetToken(buildToken(3600));

    http.post(`${environment.apiUrl}auth/Logout`, {}).subscribe();

    const req = httpMock.expectOne(`${environment.apiUrl}auth/Logout`);
    expect(req.request.headers.get('Authorization')).toBe(`Bearer ${storage.GetToken()}`);
    req.flush(null);
  });

  it.each([['Login'], ['RefreshToken'], ['ForgotPassword'], ['ResetPassword']])(
    'n\'attache aucun Bearer sur /api/auth/%s',
    endpoint => {
      storage.SetToken(buildToken(3600));

      http.post(`${environment.apiUrl}auth/${endpoint}`, {}).subscribe();

      const req = httpMock.expectOne(`${environment.apiUrl}auth/${endpoint}`);
      expect(req.request.headers.has('Authorization')).toBe(false);
      req.flush(null);
    }
  );

  it('pose X-Space-Id sur une requête métier en zone management, avec le spaceId de l\'URL affichée', async () => {
    storage.SetToken(buildToken(3600));
    authStore.setCurrentUser(buildUser(['Manager']));
    authStore.setActiveSpace(SPACE_ID);
    await router.navigateByUrl(`/management/${SPACE_ID}/dashboard`);

    http.get(`${environment.apiUrl}songs/GetById`).subscribe();

    const req = httpMock.expectOne(`${environment.apiUrl}songs/GetById`);
    expect(req.request.headers.get('X-Space-Id')).toBe(SPACE_ID);
    req.flush(null);
  });

  it('pose X-Space-Id depuis AuthStore.activeSpaceId en zone membre (/me ne porte aucun spaceId dans l\'URL)', async () => {
    storage.SetToken(buildToken(3600));
    authStore.setCurrentUser(buildUser(['Singer']));
    authStore.setActiveSpace(SPACE_ID);
    await router.navigateByUrl('/me');

    http.get(`${environment.apiUrl}member-home/GetById`).subscribe();

    const req = httpMock.expectOne(`${environment.apiUrl}member-home/GetById`);
    expect(req.request.headers.get('X-Space-Id')).toBe(SPACE_ID);
    req.flush(null);
  });

  // La zone /admin est globale : y transmettre un scope d'espace ferait porter à une requête
  // d'administration le périmètre d'une chorale.
  it('ne pose pas X-Space-Id en zone admin, même avec un espace actif stocké', async () => {
    storage.SetToken(buildToken(3600));
    authStore.setCurrentUser(buildUser([], ['Admin']));
    authStore.setActiveSpace(SPACE_ID);
    await router.navigateByUrl('/admin/audit');

    http.get(`${environment.apiUrl}admin-audit/GetPaged`).subscribe();

    const req = httpMock.expectOne(`${environment.apiUrl}admin-audit/GetPaged`);
    expect(req.request.headers.has('X-Space-Id')).toBe(false);
    req.flush(null);
  });

  /**
   * Régression bug 1 : avec l'ancienne lecture (authStore.currentZone().kind === 'admin'), un
   * Admin global voyait TOUJOURS sa zone résolue à 'admin' (priorité de resolveZone()), donc ne
   * recevait jamais X-Space-Id en naviguant manuellement dans /management/:spaceId — même s'il
   * n'a lui-même aucun SpaceRoles (spaceRoleGuard court-circuite setActiveSpace pour un admin).
   * Le spaceId doit venir de l'URL affichée, jamais d'AuthStore.activeSpaceId dans ce cas.
   */
  it('régression : pose X-Space-Id avec le spaceId de l\'URL en zone management, même pour un Admin global sans SpaceRoles', async () => {
    storage.SetToken(buildToken(3600));
    authStore.setCurrentUser(buildAdminOnlyUser());
    await router.navigateByUrl(`/management/${SPACE_ID}/dashboard`);

    expect(authStore.activeSpaceId()).toBeNull();

    http.get(`${environment.apiUrl}songs/GetById`).subscribe();

    const req = httpMock.expectOne(`${environment.apiUrl}songs/GetById`);
    expect(req.request.headers.get('X-Space-Id')).toBe(SPACE_ID);
    req.flush(null);
  });

  /**
   * Régression bug 2 : avec l'ancienne lecture, un ClientManager qui est AUSSI Manager d'une
   * chorale gardait 'management' comme currentZone().kind même en naviguant dans
   * /client/:clientId (priorité management > client dans resolveZone()) : la requête partait à
   * tort avec le X-Space-Id de son espace de gestion. /client/:clientId n'est jamais scopé
   * espace.
   */
  it('régression : ne pose pas X-Space-Id sur /client/:clientId même si un espace de management est actif', async () => {
    storage.SetToken(buildToken(3600));
    authStore.setCurrentUser(buildUser(['Manager'], [], [{ ClientId: CLIENT_ID, Name: 'Structure', Roles: ['ClientManager'] }]));
    authStore.setActiveSpace(SPACE_ID);
    await router.navigateByUrl(`/client/${CLIENT_ID}`);

    http.get(`${environment.apiUrl}clients/GetById`).subscribe();

    const req = httpMock.expectOne(`${environment.apiUrl}clients/GetById`);
    expect(req.request.headers.has('X-Space-Id')).toBe(false);
    req.flush(null);
  });

  it('rafraîchit le token expiré puis rejoue la requête avec le nouveau', () => {
    const expired = buildToken(-60);
    const renewed = buildToken(3600);
    storage.SetToken(expired);
    storage.SetRefreshToken('refresh-1');

    http.get(`${environment.apiUrl}songs/GetById`).subscribe();

    const refreshReq = httpMock.expectOne(`${environment.apiUrl}auth/RefreshToken`);
    expect(refreshReq.request.body).toMatchObject({ AccessToken: expired, RefreshToken: 'refresh-1' });
    refreshReq.flush({ AccessToken: renewed, RefreshToken: 'refresh-2' });

    const businessReq = httpMock.expectOne(`${environment.apiUrl}songs/GetById`);
    expect(businessReq.request.headers.get('Authorization')).toBe(`Bearer ${renewed}`);
    businessReq.flush(null);
  });

  // Sans refresh token il n'y a rien à rejouer : la requête part telle quelle et c'est le 401
  // du serveur qui tranche — jamais une boucle de refresh côté front.
  it('laisse passer la requête sans Bearer quand le token est expiré et qu\'aucun refresh token n\'existe', () => {
    storage.SetToken(buildToken(-60));

    http.get(`${environment.apiUrl}songs/GetById`).subscribe();

    const req = httpMock.expectOne(`${environment.apiUrl}songs/GetById`);
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush(null);
  });
});
