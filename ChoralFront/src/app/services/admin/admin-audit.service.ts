import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@env/environment';
import { appendOptionalParam, buildPaginationParams } from '@core/pagination-params.util';
import { IPaginatedResult, IPaginationQueryParams } from '@models/common-models/paginated-result.model';
import { IAdminAuditLogFilter, IAdminAuditLogListItem } from '@models/admin-models/admin-audit-log.model';

const ADMIN_AUDIT_BASE_URL = `${environment.apiUrl}admin-audit`;

// Écran d'audit de l'administration générale (`[Authorize(Roles = "Admin")]` côté back) —
// lecture seule : AdminAuditController n'expose aucune route d'écriture (voir
// audit.component.ts).
@Injectable({ providedIn: 'root' })
export class AdminAuditService {
  private readonly http = inject(HttpClient);

  // Période inversée (StartDate > EndDate) : le composant appelant ne doit PAS appeler cette
  // méthode dans ce cas (voir audit.component.ts) — le back renverrait une page vide
  // indiscernable d'un « aucun résultat », ce qui serait trompeur pour l'utilisateur.
  getPaged(
    pagination: IPaginationQueryParams,
    filter: IAdminAuditLogFilter
  ): Observable<IPaginatedResult<IAdminAuditLogListItem>> {
    let params = buildPaginationParams(pagination);
    params = appendOptionalParam(params, 'UserId', filter.UserId);
    params = appendOptionalParam(params, 'EntityType', filter.EntityType);
    params = appendOptionalParam(params, 'Action', filter.Action);
    params = appendOptionalParam(params, 'StartDate', filter.StartDate);
    params = appendOptionalParam(params, 'EndDate', filter.EndDate);

    return this.http.post<IPaginatedResult<IAdminAuditLogListItem>>(`${ADMIN_AUDIT_BASE_URL}/GetPaged`, null, { params });
  }
}
