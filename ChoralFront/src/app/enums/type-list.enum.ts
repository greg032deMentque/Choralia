// Reflète TypeListeEnum (back, Chorale.Common.Enums). Détermine le rattachement d'une
// liste de chants : libre, liée à un événement, à une saison ou à un pupitre.
export enum SongListTypeEnum {
  Free = 0,
  Event = 1,
  Season = 2,
  Section = 3
}

export function getTypeListLabel(type: SongListTypeEnum): string {
  switch (type) {
    case SongListTypeEnum.Free:
      return 'Libre';
    case SongListTypeEnum.Event:
      return 'Liée à un événement';
    case SongListTypeEnum.Season:
      return 'Saison';
    case SongListTypeEnum.Section:
      return 'Section';
  }
}
