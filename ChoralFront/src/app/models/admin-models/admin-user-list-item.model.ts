// Reflète AdminUserListItemViewModel (back, AdminUserController.GetPaged) — comptes
// administrateurs uniquement (onglet "Administrateurs"). Id est directement l'identifiant du
// compte (User.Id) — pas un identifiant de rattachement, contrairement aux items des onglets
// Chorales/Événements.
export interface IAdminUserListItem {
  Id: string;
  Email: string;
  Firstname: string;
  Lastname: string;
  IsActive: boolean;
  LastConnection: string | null;
  CreatedAt: string;
  CreatedByUserId: string | null;
  CreatedByName: string | null;
}

// Reflète AdminUsersPagedFilterViewModel (back) — partagé tel quel par GetPaged (onglet
// Administrateurs) et GetUnattachedUsersPaged (onglet Sans rattachement, voir
// admin-sans-rattachement-user-list-item.model.ts). IsGuestAccount n'a de sens métier que sur
// l'onglet Sans rattachement (aucun administrateur n'est un compte invité) mais le contrat back
// accepte ce paramètre identiquement sur les deux routes.
export interface IAdminUsersFilter {
  IsActive?: boolean;
  IsGuestAccount?: boolean;
}
