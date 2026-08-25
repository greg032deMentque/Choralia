// Reflète InstructionStatusEnum (back, Chorale.Common.Enums). Workflow de publication d'une
// consigne : Draft -> Published -> Archivee (même pattern que SongListStatusEnum).
// Ordinal aligné sur le back, ne pas réordonner.
export enum InstructionStatusEnum {
  Draft = 0,
  Published = 1,
  Archived = 2
}

export function getStatusInstructionLabel(status: InstructionStatusEnum): string {
  switch (status) {
    case InstructionStatusEnum.Draft:
      return 'Brouillon';
    case InstructionStatusEnum.Published:
      return 'Publiée';
    case InstructionStatusEnum.Archived:
      return 'Archivée';
  }
}
