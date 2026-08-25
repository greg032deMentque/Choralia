export enum UserRoleEnum {
  Admin = 0,
  SectionLeader = 1,
  Singer = 2,
  Manager = 3,
  Organizer = 4,
  Participant = 5,
  ClientManager = 6
}

// Libellés figés par `10-D40` : le lexique affiché est **choriste**, **chef de chœur**, **chef de
// pupitre**, **organisateur**. Ne pas revenir à « Membre » ou « Responsable » — ce sont les noms
// de code (UserRoleEnum.Singer, UserRoleEnum.Manager), jamais les mots de l'interface.
export function getUserRoleLabel(role: UserRoleEnum): string {
  switch (role) {
    case UserRoleEnum.Admin:
      return 'Administration générale';
    case UserRoleEnum.SectionLeader:
      return 'Chef de pupitre';
    case UserRoleEnum.Singer:
      return 'Choriste';
    case UserRoleEnum.Manager:
      return 'Chef de chœur';
    case UserRoleEnum.Organizer:
      return 'Organisateur';
    case UserRoleEnum.Participant:
      return 'Participant';
    case UserRoleEnum.ClientManager:
      return 'Responsable client';
  }
}

// Rend une liste de rôles lisible ("Membre, Responsable") — jamais une concaténation brute des
// libellés unitaires (badges collés visuellement, ou texte fusionné pour un lecteur d'écran /
// une extraction de texte). Même convention que VoicePartsWithoutPublishedRecording ailleurs
// dans le code (.map(label).join(', ')).
export function getUserRolesLabel(roles: readonly UserRoleEnum[]): string {
  return roles.map(getUserRoleLabel).join(', ');
}

export function userRoleFromString(role: string): UserRoleEnum | null {
  switch (role) {
    case 'Admin':
      return UserRoleEnum.Admin;
    case 'SectionLeader':
      return UserRoleEnum.SectionLeader;
    case 'Singer':
      return UserRoleEnum.Singer;
    case 'Manager':
      return UserRoleEnum.Manager;
    case 'Organizer':
      return UserRoleEnum.Organizer;
    case 'Participant':
      return UserRoleEnum.Participant;
    case 'ClientManager':
      return UserRoleEnum.ClientManager;
    default:
      return null;
  }
}
