export enum RecordingTypeEnum {
  General = 0,
  ByVoicePart = 1
}

export function getTypeRecordingLabel(type: RecordingTypeEnum): string {
  switch (type) {
    case RecordingTypeEnum.General:
      return 'Général';
    case RecordingTypeEnum.ByVoicePart:
      return 'Par voix';
  }
}
