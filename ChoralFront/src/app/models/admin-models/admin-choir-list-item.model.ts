import { ChoirStatusEnum } from '@app/enums/status-choir.enum';

// Reflète AdminChoirListItemViewModel (back, AdminChoraleController.GetPaged) — liste
// transverse à tous les clients, lecture seule.
export interface IAdminChoirListItem {
  Id: string;
  Name: string;
  ClientId: string;
  ClientName: string;
  MemberCount: number;
  SongCount: number;
  UpcomingEventCount: number;
  LastActivityAt: string;
  Status: ChoirStatusEnum;
}

// Reflète AdminChoirsPagedFilterViewModel — tous optionnels, en complément de Filter (texte
// libre) porté par IPaginationQueryParams.
export interface IAdminChoirsFilter {
  ClientId?: string;
  Status?: ChoirStatusEnum;
  InactiveFor30Days?: boolean;
}
