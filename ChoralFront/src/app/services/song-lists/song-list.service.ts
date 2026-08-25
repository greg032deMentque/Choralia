import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@env/environment';
import { buildPaginationParams } from '@core/pagination-params.util';
import { IPaginatedResult, IPaginationQueryParams } from '@models/common-models/paginated-result.model';
import { ISongList } from '@models/song-lists-models/song-list.model';
import { IAddSongRequest } from '@models/song-lists-models/add-song-request.model';
import { IReorderSongsRequest } from '@models/song-lists-models/reorder-songs-request.model';

const SONG_LISTS_BASE_URL = `${environment.apiUrl}song-lists`;



@Injectable({ providedIn: 'root' })
export class SongListService {
  private readonly http = inject(HttpClient);

  // SongListPagedFilterViewModel (back) n'expose que EventId en filtre — pas de
  // ChoirId (contrairement à SongController/EventController). Transmettre un
  // ChoirId ici serait un no-op silencieux côté back (champ absent du binding).
  getPaged(params: IPaginationQueryParams, eventId?: string): Observable<IPaginatedResult<ISongList>> {
    let httpParams = buildPaginationParams(params);
    if (eventId) {
      httpParams = httpParams.set('EventId', eventId);
    }
    return this.http
      .post<IPaginatedResult<ISongList>>(`${SONG_LISTS_BASE_URL}/GetPaged`, null, { params: httpParams })
  }

  getById(id: string): Observable<ISongList> {
    return this.http.get<ISongList>(`${SONG_LISTS_BASE_URL}/GetById`, { params: { id } });
  }

  create(request: ISongList): Observable<ISongList> {
    return this.http.post<ISongList>(`${SONG_LISTS_BASE_URL}/Create`, request);
  }

  update(id: string, request: ISongList): Observable<ISongList> {
    return this.http.put<ISongList>(`${SONG_LISTS_BASE_URL}/Update`, request, { params: { id } });
  }

  delete(id: string): Observable<unknown> {
    return this.http.delete<unknown>(`${SONG_LISTS_BASE_URL}/Delete`, { params: { id } });
  }

  addSong(songListId: string, songId: string, position: number): Observable<ISongList> {
    const request: IAddSongRequest = { SongId: songId, Position: position };
    return this.http.post<ISongList>(`${SONG_LISTS_BASE_URL}/${songListId}/AddSong`, request);
  }

  removeSong(songListId: string, songId: string): Observable<unknown> {
    return this.http.delete<unknown>(`${SONG_LISTS_BASE_URL}/${songListId}/RemoveSong/${songId}`);
  }

  // SongIds : composition complète et réordonnée de la liste (l'ordre = la position dans
  // le tableau). Rejeté par le back (409) si Statut != Draft, ou (400) si l'ensemble
  // ne correspond pas exactement à la composition actuelle.
  reorderSongs(songListId: string, songIds: string[]): Observable<ISongList> {
    const request: IReorderSongsRequest = { SongIds: songIds };
    return this.http
      .post<ISongList>(`${SONG_LISTS_BASE_URL}/${songListId}/ReorderSongs`, request)
  }

  publish(id: string): Observable<ISongList> {
    return this.http.post<ISongList>(`${SONG_LISTS_BASE_URL}/${id}/Publish`, null);
  }

  archive(id: string): Observable<ISongList> {
    return this.http.post<ISongList>(`${SONG_LISTS_BASE_URL}/${id}/Archive`, null);
  }

  repasserEnDraft(id: string): Observable<ISongList> {
    return this.http.post<ISongList>(`${SONG_LISTS_BASE_URL}/${id}/RevertToDraft`, null);
  }
}
