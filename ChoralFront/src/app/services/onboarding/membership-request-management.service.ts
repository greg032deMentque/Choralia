import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '@env/environment';
import { buildPaginationParams } from '@core/pagination-params.util';
import { IPaginatedResult, IPaginationQueryParams } from '@models/common-models/paginated-result.model';
import { IMembershipRequestListItem } from '@models/onboarding-models/membership-request-list-item.model';
import { IApproveRequestRequest } from '@models/onboarding-models/approve-request-request.model';
import { IDeclineRequestRequest } from '@models/onboarding-models/decline-request-request.model';
import { MembershipRequestStatusEnum } from '@app/enums/status-membership-request.enum';

const SPACES_BASE_URL = `${environment.apiUrl}spaces`;
// Aucun endpoint de comptage dédié côté back : le nombre affiché en badge (sidebar, onglet
// Demandes) est calculé sur la page chargée, bornée à cette taille — approximation assumée en
// l'absence d'agrégat serveur (écart documenté au récapitulatif de génération).
const PENDING_COUNT_PAGE_SIZE = 100;

// File des demandes d'adhésion d'un espace, côté Responsable
// (/api/spaces/{spaceId}/MembershipRequests/*). Le contrat GetPaged ne connaît qu'un filtre texte
// générique (Filter) — pas de filtre serveur par Statut : le tri "en attente d'abord" et le
// comptage du badge se font côté front, sur la page chargée (voir
// requests-adhesion-list.component).
@Injectable({ providedIn: 'root' })
export class MembershipRequestManagementService {
  private readonly http = inject(HttpClient);

  getPaged(spaceId: string, pagination: IPaginationQueryParams): Observable<IPaginatedResult<IMembershipRequestListItem>> {
    const params = buildPaginationParams(pagination);
    return this.http.post<IPaginatedResult<IMembershipRequestListItem>>(
      `${SPACES_BASE_URL}/${spaceId}/MembershipRequests/GetPaged`,
      null,
      { params }
    );
  }

  // 409 si le pupitre est au plafond : la demande reste EnAttente côté back, à afficher via un
  // bandeau persistant côté composant (jamais un simple toast qui disparaîtrait).
  approve(spaceId: string, requestId: string, request: IApproveRequestRequest): Observable<IMembershipRequestListItem> {
    return this.http.post<IMembershipRequestListItem>(`${SPACES_BASE_URL}/${spaceId}/MembershipRequests/${requestId}/Approve`, request);
  }

  decline(spaceId: string, requestId: string, request: IDeclineRequestRequest): Observable<IMembershipRequestListItem> {
    return this.http.post<IMembershipRequestListItem>(`${SPACES_BASE_URL}/${spaceId}/MembershipRequests/${requestId}/Decline`, request);
  }

  // Count de demandes EnAttente, pour le badge sidebar/onglet — voir limite documentée
  // ci-dessus (PENDING_COUNT_PAGE_SIZE).
  getPendingCount(spaceId: string): Observable<number> {
    return this.getPaged(spaceId, { Page: 1, PageSize: PENDING_COUNT_PAGE_SIZE }).pipe(
      map(result => result.Items.filter(item => item.Status === MembershipRequestStatusEnum.Pending).length)
    );
  }
}
