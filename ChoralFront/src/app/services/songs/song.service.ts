import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '@env/environment';
import { buildPaginationParams, appendOptionalParam } from '@core/pagination-params.util';
import { IPaginatedResult, IPaginationQueryParams } from '@models/common-models/paginated-result.model';
import { ISelectOption } from '@models/common-models/select-option.model';
import { ISong } from '@models/songs-models/song.model';
import { ISongListFilters } from '@models/songs-models/song-filters.model';

const SONGS_BASE_URL = `${environment.apiUrl}songs`;

// Plafond du répertoire chargé d'un coup pour alimenter un sélecteur. UN SEUL endroit, ici :
// `PaginateViewModel.PageSize` porte `[Range(1, 100)]` côté back, et la valeur 200 dupliquée
// dans cinq composants avait rendu tous ces sélecteurs vides sur un 400 silencieux. Limite
// assumée : au-delà de 100 chants, le sélecteur est tronqué sans le dire — il faudra une
// recherche serveur, pas un plafond plus haut.
const CHOIR_OPTIONS_PAGE_SIZE = 100;

@Injectable({ providedIn: 'root' })
export class SongService {
  private readonly http = inject(HttpClient);

  getPaged(filters: ISongListFilters, params: IPaginationQueryParams): Observable<IPaginatedResult<ISong>> {
    let httpParams = buildPaginationParams(params);
    // Noms EXACTS des propriétés de SongPagedFilterViewModel : le model binding ASP.NET
    // ignore silencieusement une clé inconnue. Les anciens noms français (ChoraleId, Voix,
    // Statut, Priorite) rendaient les quatre filtres inopérants sans aucun signal d'erreur.
    httpParams = appendOptionalParam(httpParams, 'ChoirId', filters.ChoirId);
    httpParams = appendOptionalParam(httpParams, 'VoicePart', filters.VoicePart);
    httpParams = appendOptionalParam(httpParams, 'Status', filters.Status);
    httpParams = appendOptionalParam(httpParams, 'Priority', filters.Priority);

    return this.http
      .post<IPaginatedResult<ISong>>(`${SONGS_BASE_URL}/GetPaged`, null, { params: httpParams })
  }

  getPagedByChoir(choirId: string, params: IPaginationQueryParams): Observable<IPaginatedResult<ISong>> {
    const httpParams = buildPaginationParams(params).set('ChoirId', choirId);
    return this.http
      .post<IPaginatedResult<ISong>>(`${SONGS_BASE_URL}/GetPagedByChoir`, null, { params: httpParams })
  }

  /**
   * Répertoire d'une chorale réduit à ce qu'un `<select>` consomme, trié par titre. Porte le
   * plafond de pagination et le mapping vers `ISelectOption` pour tous ses appelants —
   * SongPickerComponent (filtre des listes) et SongSelectComponent (champ de formulaire).
   */
  getChoirOptions(choirId: string): Observable<ISelectOption<string>[]> {
    return this.getPagedByChoir(choirId, {
      Page: 1,
      PageSize: CHOIR_OPTIONS_PAGE_SIZE,
      SortActive: 'Title',
      SortDirection: 'asc'
    }).pipe(map(result => result.Items.map(song => ({ Value: song.Id ?? '', Label: song.Title }))));
  }

  getById(id: string): Observable<ISong> {
    return this.http.get<ISong>(`${SONGS_BASE_URL}/GetById`, { params: { id } });
  }

  create(request: ISong): Observable<ISong> {
    return this.http.post<ISong>(`${SONGS_BASE_URL}/Create`, request);
  }

  update(id: string, request: ISong): Observable<ISong> {
    return this.http.put<ISong>(`${SONGS_BASE_URL}/Update`, request, { params: { id } });
  }

  delete(id: string): Observable<unknown> {
    return this.http.delete<unknown>(`${SONGS_BASE_URL}/Delete`, { params: { id } });
  }
}
