import { IAdminEventListItem } from '@models/admin-models/admin-event-list-item.model';

// Reflète AdminEventDetailViewModel (back, GET /api/admin-events/{eventId}) — mêmes
// champs que la liste, plus Description/ClosedAt/CreatedAt. Lecture seule, aucune action
// d'écriture sur ce contrôleur.
export interface IAdminEventDetail extends IAdminEventListItem {
  Description: string | null;
  ClosedAt: string | null;
  CreatedAt: string;
}
