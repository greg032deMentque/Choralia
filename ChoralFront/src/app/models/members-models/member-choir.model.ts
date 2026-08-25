import { MemberStatusEnum } from '@app/enums/member-status.enum';
import { UserRoleEnum } from '@app/enums/user-role.enum';
import { VoicePartEnum } from '@app/enums/voice-part.enum';

// Reflète MemberChoirListItemViewModel (back). Roles est global à l'utilisateur (pas
// scopé par cette liste), SectionId/SectionVoicePart reflètent le pupitre auquel le membre
// est rattaché dans la chorale actif (null si non affecté à un pupitre).
export interface IMemberChoir {
  Id: string;
  UserId: string;
  ChoirId: string;
  Status: MemberStatusEnum;
  UserFullName: string | null;
  UserEmail: string | null;
  Roles: UserRoleEnum[];
  SectionId: string | null;
  SectionVoicePart: VoicePartEnum | null;
}
