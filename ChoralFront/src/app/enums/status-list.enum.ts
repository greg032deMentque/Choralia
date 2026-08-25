// Reflète StatutListeEnum (back, Chorale.Common.Enums). Workflow d'une liste de
// chants : Draft -> Published -> Archivee, avec retour possible Published -> Draft
// (RevertToDraft). Jamais modifiable via Create/Update — uniquement via les
// endpoints dédiés Publish/Archive/RevertToDraft.
export enum SongListStatusEnum {
  Draft = 0,
  Published = 1,
  Archived = 2
}

export function getStatusListLabel(status: SongListStatusEnum): string {
  switch (status) {
    case SongListStatusEnum.Draft:
      return 'Brouillon';
    case SongListStatusEnum.Published:
      return 'Publiée';
    case SongListStatusEnum.Archived:
      return 'Archivée';
  }
}
