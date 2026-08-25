import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@env/environment';
import { buildPaginationParams, appendOptionalParam } from '@core/pagination-params.util';
import { IPaginatedResult, IPaginationQueryParams } from '@models/common-models/paginated-result.model';
import { IRecording } from '@models/recordings-models/recording.model';
import { ICreateRecordingRequest } from '@models/recordings-models/create-recording-request.model';
import { IUpdateRecordingRequest } from '@models/recordings-models/update-recording-request.model';
import { IRecordingListFilters } from '@models/recordings-models/recording-filters.model';
import { IPlaylistTrack } from '@models/recordings-models/playlist-track.model';
import { VoicePartEnum } from '@app/enums/voice-part.enum';

const RECORDINGS_BASE_URL = `${environment.apiUrl}recordings`;





@Injectable({ providedIn: 'root' })
export class RecordingService {
  private readonly http = inject(HttpClient);

  // RecordingController.GetPagedBySong exige SongId. Type/TargetVoicePart/Status/Source
  // sont ajoutés en filtres secondaires optionnels (query params supplémentaires, ignorés
  // par le back si non supportés sur cette route — sans risque de rupture) pour couvrir
  // "filtrable par chant/voix/statut/source" (bloc de transfert, notes
  // RecordingListComponent).
  getPagedBySong(
    songId: string,
    filters: IRecordingListFilters,
    params: IPaginationQueryParams
  ): Observable<IPaginatedResult<IRecording>> {
    let httpParams = buildPaginationParams(params).set('SongId', songId);
    httpParams = appendOptionalParam(httpParams, 'Type', filters.Type);
    httpParams = appendOptionalParam(httpParams, 'TargetVoicePart', filters.TargetVoicePart);
    httpParams = appendOptionalParam(httpParams, 'Status', filters.Status);
    httpParams = appendOptionalParam(httpParams, 'Source', filters.Source);

    return this.http
      .post<IPaginatedResult<IRecording>>(`${RECORDINGS_BASE_URL}/GetPagedBySong`, null, { params: httpParams })
  }

  getById(id: string): Observable<IRecording> {
    return this.http
      .get<IRecording>(`${RECORDINGS_BASE_URL}/GetById`, { params: { id } })
  }

  // Fichier validé côté composant (extension + taille ≤ 100 Mo) avant appel — le back
  // revalide de toute façon (400 si format rejeté, 413 si trop volumineux). DurationSeconds
  // est mesurée côté client via <audio> avant l'appel (aucun recalcul serveur).
  create(file: File, request: ICreateRecordingRequest): Observable<IRecording> {
    const formData = new FormData();
    formData.append('File', file);
    formData.append('SongId', request.SongId);
    formData.append('Type', request.Type.toString());
    if (request.TargetVoicePart !== null && request.TargetVoicePart !== undefined) {
      formData.append('TargetVoicePart', request.TargetVoicePart.toString());
    }
    formData.append('ContentOwner', request.ContentOwner);
    formData.append('DownloadAllowed', request.DownloadAllowed.toString());
    formData.append('DurationSeconds', request.DurationSeconds.toString());
    formData.append('Source', request.Source.toString());

    return this.http.post<IRecording>(`${RECORDINGS_BASE_URL}/Create`, formData);
  }

  update(id: string, request: IUpdateRecordingRequest): Observable<IRecording> {
    return this.http
      .put<IRecording>(`${RECORDINGS_BASE_URL}/Update`, request, { params: { id } })
  }

  sendAValidation(id: string): Observable<unknown> {
    return this.http.post<unknown>(`${RECORDINGS_BASE_URL}/${id}/SubmitForReview`, null);
  }

  // Réservé ChoirManager (pas SectionLeader) — le back rejette en 403 si appelé hors
  // droit ; le composant n'affiche le bouton qu'aux users Responsable (filtre UX).
  publish(id: string): Observable<unknown> {
    return this.http.post<unknown>(`${RECORDINGS_BASE_URL}/${id}/Publish`, null);
  }

  reject(id: string): Observable<unknown> {
    return this.http.post<unknown>(`${RECORDINGS_BASE_URL}/${id}/Reject`, null);
  }

  archive(id: string): Observable<unknown> {
    return this.http.post<unknown>(`${RECORDINGS_BASE_URL}/${id}/Archive`, null);
  }

  restore(id: string): Observable<unknown> {
    return this.http.post<unknown>(`${RECORDINGS_BASE_URL}/${id}/Restore`, null);
  }

  delete(id: string): Observable<unknown> {
    return this.http.delete<unknown>(`${RECORDINGS_BASE_URL}/Delete`, { params: { id } });
  }

  download(id: string): Observable<Blob> {
    return this.http.get(`${RECORDINGS_BASE_URL}/${id}/Stream`, { responseType: 'blob' });
  }

  // Playlist des enregistrements publiés pour une voix donnée, sur l'ensemble des lists
  // de chants de l'événement — consommée par AudioPlayerComponent (streaming via
  // download()/Blob, jamais d'URL <audio src> nue : TokenInterceptor n'intercepte pas les
  // requêtes déclenchées par l'élément <audio>).
  getEventPlaylistByVoicePart(eventId: string, voicePart: VoicePartEnum): Observable<IPlaylistTrack[]> {
    const params = { eventId, voicePart: voicePart.toString() };
    return this.http
      .get<IPlaylistTrack[]>(`${RECORDINGS_BASE_URL}/EventPlaylistByVoicePart`, { params });
  }
}
