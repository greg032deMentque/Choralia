// Reflète ClientChoraleListItemViewModel (back, POST /api/clients/{clientId}/GetChoirs) —
// chorales d'un client avec leur niveau d'consommation, écran central de la zone « Ma structure ». Ne
// porte pas ClientId : il est déjà connu (paramètre de route).
export interface IClientChoirListItem {
  Id: string;
  Name: string;
  Description: string | null;
  MemberCount: number;
  SongCount: number;
  UpcomingEventCount: number;
}
