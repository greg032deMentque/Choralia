export enum SongStatusEnum {
  Active = 0,
  Archived = 1
}

export function getSongStatusLabel(status: SongStatusEnum): string {
  switch (status) {
    case SongStatusEnum.Active:
      return 'Actif';
    case SongStatusEnum.Archived:
      return 'Archivé';
  }
}
