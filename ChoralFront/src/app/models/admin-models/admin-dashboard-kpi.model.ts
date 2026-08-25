// Reflète AdminDashboardKpiViewModel (back, AdminDashboardController.GetKpi — `10-D30`).
// Décision produit D30 : aucun indicateur financier (impayés, renouvellements, Stripe) — aucune
// source en base ne les alimente, un indicateur inventé est pire qu'un indicateur absent.
export interface IAdminDashboardKpi {
  Clients: IClientsKpi;
  Choirs: IChoirsKpi;
  Users: IUsersKpi;
  InactiveChoirs: IInactiveChoirsKpi;
  NotStartedClients: INotStartedClientsKpi;
  ClientsNearCap: IClientsNearCapKpi;
  // Volume total de files stockés (partitions et enregistrements confondus, soft-deletes
  // inclus). Non actionnable (dixit le ViewModel back) : aucun écran de liste ne porte sur ce
  // total agrégé — jamais rendu cliquable, quelle que soit sa valeur.
  TotalStorageBytes: number;
  Songs: ISongsKpi;
  UpcomingEvents30Days: number;
  EventsWithoutStructureAnomaly: IEventsWithoutStructureAnomalyKpi;
}

// Actionnable via StatutClientEnum : chaque compteur correspond à une valeur de Statut à
// passer en filtre — écart assumé : AdminClientController.GetPaged (contrat réel lu dans
// ClientController.cs) n'accepte aujourd'hui QUE PaginateViewModel, aucun filtre Statut. La
// navigation ci-dessous transmet quand même le query param (voir dashboard.component.ts) pour
// rester compatible day-1 avec un futur ajout de filtre côté back ; tant que ce filtre n'existe
// pas côté serveur, la liste target s'affiche non filtrée.
export interface IClientsKpi {
  Total: number;
  Active: number;
  Suspended: number;
  Archived: number;
}

// Actionnable via StatutChoraleEnum — AdminChoirService.getPaged (front) transmet déjà ce
// filtre au serveur (contrat confirmé). Écart assumé : choir-list.component.ts (figé) ne lit
// pas les query params de la route au chargement — la navigation positionne l'URL mais la page
// target reste à filtrer manuellement tant que ce composant n'est pas mis à jour pour lire
// ActivatedRoute.queryParams.
export interface IChoirsKpi {
  Total: number;
  Draft: number;
  Published: number;
  Cancelled: number;
  Archived: number;
}

// Non actionnable en l'état — confirmé en lisant AdminUserController.cs : GetPaged et
// GetUnattachedUsersPaged n'acceptent qu'un PaginateViewModel nu, aucun filtre
// IsActive/IsGuestAccount. Le ViewModel back documente lui-même ce gel de contrat pour ce lot.
export interface IUsersKpi {
  Total: number;
  Active: number;
  InactiveInvitees: number;
}

export interface IInactiveChoirsKpi {
  Count: number;
  ChoirIds: string[];
}

// Aucun filtre serveur (ClientController.GetPaged n'accepte aucun filtre) — seule la liste des
// identifiants est exploitable, voir dashboard.component.ts (panneau dépliant, pas de
// navigation vers une liste filtrée qui n'existe pas).
export interface INotStartedClientsKpi {
  Count: number;
  ClientIds: string[];
}

export interface IClientsNearCapKpi {
  Count: number;
  ClientIds: string[];
}

// Actionnable via AdminSongCataloguePagedFilterViewModel.DuplicatesOnly — support serveur
// confirmé (AdminSongService.getPagedCatalogue, front, transmet déjà ce filtre).
export interface ISongsKpi {
  Total: number;
  DuplicateGroups: number;
}

export interface IEventsWithoutStructureAnomalyKpi {
  Count: number;
  EventIds: string[];
}
