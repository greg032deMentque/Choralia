import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { AuthStore } from '@core/auth.store';

// Route /dashboard : jamais de contenu propre, redirige systématiquement vers la zone réelle
// de l'utilisateur (AuthStore.currentZone()). Cible neutre et stable utilisée par guestGuard,
// le fallback de returnUrl (route-paths.ts) et NotFoundComponent (composant figé — sa target
// de redirection n'a pas été modifiée, elle traverse simplement cette route).
export const zoneRedirectGuard: CanActivateFn = () => {
  const authStore = inject(AuthStore);
  const router = inject(Router);
  return router.createUrlTree([authStore.currentZone().path]);
};
