import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { AuthStore } from '@core/auth.store';
import { RoutePaths } from '@core/route-paths';

// Zone /admin — claim JWT global Admin (Roles), jamais scopé par espace. Un utilisateur
// authentifié sans le claim est renvoyé vers sa vraie zone (currentZone), pas vers une
// page bloquée : évite le "flash" d'une zone à laquelle il n'a jamais accès.
export const adminGuard: CanActivateFn = () => {
  const authStore = inject(AuthStore);
  const router = inject(Router);

  if (!authStore.isAuthenticated()) {
    return router.createUrlTree([`/${RoutePaths.Login}`]);
  }

  return authStore.isGlobalAdmin() ? true : router.createUrlTree([authStore.currentZone().path]);
};
