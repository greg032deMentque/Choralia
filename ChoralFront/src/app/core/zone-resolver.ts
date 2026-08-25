import { IAuthenticatedUser } from '@models/auth-models/authenticated-user.model';
import { ISpaceRoleAssignment } from '@models/auth-models/space-role-assignment.model';
import { UserRoleEnum, userRoleFromString } from '@app/enums/user-role.enum';
import { RoutePaths, managementPath } from '@core/route-paths';

// Point unique de la règle d'aiguillage post-connexion / post-changement d'espace actif —
// autrement dit la zone CIBLE PAR DÉFAUT / de REPLI (AuthStore.currentZone(), redirection
// post-login, cible de repli des guards quand l'accès à l'espace demandé est refusé). Ce
// n'est PAS la zone AFFICHÉE : sidebar, topbar et l'en-tête X-Space-Id de TokenInterceptor
// dérivent désormais de l'URL couramment affichée (voir core/displayed-zone.ts et
// DisplayedZoneStore), jamais d'un repli sur l'ensemble des rôles de l'utilisateur — les deux
// peuvent légitimement diverger (ex. un Admin qui navigue manuellement dans
// /management/:spaceId : resolveZone() continue de le renvoyer vers 'admin' en cas de repli,
// displayedZone reflète 'management' puisque c'est ce que l'écran affiche réellement).
// La zone n'est PAS une propriété de l'utilisateur : c'est une propriété du couple
// (utilisateur, espace actif). Ce module reste pur (aucune dépendance Angular/DI) pour rester
// trivialement testable — AuthStore.currentZone() et les guards (admin/client/espace) s'y
// réfèrent tous, c'est le SEUL endroit où l'ordre de priorité des zones de repli est décidé.
export type ZoneKind = 'admin' | 'client' | 'management' | 'member' | 'no-space';

export interface IResolvedZone {
  kind: ZoneKind;
  path: string;
  spaceId?: string;
  clientId?: string;
}

// Rôles qui donnent accès à la zone /management sur un espace donné — exporté pour être
// réutilisé tel quel par app.routes.ts (canActivate) et TopbarComponent (bascule d'espace,
// qui doit savoir vers quelle zone rediriger), plutôt que de dupliquer cette liste.
export const MANAGEMENT_ROLES: UserRoleEnum[] = [
  UserRoleEnum.Manager,
  UserRoleEnum.SectionLeader,
  UserRoleEnum.Organizer
];

// Cible de la zone 'no-space' : /start (lot 6 — onboarding), et non plus
// /no-space. /no-space reste une route valide par ailleurs (spaceRoleGuard y
// redirige encore pour un spaceId invalide dans l'URL, un cas distinct de "aucun
// rattachement") — seule cette target d'aiguillage change, pas la route elle-même.
const NO_SPACE: IResolvedZone = { kind: 'no-space', path: `/${RoutePaths.Start}` };

function rolesOf(space: ISpaceRoleAssignment): UserRoleEnum[] {
  return space.Roles.map(userRoleFromString).filter((role): role is UserRoleEnum => role !== null);
}

export function hasManagementRole(space: ISpaceRoleAssignment): boolean {
  return rolesOf(space).some(role => MANAGEMENT_ROLES.includes(role));
}

// lastEspaceId : dernier espace de management actif connu (persisté par AuthStore/StorageService).
// N'est repris que s'il correspond encore à un espace de management réel de l'utilisateur —
// un espace stocké qui n'existe plus dans SpaceRoles ne doit jamais être réutilisé (repli sur
// le premier espace de management disponible), pour ne jamais aiguiller vers un espace fantôme.
export function resolveZone(user: IAuthenticatedUser | null, lastSpaceId: string | null): IResolvedZone {
  if (!user) {
    return NO_SPACE;
  }

  // Position de priorité, du plus large au plus spécifique : Admin d'abord (un admin qui est
  // aussi membre reste redirigé vers /admin), puis Management (un ResponsableClient qui est
  // AUSSI Responsable d'une chorale part en /management, jamais en /client), puis Client, puis
  // Membre simple, puis Aucun rattachement.
  if (user.Roles.includes('Admin')) {
    return { kind: 'admin', path: `/${RoutePaths.Admin}/${RoutePaths.AdminDashboard}` };
  }

  const managementSpaces = user.SpaceRoles.filter(hasManagementRole);
  if (managementSpaces.length > 0) {
    const preferred = lastSpaceId ? managementSpaces.find(space => space.SpaceId === lastSpaceId) : undefined;
    const target = preferred ?? managementSpaces[0];
    return { kind: 'management', spaceId: target.SpaceId, path: managementPath(target.SpaceId, RoutePaths.Dashboard) };
  }

  if (user.ClientRoles.length > 0) {
    const clientId = user.ClientRoles[0].ClientId;
    return { kind: 'client', clientId, path: `/${RoutePaths.Client}/${clientId}` };
  }

  if (user.SpaceRoles.length > 0) {
    return { kind: 'member', path: `/${RoutePaths.Me}` };
  }

  return NO_SPACE;
}
