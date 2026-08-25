import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '@env/environment';
import { buildPaginationParams } from '@core/pagination-params.util';
import { mapRolesFromApi } from '@core/choir-roles.util';
import { IPaginatedResult, IPaginationQueryParams } from '@models/common-models/paginated-result.model';
import { IAssignChoirMaster, IChoir, ICreateChoir } from '@models/client-models/choir.model';
import { IChangeStatusChoir, IChoirDetail } from '@models/client-models/choir-detail.model';
import { IMemberChoir } from '@models/members-models/member-choir.model';
import { ChoirStatusEnum } from '@app/enums/status-choir.enum';

const CHOIRS_BASE_URL = `${environment.apiUrl}choirs`;
const CLIENTS_BASE_URL = `${environment.apiUrl}clients`;

// Roles est transmis en chaînes (claims JWT) — même convention que ChoirMembersService et
// AdminChoirService (voir choir-roles.util.ts, mapping partagé).
type IMemberChoirApi = Omit<IMemberChoir, 'Roles'> & { Roles: string[] };

function mapMemberChoir(raw: IMemberChoirApi): IMemberChoir {
  return { ...raw, Roles: mapRolesFromApi(raw.Roles) };
}

// Zone /client/:clientId (« Ma structure ») — création de chorale et gestion de ses chefs de
// chœur, policy AdminOrClientManager côté back (ChoirController + ChoirMastersController).
// Distinct d'AdminChoirService, qui cible un contrôleur différent (/api/admin-choirs, lecture
// seule pour l'administration générale, jamais de création/suppression).
@Injectable({ providedIn: 'root' })
export class ChoirService {
  private readonly http = inject(HttpClient);

  // Le compte du futur chef de chœur (ChoirMasterEmail) doit déjà exister — pas un flux
  // d'invitation. Erreurs propagées telles quelles : 400 (champs requis manquants), 403 (pas
  // responsable de ce client), 404 (aucun compte pour cet email), 409 (plafond de chorales ou
  // de membres atteint) — voir my-structure.component.ts pour le message inline par code.
  create(payload: ICreateChoir): Observable<IChoir> {
    return this.http.post<IChoir>(`${CHOIRS_BASE_URL}/Create`, payload);
  }

  getChoirMasters(choirId: string, pagination: IPaginationQueryParams): Observable<IPaginatedResult<IMemberChoir>> {
    const params = buildPaginationParams(pagination);
    return this.http
      .post<IPaginatedResult<IMemberChoirApi>>(`${CHOIRS_BASE_URL}/${choirId}/ChoirMasters/GetPaged`, null, { params })
      .pipe(map(res => ({ ...res, Items: res.Items.map(mapMemberChoir) })));
  }

  // 404 (aucun compte pour cet email), 409 (plafond de membres atteint ou chorale non
  // modifiable dans son état actuel) — propagés tels quels, jamais avalés ici.
  assignChoirMaster(choirId: string, payload: IAssignChoirMaster): Observable<IMemberChoir> {
    return this.http.put<IMemberChoirApi>(`${CHOIRS_BASE_URL}/${choirId}/ChoirMasters/Assign`, payload).pipe(map(mapMemberChoir));
  }

  // 204 No Content — Observable<unknown> (convention du projet, voir ClientService.removeManager).
  // 400 (l'utilisateur est aussi chef de pupitre), 409 (dernier chef de chœur, ou chorale non
  // modifiable) — propagés tels quels.
  removeChoirMaster(choirId: string, userId: string): Observable<unknown> {
    return this.http.delete<unknown>(`${CHOIRS_BASE_URL}/${choirId}/ChoirMasters/${userId}`);
  }

  // Fiche chorale de la zone « Ma structure » (policy ClientManager). 404 si clientId est
  // étranger à l'appelant OU si choirId n'appartient pas à clientId (double barrière IDOR,
  // vérifiée côté back). Une chorale Archivée est renvoyée normalement — jamais de 404 ni
  // d'exclusion sur ce seul critère.
  getDetail(clientId: string, choirId: string): Observable<IChoirDetail> {
    return this.http.get<IChoirDetail>(`${CLIENTS_BASE_URL}/${clientId}/Choirs/${choirId}`);
  }

  // 400 (statut absent/hors enum), 404 (mêmes 2 barrières IDOR que getDetail), 409 (transition
  // interdite, ou réactivation Archivée->Publiée au-delà du plafond) — propagés tels quels.
  // Appeler avec le statut déjà courant est idempotent côté back (200).
  changeStatus(clientId: string, choirId: string, status: ChoirStatusEnum): Observable<IChoirDetail> {
    const payload: IChangeStatusChoir = { Status: status };
    return this.http.put<IChoirDetail>(`${CLIENTS_BASE_URL}/${clientId}/Choirs/${choirId}/ChangeStatus`, payload);
  }
}
