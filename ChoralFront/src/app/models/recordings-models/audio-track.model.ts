// Piste telle que AudioPlayerComponent la consomme. Volontairement découplée d'IPlaylistTrack
// (playlist d'événement par voix) et d'IRecording (fiche chant) : le lecteur est le même écran
// dans les deux cas, mais ce qui identifie une piste change — un chant pour une playlist
// d'événement, une voix pour la fiche d'un chant. Chaque appelant fait sa projection.
//
// RecordingId reste le seul champ structurant : c'est lui qui alimente
// RecordingService.download() (streaming authentifié via Blob).
export interface IAudioTrack {
  RecordingId: string;
  Title: string;
  Subtitle: string | null;
  DurationSeconds: number;
}
