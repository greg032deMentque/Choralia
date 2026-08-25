import { MemberStatusEnum } from '@app/enums/member-status.enum';
import { UserRoleEnum } from '@app/enums/user-role.enum';
import { VoicePartEnum } from '@app/enums/voice-part.enum';

// Reflète AdminChoirUserListItemViewModel (back, AdminUserController.GetChoirUsersPaged).
// Une ligne = un RATTACHEMENT membre/chorale, jamais une personne : une même personne
// membre de 2 chorales produit 2 lignes distinctes ici (décision produit — voir
// user-list.component.ts). Roles est un tableau de chaînes côté back (claims JWT
// globaux du rattachement) — converti en UserRoleEnum[] par AdminUserService, toute valeur
// non reconnue par userRoleFromString est silencieusement ignorée (même convention que
// IMemberChoir/ChoirMembersService).
export interface IAdminChoirUserListItem {
  Id: string;
  UserId: string;
  Firstname: string;
  Lastname: string;
  Email: string;
  ChoirId: string;
  ChoirName: string;
  Roles: UserRoleEnum[];
  PrimaryVoicePart: VoicePartEnum | null;
  Status: MemberStatusEnum;
  IsActive: boolean;
  LastActive: string | null;
}

// Filtre de GetChoirUsersPaged (AdminChoirUsersPagedFilterViewModel, back) — Role/Statut/
// Voix transmis en entier (enums numériques), jamais en chaîne. ChoirIds : sélection multiple
// (modale de recherche par nom, user-list.component.ts) — remplace l'ancien ChoirId unique
// saisi en UUID brut.
export interface IAdminChoirUsersFilter {
  ChoirIds?: string[];
  Role?: UserRoleEnum;
  Status?: MemberStatusEnum;
  VoicePart?: VoicePartEnum;
  IsActive?: boolean;
}
