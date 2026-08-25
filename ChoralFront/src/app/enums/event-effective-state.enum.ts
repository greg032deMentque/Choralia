// Reflète EventEffectiveStateEnum (back, Chorale.Common.Enums). État calculé côté serveur
// (EventViewModel.EffectiveState) à partir de Statut ET des dates (StartDate/EndDate) —
// distinct de Statut : un événement Publié dont la date est passée devient Finished sans
// changement de Statut. Lecture seule côté front, jamais transmis en écriture.
// Ordinal aligné sur le back, ne pas réordonner.
export enum EventEffectiveStateEnum {
  Draft = 0,
  Published = 1,
  Finished = 2,
  Cancelled = 3,
  Archived = 4
}

export function getEventEffectiveStateLabel(state: EventEffectiveStateEnum): string {
  switch (state) {
    case EventEffectiveStateEnum.Draft:
      return 'Brouillon';
    case EventEffectiveStateEnum.Published:
      return 'Publié';
    case EventEffectiveStateEnum.Finished:
      return 'Terminé';
    case EventEffectiveStateEnum.Cancelled:
      return 'Annulé';
    case EventEffectiveStateEnum.Archived:
      return 'Archivé';
  }
}
