import { VoicePartEnum } from '@app/enums/voice-part.enum';

// Reflète PlaylistTrackViewModel (back). Retourné par
// RecordingController.PlaylistParVoixEvenement — une piste par chant publié pour la
// voix demandée, dans l'ordre de la/les liste(s) de chants de l'événement.
export interface IPlaylistTrack {
  RecordingId: string;
  SongId: string;
  SongTitle: string;
  TargetVoicePart: VoicePartEnum | null;
  DurationSeconds: number;
  Position: number;
}
