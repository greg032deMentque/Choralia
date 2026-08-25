import { RoutePaths } from '@core/route-paths';
import { isValidUuid } from '@core/uuid.util';
import { ISpaceRoleAssignment } from '@models/auth-models/space-role-assignment.model';
import { IClientRoleAssignment } from '@models/auth-models/client-role-assignment.model';

// Zone AFFICHÉE : dérivée uniquement de l'URL actuellement rendue par le router, jamais d'un
// repli sur l'ensemble des rôles de l'utilisateur. À distinguer de zone-resolver.ts (resolveZone),
// qui reste la cible par défaut / le repli des guards (redirection post-connexion) — les deux
// zones divergent légitimement (ex. un Admin qui navigue manuellement dans /management/:spaceId :
// resolveZone() le renvoie toujours vers 'admin', displayedZone reflète 'management' puisque
// c'est ce qu'affiche réellement l'écran). Module pur (aucune dépendance Angular), consommé par
// DisplayedZoneStore (wrapper Signal) ainsi que par TokenInterceptor pour le scope X-Space-Id.
export type DisplayedZoneKind = 'admin' | 'client' | 'management' | 'member' | 'no-space';

export interface IDisplayedZone {
  kind: DisplayedZoneKind;
  spaceId?: string;
  clientId?: string;
}

const NO_SPACE: IDisplayedZone = { kind: 'no-space' };

// Tout segment racine non reconnu, ou un id de segment dynamique qui n'a pas la forme d'un
// UUID, retombe sur 'no-space' — jamais une zone construite à partir d'un identifiant non
// validé (OWASP A01, même exigence que isAllowedReturnUrl côté route-paths.ts).
export function resolveDisplayedZone(url: string): IDisplayedZone {
  const [path] = url.split('?');
  const segments = path.split('/').filter(Boolean);
  if (segments.length === 0) return NO_SPACE;

  switch (segments[0]) {
    case RoutePaths.Admin:
      return { kind: 'admin' };
    case RoutePaths.Client:
      return isValidUuid(segments[1]) ? { kind: 'client', clientId: segments[1] } : NO_SPACE;
    case RoutePaths.Management:
      return isValidUuid(segments[1]) ? { kind: 'management', spaceId: segments[1] } : NO_SPACE;
    case RoutePaths.Me:
      return { kind: 'member' };
    default:
      return NO_SPACE;
  }
}

// Libellé affiché (en-tête sidebar, sélecteur topbar) pour la zone AFFICHÉE — recherche l'espace
// ou la structure correspondant à l'id porté par l'URL dans les listes complètes de rattachements
// de l'utilisateur. 'admin' et 'member' n'ont pas de libellé dédié (comportement identique à
// avant ce changement, hors périmètre) : chaîne vide.
export function displayedZoneLabel(
  zone: IDisplayedZone,
  spaceRoles: ISpaceRoleAssignment[],
  clientRoles: IClientRoleAssignment[]
): string {
  switch (zone.kind) {
    case 'management':
      return spaceRoles.find(space => space.SpaceId === zone.spaceId)?.Name ?? '';
    case 'client':
      return clientRoles.find(client => client.ClientId === zone.clientId)?.Name ?? '';
    default:
      return '';
  }
}
