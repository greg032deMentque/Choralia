import { VoicePartEnum } from '@app/enums/voice-part.enum';
import { UserRoleEnum } from '@app/enums/user-role.enum';

// Reflète AdmettreDemandeViewModel (back). Corps de POST
// /api/spaces/{spaceId}/MembershipRequests/{id}/Approve. Admission = affectation : voix principale et
// rôle sont exigés dans la même opération, jamais une admission « nue » qui produirait un
// membre invalide. Role ne doit porter que Chanteur (2) ou Responsable (3) — contrainte
// vérifiée côté back, l'UI ne doit proposer que ces deux valeurs.
export interface IApproveRequestRequest {
  PrimaryVoicePart: VoicePartEnum;
  Role: UserRoleEnum;
}
