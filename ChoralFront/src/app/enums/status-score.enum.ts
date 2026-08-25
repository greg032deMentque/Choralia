export enum ScoreStatusEnum {
  Draft = 0,
  Published = 1,
  Archived = 2
}

export function getStatusScoreLabel(status: ScoreStatusEnum): string {
  switch (status) {
    case ScoreStatusEnum.Draft:
      return 'Brouillon';
    case ScoreStatusEnum.Published:
      return 'Publiée';
    case ScoreStatusEnum.Archived:
      return 'Archivée';
  }
}
