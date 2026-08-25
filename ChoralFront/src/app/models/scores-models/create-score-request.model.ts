import { ScoreTypeEnum } from '@app/enums/type-score.enum';
import { VoicePartEnum } from '@app/enums/voice-part.enum';

// Champs multipart (hors Fichier, transmis séparément) de ScoreController.Create.
// Le Statut n'est jamais envoyé : forcé à Draft côté back.
export interface ICreateScoreRequest {
  SongId: string;
  Type: ScoreTypeEnum;
  TargetVoicePart?: VoicePartEnum | null;
  Version: string;
  DownloadAllowed: boolean;
}
