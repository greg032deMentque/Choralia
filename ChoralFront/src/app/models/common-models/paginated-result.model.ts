// Contrat réel du backend (PagedListViewModel<T>, ChoraleBack) : CurrentPage — pas
// PageNumber ni TotalPages (à calculer côté front si besoin : Math.ceil(TotalCount / PageSize)).
export interface IPaginatedResult<T> {
  Items: T[];
  TotalCount: number;
  CurrentPage: number;
  PageSize: number;
}

// Query params réels attendus par les endpoints GetPaged/GetPagedPar* du back :
// Page, PageSize, SortActive, SortDirection, Filter (PAS PageNumber/SortBy).
export interface IPaginationQueryParams {
  Page: number;
  PageSize: number;
  SortActive?: string;
  SortDirection?: 'asc' | 'desc';
  Filter?: string;
}
