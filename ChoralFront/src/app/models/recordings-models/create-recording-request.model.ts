import { RecordingTypeEnum } from '@app/enums/type-recording.enum';
import { RecordingSourceEnum } from '@app/enums/source-recording.enum';
import { VoicePartEnum } from '@app/enums/voice-part.enum';

// Champs multipart (hors Fichier, transmis séparément) de RecordingController.Create.
// DurationSeconds est mesurée côté client via l'élément HTML5 <audio> avant l'envoi — aucun
// recalcul serveur (décision documentée, bloc de transfert). Source exclut Partage dans
// cette UI (pas d'écran de partage inter-chorales dans ce lot) — restriction imposée par le
// formulaire (options de <select> limitées), pas par le typage TypeScript.
export interface ICreateRecordingRequest {
  SongId: string;
  Type: RecordingTypeEnum;
  TargetVoicePart?: VoicePartEnum | null;
  ContentOwner: string;
  DownloadAllowed: boolean;
  DurationSeconds: number;
  Source: RecordingSourceEnum;
}
