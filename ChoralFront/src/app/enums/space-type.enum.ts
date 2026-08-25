// Reflète SpaceTypeEnum (back, Chorale.Common.Enums). Ordinal aligné sur le back, ne pas
// réordonner ni insérer de valeur au milieu — toute valeur ajoutée côté back se reflète en
// fin d'enum front, avec le même entier des deux côtés.
export enum SpaceTypeEnum {
  Choir = 0,
  Event = 1
}

export function getSpaceTypeLabel(type: SpaceTypeEnum): string {
  switch (type) {
    case SpaceTypeEnum.Choir:
      return 'Chorale';
    case SpaceTypeEnum.Event:
      return 'Événement';
  }
}
