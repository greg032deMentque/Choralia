import { ScoreTypeEnum } from '@app/enums/type-score.enum';
import { ScoreStatusEnum } from '@app/enums/status-score.enum';
import { VoicePartEnum } from '@app/enums/voice-part.enum';

// Reflète ScoreViewModel (back).
export interface IScore {
  Id: string | null;
  SongId: string;
  Type: ScoreTypeEnum;
  TargetVoicePart: VoicePartEnum | null;
  Version: string;
  Status: ScoreStatusEnum;
  OwnerUserId: string;
  DownloadAllowed: boolean;
  OriginalFileName: string | null;
  PublishedAt: string | null;
  CreatedAt: string;
}
