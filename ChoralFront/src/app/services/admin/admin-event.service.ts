import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@env/environment';
import { appendOptionalParam, buildPaginationParams } from '@core/pagination-params.util';
import { IPaginatedResult, IPaginationQueryParams } from '@models/common-models/paginated-result.model';
import { IAdminEventListItem, IAdminEventsFilter } from '@models/admin-models/admin-event-list-item.model';
import { IAdminEventDetail } from '@models/admin-models/admin-event-detail.model';

const ADMIN_EVENTS_BASE_URL = `${environment.apiUrl}admin-events`;

// Administration générale des événements (`[Authorize(Roles = "Admin")]` côté back) — lecture
// seule : aucune écriture n'est exposée par ce contrôleur (voir EventService pour la
// management réelle côté chorale).
@Injectable({ providedIn: 'root' })
export class AdminEventService {
  private readonly http = inject(HttpClient);

  getPaged(
    pagination: IPaginationQueryParams,
    filter: IAdminEventsFilter
  ): Observable<IPaginatedResult<IAdminEventListItem>> {
    let params = buildPaginationParams(pagination);
    params = appendOptionalParam(params, 'ClientId', filter.ClientId);
    params = appendOptionalParam(params, 'ChoirId', filter.ChoirId);
    params = appendOptionalParam(params, 'Status', filter.Status);
    params = appendOptionalParam(params, 'Type', filter.Type);
    params = appendOptionalParam(params, 'Upcoming', filter.Upcoming);

    return this.http.post<IPaginatedResult<IAdminEventListItem>>(`${ADMIN_EVENTS_BASE_URL}/GetPaged`, null, { params });
  }

  getById(eventId: string): Observable<IAdminEventDetail> {
    return this.http.get<IAdminEventDetail>(`${ADMIN_EVENTS_BASE_URL}/${eventId}`);
  }
}
