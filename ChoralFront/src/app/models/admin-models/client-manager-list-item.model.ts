import { UserRoleEnum } from '@app/enums/user-role.enum';

// Reflète ClientManagerListItemViewModel (back, GET /api/clients/{clientId}/Managers) — Role
// est un UserRoleEnum simple (pas une liste de chaînes comme ChoirMemberListItemViewModel.Roles) :
// sérialisé en entier par défaut par ASP.NET Core (aucun JsonStringEnumConverter global), donc
// aucun mapping de type n'est nécessaire ici, contrairement à IMemberChoir.Roles.
export interface IClientManagerListItem {
  UserId: string;
  Firstname: string;
  Lastname: string;
  Email: string | null;
  Role: UserRoleEnum;
  AssignmentDate: string;
}
