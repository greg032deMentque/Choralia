// Reflète ClientStatusEnum (back, Chorale.Common.Enums). Ordinal aligné sur le back, ne pas
// réordonner.
export enum ClientStatusEnum {
  Active = 0,
  Suspended = 1,
  Archived = 2
}

export function getStatusClientLabel(status: ClientStatusEnum): string {
  switch (status) {
    case ClientStatusEnum.Active:
      return 'Actif';
    case ClientStatusEnum.Suspended:
      return 'Suspendu';
    case ClientStatusEnum.Archived:
      return 'Archivé';
  }
}
