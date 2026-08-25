import { VoicePartEnum } from '@app/enums/voice-part.enum';

// Reflète InviteMemberViewModel (back). Corps de POST /api/choir-members/Invite — policy
// ChoirManager, exige l'en-tête X-Space-Id (posé par TokenInterceptor depuis
// AuthStore.activeSpaceId) scopé sur CETTE même chorale : voir space-bootstrap.component.ts
// pour la garantie que l'espace actif est bien positionné avant tout appel.
export interface IInviteMemberRequest {
  ChoirId: string;
  Email: string;
  Firstname?: string;
  Lastname?: string;

  // Voix principale du membre invité. OPTIONNEL côté back (le back a été déployé avant
  // ce front, le rendre requis aurait cassé l'invitation pendant cette fenêtre), mais
  // REQUIS côté UI : `02` §132 impose qu'une ligne d'appartenance porte toujours une voix,
  // et un membre sans pupitre n'est atteint par aucune consigne ni aucun enregistrement.
  PrimaryVoicePart?: VoicePartEnum;
}
