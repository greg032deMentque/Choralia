import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { AuthStore } from '@core/auth.store';
import { RoutePaths } from '@core/route-paths';
import { isValidUuid } from '@core/uuid.util';

// Zone /client/:clientId (« Ma structure ») — rattachement ResponsableClient à CETTE
// structure précise (ClientRoles), pas à une structure quelconque. Un admin global passe
// toujours (cohérent avec les autres guards de zone).
export const clientRoleGuard: CanActivateFn = route => {
  const authStore = inject(AuthStore);
  const router = inject(Router);

  if (!authStore.isAuthenticated()) {
    return router.createUrlTree([`/${RoutePaths.Login}`]);
  }

  if (authStore.isGlobalAdmin()) {
    return true;
  }

  const clientId = route.paramMap.get('clientId');
  if (!isValidUuid(clientId)) {
    return router.createUrlTree([authStore.currentZone().path]);
  }

  const hasAccess = authStore.clientRoles().some(c => c.ClientId === clientId);
  return hasAccess ? true : router.createUrlTree([authStore.currentZone().path]);
};
