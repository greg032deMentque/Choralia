import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@env/environment';
import { buildPaginationParams, appendOptionalParam } from '@core/pagination-params.util';
import { IPaginatedResult, IPaginationQueryParams } from '@models/common-models/paginated-result.model';
import { IScore } from '@models/scores-models/score.model';
import { ICreateScoreRequest } from '@models/scores-models/create-score-request.model';
import { IUpdateScoreRequest } from '@models/scores-models/update-score-request.model';
import { IScoreListFilters } from '@models/scores-models/score-filters.model';

const SCORES_BASE_URL = `${environment.apiUrl}scores`;



@Injectable({ providedIn: 'root' })
export class ScoreService {
  private readonly http = inject(HttpClient);

  // ScoreController.GetPagedBySong exige SongId. Type/TargetVoicePart/Statut sont ajoutés
  // en filtres secondaires optionnels (query params supplémentaires, ignorés par le back si
  // non supportés sur cette route — sans risque de rupture) pour couvrir "filtrable par
  // chant/voix/statut" (bloc de transfert, notes ScoreListComponent).
  getPagedBySong(
    songId: string,
    filters: IScoreListFilters,
    params: IPaginationQueryParams
  ): Observable<IPaginatedResult<IScore>> {
    let httpParams = buildPaginationParams(params).set('SongId', songId);
    httpParams = appendOptionalParam(httpParams, 'Type', filters.Type);
    httpParams = appendOptionalParam(httpParams, 'TargetVoicePart', filters.TargetVoicePart);
    httpParams = appendOptionalParam(httpParams, 'Status', filters.Status);

    return this.http
      .post<IPaginatedResult<IScore>>(`${SCORES_BASE_URL}/GetPagedBySong`, null, { params: httpParams })
  }

  getById(id: string): Observable<IScore> {
    return this.http.get<IScore>(`${SCORES_BASE_URL}/GetById`, { params: { id } });
  }

  // Fichier validé côté composant (extension + taille ≤ 20 Mo) avant appel — le back
  // revalide de toute façon (400 si format rejeté, 413 si trop volumineux).
  create(file: File, request: ICreateScoreRequest): Observable<IScore> {
    const formData = new FormData();
    formData.append('File', file);
    formData.append('SongId', request.SongId);
    formData.append('Type', request.Type.toString());
    if (request.TargetVoicePart !== null && request.TargetVoicePart !== undefined) {
      formData.append('TargetVoicePart', request.TargetVoicePart.toString());
    }
    formData.append('Version', request.Version);
    formData.append('DownloadAllowed', request.DownloadAllowed.toString());

    return this.http.post<IScore>(`${SCORES_BASE_URL}/Create`, formData);
  }

  update(id: string, request: IUpdateScoreRequest): Observable<IScore> {
    return this.http.put<IScore>(`${SCORES_BASE_URL}/Update`, request, { params: { id } });
  }

  // Peut retourner 409 (conflit de publication concurrente) — le composant appelant doit
  // gérer ce cas explicitement en état inline (ApiErrorInterceptor affiche déjà un toast
  // global générique, mais ne doit pas être dupliqué par le composant).
  publish(id: string): Observable<unknown> {
    return this.http.post<unknown>(`${SCORES_BASE_URL}/${id}/Publish`, null);
  }

  archive(id: string): Observable<unknown> {
    return this.http.post<unknown>(`${SCORES_BASE_URL}/${id}/Archive`, null);
  }

  restore(id: string): Observable<unknown> {
    return this.http.post<unknown>(`${SCORES_BASE_URL}/${id}/Restore`, null);
  }

  delete(id: string): Observable<unknown> {
    return this.http.delete<unknown>(`${SCORES_BASE_URL}/Delete`, { params: { id } });
  }

  download(id: string): Observable<Blob> {
    return this.http.get(`${SCORES_BASE_URL}/${id}/Stream`, { responseType: 'blob' });
  }
}
