export enum EventTypeEnum {
  Concert = 0,
  Rehearsal = 1,
  Wedding = 2,
  Mass = 3,
  Funeral = 4,
  Other = 5
}

export function getEventTypeLabel(type: EventTypeEnum): string {
  switch (type) {
    case EventTypeEnum.Concert:
      return 'Concert';
    case EventTypeEnum.Rehearsal:
      return 'Répétition';
    case EventTypeEnum.Wedding:
      return 'Mariage';
    case EventTypeEnum.Mass:
      return 'Messe';
    case EventTypeEnum.Funeral:
      return 'Obsèques';
    case EventTypeEnum.Other:
      return 'Autre';
  }
}
