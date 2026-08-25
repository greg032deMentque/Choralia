export enum SongPriorityEnum {
  Low = 0,
  Normal = 1,
  High = 2
}

export function getPrioritySongLabel(priority: SongPriorityEnum): string {
  switch (priority) {
    case SongPriorityEnum.Low:
      return 'Basse';
    case SongPriorityEnum.Normal:
      return 'Normale';
    case SongPriorityEnum.High:
      return 'Haute';
  }
}
