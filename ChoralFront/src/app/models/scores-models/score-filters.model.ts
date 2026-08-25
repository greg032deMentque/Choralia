import { ScoreTypeEnum } from '@app/enums/type-score.enum';
import { ScoreStatusEnum } from '@app/enums/status-score.enum';
import { VoicePartEnum } from '@app/enums/voice-part.enum';

// Filtres secondaires appliqués en complément de SongId (requis par GetPagedBySong) et
// de Filter (texte libre, porté par IPaginationQueryParams).
export interface IScoreListFilters {
  Type?: ScoreTypeEnum;
  TargetVoicePart?: VoicePartEnum;
  Status?: ScoreStatusEnum;
}
