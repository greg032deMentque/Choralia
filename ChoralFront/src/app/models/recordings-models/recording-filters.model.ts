import { RecordingTypeEnum } from '@app/enums/type-recording.enum';
import { RecordingStatusEnum } from '@app/enums/status-recording.enum';
import { RecordingSourceEnum } from '@app/enums/source-recording.enum';
import { VoicePartEnum } from '@app/enums/voice-part.enum';

// Filtres secondaires appliqués en complément de SongId (requis par GetPagedBySong) et
// de Filter (texte libre, porté par IPaginationQueryParams).
export interface IRecordingListFilters {
  Type?: RecordingTypeEnum;
  TargetVoicePart?: VoicePartEnum;
  Status?: RecordingStatusEnum;
  Source?: RecordingSourceEnum;
}
