import { ChoirStatusEnum } from '@app/enums/status-choir.enum';

// Reflète AdminChoirDetailViewModel (back, GET /api/admin-choirs/{choirId}). Les champs
// ClientLimite*/ClientNombre*/ClientStorageQuotaBytes/ClientUsedStorageBytes portent la
// consommation DU CLIENT (jamais celle de la seule chorale) — un plafond sans consommation
// visible en regard est inexploitable à l'écran.
export interface IAdminChoirDetail {
  Id: string;
  Name: string;
  Description: string | null;
  ImageUrl: string | null;
  ClientId: string;
  ClientName: string;
  Status: ChoirStatusEnum;
  CreatedAt: string;
  MemberCount: number;
  SongCount: number;
  EventCount: number;
  ClientChoirLimit: number;
  ClientChoirCount: number;
  ClientMemberLimit: number;
  ClientMemberCount: number;
  ClientStorageQuotaBytes: number;
  ClientUsedStorageBytes: number;
}
