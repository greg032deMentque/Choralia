import { MemberStatusEnum } from '@app/enums/member-status.enum';
import { AttendanceEnum } from '@app/enums/presence.enum';
import { UserRoleEnum } from '@app/enums/user-role.enum';

// Reflète AdminEventUserListItemViewModel (back, AdminUserController.GetEventUsersPaged).
// Une ligne = un RATTACHEMENT membre/événement, jamais une personne (même décision que
// IAdminChoirUserListItem). ChoirId/ChoirName sont null pour un événement autonome (non
// rattaché à une chorale porteuse) — à afficher avec un repli explicite côté template, jamais
// "undefined" ni une case vide ambiguë. Role est une chaîne UNIQUE ici (pas un tableau,
// contrairement à AdminChoirUserListItemViewModel.Roles) — convertie en UserRoleEnum via
// userRoleFromString par AdminUserService.
export interface IAdminEventUserListItem {
  Id: string;
  UserId: string;
  Firstname: string;
  Lastname: string;
  Email: string;
  EventId: string;
  EventTitle: string;
  EventStartDate: string;
  ChoirId: string | null;
  ChoirName: string | null;
  Role: UserRoleEnum | null;
  Presence: AttendanceEnum | null;
  Status: MemberStatusEnum;
}

// Filtre de GetEventUsersPaged (AdminEventUsersPagedFilterViewModel, back). EventIds :
// sélection multiple (modale de recherche par nom, user-list.component.ts) — remplace l'ancien
// EventId unique saisi en UUID brut.
export interface IAdminEventUsersFilter {
  EventIds?: string[];
  Role?: UserRoleEnum;
  Presence?: AttendanceEnum;
  Upcoming?: boolean;
}
