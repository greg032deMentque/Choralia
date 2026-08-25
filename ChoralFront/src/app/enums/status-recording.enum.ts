export enum RecordingStatusEnum {
  Draft = 0,
  PendingReview = 1,
  Published = 2,
  Archived = 3
}

export function getStatusRecordingLabel(status: RecordingStatusEnum): string {
  switch (status) {
    case RecordingStatusEnum.Draft:
      return 'Brouillon';
    case RecordingStatusEnum.PendingReview:
      return 'À valider';
    case RecordingStatusEnum.Published:
      return 'Publié';
    case RecordingStatusEnum.Archived:
      return 'Archivé';
  }
}
