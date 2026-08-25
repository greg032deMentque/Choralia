export enum MemberStatusEnum {
  Invited = 0,
  Active = 1,
  Inactive = 2,
  Archived = 3
}

export function getMemberStatusLabel(status: MemberStatusEnum): string {
  switch (status) {
    case MemberStatusEnum.Invited:
      return 'Invité';
    case MemberStatusEnum.Active:
      return 'Actif';
    case MemberStatusEnum.Inactive:
      return 'Inactif';
    case MemberStatusEnum.Archived:
      return 'Archivé';
  }
}
