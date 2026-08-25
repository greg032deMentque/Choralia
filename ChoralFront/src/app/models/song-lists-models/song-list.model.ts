import { ISongListSong } from '@models/song-lists-models/song-list-song.model';
import { SongListTypeEnum } from '@app/enums/type-list.enum';
import { SongListStatusEnum } from '@app/enums/status-list.enum';

// Reflète SongListViewModel (back). OwnerUserId et Statut sont ignorés par le
// mapping ViewModel -> Entity côté back sur Create/Update (gérés respectivement par le
// service métier et par les endpoints de workflow Publish/Archive/RevertToDraft) —
// jamais renseignés depuis un formulaire front.
export interface ISongList {
  Id: string | null;
  Name: string;
  Description: string | null;
  ChoirId: string | null;
  SectionId: string | null;
  EventId: string | null;
  CreatedById: string | null;
  OwnerUserId: string | null;
  Type: SongListTypeEnum;
  Status: SongListStatusEnum;
  Songs: ISongListSong[];
}
