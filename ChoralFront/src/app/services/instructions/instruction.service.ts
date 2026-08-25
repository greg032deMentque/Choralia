import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@env/environment';
import { buildPaginationParams, appendOptionalParam } from '@core/pagination-params.util';
import { IPaginatedResult, IPaginationQueryParams } from '@models/common-models/paginated-result.model';
import {
  ICreateInstructionRequest,
  IInstruction,
  IInstructionListFilters,
  IUpdateInstructionRequest
} from '@models/instructions-models/instruction.model';

const INSTRUCTIONS_BASE_URL = `${environment.apiUrl}instructions`;

// InstructionController mélange TROIS conventions de route, contrairement au reste de l'API —
// les respecter à la lettre, un écart produit un 404 ou un 405 silencieux :
//   Update  : PUT  /Update            → identifiant dans le CORPS (pas de ?id=)
//   Publish : POST /{id}/Publish      → identifiant dans le CHEMIN
//   Archive : POST /{id}/Archive      → identifiant dans le CHEMIN
//   Delete  : DELETE /Delete?id=      → identifiant en query param
//
// GET /GetById existe côté back mais n'est pas exposé ici : le panneau charge par GetPaged et
// édite depuis l'élément déjà en mémoire. Ne pas ajouter de méthode sans appelant.
@Injectable({ providedIn: 'root' })
export class InstructionService {
  private readonly http = inject(HttpClient);

  getPaged(filters: IInstructionListFilters, params: IPaginationQueryParams): Observable<IPaginatedResult<IInstruction>> {
    let httpParams = buildPaginationParams(params);
    httpParams = appendOptionalParam(httpParams, 'SongId', filters.SongId);

    return this.http.post<IPaginatedResult<IInstruction>>(`${INSTRUCTIONS_BASE_URL}/GetPaged`, null, {
      params: httpParams
    });
  }

  create(request: ICreateInstructionRequest): Observable<IInstruction> {
    return this.http.post<IInstruction>(`${INSTRUCTIONS_BASE_URL}/Create`, request);
  }

  update(request: IUpdateInstructionRequest): Observable<IInstruction> {
    return this.http.put<IInstruction>(`${INSTRUCTIONS_BASE_URL}/Update`, request);
  }

  publish(id: string): Observable<IInstruction> {
    return this.http.post<IInstruction>(`${INSTRUCTIONS_BASE_URL}/${id}/Publish`, null);
  }

  archive(id: string): Observable<IInstruction> {
    return this.http.post<IInstruction>(`${INSTRUCTIONS_BASE_URL}/${id}/Archive`, null);
  }

  delete(id: string): Observable<unknown> {
    return this.http.delete<unknown>(`${INSTRUCTIONS_BASE_URL}/Delete`, { params: { id } });
  }
}
