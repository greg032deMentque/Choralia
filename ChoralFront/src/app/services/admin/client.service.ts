import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@env/environment';
import { appendOptionalArrayParam, appendOptionalParam, buildPaginationParams } from '@core/pagination-params.util';
import { IPaginatedResult, IPaginationQueryParams } from '@models/common-models/paginated-result.model';
import { IClient, IClientsFilter } from '@models/admin-models/client.model';
import { IClientChoirListItem } from '@models/admin-models/client-choir-list-item.model';
import { IClientManagerListItem } from '@models/admin-models/client-manager-list-item.model';
import {
  IChangeStatusClient,
  ICreateClient,
  IAssignManagerClient,
  IImpactSuspensionClient,
  IUpdateClient,
  IUpdateLimitsClient
} from '@models/admin-models/client-actions.model';

const CLIENTS_BASE_URL = `${environment.apiUrl}clients`;

// Consommée par deux zones : /admin (policy Roles=Admin — GetPaged, Create, Update,
// ModifierLimites, ChangeStatus, Reactivate, ImpactSuspension) et /client/:clientId, « Ma
// structure » (policy ClientManager — GetById, GetChoirs, Managers). Le back applique
// la policy par route ; ce service ne revalide rien côté front au-delà de ce que fait le guard
// de zone (clientRoleGuard / adminGuard).
@Injectable({ providedIn: 'root' })
export class ClientService {
  private readonly http = inject(HttpClient);

  // `filter` : Statut/ClientIds/ProcheDuPlafond, en cours d'ajout côté back au moment de ce
  // raccordement (voir IClientsFilter) — sans effet réel tant que ClientController.GetPaged ne
  // les lit pas encore, codés par anticipation du contrat annoncé plutôt que d'attendre.
  getPaged(pagination: IPaginationQueryParams, filter: IClientsFilter = {}): Observable<IPaginatedResult<IClient>> {
    let params = buildPaginationParams(pagination);
    params = appendOptionalParam(params, 'Status', filter.Status);
    params = appendOptionalParam(params, 'ProcheDuPlafond', filter.NearCap);
    params = appendOptionalArrayParam(params, 'ClientIds', filter.ClientIds);

    return this.http.post<IPaginatedResult<IClient>>(`${CLIENTS_BASE_URL}/GetPaged`, null, { params });
  }

  getById(clientId: string): Observable<IClient> {
    return this.http.get<IClient>(`${CLIENTS_BASE_URL}/${clientId}`);
  }

  getImpactSuspension(clientId: string): Observable<IImpactSuspensionClient> {
    return this.http.get<IImpactSuspensionClient>(`${CLIENTS_BASE_URL}/${clientId}/SuspensionImpact`);
  }

  create(payload: ICreateClient): Observable<IClient> {
    return this.http.post<IClient>(`${CLIENTS_BASE_URL}/Create`, payload);
  }

  update(payload: IUpdateClient): Observable<IClient> {
    return this.http.put<IClient>(`${CLIENTS_BASE_URL}/Update`, payload);
  }

  updateLimits(payload: IUpdateLimitsClient): Observable<IClient> {
    return this.http.put<IClient>(`${CLIENTS_BASE_URL}/UpdateLimits`, payload);
  }

  // 409 si transition interdite (ex. archivé -> autre chose) — propagé tel quel.
  changeStatus(payload: IChangeStatusClient): Observable<IClient> {
    return this.http.put<IClient>(`${CLIENTS_BASE_URL}/ChangeStatus`, payload);
  }

  // 409 déjà actif / archivé (terminal) / plafond dépassé (message chiffré) — propagé tel quel,
  // jamais avalé ici (voir client-detail.component.ts pour le message inline + renvoi vers
  // l'onglet Plafonds).
  reactivate(clientId: string): Observable<IClient> {
    return this.http.post<IClient>(`${CLIENTS_BASE_URL}/${clientId}/Reactivate`, null);
  }

  getChoirs(clientId: string, pagination: IPaginationQueryParams): Observable<IPaginatedResult<IClientChoirListItem>> {
    const params = buildPaginationParams(pagination);
    return this.http.post<IPaginatedResult<IClientChoirListItem>>(`${CLIENTS_BASE_URL}/${clientId}/GetChoirs`, null, { params });
  }

  // GET (pas POST comme les autres listes paginées de ce service) : ClientController.GetManagers
  // est déclaré en [HttpGet("{clientId:guid}/Managers")]. Policy ClientManager.
  getManagers(clientId: string, pagination: IPaginationQueryParams): Observable<IPaginatedResult<IClientManagerListItem>> {
    const params = buildPaginationParams(pagination);
    return this.http.get<IPaginatedResult<IClientManagerListItem>>(`${CLIENTS_BASE_URL}/${clientId}/Managers`, { params });
  }

  // 204 No Contenu — Observable<unknown> plutôt que <void> (convention du projet, voir
  // AdminUserService.resetPassword). L'utilisateur désigné doit déjà avoir un compte (pas un
  // flux d'invitation) : 404 si l'email ne correspond à aucun compte, 409 si déjà responsable.
  assignManager(clientId: string, payload: IAssignManagerClient): Observable<unknown> {
    return this.http.post<unknown>(`${CLIENTS_BASE_URL}/${clientId}/Managers`, payload);
  }

  removeManager(clientId: string, userId: string): Observable<unknown> {
    return this.http.delete<unknown>(`${CLIENTS_BASE_URL}/${clientId}/Managers/${userId}`);
  }
}
