import { ActivatedRouteSnapshot, CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { AuthStore } from '@core/auth.store';
import { RoutePaths, managementPath } from '@core/route-paths';
import { UserRoleEnum, userRoleFromString } from '@app/enums/user-role.enum';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';
import { isValidUuid } from '@core/uuid.util';

// app.routes.ts n'actif pas `paramsInheritanceStrategy: 'always'` (comportement Angular par
// défaut : 'emptyOnly') — un enfant avec son propre segment de chemin ('songs', 'events',
// ...) n'hérite PAS automatiquement de :spaceId matché par le parent (/management/:spaceId).
// On remonte donc explicitement la chaîne d'ancêtres plutôt que de change ce réglage global
// (moindre surface d'effet de bord sur le reste du routage).
function findSpaceId(route: ActivatedRouteSnapshot): string | null {
  for (let current: ActivatedRouteSnapshot | null = route; current; current = current.parent) {
    const value = current.paramMap.get('spaceId');
    if (value !== null) return value;
  }
  return null;
}

// Remplace chorale-role.guard.ts (rôles désormais scopés par ESPACE, pas par chorale seule).
// Filtre UX côté client — le backend reste la seule source de vérité sécurité (policies
// ASP.NET scopées par espace via X-Space-Id). Vérifie le rôle sur l'espace DÉSIGNÉ PAR LA
// ROUTE (paramètre :spaceId), jamais sur "un espace quelconque" détenu par l'utilisateur :
// un rôle Responsable détenu sur un autre espace ne donne jamais accès ici (ferme la faille
// d'accès trans-espace). Une fois l'accès validé, synchronise AuthStore (setActiveSpace) pour
// que l'espace actif suive toujours l'URL couramment affichée.
export function spaceRoleGuard(allowedRoles: UserRoleEnum[], allowedTypes?: SpaceTypeEnum[]): CanActivateFn {
  return route => {
    const authStore = inject(AuthStore);
    const router = inject(Router);

    if (!authStore.isAuthenticated()) {
      return router.createUrlTree([`/${RoutePaths.Login}`]);
    }

    if (authStore.isGlobalAdmin()) {
      return true;
    }

    const spaceId = findSpaceId(route);
    if (!isValidUuid(spaceId)) {
      // Pas d'espace exploitable dans l'URL : jamais de 403, un écran dédié explicite.
      return router.createUrlTree([`/${RoutePaths.NoSpace}`]);
    }

    const assignment = authStore.spaceRoles().find(e => e.SpaceId === spaceId);
    if (!assignment) {
      // L'utilisateur n'a AUCUN rattachement à CET espace précis — y compris s'il a le rôle
      // recherché sur un espace différent. On le renvoie vers sa vraie zone plutôt qu'un blocage.
      return router.createUrlTree([authStore.currentZone().path]);
    }

    if (allowedTypes && !allowedTypes.includes(assignment.SpaceType)) {
      // Rôle légitime sur cet espace, mais fonctionnalité réservée à un autre type d'espace
      // (ex. Chants/Membres réservés aux chorales) — renvoi vers le tableau de bord de cet
      // espace plutôt qu'une zone différente : l'utilisateur reste dans son contexte.
      return router.createUrlTree([managementPath(spaceId, RoutePaths.Dashboard)]);
    }

    const roles = assignment.Roles
      .map(role => userRoleFromString(role))
      .filter((role): role is UserRoleEnum => role !== null);
    const hasAccess = allowedRoles.some(role => roles.includes(role));

    if (!hasAccess) {
      return router.createUrlTree([authStore.currentZone().path]);
    }

    authStore.setActiveSpace(spaceId);
    return true;
  };
}
