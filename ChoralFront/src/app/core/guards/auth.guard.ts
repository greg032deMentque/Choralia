import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { AuthStore } from '@core/auth.store';
import { RoutePaths } from '@core/route-paths';

export const authGuard: CanActivateFn = () => {
  const authStore = inject(AuthStore);
  const router = inject(Router);

  return authStore.isAuthenticated() ? true : router.createUrlTree([`/${RoutePaths.Login}`]);
};
