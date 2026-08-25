export enum ScoreTypeEnum {
  General = 0,
  ByVoicePart = 1
}

export function getTypeScoreLabel(type: ScoreTypeEnum): string {
  switch (type) {
    case ScoreTypeEnum.General:
      return 'Générale';
    case ScoreTypeEnum.ByVoicePart:
      return 'Par voix';
  }
}
