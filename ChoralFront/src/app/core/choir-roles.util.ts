import { UserRoleEnum, userRoleFromString } from '@app/enums/user-role.enum';

// Mapping partagé des rôles transmis en chaînes (claims JWT) par ChoirMembersController,
// AdminChoirsController et ChoirMastersController — auparavant dupliqué à l'identique dans
// ChoirMembersService et AdminChoirService (voir choir.service.ts pour le 3e appelant). Une
// valeur non reconnue (ex. claim global "Admin" hors périmètre chorale) est ignorée plutôt que
// de faire échouer tout le mapping.
export function mapRolesFromApi(roles: readonly string[]): UserRoleEnum[] {
  return roles.map(userRoleFromString).filter((role): role is UserRoleEnum => role !== null);
}
