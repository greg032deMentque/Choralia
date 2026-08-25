import { IconNameEnum } from '@app/enums/icon-name.enum';
import { UserRoleEnum } from '@app/enums/user-role.enum';

export interface ISidebarNavItem {
  Label: string;
  Path: string;
  Icon: IconNameEnum;
  RequiredRoles?: UserRoleEnum[];
  Children?: ISidebarNavItem[];
  // Count affiché en badge à côté du libellé (ex. demandes d'adhésion en attente, lot 6
  // onboarding) — absent ou 0 : aucun badge affiché.
  Badge?: number;
}
