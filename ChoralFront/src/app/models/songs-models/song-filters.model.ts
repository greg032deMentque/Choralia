import { SongStatusEnum } from '@app/enums/song-status.enum';
import { SongPriorityEnum } from '@app/enums/priority-song.enum';
import { VoicePartEnum } from '@app/enums/voice-part.enum';

// Filtres de SongController.GetPaged — tous optionnels, en complément de Filter (texte
// libre) porté par IPaginationQueryParams.
export interface ISongListFilters {
  ChoirId?: string;
  VoicePart?: VoicePartEnum;
  Status?: SongStatusEnum;
  Priority?: SongPriorityEnum;
}
