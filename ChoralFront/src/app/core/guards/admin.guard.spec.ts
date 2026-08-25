import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot, UrlTree } from '@angular/router';
import { adminGuard } from '@core/guards/admin.guard';
import { AuthStore } from '@core/auth.store';
import { RoutePaths } from '@core/route-paths';
import { IAuthenticatedUser } from '@models/auth-models/authenticated-user.model';

function buildUser(roles: string[]): IAuthenticatedUser {
  return {
    Id: 'user-1',
    Email: 'user@choralehelper.fr',
    Firstname: 'Jean',
    Lastname: 'Dupont',
    Roles: roles,
    SpaceRoles: [],
    ClientRoles: []
  };
}

function runGuard(): boolean | UrlTree {
  return TestBed.runInInjectionContext(() => adminGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot)) as boolean | UrlTree;
}

describe('adminGuard', () => {
  let authStore: AuthStore;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({});
    authStore = TestBed.inject(AuthStore);
  });

  it('claim Admin présent : passe', () => {
    authStore.setCurrentUser(buildUser(['Admin']));

    expect(runGuard()).toBe(true);
  });

  it("claim Admin absent : redirection propre vers la vraie zone de l'utilisateur (jamais un flash de contenu admin)", () => {
    authStore.setCurrentUser(buildUser(['Singer']));

    const result = runGuard();

    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe(`/${RoutePaths.Start}`);
  });

  it("session expirée (non authentifié) : redirection vers /login", () => {
    const result = runGuard();

    expect(result).toBeInstanceOf(UrlTree);
    expect((result as UrlTree).toString()).toBe(`/${RoutePaths.Login}`);
  });
});
