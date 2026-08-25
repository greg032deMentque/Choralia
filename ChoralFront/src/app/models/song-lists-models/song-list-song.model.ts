// Reflète SongListSongViewModel (back). SongTitle est calculé côté serveur
// (mapping depuis Chant.Titre) — jamais renseigné depuis un formulaire front.
export interface ISongListSong {
  Id: string | null;
  SongListId: string;
  SongId: string;
  Position: number;
  SongTitle: string | null;
}
