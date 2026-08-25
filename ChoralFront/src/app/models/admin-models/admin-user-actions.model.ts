// Payloads des actions de la fiche utilisateur admin (back : AdminUserController).
// Regroupés dans un seul fichier — ce sont de simples DTO de requête sans logique propre,
// contrairement aux modèles de lecture (un fichier par ViewModel de lecture, voir les autres
// files de ce dossier).

// Body de PUT /api/admin-users/UpdateIdentity (AdminUserUpdateIdentityViewModel, back).
export interface IAdminUserUpdateIdentity {
  Id: string;
  Firstname: string;
  Lastname: string;
  Email: string;
}

// Body de PUT /api/admin-users/SetActive (AdminUserSetActiveViewModel, back).
export interface IAdminUserSetActive {
  UserId: string;
  IsActive: boolean;
}

// Body de POST /api/admin-users/Create (CreateAdminUserViewModel, back). Password est validé
// côté back par une regex (min 8, majuscule, minuscule, chiffre, caractère spécial) — reprise
// à l'identique côté front (admin-create-modal.component.ts) pour un retour immédiat
// sans aller-retour serveur inutile ; le back reste la seule source de vérité.
export interface ICreateAdminUser {
  Email: string;
  Firstname: string;
  Lastname: string;
  Password: string;
}
