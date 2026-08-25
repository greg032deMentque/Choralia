export enum RecordingSourceEnum {
  RecordedInApp = 0,
  UploadedFile = 1,
  Shared = 2
}

export function getSourceRecordingLabel(source: RecordingSourceEnum): string {
  switch (source) {
    case RecordingSourceEnum.RecordedInApp:
      return 'Enregistré dans l\'app';
    case RecordingSourceEnum.UploadedFile:
      return 'Fichier déposé';
    case RecordingSourceEnum.Shared:
      return 'Partagé';
  }
}
