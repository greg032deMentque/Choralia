import { ClientStatusEnum } from '@app/enums/status-client.enum';

// Reflète ClientViewModel (back, ClientController). ChoirCount/MemberCount/
// UsedStorageBytes sont la consommation constatée, TOUJOURS affichée en regard des
// plafonds (ChoirLimit/MemberLimit/StorageQuotaBytes) — un plafond sans consommation
// visible est inexploitable à l'écran.
export interface IClient {
  Id: string | null;
  Name: string;
  ContactName: string | null;
  ContactEmail: string | null;
  Status: ClientStatusEnum;

  ChoirLimit: number;
  MemberLimit: number;
  StorageQuotaBytes: number;
  MaxFileSizeBytes: number;

  ChoirCount: number;
  MemberCount: number;
  UsedStorageBytes: number;
}

// Reflète le filtre en cours d'ajout côté back sur ClientController.GetPaged (voir
// client.service.ts) — Statut/ClientIds/ProcheDuPlafond n'existent pas encore sur
// ClientController.GetPaged au moment de ce raccordement ([FromQuery] PaginateViewModel nu) ;
// codés par anticipation du contrat annoncé, sans effet réel tant que le back ne les lit pas.
export interface IClientsFilter {
  Status?: ClientStatusEnum;
  ClientIds?: string[];
  NearCap?: boolean;
}
