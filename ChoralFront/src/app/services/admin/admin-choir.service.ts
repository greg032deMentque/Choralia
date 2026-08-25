import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '@env/environment';
import { appendOptionalParam, buildPaginationParams } from '@core/pagination-params.util';
import { IPaginatedResult, IPaginationQueryParams } from '@models/common-models/paginated-result.model';
import { IAdminChoirListItem, IAdminChoirsFilter } from '@models/admin-models/admin-choir-list-item.model';
import { IAdminChoirDetail } from '@models/admin-models/admin-choir-detail.model';
import { IAdminChoirChangeStatus, IAdminChoirImpact, IAdminChoirUpdate } from '@models/admin-models/admin-choir-actions.model';
import { IMemberChoir } from '@models/members-models/member-choir.model';
import { ISong } from '@models/songs-models/song.model';
import { IEvent } from '@models/events-models/event.model';
import { mapRolesFromApi } from '@core/choir-roles.util';

const ADMIN_CHOIRS_BASE_URL = `${environment.apiUrl}admin-choirs`;

// Roles est transmis en chaînes (claims JWT) — même convention que ChoirMembersService.
type IMemberChoirApi = Omit<IMemberChoir, 'Roles'> & { Roles: string[] };

function mapMemberChoir(raw: IMemberChoirApi): IMemberChoir {
  return { ...raw, Roles: mapRolesFromApi(raw.Roles) };
}

// Administration générale des chorales (`[Authorize(Roles = "Admin")]` côté back, pas de header
// X-Space-Id — la zone admin ne scope pas par chorale actif). L'admin lit les onglets
// Membres/Chants/Événements (délégation directe aux services existants côté back, déjà lecture
// seule ici) et n'écrit que sur les informations (Update) et le statut (ChangeStatus) — jamais
// sur le contenu (`10-D23`, décision produit : pas de création/suppression de chorale ici).
@Injectable({ providedIn: 'root' })
export class AdminChoirService {
  private readonly http = inject(HttpClient);

  getPaged(pagination: IPaginationQueryParams, filter: IAdminChoirsFilter): Observable<IPaginatedResult<IAdminChoirListItem>> {
    let params = buildPaginationParams(pagination);
    params = appendOptionalParam(params, 'ClientId', filter.ClientId);
    params = appendOptionalParam(params, 'Status', filter.Status);
    params = appendOptionalParam(params, 'InactiveFor30Days', filter.InactiveFor30Days);

    return this.http.post<IPaginatedResult<IAdminChoirListItem>>(`${ADMIN_CHOIRS_BASE_URL}/GetPaged`, null, { params });
  }

  getById(choirId: string): Observable<IAdminChoirDetail> {
    return this.http.get<IAdminChoirDetail>(`${ADMIN_CHOIRS_BASE_URL}/${choirId}`);
  }

  getMembers(choirId: string, pagination: IPaginationQueryParams): Observable<IPaginatedResult<IMemberChoir>> {
    const params = buildPaginationParams(pagination);
    return this.http
      .post<IPaginatedResult<IMemberChoirApi>>(`${ADMIN_CHOIRS_BASE_URL}/${choirId}/GetMembers`, null, { params })
      .pipe(map(res => ({ ...res, Items: res.Items.map(mapMemberChoir) })));
  }

  getSongs(choirId: string, pagination: IPaginationQueryParams): Observable<IPaginatedResult<ISong>> {
    const params = buildPaginationParams(pagination);
    return this.http.post<IPaginatedResult<ISong>>(`${ADMIN_CHOIRS_BASE_URL}/${choirId}/GetSongs`, null, { params });
  }

  getEvents(choirId: string, pagination: IPaginationQueryParams): Observable<IPaginatedResult<IEvent>> {
    const params = buildPaginationParams(pagination);
    return this.http.post<IPaginatedResult<IEvent>>(`${ADMIN_CHOIRS_BASE_URL}/${choirId}/GetEvents`, null, { params });
  }

  update(payload: IAdminChoirUpdate): Observable<IAdminChoirDetail> {
    return this.http.put<IAdminChoirDetail>(`${ADMIN_CHOIRS_BASE_URL}/Update`, payload);
  }

  getImpactArchivage(choirId: string): Observable<IAdminChoirImpact> {
    return this.http.get<IAdminChoirImpact>(`${ADMIN_CHOIRS_BASE_URL}/${choirId}/ArchiveImpact`);
  }

  // 400 status hors plage, 404 choir notFound, 409 transition interdite (message nommant
  // les deux états) ou plafond dépassé à la réactivation (message chiffré) — propagés tels
  // quels, jamais avalés ici (voir choir-detail.component.ts pour le message inline).
  changeStatus(payload: IAdminChoirChangeStatus): Observable<IAdminChoirDetail> {
    return this.http.put<IAdminChoirDetail>(`${ADMIN_CHOIRS_BASE_URL}/ChangeStatus`, payload);
  }
}
