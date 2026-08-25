// Reflète AdminUnattachedUserListItemViewModel (back,
// AdminUserController.GetUnattachedUsersPaged). Seul endroit de la zone admin où un
// ResponsableClient sans espace rattaché reste visible (voir user-list.component.ts) —
// sans cet onglet, ces comptes seraient totalement ingérables. ClientName/ClientRole sont
// renseignés quand un rattachement client existe malgré l'absence d'espace ; null sinon
// (compte réellement sans aucun rattachement).
export interface IAdminUnattachedUserListItem {
  Id: string;
  Firstname: string;
  Lastname: string;
  Email: string;
  IsActive: boolean;
  IsGuestAccount: boolean;
  ClientName: string | null;
  ClientRole: string | null;
  CreatedAt: string;
  LastConnection: string | null;
}
