// Reflète AdminAuditLogListItemViewModel (back, AdminAuditController.GetPaged). Écran de
// lecture seule (`10-D30`) : un journal d'audit modifiable ne vaut rien, aucune écriture n'est
// exposée par ce contrôleur.
export interface IAdminAuditLogListItem {
  Id: string;
  UserId: string;
  // Repli sur « Utilisateur inconnu » calculé côté back quand l'acteur n'est plus résolvable
  // (compte supprimé) — jamais vide, jamais à recalculer côté front.
  UserFullName: string;
  UserEmail: string | null;
  Action: string;
  EntityType: string | null;
  EntityId: string | null;
  Detail: string | null;
  OccurredAt: string;
}

// Reflète AdminAuditLogPagedFilterViewModel — tous optionnels, en complément de Filter (texte
// libre, non exposé ici : AdminAuditListService ne filtre que sur UserId/EntityType/Action/
// StartDate/EndDate, pas de recherche texte libre sur ce endpoint).
export interface IAdminAuditLogFilter {
  UserId?: string;
  EntityType?: string;
  Action?: string;
  // Bornes ISO (incluses), UTC — voir audit.component.ts pour la construction des bornes de
  // journée à partir d'un <input type="date">.
  StartDate?: string;
  EndDate?: string;
}

// Liste blanche stricte du serveur (AdminAuditListService.AuditColonnesTriables) — seules ces
// trois colonnes sont réellement triées côté serveur, aucune autre ne doit être déclarée
// sortable dans la table (un en-tête cliquable qui ne trie rien est le défaut qu'on vient de
// corriger sur l'ensemble du projet).
export const ADMIN_AUDIT_SORTABLE_COLUMNS = ['OccurredAt', 'Action', 'EntityType'] as const;
