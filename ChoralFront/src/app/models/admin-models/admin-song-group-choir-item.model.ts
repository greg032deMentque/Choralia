import { SongStatusEnum } from '@app/enums/song-status.enum';

// Reflète AdminChantGroupeChoraleItemViewModel (back, AdminChantController.GetChoralesDuGroupe)
// — détail dépliable d'un groupe du catalogue : une ligne par chorale portant ce chant.
// Volontairement non paginé (tableau simple) : borné par nature au nombre de chorales du
// groupe, jamais un GetAll transverse.
export interface IAdminSongGroupChoirItem {
  ChoirId: string;
  ChoirName: string;
  ClientName: string;
  SongStatus: SongStatusEnum;
  CreationDate: string;
}
