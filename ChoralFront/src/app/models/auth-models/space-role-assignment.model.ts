import { SpaceTypeEnum } from '@app/enums/space-type.enum';
import { VoicePartEnum } from '@app/enums/voice-part.enum';

// Reflète SpaceRoleAssignmentViewModel (back, GET /api/auth/Me). Remplace ChoraleRoleAssignment
// (déprécié) comme source unique de vérité front pour les rattachements utilisateur <-> espace.
// Pour un espace de type Chorale, ChoirId vaut toujours null : l'espace EST la chorale
// (SpaceId sert alors directement de ChoirId pour les appels scopés chorale existants).
// Pour un espace de type Event, ChoirId porte la chorale porteuse (null si autonome).
// Les espaces dont le client est Suspendu/Archivé sont déjà exclus par le back.
export interface ISpaceRoleAssignment {
  SpaceId: string;
  Name: string;
  SpaceType: SpaceTypeEnum;
  Roles: string[];
  ClientId: string | null;
  ChoirId: string | null;
  // Voix (pupitre) principale du membre sur cet espace. Null pour un espace de type Event, et
  // pour un choriste sans voix assignée sur une chorale.
  PrimaryVoicePart: VoicePartEnum | null;
}
