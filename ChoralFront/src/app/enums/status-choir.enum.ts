// Reflète ChoirStatusEnum (back, Chorale.Common.Enums). Cycle de vie d'une chorale (migration
// 13), remplace l'ancien booléen EstArchivee. Piloté exclusivement par
// PUT /api/admin-choirs/ChangeStatus (jamais modifiable via Update). Transitions autorisées
// côté back (409 sinon, message nommant les deux états) : Draft->Publie, Draft->Archive,
// Publie->Annule, Publie->Archive, Annule->Publie, Annule->Archive, Archive->Publie.
// Ordinal aligné sur le back, ne pas réordonner.
export enum ChoirStatusEnum {
  Draft = 0,
  Published = 1,
  Cancelled = 2,
  Archived = 3
}

export function getStatusChoirLabel(status: ChoirStatusEnum): string {
  switch (status) {
    case ChoirStatusEnum.Draft:
      return 'Brouillon';
    case ChoirStatusEnum.Published:
      return 'Publiée';
    case ChoirStatusEnum.Cancelled:
      return 'Annulée';
    case ChoirStatusEnum.Archived:
      return 'Archivée';
  }
}

// Matrice exacte des transitions acceptées par le back (ChangeStatusAsync) — l'UI ne doit
// jamais proposer une transition hors de cette liste (le back répondrait 409).
const CHOIR_STATUS_ALLOWED_TRANSITIONS: Record<ChoirStatusEnum, readonly ChoirStatusEnum[]> = {
  [ChoirStatusEnum.Draft]: [ChoirStatusEnum.Published, ChoirStatusEnum.Archived],
  [ChoirStatusEnum.Published]: [ChoirStatusEnum.Cancelled, ChoirStatusEnum.Archived],
  [ChoirStatusEnum.Cancelled]: [ChoirStatusEnum.Published, ChoirStatusEnum.Archived],
  [ChoirStatusEnum.Archived]: [ChoirStatusEnum.Published]
};

export function getStatusChoirTransitionsAllowed(status: ChoirStatusEnum): readonly ChoirStatusEnum[] {
  return CHOIR_STATUS_ALLOWED_TRANSITIONS[status];
}
