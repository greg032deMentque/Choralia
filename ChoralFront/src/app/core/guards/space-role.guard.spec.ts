import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot, UrlTree, convertToParamMap } from '@angular/router';
import { spaceRoleGuard } from '@core/guards/space-role.guard';
import { AuthStore } from '@core/auth.store';
import { RoutePaths, managementPath } from '@core/route-paths';
import { UserRoleEnum } from '@app/enums/user-role.enum';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';
import { IAuthenticatedUser } from '@models/auth-models/authenticated-user.model';

const SPACE_CHOIR = 'e1111111-1111-1111-1111-111111111111';
const SPACE_EVENT = 'e2222222-2222-2222-2222-222222222222';

function buildUser(): IAuthenticatedUser {
  return {
    Id: 'user-1',
    Email: 'user@choralehelper.fr',
    Firstname: 'Jean',
    Lastname: 'Dupont',
    Roles: [],
    SpaceRoles: [
      {
        SpaceId: SPACE_CHOIR,
        Name: 'Chorale A',
        SpaceType: SpaceTypeEnum.Choir,
        Roles: ['Manager'],
        ClientId: null,
        ChoirId: null,
        PrimaryVoicePart: null
      },
      {
        SpaceId: SPACE_EVENT,
        Name: 'Concert de Noël',
        SpaceType: SpaceTypeEnum.Event,
        Roles: ['Organizer'],
        ClientId: null,
        ChoirId: SPACE_CHOIR,
        PrimaryVoicePart: null
      }
    ],
    ClientRoles: []
  };
}

// Simule une route enfant SANS son propre :spaceId (ex. /management/:spaceId/songs) : le
// paramètre n'est disponible que sur l'ancêtre, comme dans app.routes.ts réel (pas de
// paramsInheritanceStrategy: 'always' — voir le commentaire de espace-role.guard.ts).
function makeChildRoute(spaceId: string | null): ActivatedRouteSnapshot {
  const parent = { paramMap: convertToParamMap(spaceId ? { spaceId } : {}), parent: null } as ActivatedRouteSnapshot;
  return { paramMap: convertToParamMap({}), parent } as ActivatedRouteSnapshot;
}

function runGuard(
  allowedRoles: UserRoleEnum[],
  route: ActivatedRouteSnapshot,
  allowedTypes?: SpaceTypeEnum[]
): boolean | UrlTree {
  const guard = spaceRoleGuard(allowedRoles, allowedTypes);
  return TestBed.runInInjectionContext(() => guard(route, {} as RouterStateSnapshot)) as boolean | UrlTree;
}

describe('spaceRoleGuard', () => {
  let authStore: AuthStore;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({});
    authStore = TestBed.inject(AuthStore);
    authStore.setCurrentUser(buildUser());
  });

  it("rôle correct sur l'espace actif (désigné par la route) : passe et synchronise l'espace actif", () => {
    const result = runGuard([UserRoleEnum.Manager], makeChildRoute(SPACE_CHOIR));

    expect(result).toBe(true);
    expect(authStore.activeSpaceId()).toBe(SPACE_CHOIR);
  });

  it('rôle détenu sur un AUTRE espace : refusé (ferme la faille d\'accès trans-espace)', () => {
    // L'utilisateur a Organizer sur SPACE_EVENT, pas Responsable — une route qui
    // exige Responsable sur SPACE_EVENT doit être refusée même si l'utilisateur a bien
    // ce rôle ailleurs (SPACE_CHOIR).
    const result = runGuard([UserRoleEnum.Manager], makeChildRoute(SPACE_EVENT));

    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe(managementPath(SPACE_CHOIR, RoutePaths.Dashboard));
  });

  it('Organizer sur une route réservée aux chorales : refusé', () => {
    const result = runGuard([UserRoleEnum.Organizer], makeChildRoute(SPACE_EVENT), [SpaceTypeEnum.Choir]);

    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe(managementPath(SPACE_EVENT, RoutePaths.Dashboard));
  });

  it('aucun espace actif exploitable dans la route (paramètre absent) : redirection vers /no-space, pas un 403', () => {
    const result = runGuard([UserRoleEnum.Manager], makeChildRoute(null));

    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe(`/${RoutePaths.NoSpace}`);
  });
});
