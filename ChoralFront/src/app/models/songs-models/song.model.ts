import { SongStatusEnum } from '@app/enums/song-status.enum';
import { SongPriorityEnum } from '@app/enums/priority-song.enum';
import { VoicePartEnum } from '@app/enums/voice-part.enum';

// Reflète SongViewModel (back). IsCompleteForChoir et VoicePartsWithoutPublishedRecording sont
// calculés côté serveur — jamais renseignés depuis un formulaire front.
export interface ISong {
  Id: string | null;
  Title: string;
  Status: SongStatusEnum;
  VoiceParts: VoicePartEnum[];
  Author: string | null;
  Composer: string | null;
  Language: string | null;
  ApproximateDurationSeconds: number | null;
  WorkingKey: string | null;
  Priority: SongPriorityEnum | null;
  PreparationNotes: string | null;
  ChoirId: string;
  IsCompleteForChoir: boolean;
  VoicePartsWithoutPublishedRecording: VoicePartEnum[];
}
