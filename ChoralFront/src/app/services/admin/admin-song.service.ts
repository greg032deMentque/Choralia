import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@env/environment';
import { appendOptionalParam, buildPaginationParams } from '@core/pagination-params.util';
import { IPaginatedResult, IPaginationQueryParams } from '@models/common-models/paginated-result.model';
import {
  IAdminSongCatalogFilter,
  IAdminSongCatalogItem
} from '@models/admin-models/admin-song-catalog-item.model';
import { IAdminSongGroupChoirItem } from '@models/admin-models/admin-song-group-choir-item.model';

const ADMIN_SONGS_BASE_URL = `${environment.apiUrl}admin-songs`;

// Catalogue transverse des chants pour l'administration générale (`[Authorize(Roles =
// "Admin")]` côté back, lot 4) — lecture seule : aucune écriture, aucun accès aux files
// (partitions, enregistrements) n'est exposé par ce contrôleur. Le regroupement (une ligne =
// un groupe d'affichage, jamais une ligne = un Chant) est calculé entièrement côté back.
@Injectable({ providedIn: 'root' })
export class AdminSongService {
  private readonly http = inject(HttpClient);

  getPagedCatalogue(
    pagination: IPaginationQueryParams,
    filter: IAdminSongCatalogFilter
  ): Observable<IPaginatedResult<IAdminSongCatalogItem>> {
    let params = buildPaginationParams(pagination);
    params = appendOptionalParam(params, 'DuplicatesOnly', filter.DuplicatesOnly);

    return this.http.post<IPaginatedResult<IAdminSongCatalogItem>>(`${ADMIN_SONGS_BASE_URL}/GetPagedCatalogue`, null, {
      params
    });
  }

  // `cle` est une chaîne opaque calculée par le back (ChantCleHelper) : peut contenir des
  // espaces et des caractères variés. Transmise en query string (jamais en segment d'URL) —
  // HttpParams encode automatiquement la valeur ; ne pas la parser ni la reconstruire ici.
  getChoirsDuGroup(key: string): Observable<IAdminSongGroupChoirItem[]> {
    const params = new HttpParams().set('key', key);
    return this.http.get<IAdminSongGroupChoirItem[]>(`${ADMIN_SONGS_BASE_URL}/GetGroupChoirs`, { params });
  }
}
