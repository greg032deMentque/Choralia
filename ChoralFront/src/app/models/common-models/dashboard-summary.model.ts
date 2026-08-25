import { IconNameEnum } from '@app/enums/icon-name.enum';

// Reflète ChoirKpiViewModel (back). Ne contient que les indicateurs réellement
// calculables : ceux qui dépendent d'une agrégation d'AnalyticLog inexistante ne figurent
// ni ici ni à l'écran (D30) — un indicateur faux est plus nuisible qu'un indicateur absent.
export interface IChoirKpi {
  SongsInRepertoire: number;
  IncompleteSongs: number;
  RecordingsPendingReview: number;
  Members: number;
  InvitedMembers: number;
  UpcomingEvents: INextEvent[];
}

export interface INextEvent {
  Id: string;
  Title: string;
  Location: string;
  StartDate: string;
  Targets: number;
  Responses: number;
  // null quand aucun membre n'est ciblé : afficher 0 % ferait croire à une absence de
  // réponse plutôt qu'à une absence de destinataire.
  ResponseRate: number | null;
}

// Tuile affichée. Valeur reste une chaîne : le composant met en forme (compteur simple,
// ratio « 3 sur 18 »…) et la tuile n'a pas à connaître ces formats.
export interface IDashboardKpi {
  Label: string;
  Value: string;
  Icon: IconNameEnum;
}
