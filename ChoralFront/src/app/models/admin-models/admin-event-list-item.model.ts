import { EventTypeEnum } from '@app/enums/event-type.enum';
import { EventStatusEnum } from '@app/enums/event-status.enum';
import { EventEffectiveStateEnum } from '@app/enums/event-effective-state.enum';

// Reflète AdminEventListItemViewModel (back, AdminEvenementController.GetPaged) — liste
// transverse à tous les clients, lecture seule (aucune écriture exposée par ce contrôleur, voir
// EventController pour la management réelle côté chorale). ChoirId/ChoirName sont nullables :
// un événement autonome (créé hors chorale) n'en a pas — toujours prévoir un repli explicite,
// jamais afficher "undefined" ni une cellule vide ambiguë.
export interface IAdminEventListItem {
  Id: string;
  Title: string;
  Type: EventTypeEnum;
  StartDate: string;
  EndDate: string | null;
  Location: string;
  Status: EventStatusEnum;
  EffectiveState: EventEffectiveStateEnum;
  ChoirId: string | null;
  ChoirName: string | null;
  ClientId: string;
  ClientName: string;
  ParticipantCount: number;
  // Événement autonome hérité, rattaché au client technique créé par la migration 12 — anomalie
  // qu'un opérateur doit traiter (rattachement manuel à faire), à mettre en évidence à l'écran.
  IsTechnicalClientAnomaly: boolean;
}

// Reflète AdminEventsPagedFilterViewModel — tous optionnels, en complément de Filter (texte
// libre) porté par IPaginationQueryParams.
export interface IAdminEventsFilter {
  ClientId?: string;
  ChoirId?: string;
  Status?: EventStatusEnum;
  Type?: EventTypeEnum;
  Upcoming?: boolean;
}
