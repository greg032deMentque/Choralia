import { ChoirStatusEnum } from '@app/enums/status-choir.enum';

// Reflète la réponse de GET /api/clients/{clientId}/Choirs/{choirId} (zone « Ma structure »,
// policy ClientManager). Distinct d'IAdminChoirDetail (zone /admin) : pas de ClientName ni des
// plafonds client — cet écran n'affiche que la fiche de la chorale elle-même, sans onglets de
// contenu (Membres/Chants/Événements hors périmètre, `13` § Fiche chorale).
// Une chorale Archivée est renvoyée normalement par cet endpoint (pas de 404, pas d'exclusion).
export interface IChoirDetail {
  Id: string;
  Name: string;
  Status: ChoirStatusEnum;
  MemberCount: number;
  SongCount: number;
  UpcomingEventCount: number;
}

// Payload de PUT /api/clients/{clientId}/Choirs/{choirId}/ChangeStatus — un seul champ, jamais
// d'Id : la cible est portée par les paramètres de route (clientId, choirId), pas par le corps.
export interface IChangeStatusChoir {
  Status: ChoirStatusEnum;
}
