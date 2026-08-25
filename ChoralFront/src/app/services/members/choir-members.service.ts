import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '@env/environment';
import { buildPaginationParams } from '@core/pagination-params.util';
import { IPaginatedResult, IPaginationQueryParams } from '@models/common-models/paginated-result.model';
import { IMemberChoir } from '@models/members-models/member-choir.model';
import { IInviteMemberRequest } from '@models/members-models/invite-member-request.model';
import { mapRolesFromApi } from '@core/choir-roles.util';

const CHOIR_MEMBERS_BASE_URL = `${environment.apiUrl}choir-members`;

type IMemberChoirApi = Omit<IMemberChoir, 'Roles'> & { Roles: string[] };

function mapMemberChoir(raw: IMemberChoirApi): IMemberChoir {
  return { ...raw, Roles: mapRolesFromApi(raw.Roles) };
}

@Injectable({ providedIn: 'root' })
export class ChoirMembersService {
  private readonly http = inject(HttpClient);

  // `choirId` n'est PAS transmis en query param : ChoirMembersController.GetPaged lit le
  // périmètre via spaceContextAccessor.RequireSpaceId() (en-tête X-Space-Id, posé par
  // TokenInterceptor). Le paramètre reste dans la signature pour forcer l'appelant à nommer la
  // chorale qu'il croit interroger — un appel sans espace actif est une erreur d'appelant.
  getPaged(choirId: string, params: IPaginationQueryParams): Observable<IPaginatedResult<IMemberChoir>> {
    const httpParams = buildPaginationParams(params);
    return this.http
      .post<IPaginatedResult<IMemberChoirApi>>(`${CHOIR_MEMBERS_BASE_URL}/GetPaged`, null, { params: httpParams })
      .pipe(map(res => ({ ...res, Items: res.Items.map(mapMemberChoir) })));
  }

  // 201 : MemberChoirListItemViewModel. Policy ChoirManager — le ChoirId du body DOIT
  // correspondre à l'espace actif transmis via X-Space-Id (TokenInterceptor), sinon 403 ou
  // invitation envoyée vers le mauvais espace (voir space-bootstrap.component.ts).
  invite(request: IInviteMemberRequest): Observable<IMemberChoir> {
    return this.http.post<IMemberChoirApi>(`${CHOIR_MEMBERS_BASE_URL}/Invite`, request).pipe(map(mapMemberChoir));
  }
}
