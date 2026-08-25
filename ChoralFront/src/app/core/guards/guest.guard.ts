import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { AuthStore } from '@core/auth.store';
import { RoutePaths } from '@core/route-paths';

// Empêche l'accès à /login si une session est déjà actif — redirige vers /dashboard.
export const guestGuard: CanActivateFn = () => {
  const authStore = inject(AuthStore);
  const router = inject(Router);

  return authStore.isAuthenticated() ? router.createUrlTree([`/${RoutePaths.Dashboard}`]) : true;
};
