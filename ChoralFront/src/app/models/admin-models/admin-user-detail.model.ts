import { IAdminChoirUserListItem } from '@models/admin-models/admin-choir-user-list-item.model';
import { IAdminEventUserListItem } from '@models/admin-models/admin-event-user-list-item.model';

// Reflète AdminUserDetailViewModel (back, AdminUserController.GetUserDetail) — fiche agrégée
// d'une personne : déduplique tous ses rattachements (chorales, événements, clients) sous un
// même compte. Toutes les actions de management (identité, activation, mot de passe, invitation,
// suppression) vivent exclusivement sur cette fiche, jamais sur une ligne de
// user-list.component.ts (décision produit : une action sur une ligne de tableau
// suggérerait qu'on agit sur le rattachement plutôt que sur le compte entier).
export interface IAdminUserDetail {
  Id: string;
  Email: string;
  Firstname: string;
  Lastname: string;
  IsActive: boolean;
  IsGuestAccount: boolean;
  CreatedAt: string;
  LastConnection: string | null;
  LastActive: string | null;
  Choirs: IAdminChoirUserListItem[];
  Events: IAdminEventUserListItem[];
  ClientAttachments: IAdminUserDetailClientItem[];
}

// Reflète AdminUserDetailClientItemViewModel (back). Role est une chaîne (ToString() d'un
// enum métier client côté back, hors périmètre de ce lot) — affiché tel quel, pas de mapping
// front.
export interface IAdminUserDetailClientItem {
  ClientId: string;
  ClientName: string;
  Role: string;
}
