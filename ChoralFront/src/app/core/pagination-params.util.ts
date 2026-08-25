import { HttpParams } from '@angular/common/http';
import { IPaginationQueryParams } from '@models/common-models/paginated-result.model';

// Traduit IPaginationQueryParams vers les noms de query params réels attendus par le back
// (Page/PageSize/SortActive/SortDirection/Filter) — utilisé par tous les services *GetPaged*
// (SongService, ScoreService, RecordingService, EventService, SongListService).
export function buildPaginationParams(params: IPaginationQueryParams): HttpParams {
  let httpParams = new HttpParams().set('Page', params.Page.toString()).set('PageSize', params.PageSize.toString());

  if (params.SortActive) {
    httpParams = httpParams.set('SortActive', params.SortActive);
  }
  if (params.SortDirection) {
    httpParams = httpParams.set('SortDirection', params.SortDirection);
  }
  if (params.Filter) {
    httpParams = httpParams.set('Filter', params.Filter);
  }

  return httpParams;
}

// Ajoute un filtre optionnel uniquement s'il est renseigné — évite d'envoyer des query
// params vides (ex. SongId='', Statut=undefined) qui pourraient être mal interprétés par
// le model binding ASP.NET côté back (Guid.Empty, enum invalide, etc.).
export function appendOptionalParam(
  httpParams: HttpParams,
  key: string,
  value: string | number | boolean | null | undefined
): HttpParams {
  if (value === null || value === undefined || value === '') {
    return httpParams;
  }
  return httpParams.set(key, String(value));
}

// Variante liste : un même paramètre répété (`?ClientIds=a&ClientIds=b`), conforme au model
// binding ASP.NET pour un filtre de type `List<Guid>`/`Guid[]` en `[FromQuery]`. Tableau vide
// ou absent → aucun paramètre ajouté (même convention que appendOptionalParam : ne jamais
// envoyer une clé vide plutôt que de l'omettre).
export function appendOptionalArrayParam(httpParams: HttpParams, key: string, values: readonly string[] | null | undefined): HttpParams {
  if (!values || values.length === 0) {
    return httpParams;
  }
  return values.reduce((params, value) => params.append(key, value), httpParams);
}
