// Reflète EventStatusEnum (back, Chorale.Common.Enums). Cycle de vie éditorial d'un
// événement, piloté exclusivement par POST /api/events/ChangeStatus (jamais modifiable
// via Create/Update). Transitions autorisées côté back : Draft->Publie, Draft->Archive,
// Publie->Annule, Publie->Archive, Annule->Archive. Publish exige un Lieu non vide (400 sinon).
// Ordinal aligné sur le back, ne pas réordonner.
export enum EventStatusEnum {
  Draft = 0,
  Published = 1,
  Cancelled = 2,
  Archived = 3
}

export function getEventStatusLabel(status: EventStatusEnum): string {
  switch (status) {
    case EventStatusEnum.Draft:
      return 'Brouillon';
    case EventStatusEnum.Published:
      return 'Publié';
    case EventStatusEnum.Cancelled:
      return 'Annulé';
    case EventStatusEnum.Archived:
      return 'Archivé';
  }
}
