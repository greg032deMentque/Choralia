import { RecordingTypeEnum } from '@app/enums/type-recording.enum';
import { RecordingStatusEnum } from '@app/enums/status-recording.enum';
import { RecordingSourceEnum } from '@app/enums/source-recording.enum';
import { VoicePartEnum } from '@app/enums/voice-part.enum';

// Reflète RecordingViewModel (back).
export interface IRecording {
  Id: string | null;
  SongId: string;
  Type: RecordingTypeEnum;
  TargetVoicePart: VoicePartEnum | null;
  ChoirOwnerId: string;
  CreatorUserId: string;
  Status: RecordingStatusEnum;
  Source: RecordingSourceEnum;
  DurationSeconds: number;
  PublicationDate: string | null;
  ContentOwner: string;
  DownloadAllowed: boolean;
  OriginalFileName: string | null;
  CreatedAt: string;
}
